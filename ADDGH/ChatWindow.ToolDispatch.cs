using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ADDGH
{
    public static partial class ChatWindow
    {
        private sealed class ToolDispatchResult
        {
            public string ToolResult;
            public int AddComp;
            public int DelComp;
            public int AddConn;
            public int DelConn;
            public int AddCodeLines;
            public int DelCodeLines;
            public bool EndApiRoundAwaitingUser;
            public ApiResponse EarlyResponse;
            public string UndoSnapshotPath;
            public string UndoId;
        }

        private static int CountCodeLinesForStats(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return 0;
            return code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Length;
        }

        private static string ReadCSharpScriptBodyForStats(string id)
        {
            string body = null;
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) return;
                if (!Guid.TryParse(id, out Guid guid)) return;
                var obj = doc.FindObject(guid, true);
                if (obj == null || !IsCSharpScriptComponent(obj)) return;
                if (TryReadCSharpScriptBodyPreservingTemplate(obj, out string currentBody, out _))
                    body = currentBody;
            }));
            return body;
        }

        private static int ReadResultInt(string toolResult, string key)
        {
            if (string.IsNullOrWhiteSpace(toolResult) || string.IsNullOrWhiteSpace(key))
                return 0;
            try
            {
                var root = JObject.Parse(toolResult);
                return root[key]?.ToObject<int?>() ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private static double? ReadNullableDouble(JObject argsObj, string key)
        {
            return argsObj?[key] == null || argsObj[key].Type == JTokenType.Null
                ? (double?)null
                : argsObj[key].ToObject<double>();
        }

        private static int? ReadNullableInt(JObject argsObj, string key)
        {
            return argsObj?[key] == null || argsObj[key].Type == JTokenType.Null
                ? (int?)null
                : argsObj[key].ToObject<int>();
        }

        private static bool? ReadNullableBool(JObject argsObj, string key)
        {
            return argsObj?[key] == null || argsObj[key].Type == JTokenType.Null
                ? (bool?)null
                : argsObj[key].ToObject<bool>();
        }

        private static string ResolveToolObjectId(string id)
        {
            var doc = Grasshopper.Instances.ActiveCanvas?.Document;
            if (doc == null || string.IsNullOrWhiteSpace(id))
                return id;

            return TryResolveGuidFromPublicId(doc, id, out Guid guid)
                ? guid.ToString()
                : id;
        }

        private static List<string> ResolveToolObjectIds(IEnumerable<string> ids)
        {
            if (ids == null)
                return null;

            return ids.Select(ResolveToolObjectId).ToList();
        }

        private static ToolDispatchResult ExecuteToolCall(
            string funcName,
            JObject argsObj,
            string argsJson,
            string callId,
            string fullContent,
            string fullReasoning,
            List<(string primary, string secondary)> operationCards)
        {
            var result = new ToolDispatchResult { ToolResult = "" };
            if (IsCanvasMutatingTool(funcName))
                result.UndoSnapshotPath = CreateCanvasUndoSnapshot(funcName, callId);

            try
            {
                if (funcName == "create_ai_image")
                {
                    throw new InvalidOperationException("create_ai_image must be executed through the async tool dispatch path.");
                }
                if (funcName == "ensure_gh_canvas")
                {
                    result.ToolResult = ExecuteEnsureGhCanvas();
                }
                else if (funcName == "get_gh_components")
                {
                    result.ToolResult = ExecuteGetGhComponents();
                }
                else if (funcName == "recompute_gh_canvas")
                {
                    result.ToolResult = ExecuteRecomputeGhCanvas();
                }
                else if (funcName == "capture_rhino_viewport")
                {
                    result.ToolResult = "Error: capture_rhino_viewport is not exposed to AI tools.";
                }
                else if (funcName == "gh_native_script_editor")
                {
                    result.ToolResult = ExecuteGhNativeScriptEditor(
                        ResolveToolObjectId(argsObj["id"]?.ToString()),
                        argsObj["mode"]?.ToString(),
                        argsObj["code"]?.ToString(),
                        argsObj["language"]?.ToString());
                }
                else if (funcName == "add_gh_component")
                {
                    string label = argsObj["label"]?.ToString();
                    string name = argsObj["name"]?.ToString();
                    string cguid = argsObj["component_guid"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(cguid))
                    {
                        result.ToolResult = "Error: name or component_guid is required.";
                    }
                    else
                    {
                        float x = argsObj["x"]?.ToObject<float>() ?? 0f;
                        float y = argsObj["y"]?.ToObject<float>() ?? 0f;
                        result.ToolResult = ExecuteAddGhComponent(
                            name ?? "",
                            x,
                            y,
                            label,
                            cguid,
                            argsObj["graph_mapper_type"]?.ToString() ?? argsObj["graph_type"]?.ToString(),
                            argsObj["value"]?.ToString(),
                            argsObj["min"]?.ToObject<double?>(),
                            argsObj["max"]?.ToObject<double?>(),
                            argsObj["decimals"]?.ToObject<int?>());
                        if (!result.ToolResult.StartsWith("Error:")) result.AddComp++;
                    }
                }
                else if (funcName == "connect_gh_components")
                {
                    result.ToolResult = ExecuteConnectGhComponents(
                        ResolveToolObjectId(argsObj["from_id"]?.ToString()),
                        argsObj["from_index"]?.ToObject<int>() ?? 0,
                        ResolveToolObjectId(argsObj["to_id"]?.ToString()),
                        argsObj["to_index"]?.ToObject<int>() ?? 0,
                        argsObj["from_port_label"]?.ToString(),
                        argsObj["to_port_label"]?.ToString());
                    if (!result.ToolResult.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) result.AddConn++;
                }
                else if (funcName == "remove_gh_component")
                {
                    result.ToolResult = ExecuteRemoveGhComponent(ResolveToolObjectId(argsObj["id"]?.ToString()));
                    if (!result.ToolResult.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) result.DelComp++;
                }
                else if (funcName == "set_gh_component_value")
                {
                    result.ToolResult = ExecuteSetGhComponentValue(
                        ResolveToolObjectId(argsObj["id"]?.ToString()),
                        argsObj["value"]?.ToString(),
                        ReadNullableDouble(argsObj, "min"),
                        ReadNullableDouble(argsObj, "max"),
                        ReadNullableInt(argsObj, "decimals"),
                        argsObj["property"]?.ToString(),
                        argsObj["graph_mapper_type"]?.ToString() ?? argsObj["graph_type"]?.ToString());
                }
                else if (funcName == "remove_gh_connection")
                {
                    result.ToolResult = ExecuteRemoveGhConnection(
                        ResolveToolObjectId(argsObj["from_id"]?.ToString()),
                        argsObj["from_index"]?.ToObject<int>() ?? 0,
                        ResolveToolObjectId(argsObj["to_id"]?.ToString()),
                        argsObj["to_index"]?.ToObject<int>() ?? 0);
                    if (!result.ToolResult.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) result.DelConn++;
                }
                else if (funcName == "create_component_graph")
                {
                    bool autoGroup = argsObj["auto_group"]?.ToObject<bool>() ?? false;
                    string groupName = argsObj["group_name"]?.ToString();
                    if (string.IsNullOrEmpty(groupName))
                        groupName = autoGroup ? "AI Generated" : null;
                    result.ToolResult = ExecuteCreateComponentGraph(
                        argsObj["components"] as JArray,
                        argsObj["connections"] as JArray,
                        groupName);
                    if (!result.ToolResult.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
                    {
                        result.AddComp += ReadResultInt(result.ToolResult, "created_components");
                        result.AddConn += ReadResultInt(result.ToolResult, "created_connections");
                    }
                }
                else if (funcName == "create_csharp_script_component")
                {
                    string csharpName = argsObj["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(csharpName)) csharpName = argsObj["label"]?.ToString();
                    result.ToolResult = ExecuteCreateCSharpScriptComponent(
                        argsObj["alias_id"]?.ToString(),
                        csharpName,
                        argsObj["x"]?.ToObject<float>() ?? 0f,
                        argsObj["y"]?.ToObject<float>() ?? 0f,
                        argsObj["inputs"] as JArray,
                        argsObj["outputs"] as JArray,
                        argsObj["body"]?.ToString(),
                        argsObj["components"] as JArray,
                        argsObj["connections"] as JArray,
                        argsObj["group_name"]?.ToString());
                    if (!result.ToolResult.StartsWith("Error:"))
                    {
                        result.AddComp += ReadResultInt(result.ToolResult, "created_scripts");
                        result.AddCodeLines += CountCodeLinesForStats(argsObj["body"]?.ToString());
                        result.AddComp += ReadResultInt(result.ToolResult, "created_components");
                    }
                }
                else if (funcName == "edit_csharp_script_component")
                {
                    string beforeBody = null;
                    bool settingBody = string.Equals(argsObj["mode"]?.ToString(), "set_body", StringComparison.OrdinalIgnoreCase);
                    string resolvedId = ResolveToolObjectId(argsObj["id"]?.ToString());
                    if (settingBody)
                        beforeBody = ReadCSharpScriptBodyForStats(resolvedId);

                    result.ToolResult = ExecuteEditCSharpScriptComponent(
                        resolvedId,
                        argsObj["mode"]?.ToString(),
                        argsObj["body"]?.ToString());

                    if (settingBody && !result.ToolResult.StartsWith("Error:"))
                    {
                        int beforeLines = CountCodeLinesForStats(beforeBody);
                        int afterLines = CountCodeLinesForStats(argsObj["body"]?.ToString());
                        if (afterLines >= beforeLines) result.AddCodeLines += afterLines - beforeLines;
                        else result.DelCodeLines += beforeLines - afterLines;
                    }
                }
                else if (funcName == "create_script_component_graph")
                {
                    result.ToolResult = ExecuteCreateScriptComponentGraph(
                        argsObj["mode"]?.ToString(),
                        argsObj["scripts"] as JArray,
                        argsObj["components"] as JArray,
                        argsObj["connections"] as JArray,
                        argsObj["group_name"]?.ToString());
                    if (!result.ToolResult.StartsWith("Error:"))
                    {
                        result.AddComp += ReadResultInt(result.ToolResult, "created_scripts");
                        result.AddComp += ReadResultInt(result.ToolResult, "created_components");
                        result.AddConn += ReadResultInt(result.ToolResult, "created_connections");
                        if (argsObj["scripts"] is JArray scriptItems)
                        {
                            foreach (var script in scriptItems)
                            {
                                result.AddCodeLines += CountCodeLinesForStats(script?["body"]?.ToString() ?? script?["code"]?.ToString() ?? script?["value"]?.ToString());
                            }
                        }
                    }
                }
                else if (funcName == "check_gh_errors")
                {
                    result.ToolResult = ExecuteCheckGhErrors();
                }
                else if (funcName == "search_component_library")
                {
                    result.ToolResult = ExecuteSearchComponentLibrary(argsObj["keyword"]?.ToString());
                }
                else if (funcName == "search_gh_component_catalog")
                {
                    int maxResults = argsObj["max_results"]?.ToObject<int?>() ?? 30;
                    string categoryContains = argsObj["category_contains"]?.ToString();
                    result.ToolResult = ExecuteSearchGhComponentCatalog(argsObj["query"]?.ToString(), maxResults, categoryContains);
                }
                else if (funcName == "query_gh_components")
                {
                    result.ToolResult = ExecuteQueryGhComponents(
                        ResolveToolObjectId(argsObj["id"]?.ToString()),
                        argsObj["name_contains"]?.ToString(),
                        ReadNullableBool(argsObj, "has_errors"),
                        ReadNullableBool(argsObj, "is_script"),
                        ReadNullableBool(argsObj, "has_connections"),
                        argsObj["port_name_contains"]?.ToString(),
                        argsObj["max_results"]?.ToObject<int?>() ?? 8,
                        argsObj["neighbor_depth"]?.ToObject<int?>() ?? 1);
                }
                else if (funcName == "get_component_context")
                {
                    result.ToolResult = ExecuteGetComponentContext(
                        ResolveToolObjectId(argsObj["id"]?.ToString()),
                        argsObj["depth"]?.ToObject<int?>() ?? 1,
                        ReadNullableBool(argsObj, "include_script_bodies") ?? false);
                }
                else if (funcName == "read_component_script")
                {
                    result.ToolResult = ExecuteReadComponentScript(ResolveToolObjectId(argsObj["id"]?.ToString()));
                }
                else if (funcName == "set_gh_component_status")
                {
                    result.ToolResult = ExecuteSetGhComponentStatus(
                        ResolveToolObjectId(argsObj["id"]?.ToString()),
                        ReadNullableBool(argsObj, "preview"),
                        ReadNullableBool(argsObj, "enabled"));
                }
                else if (funcName == "set_all_csharp_script_previews")
                {
                    result.ToolResult = ExecuteSetAllCSharpScriptPreviews(
                        ReadNullableBool(argsObj, "preview"));
                }
                else if (funcName == "prepare_visual_review_preview")
                {
                    result.ToolResult = "Error: prepare_visual_review_preview is disabled.";
                }
                else if (funcName == "modify_gh_component_ports")
                {
                    result.ToolResult = ExecuteModifyGhComponentPorts(
                        ResolveToolObjectId(argsObj["id"]?.ToString()),
                        argsObj["is_input"]?.ToObject<bool>() ?? false,
                        argsObj["action"]?.ToString(),
                        argsObj["port_name"]?.ToString(),
                        argsObj["index"]?.ToObject<int?>(),
                        argsObj["type_hint"]?.ToString());
                }
                else if (funcName == "modify_gh_port_data")
                {
                    result.ToolResult = ExecuteModifyGhPortData(
                        ResolveToolObjectId(argsObj["id"]?.ToString()),
                        argsObj["is_input"]?.ToObject<bool>() ?? false,
                        argsObj["index"]?.ToObject<int>() ?? 0,
                        argsObj["operation"]?.ToString());
                }
                else if (funcName == "manage_gh_groups")
                {
                    string groupId = ResolveToolObjectId(argsObj["group_id"]?.ToString());
                    string groupName = argsObj["name"]?.ToString();
                    JArray idsArray = argsObj["ids"] as JArray;
                    List<string> idsList = ResolveToolObjectIds(idsArray?.Select(v => v.ToString()));
                    result.ToolResult = ExecuteManageGhGroups(argsObj["action"]?.ToString(), idsList, groupId, groupName);
                }
                else if (funcName == "read_skill_file")
                {
                    result.ToolResult = ExecuteReadSkillFile(argsObj["file_name"]?.ToString());
                }
                else if (funcName == "read_reference_json")
                {
                    result.ToolResult = ExecuteReadReferenceJson(argsObj["file_name"]?.ToString());
                }
                else if (funcName == "import_reference_gh")
                {
                    result.ToolResult = ExecuteImportReferenceGh(
                        argsObj["file_name"]?.ToString(),
                        ReadNullableDouble(argsObj, "offset_x"),
                        ReadNullableDouble(argsObj, "offset_y"),
                        argsObj["group_name"]?.ToString());
                }
                else if (funcName == "create_gh_skill")
                {
                    result.ToolResult = ExecuteCreateGhSkill(
                        argsObj["file_name"]?.ToString(),
                        argsObj["name"]?.ToString(),
                        argsObj["description"]?.ToString(),
                        argsObj["content"]?.ToString());
                }
                else if (funcName == ShowReferenceOptionsTool.FunctionName)
                {
                    var (refToolMsg, refEndRound) = ShowReferenceOptionsTool.Run(argsObj, argsJson, operationCards);
                    result.ToolResult = refToolMsg;
                    if (refEndRound)
                    {
                        _messages.Add(new { role = "tool", tool_call_id = callId, name = funcName, content = result.ToolResult });
                        EnforceChatHistoryLimit();
                        result.EndApiRoundAwaitingUser = true;
                        result.EarlyResponse = new ApiResponse
                        {
                            Content = fullContent,
                            Reasoning = fullReasoning
                        };
                    }
                }
                else if (funcName == ShowPlanStepsTool.FunctionName)
                {
                    var (planToolMsg, planEndRound) = ShowPlanStepsTool.Run(argsObj, argsJson, operationCards);
                    result.ToolResult = planToolMsg;
                    if (planEndRound)
                    {
                        _messages.Add(new { role = "tool", tool_call_id = callId, name = funcName, content = result.ToolResult });
                        EnforceChatHistoryLimit();
                        result.EndApiRoundAwaitingUser = true;
                        result.EarlyResponse = new ApiResponse
                        {
                            Content = fullContent,
                            Reasoning = fullReasoning
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                result.ToolResult = "Error: " + ex.Message;
                AddGhLog.Error("Tool dispatch failed: " + (funcName ?? "?"), ex);
            }

            return result;
        }

        private static async Task<ToolDispatchResult> ExecuteToolCallAsync(
            string funcName,
            JObject argsObj,
            string argsJson,
            string callId,
            string fullContent,
            string fullReasoning,
            List<(string primary, string secondary)> operationCards,
            System.Threading.CancellationToken ct)
        {
            if (!string.Equals(funcName, "create_ai_image", StringComparison.Ordinal)
                && !string.Equals(funcName, "capture_rhino_viewport", StringComparison.Ordinal)
                && !string.Equals(funcName, "web_research", StringComparison.Ordinal))
            {
                return ExecuteToolCall(funcName, argsObj, argsJson, callId, fullContent, fullReasoning, operationCards);
            }

            var result = new ToolDispatchResult { ToolResult = "" };
            try
            {
                if (string.Equals(funcName, "create_ai_image", StringComparison.Ordinal))
                {
                    result.ToolResult = await ExecuteCreateAiImageAsync(
                        argsObj["prompt"]?.ToString(),
                        argsObj["intent"]?.ToString(),
                        ReadNullableBool(argsObj, "use_uploaded_images") ?? true,
                        argsObj["aspect_ratio"]?.ToString(),
                        ct);
                    Rhino.RhinoApp.InvokeOnUiThread((Action)(() => ApplyAiImageToolResult(result.ToolResult)));
                }
                else if (string.Equals(funcName, "capture_rhino_viewport", StringComparison.Ordinal))
                {
                    result.ToolResult = "Error: capture_rhino_viewport is not exposed to AI tools.";
                }
                else if (string.Equals(funcName, "web_research", StringComparison.Ordinal))
                {
                    result.ToolResult = await ExecuteWebResearchAsync(
                        argsObj["mode"]?.ToString(),
                        argsObj["query"]?.ToString(),
                        argsObj["url"]?.ToString(),
                        argsObj["allowed_domains"] as JArray,
                        argsObj["max_results"]?.ToObject<int?>() ?? 5,
                        argsObj["max_chars"]?.ToObject<int?>() ?? 6000,
                        ct);
                }
            }
            catch (Exception ex)
            {
                result.ToolResult = "Error: " + ex.Message;
                AddGhLog.Error("Async tool dispatch failed: " + (funcName ?? "?"), ex);
            }

            return result;
        }
    }
}

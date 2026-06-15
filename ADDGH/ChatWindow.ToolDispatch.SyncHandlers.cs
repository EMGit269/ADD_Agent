using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ADDGH
{
    public static partial class ChatWindow
    {
        private static void ExecuteSynchronousToolCallCore(
            ToolDispatchResult result,
            string funcName,
            JObject argsObj,
            string argsJson,
            string callId,
            string fullContent,
            string fullReasoning,
            List<(string primary, string secondary)> operationCards)
        {
            if (funcName == "create_ai_image")
                throw new InvalidOperationException("create_ai_image must be executed through the async tool dispatch path.");

            if (TryExecuteBasicCanvasTool(result, funcName)
                || TryExecuteCanvasMutationTool(result, funcName, argsObj)
                || TryExecuteScriptTool(result, funcName, argsObj)
                || TryExecuteLookupTool(result, funcName, argsObj)
                || TryExecuteStateRepairTool(result, funcName, argsObj)
                || TryExecuteReferenceSkillTool(result, funcName, argsObj)
                || TryExecuteInteractiveTool(result, funcName, argsObj, argsJson, callId, fullContent, fullReasoning, operationCards))
            {
                return;
            }
        }

        private static bool TryExecuteBasicCanvasTool(ToolDispatchResult result, string funcName)
        {
            if (funcName == "ensure_gh_canvas")
            {
                result.ToolResult = ExecuteEnsureGhCanvas();
                return true;
            }
            if (funcName == "get_gh_components")
            {
                result.ToolResult = ExecuteGetGhComponents();
                return true;
            }
            if (funcName == "recompute_gh_canvas")
            {
                result.ToolResult = ExecuteRecomputeGhCanvas();
                return true;
            }
            if (funcName == "capture_rhino_viewport")
            {
                result.ToolResult = "Error: capture_rhino_viewport is not exposed to AI tools.";
                return true;
            }
            if (funcName == "check_gh_errors")
            {
                result.ToolResult = ExecuteCheckGhErrors();
                return true;
            }
            return false;
        }

        private static bool TryExecuteCanvasMutationTool(ToolDispatchResult result, string funcName, JObject argsObj)
        {
            if (funcName == "add_gh_component")
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
                    result.ToolResult = ExecuteAddGhComponent(
                        name ?? "",
                        argsObj["x"]?.ToObject<float>() ?? 0f,
                        argsObj["y"]?.ToObject<float>() ?? 0f,
                        label,
                        cguid,
                        argsObj["graph_mapper_type"]?.ToString() ?? argsObj["graph_type"]?.ToString(),
                        argsObj["value"]?.ToString(),
                        argsObj["min"]?.ToObject<double?>(),
                        argsObj["max"]?.ToObject<double?>(),
                        argsObj["decimals"]?.ToObject<int?>());
                    if (!result.ToolResult.StartsWith("Error:")) result.AddComp++;
                }
                return true;
            }

            if (funcName == "connect_gh_components")
            {
                result.ToolResult = ExecuteConnectGhComponents(
                    ResolveToolObjectId(argsObj["from_id"]?.ToString()),
                    argsObj["from_index"]?.ToObject<int>() ?? 0,
                    ResolveToolObjectId(argsObj["to_id"]?.ToString()),
                    argsObj["to_index"]?.ToObject<int>() ?? 0,
                    argsObj["from_port_label"]?.ToString(),
                    argsObj["to_port_label"]?.ToString());
                if (!result.ToolResult.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) result.AddConn++;
                return true;
            }

            if (funcName == "remove_gh_component")
            {
                result.ToolResult = ExecuteRemoveGhComponent(ResolveToolObjectId(argsObj["id"]?.ToString()));
                if (!result.ToolResult.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) result.DelComp++;
                return true;
            }

            if (funcName == "set_gh_component_value")
            {
                result.ToolResult = ExecuteSetGhComponentValue(
                    ResolveToolObjectId(argsObj["id"]?.ToString()),
                    argsObj["value"]?.ToString(),
                    ReadNullableDouble(argsObj, "min"),
                    ReadNullableDouble(argsObj, "max"),
                    ReadNullableInt(argsObj, "decimals"),
                    argsObj["property"]?.ToString(),
                    argsObj["graph_mapper_type"]?.ToString() ?? argsObj["graph_type"]?.ToString());
                return true;
            }

            if (funcName == "remove_gh_connection")
            {
                result.ToolResult = ExecuteRemoveGhConnection(
                    ResolveToolObjectId(argsObj["from_id"]?.ToString()),
                    argsObj["from_index"]?.ToObject<int>() ?? 0,
                    ResolveToolObjectId(argsObj["to_id"]?.ToString()),
                    argsObj["to_index"]?.ToObject<int>() ?? 0);
                if (!result.ToolResult.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) result.DelConn++;
                return true;
            }

            if (funcName == "create_component_graph")
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
                return true;
            }

            if (funcName == "manage_gh_groups")
            {
                string groupId = ResolveToolObjectId(argsObj["group_id"]?.ToString());
                string groupName = argsObj["name"]?.ToString();
                JArray idsArray = argsObj["ids"] as JArray;
                List<string> idsList = ResolveToolObjectIds(idsArray?.Select(v => v.ToString()));
                result.ToolResult = ExecuteManageGhGroupsUnified(argsObj["action"]?.ToString(), idsList, groupId, groupName);
                return true;
            }

            return false;
        }

        private static bool TryExecuteScriptTool(ToolDispatchResult result, string funcName, JObject argsObj)
        {
            if (funcName == "gh_native_script_editor")
            {
                result.ToolResult = ExecuteGhNativeScriptEditor(
                    ResolveToolObjectId(argsObj["id"]?.ToString()),
                    argsObj["mode"]?.ToString(),
                    argsObj["code"]?.ToString(),
                    argsObj["language"]?.ToString());
                return true;
            }

            if (funcName == "create_csharp_script_component")
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
                return true;
            }

            if (funcName == "edit_csharp_script_component")
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
                return true;
            }

            if (funcName == "create_script_component_graph")
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
                return true;
            }

            if (funcName == "read_component_script")
            {
                result.ToolResult = ExecuteReadComponentScript(ResolveToolObjectId(argsObj["id"]?.ToString()));
                return true;
            }

            return false;
        }

        private static bool TryExecuteLookupTool(ToolDispatchResult result, string funcName, JObject argsObj)
        {
            if (funcName == "search_component_library")
            {
                result.ToolResult = ExecuteSearchComponentLibrary(argsObj["keyword"]?.ToString());
                return true;
            }
            if (funcName == "search_gh_component_catalog")
            {
                int maxResults = argsObj["max_results"]?.ToObject<int?>() ?? 30;
                string categoryContains = argsObj["category_contains"]?.ToString();
                result.ToolResult = ExecuteSearchGhComponentCatalog(argsObj["query"]?.ToString(), maxResults, categoryContains);
                return true;
            }
            if (funcName == "query_gh_components")
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
                return true;
            }
            if (funcName == "get_component_context")
            {
                result.ToolResult = ExecuteGetComponentContext(
                    ResolveToolObjectId(argsObj["id"]?.ToString()),
                    argsObj["depth"]?.ToObject<int?>() ?? 1,
                    ReadNullableBool(argsObj, "include_script_bodies") ?? false);
                return true;
            }
            return false;
        }

        private static bool TryExecuteStateRepairTool(ToolDispatchResult result, string funcName, JObject argsObj)
        {
            if (funcName == "set_gh_component_status")
            {
                result.ToolResult = ExecuteSetGhComponentStatus(
                    ResolveToolObjectId(argsObj["id"]?.ToString()),
                    ReadNullableBool(argsObj, "preview"),
                    ReadNullableBool(argsObj, "enabled"));
                return true;
            }
            if (funcName == "set_all_csharp_script_previews")
            {
                result.ToolResult = ExecuteSetAllCSharpScriptPreviews(ReadNullableBool(argsObj, "preview"));
                return true;
            }
            if (funcName == "prepare_visual_review_preview")
            {
                result.ToolResult = "Error: prepare_visual_review_preview is disabled.";
                return true;
            }
            if (funcName == "modify_gh_component_ports")
            {
                result.ToolResult = ExecuteModifyGhComponentPorts(
                    ResolveToolObjectId(argsObj["id"]?.ToString()),
                    argsObj["is_input"]?.ToObject<bool>() ?? false,
                    argsObj["action"]?.ToString(),
                    argsObj["port_name"]?.ToString(),
                    argsObj["index"]?.ToObject<int?>(),
                    argsObj["type_hint"]?.ToString());
                return true;
            }
            if (funcName == "modify_gh_port_data")
            {
                result.ToolResult = ExecuteModifyGhPortData(
                    ResolveToolObjectId(argsObj["id"]?.ToString()),
                    argsObj["is_input"]?.ToObject<bool>() ?? false,
                    argsObj["index"]?.ToObject<int>() ?? 0,
                    argsObj["operation"]?.ToString());
                return true;
            }
            return false;
        }

        private static bool TryExecuteReferenceSkillTool(ToolDispatchResult result, string funcName, JObject argsObj)
        {
            if (funcName == "read_skill_file")
            {
                result.ToolResult = ExecuteReadSkillFile(argsObj["file_name"]?.ToString());
                return true;
            }
            if (funcName == "read_reference_json")
            {
                result.ToolResult = ExecuteReadReferenceJson(argsObj["file_name"]?.ToString());
                return true;
            }
            if (funcName == "import_reference_gh")
            {
                result.ToolResult = ExecuteImportReferenceGh(
                    argsObj["file_name"]?.ToString(),
                    ReadNullableDouble(argsObj, "offset_x"),
                    ReadNullableDouble(argsObj, "offset_y"),
                    argsObj["group_name"]?.ToString());
                return true;
            }
            if (funcName == "create_gh_skill")
            {
                result.ToolResult = ExecuteCreateGhSkill(
                    argsObj["file_name"]?.ToString(),
                    argsObj["name"]?.ToString(),
                    argsObj["description"]?.ToString(),
                    argsObj["content"]?.ToString());
                return true;
            }
            return false;
        }

        private static bool TryExecuteInteractiveTool(
            ToolDispatchResult result,
            string funcName,
            JObject argsObj,
            string argsJson,
            string callId,
            string fullContent,
            string fullReasoning,
            List<(string primary, string secondary)> operationCards)
        {
            if (funcName == ShowReferenceOptionsTool.FunctionName)
            {
                var (refToolMsg, refEndRound) = ShowReferenceOptionsTool.Run(argsObj, argsJson, operationCards);
                result.ToolResult = refToolMsg;
                ApplyInteractiveToolEndRound(result, refEndRound, callId, funcName, fullContent, fullReasoning);
                return true;
            }
            if (funcName == ShowPlanStepsTool.FunctionName)
            {
                var (planToolMsg, planEndRound) = ShowPlanStepsTool.Run(argsObj, argsJson, operationCards);
                result.ToolResult = planToolMsg;
                ApplyInteractiveToolEndRound(result, planEndRound, callId, funcName, fullContent, fullReasoning);
                return true;
            }
            return false;
        }

        private static void ApplyInteractiveToolEndRound(
            ToolDispatchResult result,
            bool endRound,
            string callId,
            string funcName,
            string fullContent,
            string fullReasoning)
        {
            if (!endRound) return;

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

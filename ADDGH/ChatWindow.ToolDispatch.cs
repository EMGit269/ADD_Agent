using System;
using System.Collections.Generic;
using System.Linq;
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
            public bool EndApiRoundAwaitingUser;
            public ApiResponse EarlyResponse;
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

            try
            {
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
                else if (funcName == "gh_native_script_editor")
                {
                    result.ToolResult = ExecuteGhNativeScriptEditor(
                        argsObj["id"]?.ToString(),
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
                            argsObj["graph_mapper_type"]?.ToString() ?? argsObj["graph_type"]?.ToString());
                        if (!result.ToolResult.StartsWith("Error:")) result.AddComp++;
                    }
                }
                else if (funcName == "connect_gh_components")
                {
                    result.ToolResult = ExecuteConnectGhComponents(
                        argsObj["from_id"]?.ToString(),
                        argsObj["from_index"]?.ToObject<int>() ?? 0,
                        argsObj["to_id"]?.ToString(),
                        argsObj["to_index"]?.ToObject<int>() ?? 0);
                    result.AddConn++;
                }
                else if (funcName == "remove_gh_component")
                {
                    result.ToolResult = ExecuteRemoveGhComponent(argsObj["id"]?.ToString());
                    result.DelComp++;
                }
                else if (funcName == "set_gh_component_value")
                {
                    result.ToolResult = ExecuteSetGhComponentValue(
                        argsObj["id"]?.ToString(),
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
                        argsObj["from_id"]?.ToString(),
                        argsObj["from_index"]?.ToObject<int>() ?? 0,
                        argsObj["to_id"]?.ToString(),
                        argsObj["to_index"]?.ToObject<int>() ?? 0);
                    result.DelConn++;
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
                    if (argsObj["components"] is JArray comps) result.AddComp += comps.Count;
                    if (argsObj["connections"] is JArray conns) result.AddConn += conns.Count;
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
                        result.AddComp += 1;
                        if (argsObj["components"] is JArray helperItems) result.AddComp += helperItems.Count;
                    }
                }
                else if (funcName == "edit_csharp_script_component")
                {
                    result.ToolResult = ExecuteEditCSharpScriptComponent(
                        argsObj["id"]?.ToString(),
                        argsObj["mode"]?.ToString(),
                        argsObj["body"]?.ToString());
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
                        if (argsObj["scripts"] is JArray scriptItems) result.AddComp += scriptItems.Count;
                        if (argsObj["components"] is JArray helperItems) result.AddComp += helperItems.Count;
                        if (argsObj["connections"] is JArray connectionItems) result.AddConn += connectionItems.Count;
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
                else if (funcName == "set_gh_component_status")
                {
                    result.ToolResult = ExecuteSetGhComponentStatus(
                        argsObj["id"]?.ToString(),
                        ReadNullableBool(argsObj, "preview"),
                        ReadNullableBool(argsObj, "enabled"));
                }
                else if (funcName == "modify_gh_component_ports")
                {
                    result.ToolResult = ExecuteModifyGhComponentPorts(
                        argsObj["id"]?.ToString(),
                        argsObj["is_input"]?.ToObject<bool>() ?? false,
                        argsObj["action"]?.ToString(),
                        argsObj["port_name"]?.ToString(),
                        argsObj["index"]?.ToObject<int?>());
                }
                else if (funcName == "modify_gh_port_data")
                {
                    result.ToolResult = ExecuteModifyGhPortData(
                        argsObj["id"]?.ToString(),
                        argsObj["is_input"]?.ToObject<bool>() ?? false,
                        argsObj["index"]?.ToObject<int>() ?? 0,
                        argsObj["operation"]?.ToString());
                }
                else if (funcName == "manage_gh_groups")
                {
                    string groupId = argsObj["group_id"]?.ToString();
                    string groupName = argsObj["name"]?.ToString();
                    JArray idsArray = argsObj["ids"] as JArray;
                    List<string> idsList = idsArray?.Select(v => v.ToString()).ToList();
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
            }
            catch (Exception ex)
            {
                result.ToolResult = "Error: " + ex.Message;
                AddGhLog.Error("Tool dispatch failed: " + (funcName ?? "?"), ex);
            }

            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ADDGH
{
    public static partial class ChatWindow
    {
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
                ExecuteSynchronousToolCallCore(
                    result,
                    funcName,
                    argsObj,
                    argsJson,
                    callId,
                    fullContent,
                    fullReasoning,
                    operationCards);
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

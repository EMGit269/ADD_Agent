using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ADDGH.Agent
{
    public static class ToolResultCompactor
    {
        private const int SummaryMaxChars = 420;

        public static ToolResultEnvelope BuildEnvelope(string toolName, string rawResult)
        {
            var envelope = ToolResultEnvelope.Empty(toolName);
            envelope.RawCharCount = rawResult == null ? 0 : rawResult.Length;
            envelope.TimestampUtc = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(rawResult))
            {
                envelope.Summary = "No tool result content.";
                return envelope;
            }

            string trimmed = rawResult.Trim();
            envelope.Success = !trimmed.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
            envelope.ResultKind = trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal)
                ? "json"
                : "text";

            if (TrySummarizeJson(trimmed, envelope))
                return envelope;

            envelope.Summary = Compact(trimmed, SummaryMaxChars);
            return envelope;
        }

        private static bool TrySummarizeJson(string rawResult, ToolResultEnvelope envelope)
        {
            try
            {
                var token = JToken.Parse(rawResult);
                if (token is JObject obj)
                {
                    envelope.Success = InferJsonSuccess(obj, envelope.Success);
                    envelope.ArtifactPath = FirstString(obj, "path", "file_path", "output_path", "image_path", "snapshot_path");
                    envelope.Summary = SummarizeObject(obj);
                    return true;
                }

                if (token is JArray arr)
                {
                    envelope.Summary = "JSON array result with " + arr.Count + " item(s).";
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool InferJsonSuccess(JObject obj, bool fallback)
        {
            string status = FirstString(obj, "status", "result", "ok");
            if (string.IsNullOrWhiteSpace(status))
                return fallback && string.IsNullOrWhiteSpace(FirstString(obj, "error", "message_error"));

            status = status.Trim().ToLowerInvariant();
            if (status == "ok" || status == "success" || status == "true") return true;
            if (status == "error" || status == "failed" || status == "false") return false;
            return fallback;
        }

        private static string SummarizeObject(JObject obj)
        {
            var parts = new List<string>();
            AddPart(parts, obj, "status");
            AddPart(parts, obj, "message");
            AddPart(parts, obj, "error");
            AddPart(parts, obj, "file_name");
            AddPart(parts, obj, "path");
            AddPart(parts, obj, "created_components");
            AddPart(parts, obj, "created_connections");
            AddPart(parts, obj, "created_scripts");
            AddPart(parts, obj, "imported_count");
            AddPart(parts, obj, "source_object_count");
            AddPart(parts, obj, "component_count");
            AddPart(parts, obj, "errors_count");
            AddPart(parts, obj, "warnings_count");

            if (parts.Count == 0)
            {
                var props = obj.Properties().Take(8).Select(p => p.Name + "=" + Compact(TokenPreview(p.Value), 80));
                parts.AddRange(props);
            }

            return Compact(string.Join("; ", parts), SummaryMaxChars);
        }

        private static void AddPart(List<string> parts, JObject obj, string key)
        {
            if (parts == null || obj == null || string.IsNullOrWhiteSpace(key)) return;
            var value = obj[key];
            if (value == null || value.Type == JTokenType.Null) return;
            string preview = TokenPreview(value);
            if (!string.IsNullOrWhiteSpace(preview))
                parts.Add(key + "=" + Compact(preview, 120));
        }

        private static string FirstString(JObject obj, params string[] keys)
        {
            if (obj == null || keys == null) return "";
            foreach (string key in keys)
            {
                var value = obj[key];
                if (value == null || value.Type == JTokenType.Null) continue;
                string s = value.Type == JTokenType.String ? value.ToString() : value.ToString(Newtonsoft.Json.Formatting.None);
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            return "";
        }

        private static string TokenPreview(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "";
            if (token.Type == JTokenType.String) return token.ToString();
            if (token is JArray arr) return "array[" + arr.Count + "]";
            if (token is JObject obj) return "object{" + obj.Properties().Count() + "}";
            return token.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string Compact(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var s = string.Join(" ", value.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            if (maxChars <= 0 || s.Length <= maxChars) return s;
            return s.Substring(0, Math.Max(0, maxChars - 3)) + "...";
        }
    }
}

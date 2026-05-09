using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ADDGH
{
    /// <summary>
    /// 与 UI 无关的消息压缩、参数解析与历史裁剪逻辑，便于单测与复用。
    /// </summary>
    public static class ChatMessageHelpers
    {
        public static List<object> ProjectMessagesForSend(IList<object> messages)
        {
            if (messages == null || messages.Count == 0)
                return new List<object>();
            return CompressMessages(new List<object>(messages));
        }

        public static List<object> CompressMessages(List<object> fullMessages)
        {
            var compressed = new List<object>();
            int lastCanvasStateIndex = FindLastGetGhComponentsIndex(fullMessages);
            for (int i = 0; i < fullMessages.Count; i++)
            {
                var msg = fullMessages[i];
                if (IsGetGhToolMessage(msg) && i != lastCanvasStateIndex)
                {
                    compressed.Add(CloneToolPlaceholder(msg, "get_gh_components", "[历史画布状态已折叠以节省 Token]"));
                }
                else
                {
                    compressed.Add(msg);
                }
            }
            return compressed;
        }

        /// <summary>就地折叠历史 get_gh_components（摘要失败时的机械回退）。</summary>
        public static void ApplyGetGhComponentsFoldInPlace(IList<object> messages)
        {
            if (messages == null || messages.Count == 0) return;
            int lastIdx = FindLastGetGhComponentsIndex(messages);
            if (lastIdx < 0) return;
            for (int i = 0; i < messages.Count; i++)
            {
                if (i == lastIdx || !IsGetGhToolMessage(messages[i])) continue;
                ReplaceToolContentInPlace(messages, i, "[历史画布状态已折叠以节省 Token]");
            }
        }

        /// <summary>
        /// 对体积过大的 tool 结果就地折叠，每种 function name 仅保留最后一次「大」payload。
        /// </summary>
        public static void ApplyLargeToolFoldInPlace(IList<object> messages, int minChars = 0)
        {
            if (minChars <= 0) minChars = DeploymentOptions.LargeToolFoldMinChars;
            if (messages == null || messages.Count == 0) return;
            var lastLargeByName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < messages.Count; i++)
            {
                if (!IsToolMessage(messages[i], out string name, out _)) continue;
                int len = TryGetToolContentLength(messages[i]);
                if (len < minChars) continue;
                lastLargeByName[name] = i;
            }
            for (int i = 0; i < messages.Count; i++)
            {
                if (!IsToolMessage(messages[i], out string name, out _)) continue;
                if (!lastLargeByName.TryGetValue(name, out int keep) || keep == i) continue;
                int len = TryGetToolContentLength(messages[i]);
                if (len < minChars) continue;
                ReplaceToolContentInPlace(messages, i, "[大型工具输出已折叠以节省 Token]");
            }
        }

        public static void ApplyMechanicalContextReductionInPlace(IList<object> messages)
        {
            ApplyGetGhComponentsFoldInPlace(messages);
            ApplyLargeToolFoldInPlace(messages, DeploymentOptions.LargeToolFoldMinChars);
        }

        public static int EstimateMessageListTokens(IList<object> messages)
        {
            if (messages == null || messages.Count == 0) return 0;
            try
            {
                string json = JsonConvert.SerializeObject(messages);
                return Math.Max(1, json.Length / 3);
            }
            catch
            {
                return messages.Count * 200;
            }
        }

        public static int EstimateProjectedMessageListTokens(IList<object> messages)
        {
            return EstimateMessageListTokens(ProjectMessagesForSend(messages));
        }

        /// <summary>Tier2 起始下标之后的消息估算 tokens（不含系统前缀与可选的 Tier1 摘要头）。用于 UI 圆环显示「本轮对话」增长。</summary>
        public static int EstimateTier2TailTokens(IList<object> messages)
        {
            if (messages == null || messages.Count == 0) return 0;
            GetTierBoundaries(messages, out _, out int tier2Start, out _);
            if (tier2Start >= messages.Count) return 0;
            var tail = new List<object>(messages.Count - tier2Start);
            for (int i = tier2Start; i < messages.Count; i++)
                tail.Add(messages[i]);
            return EstimateMessageListTokens(tail);
        }

        public static int EstimateProjectedTier2TailTokens(IList<object> messages)
        {
            return EstimateTier2TailTokens(ProjectMessagesForSend(messages));
        }

        /// <summary>系统前缀与可选 Tier1 摘要的估算 tokens。</summary>
        public static int EstimateTierPrefixTokens(IList<object> messages)
        {
            if (messages == null || messages.Count == 0) return 0;
            GetTierBoundaries(messages, out _, out int tier2Start, out _);
            if (tier2Start <= 0) return 0;
            var prefix = new List<object>(tier2Start);
            for (int i = 0; i < tier2Start; i++)
                prefix.Add(messages[i]);
            return EstimateMessageListTokens(prefix);
        }

        public static int EstimateProjectedTierPrefixTokens(IList<object> messages)
        {
            return EstimateTierPrefixTokens(ProjectMessagesForSend(messages));
        }

        /// <summary>Tier0 结束下标（第一条非 system）；Tier1 为可选的一条「摘要」assistant；Tier2 起始于 tier2Start。</summary>
        public static void GetTierBoundaries(IList<object> messages, out int tier0End, out int tier2Start, out bool hasTier1Summary)
        {
            tier0End = CountLeadingSystemMessages(messages);
            hasTier1Summary = false;
            tier2Start = tier0End;
            if (tier0End < messages.Count && IsRollingSummaryTier1Message(messages[tier0End], out _))
            {
                hasTier1Summary = true;
                tier2Start = tier0End + 1;
            }
        }

        public static bool IsRollingSummaryTier1Message(object msg, out string bodyAfterHeader)
        {
            bodyAfterHeader = null;
            string role = TryGetRole(msg);
            if (!string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)) return false;
            string content = TryGetPlainTextContent(msg);
            if (string.IsNullOrEmpty(content) || !content.StartsWith(DeploymentOptions.RollingSummaryHeader, StringComparison.Ordinal))
                return false;
            bodyAfterHeader = content.Substring(DeploymentOptions.RollingSummaryHeader.Length).TrimStart();
            return true;
        }

        /// <summary>在 Tier2 内选取可安全截断的下标；从 maxTail 开始逐步收紧保留条数直至得到有效 cut。</summary>
        public static bool TryFindSummaryCutExclusive(IList<object> messages, int tier2Start, int maxVerbatimTail, out int cutExclusive)
        {
            cutExclusive = messages?.Count ?? 0;
            if (messages == null || tier2Start >= messages.Count) return false;
            for (int tail = maxVerbatimTail; tail >= 1; tail--)
            {
                int cut = FindSummaryCutExclusive(messages, tier2Start, tail);
                if (cut < messages.Count && cut > tier2Start)
                {
                    cutExclusive = cut;
                    return true;
                }
            }
            return false;
        }

        /// <summary>在 Tier2 内选取可安全截断的下标，使 [cutExclusive, Count) 在 tool_call 对上自洽。</summary>
        public static int FindSummaryCutExclusive(IList<object> messages, int tier2Start, int verbatimTailCount)
        {
            if (messages == null || tier2Start >= messages.Count) return messages?.Count ?? 0;
            int cut = messages.Count - Math.Max(0, verbatimTailCount);
            if (cut <= tier2Start) return messages.Count;
            while (cut > tier2Start && !IsValidToolSuffix(messages, cut))
                cut--;
            for (int iter = 0; iter < 8; iter++)
            {
                bool changed = false;
                while (cut < messages.Count && string.Equals(TryGetRole(messages[cut]), "tool", StringComparison.OrdinalIgnoreCase))
                {
                    cut--;
                    if (cut <= tier2Start) return messages.Count;
                    changed = true;
                }
                while (cut < messages.Count && !string.Equals(TryGetRole(messages[cut]), "user", StringComparison.OrdinalIgnoreCase))
                {
                    cut--;
                    if (cut <= tier2Start) return messages.Count;
                    changed = true;
                }
                while (cut > tier2Start && !IsValidToolSuffix(messages, cut))
                {
                    cut--;
                    changed = true;
                }
                if (!changed) break;
            }
            return cut;
        }

        public static string FlattenMessagesForSummary(IList<object> messages, int fromInclusive, int toExclusive, int maxChars)
        {
            if (messages == null || fromInclusive >= toExclusive) return "";
            var sb = new StringBuilder();
            for (int i = fromInclusive; i < toExclusive && i < messages.Count; i++)
            {
                AppendOneMessageForSummary(sb, messages[i]);
                if (sb.Length >= maxChars) break;
            }
            if (sb.Length > maxChars)
                return sb.ToString(0, maxChars) + "\n[…下文已截断…]";
            return sb.ToString();
        }

        /// <summary>
        /// 保留开头的连续 system 消息，之后若存在 Tier1 摘要则保留；再之后按消息组删除最旧条目直至数量不超过上限。
        /// </summary>
        public static void TrimMessageHistory(IList<object> messages, int maxCount)
        {
            if (messages == null || messages.Count <= maxCount) return;

            int systemPrefix = CountLeadingSystemMessages(messages);
            int idx = systemPrefix;
            if (idx < messages.Count && IsRollingSummaryTier1Message(messages[idx], out _))
                idx++;

            while (messages.Count > maxCount && idx < messages.Count)
            {
                int end = EndExclusiveMessageGroup(messages, idx);
                if (end <= idx) { messages.RemoveAt(idx); break; }
                for (int k = end - 1; k >= idx; k--)
                    messages.RemoveAt(k);
            }
        }

        public static int CountLeadingSystemMessages(IList<object> messages)
        {
            int n = 0;
            for (int i = 0; i < messages.Count; i++)
            {
                if (!string.Equals(TryGetRole(messages[i]), "system", StringComparison.OrdinalIgnoreCase))
                    break;
                n++;
            }
            return n;
        }

        public static JObject ParseToolArgumentsForExecution(string argsJson, out string cardSummary, out string cardSummaryDetail)
        {
            cardSummary = null;
            cardSummaryDetail = null;
            JObject o;
            try
            {
                o = string.IsNullOrWhiteSpace(argsJson) ? new JObject() : JObject.Parse(argsJson);
            }
            catch (Exception ex)
            {
                AddGhLog.Warn("ParseToolArgumentsForExecution invalid JSON: " + ex.Message);
                o = new JObject();
            }

            JToken st = o["summary"];
            if (st != null && st.Type != JTokenType.Null) cardSummary = st.ToString().Trim();
            JToken sd = o["summary_detail"];
            if (sd != null && sd.Type != JTokenType.Null) cardSummaryDetail = sd.ToString().Trim();
            o.Remove("summary");
            o.Remove("summary_detail");
            return o;
        }

        private static int FindLastGetGhComponentsIndex(IList<object> fullMessages)
        {
            for (int i = fullMessages.Count - 1; i >= 0; i--)
            {
                if (IsGetGhToolMessage(fullMessages[i]))
                    return i;
            }
            return -1;
        }

        private static bool IsGetGhToolMessage(object msg)
        {
            return IsToolMessage(msg, out string name, out _) && name == "get_gh_components";
        }

        private static bool IsToolMessage(object msg, out string name, out string toolCallId)
        {
            name = null;
            toolCallId = null;
            if (msg is JObject j)
            {
                if (!string.Equals(j["role"]?.ToString(), "tool", StringComparison.OrdinalIgnoreCase)) return false;
                name = j["name"]?.ToString();
                toolCallId = j["tool_call_id"]?.ToString();
                return true;
            }
            var type = msg?.GetType();
            var rp = type?.GetProperty("role");
            if (rp?.GetValue(msg)?.ToString() != "tool") return false;
            name = type.GetProperty("name")?.GetValue(msg)?.ToString();
            toolCallId = type.GetProperty("tool_call_id")?.GetValue(msg)?.ToString();
            return true;
        }

        private static object CloneToolPlaceholder(object sourceMsg, string name, string placeholder)
        {
            if (sourceMsg is JObject j)
            {
                return new JObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = j["tool_call_id"],
                    ["name"] = name,
                    ["content"] = placeholder
                };
            }
            string id = sourceMsg?.GetType().GetProperty("tool_call_id")?.GetValue(sourceMsg)?.ToString();
            return new { role = "tool", tool_call_id = id, name, content = placeholder };
        }

        private static void ReplaceToolContentInPlace(IList<object> list, int index, string newContent)
        {
            object msg = list[index];
            if (msg is JObject j)
            {
                j["content"] = newContent;
                return;
            }
            var type = msg?.GetType();
            var contentProp = type?.GetProperty("content");
            if (contentProp != null && contentProp.CanWrite)
            {
                try
                {
                    contentProp.SetValue(msg, newContent, null);
                    return;
                }
                catch { /* fall through replace whole object */ }
            }
            string id = type?.GetProperty("tool_call_id")?.GetValue(msg)?.ToString();
            string name = type?.GetProperty("name")?.GetValue(msg)?.ToString();
            list[index] = new { role = "tool", tool_call_id = id, name, content = newContent };
        }

        private static int TryGetToolContentLength(object msg)
        {
            string c = TryGetToolContentString(msg);
            return c?.Length ?? 0;
        }

        private static string TryGetToolContentString(object msg)
        {
            if (msg is JObject j) return j["content"]?.ToString();
            return msg?.GetType().GetProperty("content")?.GetValue(msg)?.ToString();
        }

        private static bool IsValidToolSuffix(IList<object> messages, int cutExclusive)
        {
            for (int i = cutExclusive; i < messages.Count; i++)
            {
                if (!IsToolMessage(messages[i], out _, out string tid)) continue;
                if (string.IsNullOrEmpty(tid)) return false;
                bool ok = false;
                for (int j = i - 1; j >= cutExclusive; j--)
                {
                    if (!string.Equals(TryGetRole(messages[j]), "assistant", StringComparison.OrdinalIgnoreCase)) continue;
                    if (AssistantHasToolCallId(messages[j], tid)) { ok = true; break; }
                }
                if (!ok) return false;
            }
            return true;
        }

        private static bool AssistantHasToolCallId(object msg, string toolCallId)
        {
            if (msg is JObject j)
            {
                var arr = j["tool_calls"] as JArray;
                if (arr == null) return false;
                foreach (var t in arr)
                {
                    if (string.Equals(t?["id"]?.ToString(), toolCallId, StringComparison.Ordinal)) return true;
                }
                return false;
            }
            var type = msg?.GetType();
            var tcp = type?.GetProperty("tool_calls");
            if (tcp == null) return false;
            // anonymous / dynamic: best effort via JToken
            try
            {
                var token = JToken.FromObject(tcp.GetValue(msg));
                if (token is JArray ja)
                {
                    foreach (var t in ja)
                    {
                        if (string.Equals(t?["id"]?.ToString(), toolCallId, StringComparison.Ordinal)) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool AssistantHasAnyToolCalls(object msg)
        {
            if (msg is JObject j)
            {
                var arr = j["tool_calls"] as JArray;
                return arr != null && arr.Count > 0;
            }
            var tcp = msg?.GetType().GetProperty("tool_calls");
            if (tcp == null) return false;
            try
            {
                var token = JToken.FromObject(tcp.GetValue(msg));
                return token is JArray ja && ja.Count > 0;
            }
            catch { return false; }
        }

        private static int EndExclusiveMessageGroup(IList<object> m, int start)
        {
            if (start >= m.Count) return start;
            string r = TryGetRole(m[start]);
            if (string.Equals(r, "tool", StringComparison.OrdinalIgnoreCase))
                return start + 1;
            if (string.Equals(r, "user", StringComparison.OrdinalIgnoreCase))
            {
                int i = start + 1;
                if (i < m.Count && string.Equals(TryGetRole(m[i]), "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    if (AssistantHasAnyToolCalls(m[i]))
                    {
                        i++;
                        while (i < m.Count && string.Equals(TryGetRole(m[i]), "tool", StringComparison.OrdinalIgnoreCase))
                            i++;
                    }
                    else i++;
                }
                return i;
            }
            if (string.Equals(r, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                int i = start + 1;
                if (AssistantHasAnyToolCalls(m[start]))
                {
                    while (i < m.Count && string.Equals(TryGetRole(m[i]), "tool", StringComparison.OrdinalIgnoreCase))
                        i++;
                }
                else if (i < m.Count && string.Equals(TryGetRole(m[i]), "tool", StringComparison.OrdinalIgnoreCase))
                    i++;
                return i;
            }
            return start + 1;
        }

        private static void AppendOneMessageForSummary(StringBuilder sb, object msg)
        {
            string role = TryGetRole(msg) ?? "?";
            sb.Append(role.ToUpperInvariant()).Append(": ");
            sb.AppendLine(FlattenContentForSummary(msg));
            sb.AppendLine();
        }

        private static string FlattenContentForSummary(object msg)
        {
            if (msg is JObject j)
            {
                var content = j["content"];
                return FlattenTokenContent(content);
            }
            var prop = msg?.GetType().GetProperty("content");
            var val = prop?.GetValue(msg);
            if (val is JToken tok) return FlattenTokenContent(tok);
            string s = val?.ToString();
            if (string.IsNullOrEmpty(s)) return "";
            return StripBase64Like(s);
        }

        private static string FlattenTokenContent(JToken content)
        {
            if (content == null || content.Type == JTokenType.Null) return "";
            if (content.Type == JTokenType.String) return StripBase64Like(content.ToString());
            if (content is JArray arr)
            {
                var sb = new StringBuilder();
                foreach (var part in arr)
                {
                    string t = part["type"]?.ToString();
                    if (string.Equals(t, "text", StringComparison.OrdinalIgnoreCase))
                        sb.Append(part["text"]?.ToString());
                    else if (string.Equals(t, "image_url", StringComparison.OrdinalIgnoreCase))
                        sb.Append("[图片]");
                    else
                        sb.Append(part.ToString(Formatting.None));
                }
                return StripBase64Like(sb.ToString());
            }
            return StripBase64Like(content.ToString(Formatting.None));
        }

        private static string StripBase64Like(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 200) return s;
            if (s.IndexOf("data:image", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase) >= 0)
                return "[含 base64 图片或附件，已省略]";
            return s.Length > DeploymentOptions.SummaryRequestMaxChars
                ? s.Substring(0, 4096) + "\n[…截断…]"
                : s;
        }

        public static string TryGetPlainTextContent(object msg)
        {
            if (msg is JObject j) return FlattenTokenContent(j["content"]);
            return FlattenContentForSummary(msg);
        }

        public static string TryGetRole(object msg)
        {
            if (msg is JObject jo) return jo["role"]?.ToString();
            var type = msg?.GetType();
            var rp = type?.GetProperty("role");
            return rp?.GetValue(msg)?.ToString();
        }
    }
}

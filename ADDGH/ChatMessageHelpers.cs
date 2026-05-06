using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ADDGH
{
    /// <summary>
    /// 与 UI 无关的消息压缩、参数解析与历史裁剪逻辑，便于单测与复用。
    /// </summary>
    public static class ChatMessageHelpers
    {
        public static List<object> CompressMessages(List<object> fullMessages)
        {
            var compressed = new List<object>();
            int lastCanvasStateIndex = -1;

            for (int i = fullMessages.Count - 1; i >= 0; i--)
            {
                var msg = fullMessages[i] as JObject;
                if (msg == null)
                {
                    var type = fullMessages[i].GetType();
                    var roleProp = type.GetProperty("role");
                    var nameProp = type.GetProperty("name");
                    if (roleProp != null && nameProp != null)
                    {
                        string role = roleProp.GetValue(fullMessages[i])?.ToString();
                        string name = nameProp.GetValue(fullMessages[i])?.ToString();
                        if (role == "tool" && name == "get_gh_components")
                        {
                            lastCanvasStateIndex = i;
                            break;
                        }
                    }
                }
                else
                {
                    string role = msg["role"]?.ToString();
                    string name = msg["name"]?.ToString();
                    if (role == "tool" && name == "get_gh_components")
                    {
                        lastCanvasStateIndex = i;
                        break;
                    }
                }
            }

            for (int i = 0; i < fullMessages.Count; i++)
            {
                var msg = fullMessages[i];
                bool isCanvasState = false;

                var jmsg = msg as JObject;
                if (jmsg == null)
                {
                    var type = msg.GetType();
                    var roleProp = type.GetProperty("role");
                    var nameProp = type.GetProperty("name");
                    if (roleProp != null && nameProp != null)
                    {
                        string role = roleProp.GetValue(msg)?.ToString();
                        string name = nameProp.GetValue(msg)?.ToString();
                        if (role == "tool" && name == "get_gh_components") isCanvasState = true;
                    }
                }
                else
                {
                    string role = jmsg["role"]?.ToString();
                    string name = jmsg["name"]?.ToString();
                    if (role == "tool" && name == "get_gh_components") isCanvasState = true;
                }

                if (isCanvasState && i != lastCanvasStateIndex)
                {
                    compressed.Add(new
                    {
                        role = "tool",
                        tool_call_id = jmsg != null ? jmsg["tool_call_id"]?.ToString() : msg.GetType().GetProperty("tool_call_id")?.GetValue(msg)?.ToString(),
                        name = "get_gh_components",
                        content = "[历史画布状态已折叠以节省 Token]"
                    });
                }
                else
                {
                    compressed.Add(msg);
                }
            }

            return compressed;
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

        /// <summary>
        /// 保留开头的连续 system 消息，删除其后的最旧条目直至数量不超过上限。
        /// </summary>
        public static void TrimMessageHistory(IList<object> messages, int maxCount)
        {
            if (messages == null || messages.Count <= maxCount) return;

            int systemPrefix = CountLeadingSystemMessages(messages);
            while (messages.Count > maxCount && messages.Count > systemPrefix)
                messages.RemoveAt(systemPrefix);
        }

        private static int CountLeadingSystemMessages(IList<object> messages)
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

        private static string TryGetRole(object msg)
        {
            if (msg is JObject jo) return jo["role"]?.ToString();
            var type = msg?.GetType();
            var rp = type?.GetProperty("role");
            return rp?.GetValue(msg)?.ToString();
        }
    }
}

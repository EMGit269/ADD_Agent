using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Grasshopper.Kernel;
using Newtonsoft.Json.Linq;

namespace ADDGH
{
    public static partial class ChatWindow
    {
        private sealed class CanvasUndoRecord
        {
            public string UndoId { get; set; }
            public string ToolName { get; set; }
            public string ToolCallId { get; set; }
            public string Summary { get; set; }
            public string SnapshotPath { get; set; }
            public DateTime CreatedAtUtc { get; set; }
            public bool IsUndone { get; set; }
            public Button UndoButton { get; set; }
        }

        private static readonly List<CanvasUndoRecord> _canvasUndoStack = new List<CanvasUndoRecord>();
        private const int MaxCanvasUndoRecords = 30;

        private static bool IsCanvasMutatingTool(string funcName)
        {
            switch (funcName ?? "")
            {
                case "ensure_gh_canvas":
                case "add_gh_component":
                case "connect_gh_components":
                case "remove_gh_component":
                case "set_gh_component_value":
                case "remove_gh_connection":
                case "create_component_graph":
                case "create_csharp_script_component":
                case "edit_csharp_script_component":
                case "create_script_component_graph":
                case "import_reference_gh":
                case "set_gh_component_status":
                case "set_all_csharp_script_previews":
                case "modify_gh_component_ports":
                case "modify_gh_port_data":
                case "manage_gh_groups":
                    return true;
                default:
                    return false;
            }
        }

        private static string CreateCanvasUndoSnapshot(string funcName, string callId)
        {
            string path = null;
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                if (doc == null) return;

                string dir = Path.Combine(GetProjectRootDirectory(), ".addgh", "undo");
                Directory.CreateDirectory(dir);
                string safeName = SanitizeUndoFilePart(funcName) + "_" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".gh";
                string target = Path.Combine(dir, safeName);

                var io = new GH_DocumentIO();
                io.Document = doc;
                io.SaveQuiet(target);
                if (File.Exists(target))
                    path = target;
            }));
            return path;
        }

        private static string SanitizeUndoFilePart(string value)
        {
            string text = string.IsNullOrWhiteSpace(value) ? "tool" : value.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                text = text.Replace(c, '_');
            return text.Length > 40 ? text.Substring(0, 40) : text;
        }

        private static string RegisterCanvasUndoRecord(string funcName, string callId, string snapshotPath, string toolResult)
        {
            if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
            {
                if (IsCanvasMutatingTool(funcName))
                    AddGhLog.Warn("Canvas undo snapshot missing for tool: " + (funcName ?? "?"));
                return null;
            }
            if (string.IsNullOrWhiteSpace(toolResult) || toolResult.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) return null;

            var record = new CanvasUndoRecord
            {
                UndoId = Guid.NewGuid().ToString("N"),
                ToolName = funcName ?? "",
                ToolCallId = callId ?? "",
                Summary = BuildUndoSummary(funcName, toolResult),
                SnapshotPath = snapshotPath,
                CreatedAtUtc = DateTime.UtcNow
            };

            _canvasUndoStack.Add(record);
            while (_canvasUndoStack.Count > MaxCanvasUndoRecords)
            {
                var old = _canvasUndoStack[0];
                _canvasUndoStack.RemoveAt(0);
                TryDeleteUndoSnapshot(old);
            }
            return record.UndoId;
        }

        private static string BuildUndoSummary(string funcName, string toolResult)
        {
            string name = string.IsNullOrWhiteSpace(funcName) ? "画布操作" : funcName.Trim();
            string detail = (toolResult ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (detail.Length > 90) detail = detail.Substring(0, 90) + "...";
            return string.IsNullOrWhiteSpace(detail) ? name : name + " · " + detail;
        }

        private static List<CanvasUndoRecord> GetUndoRecordsFrom(CanvasUndoRecord selected)
        {
            var records = new List<CanvasUndoRecord>();
            if (selected == null) return records;

            int start = _canvasUndoStack.IndexOf(selected);
            if (start < 0) return records;
            for (int i = start; i < _canvasUndoStack.Count; i++)
            {
                var record = _canvasUndoStack[i];
                if (record != null && !record.IsUndone)
                    records.Add(record);
            }
            return records;
        }

        private static CanvasUndoRecord FindLatestUndoableRecord()
        {
            for (int i = _canvasUndoStack.Count - 1; i >= 0; i--)
            {
                var record = _canvasUndoStack[i];
                if (record != null && !record.IsUndone)
                    return record;
            }
            return null;
        }

        private static void AttachUndoButtonToStatsCard(Border card, string undoId)
        {
            var record = _canvasUndoStack.FirstOrDefault(r => r != null && r.UndoId == undoId);
            if (record == null || card == null || record.UndoButton != null) return;

            var grid = card.Child as Grid;
            if (grid == null) return;

            if (grid.ColumnDefinitions.Count < 3)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var button = new Button
            {
                Content = "撤销",
                Tag = record.UndoId,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(210, 210, 210)),
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(10, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Content = "↶ 撤销";
            button.FontSize = 11;
            button.Padding = new Thickness(9, 3, 9, 3);
            button.Margin = new Thickness(12, 0, 0, 0);
            button.Content = "↶ 撤销";
            button.Template = BuildSmallUndoButtonTemplate();
            button.Click += (s, e) => TryUndoCanvasOperation(record.UndoId);

            Grid.SetColumn(button, 2);
            grid.Children.Add(button);
            record.UndoButton = button;
        }

        private static void AttachUnavailableUndoButtonToStatsCard(Border card)
        {
            if (card == null) return;
            var grid = card.Child as Grid;
            if (grid == null) return;

            if (grid.ColumnDefinitions.Count < 3)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var button = new Button
            {
                Content = "不可撤销",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130)),
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = false,
                ToolTip = "这条统计没有可用的撤销快照，常见于历史会话刷新、工具失败或快照创建失败。"
            };
            button.Template = BuildSmallUndoButtonTemplate();

            Grid.SetColumn(button, 2);
            grid.Children.Add(button);
        }

        private static ControlTemplate BuildSmallUndoButtonTemplate()
        {
            const string xaml =
                @"<ControlTemplate TargetType=""Button"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
                    <Border x:Name=""Bd"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""6"" Padding=""{TemplateBinding Padding}"">
                        <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""Bd"" Property=""Background"" Value=""#2A2A2A""/>
                        </Trigger>
                        <Trigger Property=""IsEnabled"" Value=""False"">
                            <Setter Property=""Opacity"" Value=""0.55""/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>";
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        private static void TryUndoCanvasOperation(string undoId)
        {
            var record = _canvasUndoStack.FirstOrDefault(r => r != null && r.UndoId == undoId);
            if (record == null || record.IsUndone) return;

            var affectedRecords = GetUndoRecordsFrom(record);
            if (affectedRecords.Count == 0) return;

            var historyConfirm = System.Windows.MessageBox.Show(
                "将强制回滚 Grasshopper 画布到这次操作执行前的状态。\n\n这会覆盖此操作之后的手动改动，并使这次及之后的 agent 画布操作都视为已撤销。\n\n是否继续？",
                "撤销历史 agent 画布操作",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (historyConfirm != MessageBoxResult.OK) return;

            string historyError = RestoreCanvasUndoSnapshot(record);
            if (!string.IsNullOrWhiteSpace(historyError))
            {
                AppendQuietDiagnosticCard("撤销画布操作", "回滚失败：" + historyError);
                return;
            }

            foreach (var affected in affectedRecords)
            {
                affected.IsUndone = true;
                if (affected.UndoButton != null)
                {
                    affected.UndoButton.Content = "已撤销";
                    affected.UndoButton.IsEnabled = false;
                }
                PruneMessagesForUndoneTool(affected.ToolCallId);
            }

            _messages.Add(new JObject
            {
                ["role"] = "system",
                ["content"] = "用户撤销了一次历史画布操作，当前 Grasshopper 画布已回退到所选操作执行前的状态；该操作及之后的 agent 画布操作不再可作为当前上下文依据。"
            });
            EnforceChatHistoryLimit();
            SyncActiveHistoryConversation();
            NotifyCanvasConversationChanged(true);
            AppendSystemMessage("已撤销历史画布操作：" + record.Summary);
            if (affectedRecords.Count >= 0) return;

            var latest = FindLatestUndoableRecord();
            if (!ReferenceEquals(record, latest))
            {
                AppendQuietDiagnosticCard("撤销画布操作", "只能从最近一次 agent 画布操作开始撤销。请先撤销更新的操作。");
                return;
            }

            var confirm = System.Windows.MessageBox.Show(
                "将强制回滚 Grasshopper 画布到该操作执行前的状态，可能覆盖此后你手动修改的画布内容。\n\n是否继续？",
                "撤销 agent 画布操作",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;

            string error = RestoreCanvasUndoSnapshot(record);
            if (!string.IsNullOrWhiteSpace(error))
            {
                AppendQuietDiagnosticCard("撤销画布操作", "回滚失败：" + error);
                return;
            }

            record.IsUndone = true;
            if (record.UndoButton != null)
            {
                record.UndoButton.Content = "已撤销";
                record.UndoButton.Content = "已撤销";
                record.UndoButton.IsEnabled = false;
            }

            PruneMessagesForUndoneTool(record.ToolCallId);
            _messages.Add(new JObject
            {
                ["role"] = "system",
                ["content"] = "用户撤销了一次画布操作，当前 Grasshopper 画布已回退到撤销后的状态。"
            });
            EnforceChatHistoryLimit();
            SyncActiveHistoryConversation();
            NotifyCanvasConversationChanged(true);
            AppendSystemMessage("已撤销：" + record.Summary);
        }

        private static string RestoreCanvasUndoSnapshot(CanvasUndoRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.SnapshotPath) || !File.Exists(record.SnapshotPath))
                return "撤销快照不存在。";

            string error = null;
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                try
                {
                    var io = new GH_DocumentIO();
                    if (!io.Open(record.SnapshotPath))
                    {
                        error = "无法打开撤销快照。";
                        return;
                    }

                    var restored = io.Document;
                    if (restored == null)
                    {
                        error = "撤销快照没有有效 GH 文档。";
                        return;
                    }

                    var server = Grasshopper.Instances.DocumentServer;
                    var activeCanvas = Grasshopper.Instances.ActiveCanvas;
                    var current = activeCanvas?.Document;
                    if (server != null && current != null)
                    {
                        try { server.RemoveDocument(current); } catch { }
                    }
                    if (server != null)
                    {
                        try { server.AddDocument(restored); } catch { }
                    }
                    if (activeCanvas != null)
                    {
                        activeCanvas.Document = restored;
                        activeCanvas.Refresh();
                    }

                    ResetPublicIdMap(restored);
                    RefreshPublicIdMap(restored);
                    _canvasChanged = true;
                    _cachedCanvasState = null;
                    try { restored.ScheduleSolution(80); } catch { }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }));
            return error;
        }

        private static void PruneMessagesForUndoneTool(string toolCallId)
        {
            if (string.IsNullOrWhiteSpace(toolCallId) || _messages == null) return;

            for (int i = _messages.Count - 1; i >= 0; i--)
            {
                var jo = _messages[i] as JObject ?? JObject.FromObject(_messages[i]);
                string role = jo["role"]?.ToString();
                if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(jo["tool_call_id"]?.ToString(), toolCallId, StringComparison.Ordinal))
                {
                    _messages.RemoveAt(i);
                    break;
                }
            }

            for (int i = _messages.Count - 1; i >= 0; i--)
            {
                var jo = _messages[i] as JObject ?? JObject.FromObject(_messages[i]);
                if (!string.Equals(jo["role"]?.ToString(), "assistant", StringComparison.OrdinalIgnoreCase)) continue;
                var calls = jo["tool_calls"] as JArray;
                if (calls == null) continue;

                bool removed = false;
                for (int j = calls.Count - 1; j >= 0; j--)
                {
                    if (string.Equals(calls[j]?["id"]?.ToString(), toolCallId, StringComparison.Ordinal))
                    {
                        calls.RemoveAt(j);
                        removed = true;
                    }
                }

                string content = jo["content"]?.ToString();
                string reasoning = jo["reasoning_content"]?.ToString();
                if (removed && calls.Count == 0 && string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(reasoning))
                    _messages.RemoveAt(i);
                if (removed) break;
            }
        }

        private static void TryDeleteUndoSnapshot(CanvasUndoRecord record)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(record?.SnapshotPath) && File.Exists(record.SnapshotPath))
                    File.Delete(record.SnapshotPath);
            }
            catch { }
        }
    }
}

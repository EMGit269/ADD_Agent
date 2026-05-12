using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Newtonsoft.Json.Linq;
using WpfPath = System.Windows.Shapes.Path;

namespace ADDGH
{
    public static partial class ChatWindow
    {
        private static void RefreshUI()
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                _chatPanel.Children.Clear();
                foreach (var msg in _messages)
                {
                    var m = msg as JObject;
                    if (m == null) continue;

                    string role = m["role"]?.ToString();
                    if (role == "system") continue;

                    if (role == "user")
                    {
                        AppendBubble(m["content"]?.ToString(), true);
                    }
                    else if (role == "assistant")
                    {
                        string reasoning = m["reasoning_content"]?.ToString();
                        string content = m["content"]?.ToString();

                        if (!string.IsNullOrEmpty(reasoning))
                            AppendCollapsibleBubble(reasoning, "已思考", "💭");
                        if (!string.IsNullOrEmpty(content))
                            AppendBubble(content, false, false);
                    }
                }
                RefreshContextMeter();
            }));
        }

        private static void AppendBubble(string text, bool isUser, bool showHeader = true)
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var container = new StackPanel {
                    Margin = new Thickness(0, 0, 0, 20),
                    HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
                };

                if (showHeader && isUser)
                {
                    var header = new TextBlock {
                        Text = "YOU",
                        Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 0, 0, 6),
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    container.Children.Add(header);
                }

                var bubble = new Border {
                    Padding = new Thickness(0, 5, 0, 10),
                    MaxWidth = 380,
                    HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
                };

                bubble.Child = BuildMarkdownPanel(text, isUser);
                container.Children.Add(bubble);
                if (_thinkingBubble != null) {
                    _chatPanel.Children.Remove(_thinkingBubble);
                    _chatPanel.Children.Add(container);
                    _chatPanel.Children.Add(_thinkingBubble);
                } else {
                    _chatPanel.Children.Add(container);
                }

                var anim = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3));
                container.BeginAnimation(UIElement.OpacityProperty, anim);
                _chatScroll.ScrollToEnd();
            }));
        }

        private static void AppendUserMessageWithAttachments(string text, List<AttachmentItem> attachments)
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var container = new StackPanel {
                    Margin = new Thickness(0, 0, 0, 20),
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                container.Children.Add(new TextBlock
                {
                    Text = "YOU",
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 6),
                    HorizontalAlignment = HorizontalAlignment.Right
                });

                var bubbleContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 380 };
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var bubble = new Border {
                        Padding = new Thickness(0, 5, 0, 10),
                        MaxWidth = 380,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    bubble.Child = BuildMarkdownPanel(text, true);
                    bubbleContent.Children.Add(bubble);
                }

                var cards = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 380 };
                foreach (var attachment in attachments)
                {
                    cards.Children.Add(CreateAttachmentCard(attachment, false));
                }
                bubbleContent.Children.Add(cards);
                container.Children.Add(bubbleContent);

                if (_thinkingBubble != null) {
                    _chatPanel.Children.Remove(_thinkingBubble);
                    _chatPanel.Children.Add(container);
                    _chatPanel.Children.Add(_thinkingBubble);
                } else {
                    _chatPanel.Children.Add(container);
                }

                container.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));
                _chatScroll.ScrollToEnd();
            }));
        }

        private static void AppendCollapsibleBubble(string text, string title, string icon)
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var groupPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 15), HorizontalAlignment = HorizontalAlignment.Left };

                var headerGrid = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(0,0,0,5) };
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var statusIcon = new TextBlock { Text = icon, Foreground = Brushes.Gray, FontSize = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                var headerText = new TextBlock { Text = title, Foreground = Brushes.Gray, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                var toggleIcon = new TextBlock { Text = "▼", Foreground = Brushes.DimGray, FontSize = 9, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

                Grid.SetColumn(statusIcon, 0);
                Grid.SetColumn(headerText, 1);
                Grid.SetColumn(toggleIcon, 2);
                headerGrid.Children.Add(statusIcon);
                headerGrid.Children.Add(headerText);
                headerGrid.Children.Add(toggleIcon);

                var logPanel = new StackPanel { Margin = new Thickness(22, 4, 0, 0), Visibility = Visibility.Collapsed };

                var contentBorder = new Border {
                    Background = new SolidColorBrush(Color.FromRgb(22, 22, 22)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10)
                };

                var content = BuildMarkdownPanel(text, false, true);
                content.MaxHeight = 260;
                contentBorder.Child = content;
                logPanel.Children.Add(contentBorder);

                headerGrid.MouseLeftButtonDown += (s, e) => {
                    if (logPanel.Visibility == Visibility.Visible) {
                        logPanel.Visibility = Visibility.Collapsed;
                        toggleIcon.Text = "▼";
                    } else {
                        logPanel.Visibility = Visibility.Visible;
                        toggleIcon.Text = "▲";
                    }
                };

                groupPanel.Children.Add(headerGrid);
                groupPanel.Children.Add(logPanel);

                if (_thinkingBubble != null) { _chatPanel.Children.Remove(_thinkingBubble); _chatPanel.Children.Add(groupPanel); _chatPanel.Children.Add(_thinkingBubble); }
                else _chatPanel.Children.Add(groupPanel);

                groupPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5)));
                _chatScroll.ScrollToEnd();
            }));
        }

        private static void AppendMarkdownInlines(InlineCollection inlines, string text)
        {
            string[] parts = Regex.Split(text ?? "", @"(\*\*.*?\*\*|`.*?`|\*.*?\*)");
            foreach (var part in parts) {
                if (string.IsNullOrEmpty(part)) continue;
                if (part.StartsWith("**") && part.EndsWith("**") && part.Length >= 4) {
                    inlines.Add(new Bold(new Run(part.Substring(2, part.Length - 4))));
                } else if (part.StartsWith("*") && part.EndsWith("*") && part.Length >= 2) {
                    inlines.Add(new Italic(new Run(part.Substring(1, part.Length - 2))));
                } else if (part.StartsWith("`") && part.EndsWith("`") && part.Length >= 2) {
                    inlines.Add(new Run(part.Substring(1, part.Length - 2)) {
                        FontFamily = new FontFamily("Consolas, Courier New"),
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 100)),
                        Background = new SolidColorBrush(Color.FromRgb(60, 60, 60))
                    });
                } else {
                    inlines.Add(new Run(part));
                }
            }
        }

        private static bool IsMarkdownHorizontalRule(string trimmed)
        {
            if (trimmed.Length < 3) return false;
            char c = trimmed[0];
            if (c != '-' && c != '*' && c != '_') return false;
            for (int k = 0; k < trimmed.Length; k++)
                if (trimmed[k] != c) return false;
            return true;
        }

        private static string[] SplitMarkdownTableRow(string line)
        {
            string t = line.Trim();
            if (!t.Contains("|")) return null;
            string inner = t;
            if (inner.StartsWith("|")) inner = inner.Substring(1);
            if (inner.EndsWith("|")) inner = inner.Substring(0, inner.Length - 1);
            string[] parts = inner.Split('|');
            if (parts.Length < 2) return null;
            return parts.Select(p => p.Trim()).ToArray();
        }

        private static bool IsMarkdownTableSeparatorRow(string[] cells)
        {
            if (cells == null || cells.Length == 0) return false;
            foreach (string cell in cells) {
                string s = cell.Replace(" ", "");
                if (!Regex.IsMatch(s, @"^:?-{3,}:?$")) return false;
            }
            return true;
        }

        private static void AppendMarkdownTable(FlowDocument doc, List<string[]> rows)
        {
            int cols = rows.Max(r => r.Length);
            for (int r = 0; r < rows.Count; r++)
                while (rows[r].Length < cols) {
                    var list = rows[r].ToList();
                    list.Add("");
                    rows[r] = list.ToArray();
                }

            var table = new Table {
                CellSpacing = 0,
                Margin = new Thickness(0, 6, 0, 10)
            };
            for (int c = 0; c < cols; c++)
                table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

            var borderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            var headerBg = new SolidColorBrush(Color.FromRgb(40, 40, 40));

            var rowGroup = new TableRowGroup();
            for (int r = 0; r < rows.Count; r++) {
                var tableRow = new TableRow();
                for (int c = 0; c < cols; c++) {
                    var paragraph = new Paragraph {
                        Margin = new Thickness(0),
                        FontSize = 14,
                        LineHeight = 22,
                        Foreground = new SolidColorBrush(Color.FromRgb(235, 235, 235)),
                        FontWeight = r == 0 ? FontWeights.SemiBold : FontWeights.Normal
                    };
                    AppendMarkdownInlines(paragraph.Inlines, rows[r][c]);
                    var cell = new TableCell(paragraph) {
                        BorderBrush = borderBrush,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8, 6, 8, 6),
                        Background = r == 0 ? headerBg : Brushes.Transparent
                    };
                    tableRow.Cells.Add(cell);
                }
                rowGroup.Rows.Add(tableRow);
            }

            table.RowGroups.Add(rowGroup);
            doc.Blocks.Add(table);
        }

        private static bool TryConsumeMarkdownTable(string[] lines, ref int i, FlowDocument doc)
        {
            int start = i;
            if (start >= lines.Length) return false;
            string firstTrim = lines[start].Trim();
            if (string.IsNullOrEmpty(firstTrim) || firstTrim.StartsWith("```") || !firstTrim.Contains("|")) return false;

            var rows = new List<string[]>();
            int j = start;
            while (j < lines.Length) {
                string raw = lines[j];
                string t = raw.Trim();
                if (string.IsNullOrWhiteSpace(t)) break;
                if (t.StartsWith("```")) break;
                if (!t.Contains("|")) break;
                string[] cells = SplitMarkdownTableRow(lines[j]);
                if (cells == null || cells.Length < 2) break;
                rows.Add(cells);
                j++;
            }

            if (rows.Count < 2 || !IsMarkdownTableSeparatorRow(rows[1])) return false;

            var bodyRows = new List<string[]>();
            bodyRows.Add(rows[0]);
            for (int k = 2; k < rows.Count; k++)
                bodyRows.Add(rows[k]);

            AppendMarkdownTable(doc, bodyRows);
            i = j - 1;
            return true;
        }

        private static string TrimMessageForDisplay(string text)
        {
            return (text ?? "").TrimEnd(' ', '\t', '\r', '\n', '\u00A0');
        }

        private static RichTextBox BuildMarkdownPanel(string text, bool alignRight = false, bool subdued = false)
        {
            Color bodyColor = subdued ? Color.FromRgb(205, 205, 205) : Color.FromRgb(235, 235, 235);
            Color codeBodyColor = subdued ? Color.FromRgb(220, 220, 220) : Color.FromRgb(230, 230, 230);
            Color codeHeaderColor = subdued ? Color.FromRgb(190, 190, 190) : Color.FromRgb(224, 224, 224);
            Color codeGutterColor = subdued ? Color.FromRgb(88, 88, 88) : Color.FromRgb(100, 100, 100);
            double bodyFontSize = subdued ? 12 : 14;
            double bodyLineHeight = subdued ? 19 : 22;

            var doc = new FlowDocument {
                PagePadding = new Thickness(0),
                Background = Brushes.Transparent,
                FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                FontSize = bodyFontSize,
                TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left
            };

            var viewer = new RichTextBox {
                Document = doc,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(bodyColor),
                Padding = new Thickness(0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                IsDocumentEnabled = true
            };
            text = TrimMessageForDisplay(text);
            if (string.IsNullOrEmpty(text)) return viewer;

            var lines = text.Replace("\r\n", "\n").Split('\n');
            bool inCode = false;
            string codeLang = "";
            var code = new StringBuilder();

            Action flushCode = () => {
                var codeText = code.ToString().TrimEnd('\n');
                code.Clear();

                string langDisplay = string.IsNullOrWhiteSpace(codeLang) ? "CODE" : codeLang.ToUpperInvariant();
                var header = new TextBlock {
                    Text = langDisplay,
                    Foreground = new SolidColorBrush(codeHeaderColor),
                    FontSize = subdued ? 11 : 12,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                };

                string normalized = codeText.Replace("\r\n", "\n");
                string[] parts = normalized.Length == 0 ? new[] { "" } : normalized.Split('\n');
                int lineCountForGutter = Math.Max(1, parts.Length);
                int gutterDigits = Math.Max(2, (int)Math.Floor(Math.Log10(lineCountForGutter)) + 1);

                string lineNumText = string.Join(Environment.NewLine,
                    Enumerable.Range(1, lineCountForGutter).Select(i => i.ToString().PadLeft(gutterDigits)));

                var lineNumColumn = new TextBlock {
                    Text = lineNumText,
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    FontSize = subdued ? 11 : 12,
                    Foreground = new SolidColorBrush(codeGutterColor),
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 3, 12, 0)
                };

                var codeBlock = new TextBox {
                    Text = codeText,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.NoWrap,
                    AcceptsReturn = true,
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    FontSize = subdued ? 11 : 12,
                    Foreground = new SolidColorBrush(codeBodyColor),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0, 3, 0, 0),
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 280
                };

                var codeRow = new Grid();
                codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                codeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(lineNumColumn, 0);
                Grid.SetColumn(codeBlock, 1);
                codeRow.Children.Add(lineNumColumn);
                codeRow.Children.Add(codeBlock);

                var inner = new StackPanel();
                inner.Children.Add(header);
                inner.Children.Add(codeRow);

                doc.Blocks.Add(new BlockUIContainer(new Border {
                    Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(18, 16, 20, 18),
                    Margin = new Thickness(0, 8, 0, 10),
                    Child = inner
                }));
            };

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++) {
                string line = lines[lineIndex];
                if (line.TrimStart().StartsWith("```")) {
                    if (!inCode) {
                        inCode = true;
                        codeLang = line.Trim().Trim('`').Trim();
                    } else {
                        inCode = false;
                        flushCode();
                        codeLang = "";
                    }
                    continue;
                }

                if (inCode) {
                    code.AppendLine(line);
                    continue;
                }

                string trimmed = line.Trim();
                if (IsMarkdownHorizontalRule(trimmed)) {
                    doc.Blocks.Add(new BlockUIContainer(new Border {
                        Height = 1,
                        Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                        Margin = new Thickness(0, 10, 0, 10)
                    }));
                    continue;
                }

                int idxForTable = lineIndex;
                if (TryConsumeMarkdownTable(lines, ref idxForTable, doc)) {
                    lineIndex = idxForTable;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(trimmed)) {
                    doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 6), LineHeight = 6 });
                    continue;
                }

                var paragraph = new Paragraph {
                    Foreground = new SolidColorBrush(bodyColor),
                    FontSize = bodyFontSize,
                    LineHeight = bodyLineHeight,
                    Margin = new Thickness(0, 2, 0, 2),
                    TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left
                };

                if (trimmed.StartsWith("### ")) {
                    paragraph.FontSize = 15;
                    paragraph.FontWeight = FontWeights.SemiBold;
                    paragraph.Foreground = new SolidColorBrush(subdued ? Color.FromRgb(205, 205, 205) : Color.FromRgb(255, 220, 150));
                    paragraph.TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left;
                    AppendMarkdownInlines(paragraph.Inlines, trimmed.Substring(4));
                } else if (trimmed.StartsWith("## ")) {
                    paragraph.FontSize = subdued ? 13 : 16;
                    paragraph.FontWeight = FontWeights.SemiBold;
                    paragraph.Foreground = new SolidColorBrush(subdued ? Color.FromRgb(205, 205, 205) : Color.FromRgb(255, 220, 150));
                    paragraph.Margin = new Thickness(0, 8, 0, 4);
                    paragraph.TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left;
                    AppendMarkdownInlines(paragraph.Inlines, trimmed.Substring(3));
                } else if (trimmed.StartsWith("# ")) {
                    paragraph.FontSize = subdued ? 14 : 17;
                    paragraph.FontWeight = FontWeights.Bold;
                    paragraph.Foreground = new SolidColorBrush(subdued ? Color.FromRgb(205, 205, 205) : Color.FromRgb(255, 220, 150));
                    paragraph.Margin = new Thickness(0, 8, 0, 4);
                    paragraph.TextAlignment = alignRight ? TextAlignment.Right : TextAlignment.Left;
                    AppendMarkdownInlines(paragraph.Inlines, trimmed.Substring(2));
                } else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ")) {
                    paragraph.Inlines.Add(new Run("• ") { Foreground = new SolidColorBrush(subdued ? Color.FromRgb(170, 170, 170) : Color.FromRgb(255, 200, 100)) });
                    AppendMarkdownInlines(paragraph.Inlines, trimmed.Substring(2));
                } else if (trimmed.StartsWith("> ")) {
                    paragraph.Foreground = new SolidColorBrush(subdued ? Color.FromRgb(175, 175, 175) : Color.FromRgb(190, 190, 190));
                    paragraph.Margin = new Thickness(10, 4, 0, 4);
                    paragraph.Inlines.Add(new Run("│ ") { Foreground = new SolidColorBrush(subdued ? Color.FromRgb(75, 75, 75) : Color.FromRgb(70, 70, 70)) });
                    AppendMarkdownInlines(paragraph.Inlines, trimmed.Substring(2));
                } else {
                    AppendMarkdownInlines(paragraph.Inlines, line);
                }

                doc.Blocks.Add(paragraph);
            }

            if (inCode) flushCode();
            return viewer;
        }

        private static void SaveReference(string description)
        {
            string canvasJson = "";
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                canvasJson = ExecuteGetGhComponents();
            }));

            System.Threading.Tasks.Task.Run(() => {
                try {
                    if (string.IsNullOrWhiteSpace(canvasJson) || canvasJson.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) {
                        string hint = "无法读取有效画布 JSON（无文档、画布为空或返回错误）。请打开 Grasshopper 文档并确认有电池后再试。";
                        if (!string.IsNullOrWhiteSpace(canvasJson))
                            hint += "\n" + ClampDiagDetail(canvasJson, 320);
                        AppendQuietDiagnosticCard("保存参考", hint);
                        return;
                    }

                    string refPath = GetReferenceDirectory();
                    string indexPath = GetReferenceIndexPath();
                    if (!System.IO.Directory.Exists(refPath)) System.IO.Directory.CreateDirectory(refPath);
                    string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    string fileName = "ref_" + timestamp + ".json";
                    string filePath = System.IO.Path.Combine(refPath, fileName);
                    System.IO.File.WriteAllText(filePath, canvasJson, System.Text.Encoding.UTF8);

                    string result = UpdateReferenceIndexSkill(description, fileName);

                    AppendSystemMessage($"参考已保存：{fileName}\n{result}\nJSON：{filePath}\n索引：{indexPath}");
                } catch (Exception ex) {
                    AddGhLog.Error("SaveReference failed", ex);
                    AppendQuietDiagnosticCard("保存参考", "出现异常：" + ex.Message);
                }
            });
        }

        /// <summary> 仓库根（含 skills/、reference/）。插件在 Rhino 中加载时 BaseDirectory 常在 bin 下，需向上查找；也可设环境变量 ADDGH_PROJECT_ROOT。 </summary>
        private static string GetProjectRootDirectory()
        {
            string env = Environment.GetEnvironmentVariable("ADDGH_PROJECT_ROOT");
            if (!string.IsNullOrWhiteSpace(env))
            {
                string full = System.IO.Path.GetFullPath(env.Trim());
                if (System.IO.Directory.Exists(full)) return full;
            }

            bool HasRepoSkills(string d) =>
                !string.IsNullOrEmpty(d)
                && System.IO.Directory.Exists(System.IO.Path.Combine(d, "skills"))
                && System.IO.File.Exists(System.IO.Path.Combine(d, "skills", "reference_index.md"));

            bool HasAddGhSubfolder(string d) =>
                !string.IsNullOrEmpty(d)
                && System.IO.File.Exists(System.IO.Path.Combine(d, "ADDGH", "ADDGH.csproj"));

            bool HasAddGhProject(string d) =>
                !string.IsNullOrEmpty(d)
                && System.IO.File.Exists(System.IO.Path.Combine(d, "ADDGH.csproj"));

            string TryWalk(string start, int maxSteps)
            {
                string dir = start;
                for (int i = 0; i < maxSteps && !string.IsNullOrEmpty(dir); i++)
                {
                    if (HasAddGhSubfolder(dir)) return dir;
                    if (HasRepoSkills(dir)) return dir;
                    if (HasAddGhProject(dir))
                        return System.IO.Directory.GetParent(dir)?.FullName ?? dir;
                    dir = System.IO.Directory.GetParent(dir)?.FullName;
                }
                return null;
            }

            string found = TryWalk(AppDomain.CurrentDomain.BaseDirectory, 22);
            if (!string.IsNullOrEmpty(found)) return found;

            found = TryWalk(Environment.CurrentDirectory, 18);
            if (!string.IsNullOrEmpty(found)) return found;

            try
            {
                string asm = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(asm))
                {
                    found = TryWalk(System.IO.Path.GetDirectoryName(asm), 22);
                    if (!string.IsNullOrEmpty(found)) return found;
                }
            }
            catch (Exception ex)
            {
                AddGhLog.Warn("GetProjectRootDirectory assembly path walk failed: " + ex.Message);
            }

            return Environment.CurrentDirectory;
        }

        private static string GetSkillsDirectory()
        {
            return System.IO.Path.Combine(GetProjectRootDirectory(), "skills");
        }

        private static string GetReferenceDirectory()
        {
            return System.IO.Path.Combine(GetProjectRootDirectory(), "reference");
        }

        private class ReferenceEntry
        {
            public string Description { get; set; }
            public string FileName { get; set; }
            public bool JsonExists { get; set; }
        }

        private static string GetReferenceIndexTemplate()
        {
            return "---\n" +
                "name: reference-index\n" +
                "description: 在完成初步 GH 建模逻辑规划之后查阅；仅当条目与已定方案相关时，再调用 read_reference_json 读取 JSON 对照实现。\n" +
                "---\n\n" +
                "# Reference Index\n\n" +
                "使用流程：\n" +
                "1. 先规划：用简短步骤说明本任务的 GH 逻辑（数据流、关键电池、风险点等）。\n" +
                "2. 再浏览：查阅下列参考条目，看是否与**已定方案**高度相关。\n" +
                "3. 后读取：若相关，调用 `read_reference_json` 并传入对应 `file_name`，用 JSON 对齐细节、补充或改造实现。\n\n" +
                "## References\n";
        }

        private static string GetReferenceIndexPath()
        {
            return System.IO.Path.Combine(GetSkillsDirectory(), "reference_index.md");
        }

        private static void EnsureReferenceIndexSkill()
        {
            string skillsPath = GetSkillsDirectory();
            if (!System.IO.Directory.Exists(skillsPath)) System.IO.Directory.CreateDirectory(skillsPath);

            string indexPath = GetReferenceIndexPath();
            if (!System.IO.File.Exists(indexPath))
            {
                System.IO.File.WriteAllText(indexPath, GetReferenceIndexTemplate(), Encoding.UTF8);
            }
        }

        private static string FormatReferenceEntry(string description, string jsonFileName)
        {
            string safeDescription = (description ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (string.IsNullOrWhiteSpace(safeDescription)) safeDescription = "未命名参考画布";
            string safeFileName = System.IO.Path.GetFileName(jsonFileName ?? "");

            return $"- 描述：{safeDescription}\n" +
                $"  文件：reference/{safeFileName}\n" +
                $"  调用：read_reference_json(file_name=\"{safeFileName}\")\n";
        }

        private static List<ReferenceEntry> ReadReferenceIndexEntries()
        {
            EnsureReferenceIndexSkill();

            string content = System.IO.File.ReadAllText(GetReferenceIndexPath(), Encoding.UTF8);
            var entries = new List<ReferenceEntry>();
            var matches = System.Text.RegularExpressions.Regex.Matches(
                content,
                @"-\s*描述：(?<desc>.*?)\r?\n\s*文件：reference/(?<file>[^\r\n]+)\r?\n\s*调用：read_reference_json\(file_name=""(?<call>[^""]+)""\)",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            string referencePath = GetReferenceDirectory();
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                string fileName = System.IO.Path.GetFileName(match.Groups["file"].Value.Trim());
                if (string.IsNullOrWhiteSpace(fileName)) continue;

                entries.Add(new ReferenceEntry
                {
                    Description = match.Groups["desc"].Value.Trim(),
                    FileName = fileName,
                    JsonExists = System.IO.File.Exists(System.IO.Path.Combine(referencePath, fileName))
                });
            }

            return entries;
        }

        private static void WriteReferenceIndexEntries(IEnumerable<ReferenceEntry> entries)
        {
            EnsureReferenceIndexSkill();
            var sb = new StringBuilder(GetReferenceIndexTemplate());

            foreach (var entry in entries)
            {
                sb.Append(FormatReferenceEntry(entry.Description, entry.FileName));
            }

            System.IO.File.WriteAllText(GetReferenceIndexPath(), sb.ToString(), Encoding.UTF8);
        }

        private static string UpdateReferenceIndexSkill(string description, string jsonFileName)
        {
            EnsureReferenceIndexSkill();
            string indexPath = GetReferenceIndexPath();
            string safeFileName = System.IO.Path.GetFileName(jsonFileName ?? "");
            if (string.IsNullOrWhiteSpace(safeFileName))
                return "Error: 参考文件名为空，未写入索引。";

            string content = System.IO.File.Exists(indexPath)
                ? System.IO.File.ReadAllText(indexPath, Encoding.UTF8)
                : GetReferenceIndexTemplate();

            if (content.IndexOf("reference/" + safeFileName, StringComparison.OrdinalIgnoreCase) >= 0
                || content.IndexOf("file_name=\"" + safeFileName + "\"", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() => { UpdateSkillLibraryUI(); }));
                return "索引中已包含该文件，未重复追加。";
            }

            if (content.IndexOf("## References", StringComparison.Ordinal) < 0)
            {
                if (!content.EndsWith("\n")) content += "\n";
                content += "\n## References\n";
            }
            if (!content.EndsWith("\n")) content += "\n";
            content += FormatReferenceEntry(description, safeFileName);
            System.IO.File.WriteAllText(indexPath, content, Encoding.UTF8);

            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => { UpdateSkillLibraryUI(); }));

            return "已更新统一参考索引 skills/reference_index.md。";
        }

        private static void ShowReferenceLibraryUI()
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                EnsureReferenceIndexSkill();

                if (_referenceLibraryWindow != null)
                {
                    _referenceLibraryWindow.Close();
                    _referenceLibraryWindow = null;
                }

                var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(16, 16, 16)), Margin = new Thickness(0) };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var header = new Grid { Margin = new Thickness(18, 16, 18, 10) };
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var titlePanel = new StackPanel { Orientation = Orientation.Vertical };
                titlePanel.Children.Add(new TextBlock
                {
                    Text = "我的参考",
                    Foreground = Brushes.White,
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold
                });
                titlePanel.Children.Add(new TextBlock
                {
                    Text = "从 reference_index.md 管理已保存的画布参考",
                    Foreground = new SolidColorBrush(Color.FromRgb(145, 145, 145)),
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 0)
                });
                Grid.SetColumn(titlePanel, 0);
                header.Children.Add(titlePanel);

                var refreshButton = CreateReferenceLibraryButton("刷新", false);
                refreshButton.Click += (s, e) => ShowReferenceLibraryUI();
                Grid.SetColumn(refreshButton, 1);
                header.Children.Add(refreshButton);

                var closeButton = CreateReferenceLibraryButton("关闭", false);
                closeButton.Margin = new Thickness(8, 0, 0, 0);
                closeButton.Click += (s, e) => _referenceLibraryWindow?.Close();
                Grid.SetColumn(closeButton, 2);
                header.Children.Add(closeButton);

                Grid.SetRow(header, 0);
                root.Children.Add(header);

                var entries = ReadReferenceIndexEntries();
                var content = new StackPanel { Margin = new Thickness(18, 0, 18, 18) };

                if (entries.Count == 0)
                {
                    content.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(44, 44, 44)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(18),
                        Child = new TextBlock
                        {
                            Text = "还没有保存的参考。点击“创建参考”后，这里会显示对应 JSON 和描述。",
                            Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                            FontSize = 13,
                            TextWrapping = TextWrapping.Wrap
                        }
                    });
                }
                else
                {
                    foreach (var entry in entries)
                    {
                        content.Children.Add(CreateReferenceCard(entry));
                    }
                }

                var scroll = new ScrollViewer
                {
                    Content = content,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Padding = new Thickness(0)
                };
                Grid.SetRow(scroll, 1);
                root.Children.Add(scroll);

                _referenceLibraryWindow = new Window
                {
                    Title = "我的参考",
                    Width = 560,
                    Height = 520,
                    MinWidth = 460,
                    MinHeight = 360,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = new SolidColorBrush(Color.FromRgb(16, 16, 16)),
                    Content = root,
                    Owner = _window
                };
                _referenceLibraryWindow.Closed += (s, e) => _referenceLibraryWindow = null;
                _referenceLibraryWindow.Show();
            }));
        }

        private static Button CreateReferenceLibraryButton(string text, bool danger)
        {
            var button = new Button
            {
                Content = text,
                Background = new SolidColorBrush(danger ? Color.FromRgb(60, 28, 28) : Color.FromRgb(34, 34, 34)),
                Foreground = new SolidColorBrush(danger ? Color.FromRgb(255, 170, 170) : Color.FromRgb(230, 230, 230)),
                BorderBrush = new SolidColorBrush(danger ? Color.FromRgb(95, 42, 42) : Color.FromRgb(56, 56, 56)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 6, 12, 6),
                Cursor = Cursors.Hand,
                FontSize = 12
            };

            button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
                <ControlTemplate TargetType=""Button"" xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                    <Border Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""8"">
                        <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Margin=""{TemplateBinding Padding}""/>
                    </Border>
                </ControlTemplate>");

            return button;
        }

        private static Border CreateReferenceCard(ReferenceEntry entry)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(46, 46, 46)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { Orientation = Orientation.Vertical };
            info.Children.Add(new TextBlock
            {
                Text = entry.Description,
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            info.Children.Add(new TextBlock
            {
                Text = $"reference/{entry.FileName}",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (!entry.JsonExists)
            {
                info.Children.Add(new TextBlock
                {
                    Text = "JSON 文件缺失，删除会清理索引条目",
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 90)),
                    FontSize = 11,
                    Margin = new Thickness(0, 5, 0, 0)
                });
            }

            Grid.SetColumn(info, 0);
            grid.Children.Add(info);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var useButton = CreateReferenceLibraryButton("使用", false);
            useButton.Click += (s, e) => {
                if (_txtInput != null)
                {
                    _txtInput.Text = $"请先说明本任务的 GH 建模规划（步骤与关键电池）。方案确定后查阅 skills/reference_index.md；若与条目「{entry.FileName}」相关，再调用 read_reference_json 读取该 JSON 并对照实现。";
                    _txtInput.Focus();
                }
                _referenceLibraryWindow?.Close();
            };
            actions.Children.Add(useButton);

            var deleteButton = CreateReferenceLibraryButton("删除", true);
            deleteButton.Margin = new Thickness(8, 0, 0, 0);
            deleteButton.Click += (s, e) => DeleteReferenceEntryWithConfirmation(entry);
            actions.Children.Add(deleteButton);

            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);

            card.Child = grid;
            return card;
        }

        private static void DeleteReferenceEntryWithConfirmation(ReferenceEntry entry)
        {
            var result = System.Windows.MessageBox.Show(
                $"确定删除参考“{entry.Description}”？\n\n将同时删除 reference/{entry.FileName} 并清理 reference_index.md 中的对应条目。",
                "删除参考",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                DeleteReferenceEntry(entry.FileName);
                ShowReferenceLibraryUI();
                AppendSystemMessage($"已删除参考：{entry.FileName}");
            }
            catch (Exception ex)
            {
                AddGhLog.Error("DeleteReferenceEntryWithConfirmation failed", ex);
                AppendQuietDiagnosticCard("删除参考", "出现异常：" + ex.Message);
            }
        }

        private static void DeleteReferenceEntry(string fileName)
        {
            string safeFileName = System.IO.Path.GetFileName(fileName ?? "");
            if (string.IsNullOrWhiteSpace(safeFileName)) throw new InvalidOperationException("参考文件名为空。");

            string referencePath = GetReferenceDirectory();
            string jsonPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(referencePath, safeFileName));
            string referenceFullPath = System.IO.Path.GetFullPath(referencePath);

            if (!jsonPath.StartsWith(referenceFullPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("非法 reference 文件路径。");

            if (System.IO.File.Exists(jsonPath)) System.IO.File.Delete(jsonPath);

            var remaining = ReadReferenceIndexEntries()
                .Where(entry => !entry.FileName.Equals(safeFileName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            WriteReferenceIndexEntries(remaining);

            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                UpdateSkillLibraryUI();
            }));
        }

        private static async void SendHiddenPromptAsync(string displayText, string actualPrompt)
        {
            if (_isGenerating) { _cts?.Cancel(); return; }

            _isGenerating = true;
            ApplySendButtonGeneratingState();
            _txtInput.Text = "";

            if (_messages.Count == 0) {
                _messages.AddRange(BuildInitialSystemMessages());
            }

            _messages.Add(new { role = "user", content = actualPrompt });
            AppendBubble(displayText, true);

            SyncActiveHistoryConversation(string.IsNullOrWhiteSpace(displayText) ? actualPrompt : displayText);

            EnforceChatHistoryLimit();

            _pendingAttachments.Clear();
            RefreshAttachmentPreview();
            if (_btnClearImage != null) _btnClearImage.Visibility = Visibility.Collapsed;

            try { _cts?.Dispose(); } catch (Exception ex) { AddGhLog.Warn("Dispose prior CTS: " + ex.Message); }
            _cts = new System.Threading.CancellationTokenSource();
            string apiKey = GetProviderRuntimeSettings().ApiKey;

            try {
                ShowThinkingAnimation();
                await CallLLMAPI(apiKey, 0, _cts.Token);
            } catch (OperationCanceledException) {
                AppendSystemMessage("已停止生成。");
            } catch (Exception ex) {
                AddGhLog.Error("SendHiddenPrompt CallLLMAPI failed", ex);
                AppendQuietDiagnosticCard("后台任务",
                    BuildProviderDiagnostic(GetProviderRuntimeSettings(), "出现异常：" + ex.GetType().Name, ex.Message));
            } finally {
                HideThinkingAnimation();
                _isGenerating = false;
                ApplySendButtonIdleState();
                try { _cts?.Dispose(); } catch (Exception ex) { AddGhLog.Warn("Dispose CTS after hidden prompt: " + ex.Message); }
                _cts = null;
            }
        }

        private static void AppendColoredStatsMessage(int addComp, int delComp, int addConn, int delConn)
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var card = new Border {
                    Background = new SolidColorBrush(Color.FromRgb(35, 35, 35)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 15),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                    BorderThickness = new Thickness(1)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                titleStack.Children.Add(new TextBlock {
                    Text = "操作统计",
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                Grid.SetColumn(titleStack, 0);
                grid.Children.Add(titleStack);

                var stack = new StackPanel {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };

                if (addComp > 0) stack.Children.Add(CreateStatBadge("电池", $"+{addComp}", Color.FromRgb(46, 204, 113)));
                if (delComp > 0) stack.Children.Add(CreateStatBadge("电池", $"-{delComp}", Color.FromRgb(231, 76, 60)));
                if (addConn > 0) stack.Children.Add(CreateStatBadge("连线", $"+{addConn}", Color.FromRgb(46, 204, 113)));
                if (delConn > 0) stack.Children.Add(CreateStatBadge("连线", $"-{delConn}", Color.FromRgb(231, 76, 60)));

                Grid.SetColumn(stack, 1);
                grid.Children.Add(stack);
                card.Child = grid;

                if (_thinkingBubble != null) { _chatPanel.Children.Remove(_thinkingBubble); _chatPanel.Children.Add(card); _chatPanel.Children.Add(_thinkingBubble); }
                else _chatPanel.Children.Add(card);
                _chatScroll.ScrollToEnd();
            }));
        }

        private static Border CreateStatBadge(string label, string value, Color color)
        {
            var badge = new Border {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(6, 0, 0, 0)
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)), FontSize = 11, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = value, Foreground = new SolidColorBrush(color), FontSize = 12, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            badge.Child = sp;
            return badge;
        }

        private static string ClampDiagDetail(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Trim();
            if (text.Length <= maxChars) return text;
            return text.Substring(0, maxChars) + "…";
        }

        private static TextBox CreateSelectableTextBox(string text, Brush foreground, double fontSize, Thickness margin, TextAlignment alignment = TextAlignment.Left)
        {
            return new TextBox
            {
                Text = text ?? "",
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = foreground,
                FontSize = fontSize,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(0),
                Margin = margin,
                TextAlignment = alignment,
                Cursor = Cursors.IBeam
            };
        }

        /// <summary> 对话区低调诊断卡片（灰阶小字，左侧对齐）；完整栈仍写入 AddGhLog。 </summary>
        private static void AppendQuietDiagnosticCard(string categoryLabel, string detail)
        {
            string cat = string.IsNullOrWhiteSpace(categoryLabel) ? "诊断" : categoryLabel.Trim();
            string body = ClampDiagDetail(detail ?? "", 1400);

            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                if (_chatPanel == null) return;

                var stack = new StackPanel
                {
                    Margin = new Thickness(0, 0, 0, 12),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    MaxWidth = 380
                };

                stack.Children.Add(new TextBlock
                {
                    Text = cat,
                    Foreground = new SolidColorBrush(Color.FromRgb(82, 82, 82)),
                    FontSize = 10,
                    FontWeight = FontWeights.Normal,
                    Margin = new Thickness(2, 0, 0, 4)
                });

                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 8, 10, 8)
                };

                card.Child = CreateSelectableTextBox(
                    string.IsNullOrEmpty(body) ? "（无详情）" : body,
                    new SolidColorBrush(Color.FromRgb(130, 130, 130)),
                    11,
                    new Thickness(0));

                stack.Children.Add(card);

                if (_thinkingBubble != null)
                {
                    _chatPanel.Children.Remove(_thinkingBubble);
                    _chatPanel.Children.Add(stack);
                    _chatPanel.Children.Add(_thinkingBubble);
                }
                else
                {
                    _chatPanel.Children.Add(stack);
                }

                stack.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.18)));
                if (_chatScroll != null) _chatScroll.ScrollToEnd();
            }));
        }

        private static void AppendSystemMessage(string text, bool isError = false)
        {
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                var tb = CreateSelectableTextBox(
                    text,
                    isError ? Brushes.Tomato : Brushes.Gray,
                    12,
                    new Thickness(0, 0, 0, 15),
                    TextAlignment.Center);
                tb.HorizontalAlignment = HorizontalAlignment.Center;
                tb.MaxWidth = 380;
                if (_thinkingBubble != null) {
                    _chatPanel.Children.Remove(_thinkingBubble);
                    _chatPanel.Children.Add(tb);
                    _chatPanel.Children.Add(_thinkingBubble);
                } else _chatPanel.Children.Add(tb);
                _chatScroll.ScrollToEnd();
            }));
        }

    }
}

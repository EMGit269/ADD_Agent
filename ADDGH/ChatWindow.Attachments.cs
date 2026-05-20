using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace ADDGH
{
    public static partial class ChatWindow
    {
        private static void AddPendingAttachments(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                try
                {
                    _pendingAttachments.Add(CreateAttachmentItem(path));
                }
                catch (Exception ex)
                {
                    _pendingAttachments.Add(new AttachmentItem
                    {
                        Path = path,
                        FileName = System.IO.Path.GetFileName(path),
                        Kind = AttachmentKind.Unsupported,
                        MimeType = "application/octet-stream",
                        SizeBytes = System.IO.File.Exists(path) ? new FileInfo(path).Length : 0,
                        Error = "读取失败: " + ex.Message
                    });
                }
            }

            RefreshAttachmentPreview();
        }

        private static AttachmentItem CreateAttachmentItem(string path)
        {
            var file = new FileInfo(path);
            string ext = file.Extension.ToLowerInvariant();
            var item = new AttachmentItem
            {
                Path = path,
                FileName = file.Name,
                SizeBytes = file.Exists ? file.Length : 0,
                MimeType = GetMimeType(ext)
            };

            if (IsImageExtension(ext))
            {
                item.Kind = AttachmentKind.Image;
                item.Base64 = Convert.ToBase64String(System.IO.File.ReadAllBytes(path));
            }
            else if (IsTextExtension(ext))
            {
                item.Kind = AttachmentKind.Text;
                item.ExtractedText = TruncateAttachmentText(System.IO.File.ReadAllText(path, Encoding.UTF8), item.FileName);
            }
            else if (IsDocumentExtension(ext))
            {
                item.Kind = AttachmentKind.Document;
                item.ExtractedText = TruncateAttachmentText(ExtractDocumentText(path, ext), item.FileName);
            }
            else
            {
                item.Kind = AttachmentKind.Unsupported;
                item.ExtractedText = $"文件 {item.FileName} 已上传，但当前不支持读取该格式内容。";
            }

            return item;
        }

        private static AttachmentItem CreateAttachmentItemFromDataUrl(string dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return null;

            int commaIndex = dataUrl.IndexOf(',');
            if (commaIndex <= 5)
                return null;

            string header = dataUrl.Substring(5, commaIndex - 5);
            string base64 = dataUrl.Substring(commaIndex + 1);
            if (string.IsNullOrWhiteSpace(base64))
                return null;

            string mimeType = "image/png";
            int semicolonIndex = header.IndexOf(';');
            if (semicolonIndex > 0)
                mimeType = header.Substring(0, semicolonIndex);
            else if (!string.IsNullOrWhiteSpace(header))
                mimeType = header;

            byte[] bytes = Convert.FromBase64String(base64);
            string extension = MimeTypeToImageExtension(mimeType);
            string tempPath = Path.Combine(
                Path.GetTempPath(),
                "ADDGH_restore_" + DateTime.UtcNow.Ticks + "_" + Guid.NewGuid().ToString("n").Substring(0, 8) + extension);
            File.WriteAllBytes(tempPath, bytes);

            return new AttachmentItem
            {
                Path = tempPath,
                FileName = Path.GetFileName(tempPath),
                MimeType = mimeType,
                Kind = AttachmentKind.Image,
                Base64 = base64,
                SizeBytes = bytes.LongLength
            };
        }

        private static bool IsImageExtension(string ext)
        {
            return new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }.Contains(ext);
        }

        private static bool IsTextExtension(string ext)
        {
            return new[] { ".txt", ".md", ".json", ".csv", ".xml", ".ghx" }.Contains(ext);
        }

        private static bool IsDocumentExtension(string ext)
        {
            return new[] { ".pdf", ".docx", ".xlsx", ".pptx", ".doc", ".xls", ".ppt" }.Contains(ext);
        }

        private static string GetMimeType(string ext)
        {
            switch (ext)
            {
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".bmp": return "image/bmp";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".pdf": return "application/pdf";
                case ".docx": return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                case ".xlsx": return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                case ".pptx": return "application/vnd.openxmlformats-officedocument.presentationml.presentation";
                default: return "text/plain";
            }
        }

        private static string MimeTypeToImageExtension(string mimeType)
        {
            switch ((mimeType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "image/jpeg":
                case "image/jpg":
                    return ".jpg";
                case "image/bmp":
                    return ".bmp";
                case "image/gif":
                    return ".gif";
                case "image/webp":
                    return ".webp";
                default:
                    return ".png";
            }
        }

        private static string TruncateAttachmentText(string text, string fileName)
        {
            if (string.IsNullOrWhiteSpace(text)) return $"文件 {fileName} 未提取到可读文本。";
            const int maxChars = 12000;
            if (text.Length <= maxChars) return text;
            return text.Substring(0, maxChars) + $"\n\n[附件 {fileName} 内容过长，已截断到 {maxChars} 字符。]";
        }

        private static string ExtractDocumentText(string path, string ext)
        {
            try
            {
                if (ext == ".docx") return ExtractTextFromZipXml(path, "word/document.xml");
                if (ext == ".pptx") return ExtractPptxText(path);
                if (ext == ".xlsx") return ExtractXlsxText(path);
                if (ext == ".pdf") return ExtractPdfTextBestEffort(path);
                return $"旧版 Office 文件 {System.IO.Path.GetFileName(path)} 已上传，但当前仅能稳定读取 .docx/.xlsx/.pptx。";
            }
            catch (Exception ex)
            {
                return $"文件 {System.IO.Path.GetFileName(path)} 内容解析失败: {ex.Message}";
            }
        }

        private static string ExtractTextFromZipXml(string path, string entryName)
        {
            using (var archive = ZipFile.OpenRead(path))
            {
                var entry = archive.GetEntry(entryName);
                if (entry == null) return "";
                using (var stream = entry.Open())
                using (var reader = new StreamReader(stream))
                {
                    return ExtractTextFromXml(reader.ReadToEnd());
                }
            }
        }

        private static string ExtractPptxText(string path)
        {
            var sb = new StringBuilder();
            using (var archive = ZipFile.OpenRead(path))
            {
                foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml")).OrderBy(e => e.FullName))
                {
                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        sb.AppendLine(ExtractTextFromXml(reader.ReadToEnd()));
                    }
                }
            }
            return sb.ToString();
        }

        private static string ExtractXlsxText(string path)
        {
            var sb = new StringBuilder();
            using (var archive = ZipFile.OpenRead(path))
            {
                foreach (var entry in archive.Entries.Where(e => (e.FullName.StartsWith("xl/worksheets/") || e.FullName == "xl/sharedStrings.xml") && e.FullName.EndsWith(".xml")).OrderBy(e => e.FullName))
                {
                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        string text = ExtractTextFromXml(reader.ReadToEnd());
                        if (!string.IsNullOrWhiteSpace(text)) sb.AppendLine(text);
                    }
                }
            }
            return sb.ToString();
        }

        private static string ExtractTextFromXml(string xml)
        {
            var doc = XDocument.Parse(xml);
            return string.Join(" ", doc.DescendantNodes().OfType<XText>().Select(t => t.Value).Where(v => !string.IsNullOrWhiteSpace(v))).Trim();
        }

        private static string ExtractPdfTextBestEffort(string path)
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            string raw = Encoding.GetEncoding("ISO-8859-1").GetString(bytes);
            var matches = Regex.Matches(raw, @"\((?<text>(?:\\.|[^\\)])*)\)");
            var text = string.Join(" ", matches.Cast<Match>().Select(m => m.Groups["text"].Value.Replace("\\)", ")").Replace("\\(", "(")).Where(v => v.Length > 1));
            return string.IsNullOrWhiteSpace(text)
                ? "PDF 已上传，但未提取到可读文本。若该 PDF 为扫描件，需要 OCR 后再上传文本。"
                : text;
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return Math.Round(bytes / 1024.0, 1) + " KB";
            return Math.Round(bytes / 1024.0 / 1024.0, 1) + " MB";
        }

        private static void RefreshAttachmentPreview()
        {
            if (_attachmentPreviewPanel == null) return;

            Rhino.RhinoApp.InvokeOnUiThread((Action)(() => {
                _attachmentPreviewPanel.Children.Clear();
                _attachmentPreviewPanel.Visibility = _pendingAttachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                foreach (var attachment in _pendingAttachments.ToList())
                {
                    _attachmentPreviewPanel.Children.Add(CreateAttachmentCard(attachment, true));
                }
            }));
        }

        private static FrameworkElement CreateAttachmentCard(AttachmentItem attachment, bool removable)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 8, 8),
                MaxWidth = 210
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (removable) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement preview;
            if (attachment.Kind == AttachmentKind.Image && System.IO.File.Exists(attachment.Path))
            {
                preview = new Image
                {
                    Source = LoadBitmapImage(attachment.Path),
                    Width = 44,
                    Height = 44,
                    Stretch = Stretch.UniformToFill,
                    ClipToBounds = true
                };
            }
            else
            {
                preview = new Border
                {
                    Width = 44,
                    Height = 44,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
                    Child = new TextBlock
                    {
                        Text = GetAttachmentBadge(attachment),
                        Foreground = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
            }
            Grid.SetColumn(preview, 0);
            grid.Children.Add(preview);

            var info = new StackPanel { Margin = new Thickness(9, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text = attachment.FileName,
                Foreground = new SolidColorBrush(Color.FromRgb(238, 238, 238)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 125
            });
            info.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(attachment.Error) ? FormatFileSize(attachment.SizeBytes) : attachment.Error,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 125
            });
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            if (removable)
            {
                var remove = new Button
                {
                    Content = "×",
                    Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 190)),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    FontSize = 14,
                    Width = 22,
                    Height = 22,
                    VerticalAlignment = VerticalAlignment.Top
                };
                remove.Click += (s, e) => {
                    _pendingAttachments.Remove(attachment);
                    RefreshAttachmentPreview();
                };
                Grid.SetColumn(remove, 2);
                grid.Children.Add(remove);
            }

            border.Child = grid;
            return border;
        }

        private static FrameworkElement CreateChatImageThumbnail(AttachmentItem attachment, double size = 120)
        {
            if (attachment == null || string.IsNullOrWhiteSpace(attachment.Path) || !System.IO.File.Exists(attachment.Path))
                return new Border();

            var thumbnailBorder = new Border
            {
                Width = size,
                Height = size,
                Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(14),
                BorderBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(22, 22, 22)),
                ClipToBounds = true,
                Cursor = Cursors.Hand
            };

            thumbnailBorder.Child = new Image
            {
                Source = LoadBitmapImage(attachment.Path, 320),
                Stretch = Stretch.UniformToFill,
                SnapsToDevicePixels = true
            };

            thumbnailBorder.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                ShowImagePreviewWindow(attachment.Path, attachment.FileName);
            };

            return thumbnailBorder;
        }

        private static FrameworkElement CreateChatImageStrip(IEnumerable<AttachmentItem> attachments)
        {
            var imageItems = (attachments ?? Enumerable.Empty<AttachmentItem>())
                .Where(a => a != null && a.Kind == AttachmentKind.Image && !string.IsNullOrWhiteSpace(a.Path) && System.IO.File.Exists(a.Path))
                .ToList();
            if (imageItems.Count == 0)
                return null;

            var wrap = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0)
            };

            foreach (var attachment in imageItems)
                wrap.Children.Add(CreateChatImageThumbnail(attachment));

            return wrap;
        }

        private static void ShowImagePreviewWindow(string path, string title = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                return;

            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                try
                {
                    var preview = new Window
                    {
                        Title = string.IsNullOrWhiteSpace(title) ? System.IO.Path.GetFileName(path) : title,
                        Width = 980,
                        Height = 760,
                        MinWidth = 520,
                        MinHeight = 420,
                        Background = new SolidColorBrush(Color.FromRgb(10, 10, 10)),
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Owner = _window
                    };

                    preview.Content = new Grid
                    {
                        Background = new SolidColorBrush(Color.FromRgb(10, 10, 10)),
                        Children =
                        {
                            new ScrollViewer
                            {
                                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                                Padding = new Thickness(18),
                                Content = new Image
                                {
                                    Source = LoadBitmapImage(path, null),
                                    Stretch = Stretch.Uniform,
                                    SnapsToDevicePixels = true
                                }
                            }
                        }
                    };

                    preview.Show();
                }
                catch (Exception ex)
                {
                    AddGhLog.Warn("ShowImagePreviewWindow: " + ex.Message);
                }
            }));
        }

        private static BitmapImage LoadBitmapImage(string path, int? decodePixelWidth = 120)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            if (decodePixelWidth.HasValue && decodePixelWidth.Value > 0)
                bitmap.DecodePixelWidth = decodePixelWidth.Value;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private static string GetAttachmentBadge(AttachmentItem attachment)
        {
            string ext = System.IO.Path.GetExtension(attachment.FileName).TrimStart('.').ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(ext)) return "FILE";
            return ext.Length > 4 ? ext.Substring(0, 4) : ext;
        }

        private static List<object> BuildUserMessageContent(string input, List<AttachmentItem> attachments)
        {
            var contentArr = new List<object>();
            var textBuilder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(input)) textBuilder.AppendLine(input);

            foreach (var attachment in attachments.Where(a => a.Kind != AttachmentKind.Image))
            {
                textBuilder.AppendLine();
                textBuilder.AppendLine($"【附件内容：{attachment.FileName}】");
                textBuilder.AppendLine(attachment.ExtractedText ?? "未提取到文本内容。");
            }

            string text = textBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                contentArr.Add(new { type = "text", text = text });
            }

            foreach (var attachment in attachments.Where(a => a.Kind == AttachmentKind.Image && !string.IsNullOrEmpty(a.Base64)))
            {
                contentArr.Add(new
                {
                    type = "image_url",
                    image_url = new { url = $"data:{attachment.MimeType};base64,{attachment.Base64}" }
                });
            }

            if (contentArr.Count == 0)
            {
                contentArr.Add(new { type = "text", text = input });
            }

            return contentArr;
        }

        private static void AppendNonImageAttachmentText(StringBuilder textBuilder, IEnumerable<AttachmentItem> attachments)
        {
            if (textBuilder == null || attachments == null) return;

            foreach (var attachment in attachments.Where(a => a.Kind != AttachmentKind.Image))
            {
                textBuilder.AppendLine();
                textBuilder.AppendLine($"【附件内容：{attachment.FileName}】");
                if (!string.IsNullOrWhiteSpace(attachment.ExtractedText))
                    textBuilder.AppendLine(attachment.ExtractedText);
                else if (!string.IsNullOrWhiteSpace(attachment.Error))
                    textBuilder.AppendLine("附件读取失败：" + attachment.Error);
                else
                    textBuilder.AppendLine("未提取到文本内容。");
            }
        }

        private static string BuildVisionExecutionUserText(string input, List<AttachmentItem> attachments, string visionAnalysis)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(input))
            {
                sb.AppendLine("用户原始请求：");
                sb.AppendLine(input.Trim());
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("用户发送了图片附件，但没有额外文字说明。");
                sb.AppendLine();
            }

            AppendNonImageAttachmentText(sb, attachments);
            if (attachments != null && attachments.Any(a => a.Kind != AttachmentKind.Image))
                sb.AppendLine();

            sb.AppendLine("以下图片理解来自视觉预处理模型；执行模型没有直接看到原图。");
            sb.AppendLine(_agentMode == AgentMode.Plan
                ? "把“视觉事实”当事实，把“关联画布定位”“执行模型下一步检查点”“给执行模型的任务摘要”“最终截图复核建议”当待核实线索；先核实，再输出实施步骤卡片，不要直接修改画布。"
                : "把“视觉事实”当事实，把“关联画布定位”“执行模型下一步检查点”“给执行模型的任务摘要”“最终截图复核建议”当待核实线索；先核实再修改。");
            sb.AppendLine();
            sb.AppendLine(visionAnalysis?.Trim() ?? "");
            return sb.ToString().Trim();
        }

        private static string BuildFinalVisualReviewExecutionUserText(string priorDraft, string visualReview)
        {
            var sb = new StringBuilder();
            sb.AppendLine(_agentMode == AgentMode.Plan
                ? "以下是最终截图视觉复核结果。你需要结合这份复核补充或修正实施步骤，并明确说明仍存在的偏差。"
                : "以下是最终截图视觉复核结果。你需要结合这份复核，决定是否确认完成、继续修改，或明确说明仍存在的偏差。");
            if (!string.IsNullOrWhiteSpace(priorDraft))
            {
                sb.AppendLine();
                sb.AppendLine("你在复核前的结论：");
                sb.AppendLine(priorDraft.Trim());
            }
            sb.AppendLine();
            sb.AppendLine("最终截图视觉复核：");
            sb.AppendLine(visualReview?.Trim() ?? "");
            sb.AppendLine();
            sb.AppendLine(_agentMode == AgentMode.Plan
                ? "要求：如果复核指出未达标或存在明显偏差，不要宣称已完成；应补充或修正实施步骤，并向用户明确说明当前差距。"
                : "要求：如果复核指出未达标或存在明显偏差，不要直接结束；应继续修正或向用户明确说明当前差距。");
            return sb.ToString().Trim();
        }

        private static bool ShouldIncludeVisionCanvasContext(string input)
        {
            string text = input?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string[] keywords =
            {
                "改", "修改", "调整", "修", "修正", "纠正", "不对", "错误", "报错", "问题",
                "诊断", "检查", "看看", "结果", "输出", "null", "panel", "一致", "对不上", "偏"
            };
            return keywords.Any(k => text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string BuildVisionCanvasContext(string input)
        {
            try
            {
                string raw = ExecuteGetGhComponents();
                if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
                    return "当前无可用 Grasshopper 画布上下文。";

                var root = JObject.Parse(raw);
                var components = root["components"] as JArray ?? new JArray();
                var canvasErrors = root["canvas_errors"] as JArray ?? new JArray();
                var units = root["rhino_units"] as JObject;

                if (!ShouldIncludeVisionCanvasContext(input) && canvasErrors.Count == 0)
                    return "";

                var sb = new StringBuilder();
                sb.AppendLine("画布上下文：");
                sb.AppendLine($"组件={components.Count} 问题={canvasErrors.Count}");
                if (units != null)
                {
                    string modelUnit = units["model_unit_system"]?.ToString();
                    string absTol = units["model_absolute_tolerance"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(modelUnit) || !string.IsNullOrWhiteSpace(absTol))
                        sb.AppendLine($"单位={modelUnit ?? "未知"} 公差={absTol ?? "未知"}");
                }

                string canvasIssueText = _txtCanvasIssues?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(canvasIssueText))
                {
                    sb.AppendLine("诊断：" + ClampVisionText(canvasIssueText, 220).Replace("\r", " ").Replace("\n", " "));
                }

                if (canvasErrors.Count > 0)
                {
                    sb.AppendLine("关键问题：");
                    foreach (var err in canvasErrors.Take(4))
                    {
                        string name = err?["name"]?.ToString();
                        string level = err?["level"]?.ToString();
                        string message = err?["message"]?.ToString();
                        sb.AppendLine($"- {name}[{level}] {ClampVisionText(message, 80)}".TrimEnd());
                    }
                }

                var selected = new List<JToken>();
                selected.AddRange(components.Where(c => c?["runtime_messages"] is JArray).Take(3));
                selected.AddRange(components.Reverse().Take(4));
                var unique = selected
                    .Where(c => c != null)
                    .GroupBy(c => c["id"]?.ToString() ?? Guid.NewGuid().ToString("n"))
                    .Select(g => g.First())
                    .Take(5)
                    .ToList();

                if (unique.Count > 0)
                {
                    sb.AppendLine("相关组件：");
                    foreach (var comp in unique)
                    {
                        string name = comp["name"]?.ToString() ?? "未知组件";
                        string nickname = comp["nickname"]?.ToString();
                        string id = comp["id"]?.ToString();
                        string idShort = string.IsNullOrWhiteSpace(id) || id.Length < 8 ? id : id.Substring(0, 8);
                        sb.AppendLine($"- {name}" + (string.IsNullOrWhiteSpace(nickname) || nickname == name ? "" : $"({nickname})") + (string.IsNullOrWhiteSpace(idShort) ? "" : $"#{idShort}"));

                        if (comp["runtime_messages"] is JArray msgs && msgs.Count > 0)
                            sb.AppendLine("  消息=" + string.Join(" | ", msgs.Take(2).Select(m => ClampVisionText(m?.ToString(), 50)).Where(m => !string.IsNullOrWhiteSpace(m))));

                        if (comp["inputs"] is JArray inputs && inputs.Count > 0)
                        {
                            var inputParts = inputs.Take(2).Select(i =>
                            {
                                string portName = i?["name"]?.ToString() ?? "?";
                                string ds = i?["data_structure"]?.ToString() ?? "未知";
                                string src = "";
                                if (i?["sources"] is JArray srcs && srcs.Count > 0)
                                    src = "<-" + string.Join(",", srcs.Take(1).Select(s => s?["name"]?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)));
                                return $"{portName}: {ds}{src}";
                            });
                            sb.AppendLine("  输入=" + string.Join(" ; ", inputParts));
                        }

                        if (comp["outputs"] is JArray outputs && outputs.Count > 0)
                        {
                            var outputParts = outputs.Take(2).Select(o =>
                            {
                                string portName = o?["name"]?.ToString() ?? "?";
                                string ds = o?["data_structure"]?.ToString();
                                string type = o?["type"]?.ToString();
                                return string.IsNullOrWhiteSpace(ds) ? $"{portName}: {type}" : $"{portName}: {ds}";
                            });
                            sb.AppendLine("  输出=" + string.Join(" ; ", outputParts));
                        }
                    }
                }

                return ClampVisionText(sb.ToString().Trim(), 2200);
            }
            catch (Exception ex)
            {
                return "画布上下文读取失败：" + ex.Message;
            }
        }

        private static string ClampVisionText(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return text.Length <= maxChars ? text.Trim() : text.Substring(0, maxChars).TrimEnd() + "\n[已截断]";
        }

        private static JObject BuildVisionPreprocessRequestBody(ProviderRuntimeSettings providerSettings, string input, List<AttachmentItem> attachments)
        {
            var content = new JArray();
            var textBuilder = new StringBuilder();
            textBuilder.AppendLine("只分析图片、文字和可选画布上下文；不要调用工具，不要执行 Grasshopper 操作。");
            textBuilder.AppendLine("用户发图不一定是要建模，也可能是修改、诊断、解释或误发。若有画布上下文，只用于定位。");
            textBuilder.AppendLine("如果没有用户参考图，而只是让你检查当前模型截图的形态，请不要假设目标形状；只判断是否存在明显的形态异常、比例失衡、方向错误、连续性问题、缺失或可疑结构。");
            textBuilder.AppendLine("按以下标题顺序输出，简洁作答：");
            textBuilder.AppendLine("【视觉事实】");
            textBuilder.AppendLine("【用户意图判断】");
            textBuilder.AppendLine("【问题点】");
            textBuilder.AppendLine("【关联画布定位】");
            textBuilder.AppendLine("【执行模型下一步检查点】");
            textBuilder.AppendLine("【与 Grasshopper 建模/画图相关的信息】");
            textBuilder.AppendLine("【不确定性】");
            textBuilder.AppendLine("【最终截图复核建议】");
            textBuilder.AppendLine("【给执行模型的任务摘要】");
            textBuilder.AppendLine("要求：视觉事实只写可见事实；关联画布定位最多3项并给高/中/低置信度；执行模型下一步检查点最多3项；最终截图复核建议明确写“需要”或“不需要”，并给一句理由；无信息就写“无”或“不确定”。");

            if (!string.IsNullOrWhiteSpace(input))
            {
                textBuilder.AppendLine();
                textBuilder.AppendLine("用户原始请求：");
                textBuilder.AppendLine(input.Trim());
            }

            string canvasContext = BuildVisionCanvasContext(input);
            if (!string.IsNullOrWhiteSpace(canvasContext))
            {
                textBuilder.AppendLine();
                textBuilder.AppendLine(canvasContext);
            }

            AppendNonImageAttachmentText(textBuilder, attachments);
            content.Add(new JObject
            {
                ["type"] = "text",
                ["text"] = textBuilder.ToString().Trim()
            });

            foreach (var attachment in attachments.Where(a => a.Kind == AttachmentKind.Image && !string.IsNullOrEmpty(a.Base64)))
            {
                content.Add(new JObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JObject
                    {
                        ["url"] = $"data:{attachment.MimeType};base64,{attachment.Base64}"
                    }
                });
            }

            return new JObject
            {
                ["model"] = providerSettings.ModelName,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = "你是图像理解与定位预处理器。职责：把用户图片、文字和可选画布上下文整理成给执行模型使用的定位报告。不要调用工具，不要执行 Grasshopper 操作，不要默认用户发图就是要建模。若有画布上下文，只用于定位，不用于代替执行模型规划或修改。严格按用户要求的标题顺序输出，区分事实、推断、置信度和不确定性，保持简洁。"
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = content
                    }
                },
                ["stream"] = false,
                ["temperature"] = 0.1
            };
        }

        private static JObject BuildFinalVisualReviewRequestBody(
            ProviderRuntimeSettings providerSettings,
            string originalInput,
            List<AttachmentItem> originalImageAttachments,
            string screenshotPath,
            string priorDraft)
        {
            var content = new JArray();
            var textBuilder = new StringBuilder();
            textBuilder.AppendLine("你是结果验收视觉评估器。不要调用工具，不要规划完整建模方案。");
            textBuilder.AppendLine("任务：对比用户原始图像参考/问题图片与当前 Rhino 结果截图，判断当前结果是否满足目标。");
            textBuilder.AppendLine("若没有参考图，或图片本身是在指出问题，则重点判断当前结果是否仍存在明显偏差。");
            textBuilder.AppendLine("按以下标题顺序输出，保持简洁：");
            textBuilder.AppendLine("【是否达标】");
            textBuilder.AppendLine("【主要偏差】");
            textBuilder.AppendLine("【偏差性质】");
            textBuilder.AppendLine("【给执行模型的反馈】");
            textBuilder.AppendLine("要求：如果没有明显问题，也要明确写“基本达标”；偏差最多写 5 项，反馈只写局部修正方向。");

            if (!string.IsNullOrWhiteSpace(originalInput))
            {
                textBuilder.AppendLine();
                textBuilder.AppendLine("用户原始请求：");
                textBuilder.AppendLine(originalInput.Trim());
            }

            if (!string.IsNullOrWhiteSpace(priorDraft))
            {
                textBuilder.AppendLine();
                textBuilder.AppendLine("执行模型在复核前的结论：");
                textBuilder.AppendLine(ClampVisionText(priorDraft, 800));
            }

            textBuilder.AppendLine();
            textBuilder.AppendLine("图片顺序说明：先是用户原始图片参考/问题图片，最后一张是当前 Rhino 结果截图。");

            content.Add(new JObject
            {
                ["type"] = "text",
                ["text"] = textBuilder.ToString().Trim()
            });

            foreach (var attachment in (originalImageAttachments ?? new List<AttachmentItem>()).Where(a => a.Kind == AttachmentKind.Image && !string.IsNullOrEmpty(a.Base64)))
            {
                content.Add(new JObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JObject
                    {
                        ["url"] = $"data:{attachment.MimeType};base64,{attachment.Base64}"
                    }
                });
            }

            if (!string.IsNullOrWhiteSpace(screenshotPath) && File.Exists(screenshotPath))
            {
                string mimeType = GetMimeType(Path.GetExtension(screenshotPath).ToLowerInvariant());
                string base64 = Convert.ToBase64String(File.ReadAllBytes(screenshotPath));
                content.Add(new JObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JObject
                    {
                        ["url"] = $"data:{mimeType};base64,{base64}"
                    }
                });
            }

            return new JObject
            {
                ["model"] = providerSettings.ModelName,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = "你是结果验收视觉评估器。你只负责比较图片与当前结果截图，给出是否达标、主要偏差和局部修正方向；不要调用工具，不要输出无关说明。"
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = content
                    }
                },
                ["stream"] = false,
                ["temperature"] = 0.1
            };
        }
    }
}

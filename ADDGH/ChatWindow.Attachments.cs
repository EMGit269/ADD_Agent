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

        private static BitmapImage LoadBitmapImage(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = 120;
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
            sb.AppendLine("职责分工：视觉预处理模型只负责识别图片内容、可读文字、用户可能意图、不确定性，以及结合受控画布上下文定位疑似问题位置；执行模型负责结合用户原始请求与上下文判断是否需要画图/建模，并在需要时继续规划和执行 Grasshopper 操作。");
            sb.AppendLine("该报告应按字段消费：把“视觉事实”视为高优先级事实，把“关联画布定位”与“给执行模型的任务摘要”视为待核实线索；优先检查“执行模型下一步检查点”，不要把视觉模型的定位结论直接当成最终事实。");
            sb.AppendLine("不要默认把图片当作建模参考；如果意图是修改建议、问题诊断、内容解释、素材输入、错误上传或不明确，请按对应意图处理，必要时先向用户澄清。");
            sb.AppendLine();
            sb.AppendLine(visionAnalysis?.Trim() ?? "");
            return sb.ToString().Trim();
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

                var sb = new StringBuilder();
                sb.AppendLine("当前 Grasshopper 画布受控上下文：");
                sb.AppendLine($"- 组件数：{components.Count}");
                sb.AppendLine($"- 运行时问题数：{canvasErrors.Count}");
                if (units != null)
                {
                    string modelUnit = units["model_unit_system"]?.ToString();
                    string absTol = units["model_absolute_tolerance"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(modelUnit) || !string.IsNullOrWhiteSpace(absTol))
                        sb.AppendLine($"- Rhino 单位：{modelUnit ?? "未知"}，绝对公差：{absTol ?? "未知"}");
                }

                string canvasIssueText = _txtCanvasIssues?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(canvasIssueText))
                {
                    sb.AppendLine("- 画布诊断：");
                    sb.AppendLine(ClampVisionText(canvasIssueText, 800));
                }

                if (canvasErrors.Count > 0)
                {
                    sb.AppendLine("- 关键运行时问题：");
                    foreach (var err in canvasErrors.Take(8))
                    {
                        string name = err?["name"]?.ToString();
                        string level = err?["level"]?.ToString();
                        string message = err?["message"]?.ToString();
                        sb.AppendLine($"  - {name} [{level}] {message}".TrimEnd());
                    }
                }

                var selected = new List<JToken>();
                selected.AddRange(components.Where(c => c?["runtime_messages"] is JArray).Take(8));
                selected.AddRange(components.Reverse().Take(10));
                var unique = selected
                    .Where(c => c != null)
                    .GroupBy(c => c["id"]?.ToString() ?? Guid.NewGuid().ToString("n"))
                    .Select(g => g.First())
                    .Take(12)
                    .ToList();

                if (unique.Count > 0)
                {
                    sb.AppendLine("- 相关组件摘要：");
                    foreach (var comp in unique)
                    {
                        string name = comp["name"]?.ToString() ?? "未知组件";
                        string nickname = comp["nickname"]?.ToString();
                        string id = comp["id"]?.ToString();
                        string idShort = string.IsNullOrWhiteSpace(id) || id.Length < 8 ? id : id.Substring(0, 8);
                        sb.AppendLine($"  - {name}" + (string.IsNullOrWhiteSpace(nickname) || nickname == name ? "" : $"（{nickname}）") + (string.IsNullOrWhiteSpace(idShort) ? "" : $" #{idShort}"));

                        if (comp["runtime_messages"] is JArray msgs && msgs.Count > 0)
                            sb.AppendLine($"    - 运行时消息：{string.Join(" | ", msgs.Take(3).Select(m => m?.ToString()).Where(m => !string.IsNullOrWhiteSpace(m)))}");

                        if (comp["inputs"] is JArray inputs && inputs.Count > 0)
                        {
                            var inputParts = inputs.Take(3).Select(i =>
                            {
                                string portName = i?["name"]?.ToString() ?? "?";
                                string ds = i?["data_structure"]?.ToString() ?? "未知";
                                string src = "";
                                if (i?["sources"] is JArray srcs && srcs.Count > 0)
                                    src = " <- " + string.Join(", ", srcs.Take(2).Select(s => s?["name"]?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)));
                                return $"{portName}: {ds}{src}";
                            });
                            sb.AppendLine("    - 输入：" + string.Join(" ; ", inputParts));
                        }

                        if (comp["outputs"] is JArray outputs && outputs.Count > 0)
                        {
                            var outputParts = outputs.Take(3).Select(o =>
                            {
                                string portName = o?["name"]?.ToString() ?? "?";
                                string ds = o?["data_structure"]?.ToString();
                                string type = o?["type"]?.ToString();
                                return string.IsNullOrWhiteSpace(ds) ? $"{portName}: {type}" : $"{portName}: {ds}";
                            });
                            sb.AppendLine("    - 输出：" + string.Join(" ; ", outputParts));
                        }
                    }
                }

                return ClampVisionText(sb.ToString().Trim(), 5000);
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
            textBuilder.AppendLine("请只分析用户上传的图片和文字，不要调用工具，也不要执行任何 Grasshopper 操作。");
            textBuilder.AppendLine("用户发图不一定是为了根据图片建模；也可能是误发、修改建议、截图报错、内容解释、素材输入或其它相关要求。请先判断真实意图。");
            textBuilder.AppendLine("你可以利用随附的受控 Grasshopper 画布上下文帮助定位问题、判断图片中的修改意见对应到哪一段逻辑，但不能代替执行模型规划或执行修改。");
            textBuilder.AppendLine("输出必须严格按以下标题顺序组织，缺一不可：");
            textBuilder.AppendLine("【视觉事实】");
            textBuilder.AppendLine("只写可直接观察到的事实，不要推断。");
            textBuilder.AppendLine("【用户意图判断】");
            textBuilder.AppendLine("从建模参考、修改建议、问题诊断、内容解释、素材输入、错误上传、不明确中选择一个或多个，并给出一句理由。");
            textBuilder.AppendLine("【问题点】");
            textBuilder.AppendLine("如果用户是在提修改或纠错，明确区分最终结果问题、中间过程问题、标注/尺寸/比例/位置问题、数据或界面异常问题；如果不适用，写“不适用”。");
            textBuilder.AppendLine("【关联画布定位】");
            textBuilder.AppendLine("如果提供的画布上下文足以支持判断，请列出最可能相关的组件、输出、数据流或问题区域，并按优先级排序；每项包含：组件名/昵称/简短标识、关联原因、置信度（高/中/低）。若不足以判断，明确说明缺什么信息。");
            textBuilder.AppendLine("【执行模型下一步检查点】");
            textBuilder.AppendLine("只列最值得先检查的 1-3 项，例如某输出是否为 Null、某端口数据树结构、某参数是否与参考冲突、是否需要在某输出端接 Panel 查看实际值。");
            textBuilder.AppendLine("【与 Grasshopper 建模/画图相关的信息】");
            textBuilder.AppendLine("仅在相关时提取形状、结构、比例、材质、颜色、布局、约束和可执行线索；无关时写“无”。");
            textBuilder.AppendLine("【不确定性】");
            textBuilder.AppendLine("说明图片、文字和上下文之间的冲突，以及当前无法确定的点；不要编造。");
            textBuilder.AppendLine("【给执行模型的任务摘要】");
            textBuilder.AppendLine("用 3-6 句话说明用户真正要的动作、疑似问题区域、优先验证项、是否应先澄清。");

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
                        ["content"] = "你是图像理解与问题定位预处理器。你的职责不是规划修改方案，也不是执行 Grasshopper 操作，而是把用户图片、文字和受控画布上下文转写成可供执行模型直接使用的定位报告。你可以使用随附的受控 Grasshopper 画布上下文帮助定位问题、判断图片中的修改意见可能对应哪一段逻辑，但不能代替执行模型做最终规划、工具调用或画布修改。你下游有一个非多模态执行模型会根据你的分析和用户原始请求决定是否画图、建模、修改 Grasshopper 画布或向用户澄清；它没有直接看到原图。不要调用工具，不要执行 Grasshopper 操作，不要默认用户发图就是要建模。输出必须严格按用户消息中要求的标题顺序组织；区分视觉事实、推断、置信度和不确定性，语言要准确、简洁、可执行。"
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

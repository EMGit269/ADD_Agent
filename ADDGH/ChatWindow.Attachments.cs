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

            sb.AppendLine("以下图片理解来自视觉预处理模型，执行模型没有直接看到原图。请基于该分析和用户原始请求继续规划并执行 Grasshopper 操作。");
            sb.AppendLine();
            sb.AppendLine(visionAnalysis?.Trim() ?? "");
            return sb.ToString().Trim();
        }

        private static JObject BuildVisionPreprocessRequestBody(ProviderRuntimeSettings providerSettings, string input, List<AttachmentItem> attachments)
        {
            var content = new JArray();
            var textBuilder = new StringBuilder();
            textBuilder.AppendLine("请只分析用户上传的图片和文字，不要调用工具，也不要执行任何 Grasshopper 操作。");
            textBuilder.AppendLine("输出应包含：图片概述、关键对象/可读文字、用户可能意图、与 Grasshopper 建模相关的信息、约束/不确定性、给执行模型的简短任务摘要。");
            textBuilder.AppendLine("如果无法确定，请明确写出不确定点，不要编造。");

            if (!string.IsNullOrWhiteSpace(input))
            {
                textBuilder.AppendLine();
                textBuilder.AppendLine("用户原始请求：");
                textBuilder.AppendLine(input.Trim());
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
                        ["content"] = "你是图像理解预处理器。你的任务是把图片内容和用户意图转写成准确、简洁、可执行的中文分析，供另一个低成本 LLM 继续操作。不要调用工具，不要输出无关说明。"
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

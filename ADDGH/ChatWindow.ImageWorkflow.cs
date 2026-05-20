using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ADDGH
{
    public static partial class ChatWindow
    {
        private sealed class GeneratedImageRecord
        {
            public string Path { get; set; }
            public string MimeType { get; set; }
            public string Prompt { get; set; }
            public string Provider { get; set; }
            public string Model { get; set; }
            public string Intent { get; set; }
        }

        private sealed class AiImageExecutionResult
        {
            public bool Success { get; set; }
            public string Intent { get; set; }
            public string Provider { get; set; }
            public string Model { get; set; }
            public string Prompt { get; set; }
            public string Error { get; set; }
            public List<GeneratedImageRecord> Images { get; set; } = new List<GeneratedImageRecord>();
        }

        private static JObject NormalizeCanvasImageNode(JObject node)
        {
            if (node == null)
                return null;

            string nodeType = node["nodeType"]?.ToString();
            if (!string.Equals(nodeType, "image", StringComparison.OrdinalIgnoreCase))
                return node;

            JObject meta = node["meta"] as JObject ?? new JObject();
            string imagePath = meta["imagePath"]?.ToString() ?? "";
            string imageDataUrl = meta["imageDataUrl"]?.ToString() ?? "";
            string mimeType = meta["mimeType"]?.ToString() ?? node["mimeType"]?.ToString() ?? "image/png";

            if (string.IsNullOrWhiteSpace(imageDataUrl) && !string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
                meta["imageDataUrl"] = BuildImageDataUrl(imagePath, mimeType);

            node["meta"] = meta;
            return node;
        }

        private static async Task<string> ExecuteCreateAiImageAsync(string prompt, string intent, bool useUploadedImages, string aspectRatio, System.Threading.CancellationToken ct)
        {
            AiImageExecutionResult result = await RunAiImageGenerationAsync(prompt, intent, useUploadedImages, aspectRatio, ct).ConfigureAwait(false);
            return SerializeAiImageExecutionResult(result);
        }

        private static async Task<AiImageExecutionResult> RunAiImageGenerationAsync(string prompt, string intent, bool useUploadedImages, string aspectRatio, System.Threading.CancellationToken ct)
        {
            string normalizedIntent = string.Equals(intent, "edit", StringComparison.OrdinalIgnoreCase) ? "edit" : "generate";
            var providerSettings = GetImageProviderRuntimeSettings();
            var outcome = new AiImageExecutionResult
            {
                Success = false,
                Intent = normalizedIntent,
                Provider = providerSettings?.Config?.DisplayName ?? "",
                Model = providerSettings?.ModelName ?? "",
                Prompt = prompt ?? "",
                Error = ""
            };

            if (string.IsNullOrWhiteSpace(providerSettings.ApiKey))
            {
                outcome.Error = BuildProviderDiagnostic(providerSettings, "图片生成失败：请先配置图片生成模型的 API Key。");
                return outcome;
            }

            var sourceImages = useUploadedImages
                ? (_currentTurnAttachments ?? new List<AttachmentItem>()).Where(a => a.Kind == AttachmentKind.Image && !string.IsNullOrEmpty(a.Base64)).ToList()
                : new List<AttachmentItem>();

            if (normalizedIntent == "edit" && sourceImages.Count != 1)
            {
                outcome.Error = sourceImages.Count == 0
                    ? "图片编辑需要当前轮恰好上传 1 张图片。"
                    : "v1 图片编辑仅支持单图编辑，请只保留 1 张原图。";
                return outcome;
            }

            HttpResponseMessage response = null;
            string usedEndpoint = null;
            try
            {
                if (normalizedIntent == "edit")
                {
                    usedEndpoint = BuildImageEndpoint(providerSettings.BaseUrl, true);
                    response = await SendImageEditRequestAsync(providerSettings, prompt, sourceImages[0], aspectRatio, usedEndpoint, ct).ConfigureAwait(false);
                }
                else
                {
                    usedEndpoint = BuildImageEndpoint(providerSettings.BaseUrl, false);
                    response = await SendImageGenerationRequestAsync(providerSettings, prompt, sourceImages, aspectRatio, usedEndpoint, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                outcome.Error = BuildProviderDiagnostic(providerSettings, "图片生成失败：请求未能发送到图片模型服务，" + ex.GetType().Name, FormatExceptionChain(ex), usedEndpoint);
                return outcome;
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                string errPreview = response == null ? "no_response" : await SafeReadErrorAsync(response).ConfigureAwait(false);
                outcome.Error = BuildProviderDiagnostic(
                    providerSettings,
                    "图片生成失败：图片模型服务返回 HTTP " + (response == null ? "?" : ((int)response.StatusCode).ToString()) + " " + (response?.ReasonPhrase ?? ""),
                    errPreview,
                    usedEndpoint);
                return outcome;
            }

            string responseText = await ReadResponseTextAsync(response, ct).ConfigureAwait(false);
            try
            {
                outcome.Images = await SaveGeneratedImagesFromResponseAsync(responseText, prompt, normalizedIntent, providerSettings, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                outcome.Error = BuildProviderDiagnostic(providerSettings, "图片生成失败：响应解析或图片落盘失败，" + ex.GetType().Name, ex.Message, usedEndpoint);
                return outcome;
            }

            if (outcome.Images.Count == 0)
            {
                outcome.Error = BuildProviderDiagnostic(providerSettings, "图片生成失败：接口返回成功，但未找到可保存的图片结果。", responseText, usedEndpoint);
                return outcome;
            }

            outcome.Success = true;
            outcome.Error = "";
            return outcome;
        }

        private static string BuildImageEndpoint(string baseUrl, bool isEdit)
        {
            string raw = (baseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(raw))
                raw = "https://api.openai.com/v1";

            if (raw.EndsWith("/v1/images/generations", StringComparison.OrdinalIgnoreCase) ||
                raw.EndsWith("/v1/images/edits", StringComparison.OrdinalIgnoreCase))
            {
                int suffixIndex = raw.LastIndexOf("/v1/images", StringComparison.OrdinalIgnoreCase);
                if (suffixIndex > 0)
                    raw = raw.Substring(0, suffixIndex);
            }

            if (raw.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                return raw + (isEdit ? "/images/edits" : "/images/generations");

            return raw + (isEdit ? "/v1/images/edits" : "/v1/images/generations");
        }

        private static async Task<HttpResponseMessage> SendImageGenerationRequestAsync(ProviderRuntimeSettings providerSettings, string prompt, List<AttachmentItem> sourceImages, string aspectRatio, string endpoint, System.Threading.CancellationToken ct)
        {
            var body = new JObject
            {
                ["model"] = providerSettings.ModelName,
                ["prompt"] = prompt ?? "",
                ["response_format"] = "b64_json"
            };

            if (!string.IsNullOrWhiteSpace(aspectRatio))
                body["aspect_ratio"] = aspectRatio.Trim();

            if (sourceImages != null && sourceImages.Count > 0)
                body["image"] = new JArray(sourceImages.Select(image => $"data:{image.MimeType};base64,{image.Base64}"));

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", providerSettings.ApiKey);
            request.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
            return await GetConfiguredHttpClient(providerSettings).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }

        private static async Task<HttpResponseMessage> SendImageEditRequestAsync(ProviderRuntimeSettings providerSettings, string prompt, AttachmentItem sourceImage, string aspectRatio, string endpoint, System.Threading.CancellationToken ct)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", providerSettings.ApiKey);

            var form = new MultipartFormDataContent();
            form.Add(new StringContent(providerSettings.ModelName ?? ""), "model");
            form.Add(new StringContent(prompt ?? ""), "prompt");
            form.Add(new StringContent("b64_json"), "response_format");
            if (!string.IsNullOrWhiteSpace(aspectRatio))
                form.Add(new StringContent(aspectRatio.Trim()), "aspect_ratio");

            byte[] bytes = await GetImageBytesFromAttachmentAsync(sourceImage, ct).ConfigureAwait(false);
            var imageContent = new ByteArrayContent(bytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(sourceImage.MimeType ?? "image/png");
            form.Add(imageContent, "image", sourceImage.FileName ?? "image.png");

            request.Content = form;
            return await GetConfiguredHttpClient(providerSettings).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }

        private static async Task<string> SafeReadErrorAsync(HttpResponseMessage response)
        {
            try
            {
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return "无法读取错误响应体：" + ex.Message;
            }
        }

        private static async Task<List<GeneratedImageRecord>> SaveGeneratedImagesFromResponseAsync(string responseText, string prompt, string intent, ProviderRuntimeSettings providerSettings, System.Threading.CancellationToken ct)
        {
            var result = new List<GeneratedImageRecord>();
            var root = JObject.Parse(responseText);
            var data = root["data"] as JArray ?? new JArray();
            int index = 0;
            foreach (var item in data.OfType<JObject>())
            {
                string mimeType = "image/png";
                byte[] bytes = null;

                if (!string.IsNullOrWhiteSpace(item["b64_json"]?.ToString()))
                {
                    bytes = DecodeImageBytes(item["b64_json"]?.ToString(), out string detectedMimeType);
                    mimeType = detectedMimeType ?? mimeType;
                }
                else if (!string.IsNullOrWhiteSpace(item["url"]?.ToString()))
                {
                    bytes = await DownloadImageBytesAsync(item["url"]?.ToString(), providerSettings, ct).ConfigureAwait(false);
                    mimeType = GuessMimeTypeFromUrl(item["url"]?.ToString()) ?? mimeType;
                }

                if (bytes == null || bytes.Length == 0)
                    continue;

                string path = SaveImageBytesToConversationPath(bytes, mimeType, index++);
                result.Add(new GeneratedImageRecord
                {
                    Path = path,
                    MimeType = mimeType,
                    Prompt = prompt ?? "",
                    Provider = providerSettings?.Config?.DisplayName ?? "",
                    Model = providerSettings?.ModelName ?? "",
                    Intent = intent
                });
            }

            return result;
        }

        private static async Task<byte[]> GetImageBytesFromAttachmentAsync(AttachmentItem sourceImage, System.Threading.CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(sourceImage?.Base64))
                return DecodeImageBytes(sourceImage.Base64, out _);

            if (!string.IsNullOrWhiteSpace(sourceImage?.Path) && File.Exists(sourceImage.Path))
                return File.ReadAllBytes(sourceImage.Path);

            return Array.Empty<byte>();
        }

        private static byte[] DecodeImageBytes(string raw, out string mimeType)
        {
            mimeType = "image/png";
            string value = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return Array.Empty<byte>();

            if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int commaIndex = value.IndexOf(',');
                if (commaIndex > 5)
                {
                    string header = value.Substring(5, commaIndex - 5);
                    string payload = value.Substring(commaIndex + 1);
                    string mime = header.Split(';').FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(mime))
                        mimeType = mime.Trim();
                    value = payload.Trim();
                }
            }

            value = value.Replace("\r", "").Replace("\n", "").Trim();
            return Convert.FromBase64String(value);
        }

        private static async Task<byte[]> DownloadImageBytesAsync(string url, ProviderRuntimeSettings providerSettings, System.Threading.CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(url))
                return Array.Empty<byte>();

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            using (var response = await GetConfiguredHttpClient(providerSettings).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        private static string GuessMimeTypeFromUrl(string url)
        {
            string lower = (url ?? "").ToLowerInvariant();
            if (lower.Contains(".jpg") || lower.Contains(".jpeg")) return "image/jpeg";
            if (lower.Contains(".webp")) return "image/webp";
            if (lower.Contains(".gif")) return "image/gif";
            if (lower.Contains(".bmp")) return "image/bmp";
            return "image/png";
        }

        private static string SaveImageBytesToConversationPath(byte[] bytes, string mimeType, int index)
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ADDGH",
                "generated-images",
                GetCurrentCanvasConversationId());
            Directory.CreateDirectory(dir);

            string extension = ".png";
            if (string.Equals(mimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase)) extension = ".jpg";
            else if (string.Equals(mimeType, "image/webp", StringComparison.OrdinalIgnoreCase)) extension = ".webp";
            else if (string.Equals(mimeType, "image/gif", StringComparison.OrdinalIgnoreCase)) extension = ".gif";
            else if (string.Equals(mimeType, "image/bmp", StringComparison.OrdinalIgnoreCase)) extension = ".bmp";

            string fileName = DateTime.Now.ToString("yyyyMMdd_HHmmssfff") + "_" + index + extension;
            string path = Path.Combine(dir, fileName);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static string SerializeAiImageExecutionResult(AiImageExecutionResult result)
        {
            return new JObject
            {
                ["success"] = result?.Success ?? false,
                ["intent"] = result?.Intent ?? "",
                ["provider"] = result?.Provider ?? "",
                ["model"] = result?.Model ?? "",
                ["prompt"] = result?.Prompt ?? "",
                ["savedImages"] = new JArray((result?.Images ?? new List<GeneratedImageRecord>()).Select(item => new JObject
                {
                    ["path"] = item.Path,
                    ["mimeType"] = item.MimeType,
                    ["prompt"] = item.Prompt,
                    ["provider"] = item.Provider,
                    ["model"] = item.Model,
                    ["intent"] = item.Intent
                })),
                ["error"] = result?.Error ?? ""
            }.ToString();
        }

        private static void ApplyAiImageToolResult(string toolResultJson)
        {
            if (string.IsNullOrWhiteSpace(toolResultJson))
                return;

            try
            {
                var root = JObject.Parse(toolResultJson);
                bool success = root["success"]?.ToObject<bool>() ?? false;
                if (!success)
                {
                    string error = root["error"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(error))
                        AppendQuietDiagnosticCard("图片生成", error);
                    return;
                }

                var savedImages = root["savedImages"] as JArray;
                if (savedImages == null || savedImages.Count == 0)
                    return;

                var generated = new List<GeneratedImageRecord>();
                foreach (var item in savedImages.OfType<JObject>())
                {
                    generated.Add(new GeneratedImageRecord
                    {
                        Path = item["path"]?.ToString(),
                        MimeType = item["mimeType"]?.ToString() ?? "image/png",
                        Prompt = item["prompt"]?.ToString() ?? root["prompt"]?.ToString() ?? "",
                        Provider = item["provider"]?.ToString() ?? root["provider"]?.ToString() ?? "",
                        Model = item["model"]?.ToString() ?? root["model"]?.ToString() ?? "",
                        Intent = item["intent"]?.ToString() ?? root["intent"]?.ToString() ?? ""
                    });
                }

                AppendGeneratedImageAssistantMessage(generated);
                SaveGeneratedImagesToCanvasSnapshot(generated);
            }
            catch (Exception ex)
            {
                AppendQuietDiagnosticCard("图片生成", "工具结果解析失败：" + ex.Message);
            }
        }

        private static void AppendGeneratedImageAssistantMessage(List<GeneratedImageRecord> generated)
        {
            if (generated == null || generated.Count == 0)
                return;

            var generatedImages = new JArray();
            foreach (var item in generated)
            {
                generatedImages.Add(new JObject
                {
                    ["path"] = item.Path,
                    ["mimeType"] = item.MimeType,
                    ["prompt"] = item.Prompt,
                    ["provider"] = item.Provider,
                    ["model"] = item.Model,
                    ["intent"] = item.Intent
                });
            }

            var messageNode = new JObject
            {
                ["role"] = "assistant",
                ["content"] = $"已生成 {generated.Count} 张图片。",
                ["generated_images"] = generatedImages
            };

            AppendBubble($"已生成 {generated.Count} 张图片。", false, false);
            AppendAssistantImageMessage(messageNode);
        }

        private static void SaveGeneratedImagesToCanvasSnapshot(List<GeneratedImageRecord> generated)
        {
            if (generated == null || generated.Count == 0)
                return;

            string conversationId = GetCurrentCanvasConversationId();
            JObject envelope = LoadCanvasConversationEnvelope(conversationId) ?? new JObject();
            JObject snapshot = envelope["snapshot"] as JObject ?? new JObject();
            snapshot["kind"] = "addgh-lightweight-canvas-v1";
            snapshot["viewport"] = snapshot["viewport"] as JObject ?? new JObject { ["x"] = 80, ["y"] = 90, ["z"] = 1.0 };
            var nodes = snapshot["nodes"] as JArray ?? new JArray();
            var connections = snapshot["connections"] as JArray ?? new JArray();
            var typedNodes = new JArray();

            foreach (var node in nodes.OfType<JObject>())
            {
                string sourceRef = node["sourceRef"]?.ToString() ?? "";
                if (sourceRef.StartsWith("input_prompt:", StringComparison.OrdinalIgnoreCase)
                    || sourceRef.StartsWith("input_image:", StringComparison.OrdinalIgnoreCase)
                    || sourceRef.StartsWith("generated_image:", StringComparison.OrdinalIgnoreCase))
                    typedNodes.Add(NormalizeCanvasImageNode((JObject)node.DeepClone()));
            }

            double centerX = 160;
            double centerY = 120;
            if (snapshot["viewport"] is JObject viewport)
            {
                centerX = viewport["x"]?.ToObject<double?>() ?? 80;
                centerY = viewport["y"]?.ToObject<double?>() ?? 90;
            }

            for (int i = 0; i < generated.Count; i++)
            {
                var item = generated[i];
                string sourceRef = $"generated_image:{conversationId}:{DateTime.UtcNow.Ticks}:{i}";
                typedNodes.Add(NormalizeCanvasImageNode(new JObject
                {
                    ["id"] = "node:" + sourceRef.Replace(":", "_"),
                    ["sourceRef"] = sourceRef,
                    ["nodeType"] = "image",
                    ["x"] = centerX + i * 380,
                    ["y"] = centerY,
                    ["w"] = 360,
                    ["h"] = 260,
                    ["meta"] = new JObject
                    {
                        ["sourceRef"] = sourceRef,
                        ["nodeType"] = "image",
                        ["title"] = "Generated Image",
                        ["summary"] = "AI image result",
                        ["body"] = item.Prompt ?? "",
                        ["imagePath"] = item.Path,
                        ["imageDataUrl"] = BuildImageDataUrl(item.Path, item.MimeType),
                        ["prompt"] = item.Prompt ?? "",
                        ["provider"] = item.Provider ?? "",
                        ["model"] = item.Model ?? "",
                        ["intent"] = item.Intent ?? "",
                        ["ports"] = new JArray
                        {
                            new JObject { ["id"] = "in", ["label"] = "Input", ["direction"] = "input", ["dataType"] = "image", ["slot"] = 0 },
                            new JObject { ["id"] = "out", ["label"] = "Output", ["direction"] = "output", ["dataType"] = "image", ["slot"] = 1 }
                        },
                        ["w"] = 360,
                        ["h"] = 260
                    }
                }));
            }

            snapshot["nodes"] = typedNodes;
            snapshot["connections"] = connections;
            SaveCanvasConversationSnapshot(conversationId, snapshot, envelope["cardMetaPatches"]);
            NotifyCanvasConversationChanged(true);
        }

        private static void SavePromptAndInputImagesToCanvasSnapshot(string promptText, List<AttachmentItem> attachments)
        {
            string conversationId = GetCurrentCanvasConversationId();
            JObject envelope = LoadCanvasConversationEnvelope(conversationId) ?? new JObject();
            JObject snapshot = envelope["snapshot"] as JObject ?? new JObject();
            snapshot["kind"] = "addgh-lightweight-canvas-v1";
            snapshot["viewport"] = snapshot["viewport"] as JObject ?? new JObject { ["x"] = 80, ["y"] = 90, ["z"] = 1.0 };

            var nodes = snapshot["nodes"] as JArray ?? new JArray();
            var connections = snapshot["connections"] as JArray ?? new JArray();
            var freshNodes = new JArray();

            foreach (var node in nodes.OfType<JObject>())
            {
                string sourceRef = node["sourceRef"]?.ToString() ?? "";
                if (!sourceRef.StartsWith("input_prompt:", StringComparison.OrdinalIgnoreCase)
                    && !sourceRef.StartsWith("input_image:", StringComparison.OrdinalIgnoreCase)
                    && !sourceRef.StartsWith("generated_image:", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            double originX = 120;
            double originY = 100;

            if (!string.IsNullOrWhiteSpace(promptText))
            {
                string promptSourceRef = "input_prompt:" + conversationId;
                freshNodes.Add(NormalizeCanvasImageNode(new JObject
                {
                    ["id"] = "node:" + promptSourceRef.Replace(":", "_"),
                    ["sourceRef"] = promptSourceRef,
                    ["nodeType"] = "prompt",
                    ["x"] = originX,
                    ["y"] = originY,
                    ["w"] = 320,
                    ["h"] = 180,
                    ["meta"] = new JObject
                    {
                        ["sourceRef"] = promptSourceRef,
                        ["nodeType"] = "prompt",
                        ["title"] = "Prompt",
                        ["summary"] = "User prompt",
                        ["body"] = promptText ?? "",
                        ["prompt"] = promptText ?? "",
                        ["ports"] = new JArray
                        {
                            new JObject { ["id"] = "out", ["label"] = "Prompt", ["direction"] = "output", ["dataType"] = "text", ["slot"] = 0 }
                        },
                        ["w"] = 320,
                        ["h"] = 180
                    }
                }));
            }

            var imageAttachments = (attachments ?? new List<AttachmentItem>())
                .Where(a => a != null && a.Kind == AttachmentKind.Image && !string.IsNullOrWhiteSpace(a.Path) && File.Exists(a.Path))
                .ToList();

            for (int i = 0; i < imageAttachments.Count; i++)
            {
                var item = imageAttachments[i];
                string sourceRef = $"input_image:{conversationId}:{i}";
                freshNodes.Add(NormalizeCanvasImageNode(new JObject
                {
                    ["id"] = "node:" + sourceRef.Replace(":", "_"),
                    ["sourceRef"] = sourceRef,
                    ["nodeType"] = "image",
                    ["x"] = originX + i * 380,
                    ["y"] = originY + 230,
                    ["w"] = 360,
                    ["h"] = 260,
                    ["meta"] = new JObject
                    {
                        ["sourceRef"] = sourceRef,
                        ["nodeType"] = "image",
                        ["title"] = "Input Image",
                        ["summary"] = "User reference image",
                        ["body"] = promptText ?? "",
                        ["imagePath"] = item.Path,
                        ["imageDataUrl"] = BuildImageDataUrl(item.Path, item.MimeType),
                        ["prompt"] = promptText ?? "",
                        ["intent"] = "input",
                        ["ports"] = new JArray
                        {
                            new JObject { ["id"] = "in", ["label"] = "Input", ["direction"] = "input", ["dataType"] = "image", ["slot"] = 0 },
                            new JObject { ["id"] = "out", ["label"] = "Output", ["direction"] = "output", ["dataType"] = "image", ["slot"] = 1 }
                        },
                        ["w"] = 360,
                        ["h"] = 260
                    }
                }));
            }

            snapshot["nodes"] = freshNodes;
            snapshot["connections"] = connections;
            SaveCanvasConversationSnapshot(conversationId, snapshot, envelope["cardMetaPatches"]);
            NotifyCanvasConversationChanged(true);
        }
    }
}

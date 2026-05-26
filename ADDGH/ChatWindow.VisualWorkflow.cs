using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ADDGH
{
    public static partial class ChatWindow
    {
        private static string _visualReviewTargetSourceId = null;
        private static int _visualReviewTargetOutputIndex = 0;

        private static void ResetVisualWorkflowState(string input, List<AttachmentItem> attachmentsToSend)
        {
            bool hasImageAttachments = attachmentsToSend.Any(a => a.Kind == AttachmentKind.Image && !string.IsNullOrEmpty(a.Base64));
            bool enableVisualFlow = _activeImageIntentRoute == ImageIntentRoute.VisualModeling;
            _currentTurnHadToolExecution = false;
            _finalVisualReviewCompleted = false;
            _finalVisualReviewAttempted = false;
            _hasActiveVisionInputContext = hasImageAttachments && enableVisualFlow;
            _pendingFinalVisualReview = hasImageAttachments && enableVisualFlow && _agentMode == AgentMode.Create;
            _finalVisualReviewSourceInput = input;
            _finalVisualReviewSourceImages = hasImageAttachments
                ? attachmentsToSend
                    .Where(a => a.Kind == AttachmentKind.Image && !string.IsNullOrEmpty(a.Base64))
                    .ToList()
                : new List<AttachmentItem>();
            _visualReviewPreviewComponentId = null;
            _visualReviewTargetSourceId = null;
            _visualReviewTargetOutputIndex = 0;
        }

        private static bool IsVisionToolContextActive()
        {
            return _hasActiveVisionInputContext
                || (_finalVisualReviewSourceImages != null && _finalVisualReviewSourceImages.Any(a => a.Kind == AttachmentKind.Image && !string.IsNullOrEmpty(a.Base64)));
        }

        private static bool CanUseViewportCaptureTool()
        {
            return IsVisionToolContextActive();
        }

        private static async Task<bool> PrepareImageDrivenExecutionContextAsync(string input, List<AttachmentItem> attachmentsToSend, System.Threading.CancellationToken ct)
        {
            if (_activeImageIntentRoute != ImageIntentRoute.VisualModeling)
                return true;

            if (!attachmentsToSend.Any(a => a.Kind == AttachmentKind.Image && !string.IsNullOrEmpty(a.Base64)))
                return true;

            string visionAnalysis = await PreprocessImageAttachmentsAsync(input, attachmentsToSend, ct);
            if (string.IsNullOrWhiteSpace(visionAnalysis))
                return false;

            _messages.Add(new { role = "user", content = BuildVisionExecutionUserText(input, attachmentsToSend, visionAnalysis) });
            EnforceChatHistoryLimit();
            SyncActiveHistoryConversation();
            return true;
        }

        private static bool ShouldRunFinalVisualReviewThisRound(JArray fullToolCalls)
        {
            if (_agentMode == AgentMode.Plan)
                return false;

            return (fullToolCalls == null || fullToolCalls.Count == 0)
                && _pendingFinalVisualReview
                && !_finalVisualReviewCompleted
                && !_finalVisualReviewAttempted
                && _currentTurnHadToolExecution;
        }

        private static async Task<ApiResponse> TryContinueWithFinalVisualReviewAsync(string apiKey, int depth, string fullContent, System.Threading.CancellationToken ct)
        {
            if (!ShouldRunFinalVisualReviewThisRound(null))
                return null;

            _finalVisualReviewAttempted = true;
            string finalVisualReview = await RunFinalVisualReviewAsync(fullContent, ct);
            if (string.IsNullOrWhiteSpace(finalVisualReview))
                return null;

            _finalVisualReviewCompleted = true;
            _pendingFinalVisualReview = false;
            _messages.Add(new JObject
            {
                ["role"] = "assistant",
                ["content"] = fullContent ?? ""
            });
            _messages.Add(new { role = "user", content = BuildFinalVisualReviewExecutionUserText(fullContent, finalVisualReview) });
            EnforceChatHistoryLimit();
            SyncActiveHistoryConversation();
            ct.ThrowIfCancellationRequested();
            return await CallLLMAPI(apiKey, depth + 1, ct);
        }

        private static bool EnsureVisualReviewPreviewReady()
        {
            if (!string.IsNullOrWhiteSpace(_visualReviewPreviewComponentId))
                return true;

            if (string.IsNullOrWhiteSpace(_visualReviewTargetSourceId))
            {
                AppendQuietDiagnosticCard("最终视觉复核", "未记录最终目标输出，无法自动创建干净的预览出口。");
                return false;
            }

            string prepareResult = ExecutePrepareVisualReviewPreview(_visualReviewTargetSourceId, _visualReviewTargetOutputIndex, "VisualReviewPreview");
            if (string.IsNullOrWhiteSpace(prepareResult) || prepareResult.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            {
                AppendQuietDiagnosticCard("最终视觉复核", string.IsNullOrWhiteSpace(prepareResult)
                    ? "自动准备视觉预览出口失败。"
                    : prepareResult);
                return false;
            }

            return true;
        }

        private static async Task<string> RunFinalVisualReviewAsync(string priorDraft, System.Threading.CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!_pendingFinalVisualReview || _finalVisualReviewCompleted || !_currentTurnHadToolExecution)
                return null;

            var sourceImages = _finalVisualReviewSourceImages ?? new List<AttachmentItem>();
            if (sourceImages.Count == 0)
                return null;

            EnsureVisualReviewPreviewReady();

            try
            {
                string previewCleanup = ExecuteSetAllCSharpScriptPreviews(false);
            }
            catch (Exception ex)
            {
                AddGhLog.Debug("Final visual review preview cleanup failed: " + ex.Message);
            }

            string captureJson = ExecuteCaptureRhinoViewport("auto", 1600, 900, 0.12);
            if (string.IsNullOrWhiteSpace(captureJson) || captureJson.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            {
                AppendQuietDiagnosticCard("最终视觉复核", string.IsNullOrWhiteSpace(captureJson) ? "截图失败。" : captureJson);
                return null;
            }

            string screenshotPath = null;
            try
            {
                screenshotPath = JObject.Parse(captureJson)["path"]?.ToString();
            }
            catch (Exception ex)
            {
                AppendQuietDiagnosticCard("最终视觉复核", "截图结果解析失败: " + ex.Message);
                return null;
            }

            if (string.IsNullOrWhiteSpace(screenshotPath) || !File.Exists(screenshotPath))
            {
                AppendQuietDiagnosticCard("最终视觉复核", "截图结果缺少有效文件路径。");
                return null;
            }

            var providerSettings = GetVisionProviderRuntimeSettings();
            if (string.IsNullOrWhiteSpace(providerSettings.ApiKey))
            {
                string diag = BuildProviderDiagnostic(providerSettings, "最终视觉复核失败：请先配置 " + providerSettings.Config.DisplayName + " 的 API Key。");
                AppendQuietDiagnosticCard("最终视觉复核", diag);
                return null;
            }

            JObject requestBody = BuildFinalVisualReviewRequestBody(
                providerSettings,
                _finalVisualReviewSourceInput,
                sourceImages,
                screenshotPath,
                priorDraft);

            HttpResponseMessage response = null;
            string usedEndpoint = null;
            string lastEndpointError = null;
            DateTime startTime = DateTime.Now;
            try
            {
                ShowThinkingAnimation("复核中...");
                foreach (var endpoint in BuildEndpointCandidates(providerSettings.BaseUrl))
                {
                    ct.ThrowIfCancellationRequested();
                    usedEndpoint = endpoint.Url;
                    response = await SendProviderRequestAsync(providerSettings, requestBody, endpoint.Url, ct);
                    if (response.IsSuccessStatusCode)
                        break;

                    string errPreview = "";
                    try { errPreview = await response.Content.ReadAsStringAsync(); }
                    catch (Exception readEx) { errPreview = "无法读取错误响应体：" + readEx.Message; }

                    lastEndpointError = "HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + "\n" + ClampDiagDetail(errPreview, 900);
                    if (!ShouldTryNextEndpoint(response.StatusCode))
                    {
                        AppendQuietDiagnosticCard("最终视觉复核",
                            BuildProviderDiagnostic(providerSettings, "最终视觉复核失败：视觉模型服务返回 HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase, errPreview, endpoint.Url));
                        return null;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppendQuietDiagnosticCard("最终视觉复核",
                    BuildProviderDiagnostic(providerSettings, "最终视觉复核失败：请求未能发送到视觉模型服务，" + ex.GetType().Name, FormatExceptionChain(ex), usedEndpoint));
                return null;
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                AppendQuietDiagnosticCard("最终视觉复核",
                    BuildProviderDiagnostic(providerSettings, "最终视觉复核失败：视觉模型服务没有返回成功响应。", lastEndpointError, usedEndpoint));
                return null;
            }

            string responseJson = await ReadResponseTextAsync(response, ct);
            if (!TryParseAssistantMessageFromResponse(responseJson, out JObject messageNode, out string parseError))
            {
                AppendQuietDiagnosticCard("最终视觉复核",
                    BuildProviderDiagnostic(providerSettings, "最终视觉复核失败：视觉模型响应不是可解析的聊天响应，" + parseError, responseJson, usedEndpoint));
                return null;
            }

            string analysis = messageNode["content"]?.ToString();
            if (string.IsNullOrWhiteSpace(analysis))
                analysis = messageNode["reasoning_content"]?.ToString();

            if (string.IsNullOrWhiteSpace(analysis))
            {
                AppendQuietDiagnosticCard("最终视觉复核",
                    BuildProviderDiagnostic(providerSettings, "最终视觉复核失败：视觉模型返回成功，但没有输出复核结论。", responseJson, usedEndpoint));
                return null;
            }

            double durationSeconds = (DateTime.Now - startTime).TotalSeconds;
            AppendCollapsibleBubble(analysis.Trim(), "最终视觉复核 " + Math.Round(durationSeconds, 1) + "s", "👁");
            return analysis.Trim();
        }
    }
}

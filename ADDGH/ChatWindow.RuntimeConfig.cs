using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;

namespace ADDGH
{
    public static partial class ChatWindow
    {
        private class ApiResponse { public string Content; public string Reasoning; }

        private enum AttachmentKind
        {
            Image,
            Text,
            Document,
            Unsupported
        }

        private class AttachmentItem
        {
            public string Path { get; set; }
            public string FileName { get; set; }
            public string MimeType { get; set; }
            public AttachmentKind Kind { get; set; }
            public string Base64 { get; set; }
            public string ExtractedText { get; set; }
            public long SizeBytes { get; set; }
            public string Error { get; set; }
        }

        private static AttachmentItem CloneAttachmentItem(AttachmentItem source)
        {
            if (source == null) return null;
            return new AttachmentItem
            {
                Path = source.Path,
                FileName = source.FileName,
                MimeType = source.MimeType,
                Kind = source.Kind,
                Base64 = source.Base64,
                ExtractedText = source.ExtractedText,
                SizeBytes = source.SizeBytes,
                Error = source.Error
            };
        }

        private static List<AttachmentItem> CloneAttachments(IEnumerable<AttachmentItem> source)
        {
            return (source ?? Enumerable.Empty<AttachmentItem>())
                .Select(CloneAttachmentItem)
                .Where(a => a != null)
                .ToList();
        }

        private class ChatHistoryConversation
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public DateTime CreatedAtUtc { get; set; }
            public DateTime UpdatedAtUtc { get; set; }
            public JArray Messages { get; set; }
        }

        private class ModelProviderConfig
        {
            public string ProviderId { get; set; }
            public string DisplayName { get; set; }
            public string DefaultBaseUrl { get; set; }
            public string DefaultModel { get; set; }
            public bool SupportsTools { get; set; } = true;
            public bool SupportsVision { get; set; } = true;
            public string DefaultReasoningEffort { get; set; }
            public bool EnableThinking { get; set; }
            public string ImageContentFormat { get; set; } = "image_url";
        }

        private class ProviderRuntimeSettings
        {
            public ModelProviderConfig Config { get; set; }
            public string ApiKey { get; set; }
            public string BaseUrl { get; set; }
            public string ModelName { get; set; }
        }

        private class EndpointCandidate
        {
            public string Url { get; set; }
            public bool IsFallback { get; set; }
        }

        private static List<ModelProviderConfig> GetProviderConfigs()
        {
            return new List<ModelProviderConfig>
            {
                new ModelProviderConfig
                {
                    ProviderId = "deepseek",
                    DisplayName = "DeepSeek",
                    DefaultBaseUrl = "https://api.deepseek.com/chat/completions",
                    DefaultModel = "deepseek-v4-flash",
                    EnableThinking = true,
                    DefaultReasoningEffort = "high"
                },
                new ModelProviderConfig
                {
                    ProviderId = "seed",
                    DisplayName = "Seed / 火山方舟",
                    DefaultBaseUrl = "https://ark.cn-beijing.volces.com/api/v3/chat/completions",
                    DefaultModel = "doubao-seed-2-0-lite-260215",
                    DefaultReasoningEffort = "medium"
                },
                new ModelProviderConfig
                {
                    ProviderId = "qwen",
                    DisplayName = "Qwen / 通义千问",
                    DefaultBaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions",
                    DefaultModel = "qwen3.6-plus"
                },
                new ModelProviderConfig
                {
                    ProviderId = "kimi",
                    DisplayName = "Kimi / Moonshot",
                    DefaultBaseUrl = "https://api.moonshot.cn/v1/chat/completions",
                    DefaultModel = "kimi-k2.6"
                },
                new ModelProviderConfig
                {
                    ProviderId = "gemini-flash",
                    DisplayName = "Gemini 3 Flash",
                    DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
                    DefaultModel = "gemini-3-flash-preview"
                },
                new ModelProviderConfig
                {
                    ProviderId = "gemini-pro",
                    DisplayName = "Gemini 3.1 Pro",
                    DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
                    DefaultModel = "gemini-3.1-pro-preview"
                },
                new ModelProviderConfig
                {
                    ProviderId = "openai",
                    DisplayName = "OpenAI / GPT 5.5",
                    DefaultBaseUrl = "https://api.openai.com/v1/chat/completions",
                    DefaultModel = "gpt-5.5-medium"
                },
                new ModelProviderConfig
                {
                    ProviderId = "custom",
                    DisplayName = "Custom",
                    DefaultBaseUrl = "https://api.deepseek.com/chat/completions",
                    DefaultModel = "deepseek-v4-flash"
                }
            };
        }

        private static ModelProviderConfig GetProviderConfig(string providerId)
        {
            var config = GetProviderConfigs().FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
            return config ?? GetProviderConfigs().First();
        }

        private static string GetCurrentProviderId()
        {
            return Grasshopper.Instances.Settings.GetValue("AI_CurrentProvider", "deepseek");
        }

        private static string GetCurrentVisionProviderId()
        {
            return Grasshopper.Instances.Settings.GetValue("AI_VisionProvider", "qwen");
        }

        private static string GetProviderSettingKey(string providerId, string name)
        {
            return $"AI_{providerId}_{name}";
        }

        private static string ReadResolvedApiKey(string providerId)
        {
            string dpapiEnc = Grasshopper.Instances.Settings.GetValue(GetProviderSettingKey(providerId, "API_Key_DPAPI"), "");
            if (!string.IsNullOrWhiteSpace(dpapiEnc) && ApiCredentialStore.TryUnprotectFromBase64(dpapiEnc, out string dec) && dec != null)
                return dec;

            string per = Grasshopper.Instances.Settings.GetValue(GetProviderSettingKey(providerId, "API_Key"), "");
            string legacy = providerId.Equals("deepseek", StringComparison.OrdinalIgnoreCase)
                ? Grasshopper.Instances.Settings.GetValue("AI_API_Key", "")
                : "";
            return string.IsNullOrWhiteSpace(per) ? legacy : per;
        }

        private static void PersistApiKey(string providerId, string apiKeyPlain)
        {
            string key = apiKeyPlain ?? "";
            if (string.IsNullOrEmpty(key))
            {
                Grasshopper.Instances.Settings.SetValue(GetProviderSettingKey(providerId, "API_Key_DPAPI"), "");
                Grasshopper.Instances.Settings.SetValue(GetProviderSettingKey(providerId, "API_Key"), "");
                if (providerId.Equals("deepseek", StringComparison.OrdinalIgnoreCase))
                    Grasshopper.Instances.Settings.SetValue("AI_API_Key", "");
                return;
            }

            if (DeploymentOptions.UseDpapiForApiKeys)
            {
                if (ApiCredentialStore.TryProtectToBase64(key, out string enc))
                {
                    Grasshopper.Instances.Settings.SetValue(GetProviderSettingKey(providerId, "API_Key_DPAPI"), enc);
                    Grasshopper.Instances.Settings.SetValue(GetProviderSettingKey(providerId, "API_Key"), "");
                    if (providerId.Equals("deepseek", StringComparison.OrdinalIgnoreCase))
                        Grasshopper.Instances.Settings.SetValue("AI_API_Key", "");
                    return;
                }
                AddGhLog.Warn("ADDGH: DPAPI protect failed; storing API key as plaintext for provider " + providerId);
                AppendQuietDiagnosticCard("密钥存储",
                    "系统加密不可用，密钥已暂时以明文写入 Grasshopper 设置。详细信息见本地日志。");
            }

            Grasshopper.Instances.Settings.SetValue(GetProviderSettingKey(providerId, "API_Key_DPAPI"), "");
            Grasshopper.Instances.Settings.SetValue(GetProviderSettingKey(providerId, "API_Key"), key);
            if (providerId.Equals("deepseek", StringComparison.OrdinalIgnoreCase))
                Grasshopper.Instances.Settings.SetValue("AI_API_Key", key);
        }

        private static ProviderRuntimeSettings GetProviderRuntimeSettings()
        {
            return GetProviderRuntimeSettings(GetCurrentProviderId());
        }

        private static ProviderRuntimeSettings GetProviderRuntimeSettings(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
                providerId = GetCurrentProviderId();

            var config = GetProviderConfig(providerId);
            string legacyBaseUrl = providerId == "deepseek" ? Grasshopper.Instances.Settings.GetValue("AI_API_BaseUrl", config.DefaultBaseUrl) : config.DefaultBaseUrl;
            string legacyModel = providerId == "deepseek" ? Grasshopper.Instances.Settings.GetValue("AI_ModelName", config.DefaultModel) : config.DefaultModel;

            return new ProviderRuntimeSettings
            {
                Config = config,
                ApiKey = ReadResolvedApiKey(providerId),
                BaseUrl = Grasshopper.Instances.Settings.GetValue(GetProviderSettingKey(providerId, "BaseUrl"), legacyBaseUrl),
                ModelName = Grasshopper.Instances.Settings.GetValue(GetProviderSettingKey(providerId, "ModelName"), legacyModel)
            };
        }

        private static void PopulateProviderCombo()
        {
            var providers = GetProviderConfigs();

            if (_comboProvider != null)
            {
                _comboProvider.Items.Clear();
                foreach (var provider in providers)
                    _comboProvider.Items.Add(new ComboBoxItem { Content = provider.DisplayName, Tag = provider.ProviderId });
            }

            if (_comboVisionProvider != null)
            {
                _comboVisionProvider.Items.Clear();
                foreach (var provider in providers)
                    _comboVisionProvider.Items.Add(new ComboBoxItem { Content = provider.DisplayName, Tag = provider.ProviderId });
            }
        }

        private static string GetSelectedProviderId()
        {
            if (_comboProvider?.SelectedItem is ComboBoxItem item && item.Tag != null) return item.Tag.ToString();
            return GetCurrentProviderId();
        }

        private static void SelectProviderComboItem(string providerId)
        {
            if (_comboProvider == null) return;

            foreach (var item in _comboProvider.Items.OfType<ComboBoxItem>())
            {
                if ((item.Tag?.ToString() ?? "").Equals(providerId, StringComparison.OrdinalIgnoreCase))
                {
                    _comboProvider.SelectedItem = item;
                    return;
                }
            }

            if (_comboProvider.Items.Count > 0) _comboProvider.SelectedIndex = 0;
        }

        private static string GetSelectedVisionProviderId()
        {
            if (_comboVisionProvider?.SelectedItem is ComboBoxItem item && item.Tag != null) return item.Tag.ToString();
            return GetCurrentVisionProviderId();
        }

        private static void SelectVisionProviderComboItem(string providerId)
        {
            if (_comboVisionProvider == null) return;

            foreach (var item in _comboVisionProvider.Items.OfType<ComboBoxItem>())
            {
                if ((item.Tag?.ToString() ?? "").Equals(providerId, StringComparison.OrdinalIgnoreCase))
                {
                    _comboVisionProvider.SelectedItem = item;
                    return;
                }
            }

            foreach (var item in _comboVisionProvider.Items.OfType<ComboBoxItem>())
            {
                if ((item.Tag?.ToString() ?? "").Equals("qwen", StringComparison.OrdinalIgnoreCase))
                {
                    _comboVisionProvider.SelectedItem = item;
                    return;
                }
            }

            if (_comboVisionProvider.Items.Count > 0) _comboVisionProvider.SelectedIndex = 0;
        }

        private static void LoadProviderSettingsToUI(string providerId)
        {
            if (_txtApiKey == null || _txtApiBaseUrl == null || _txtModel == null) return;

            _isLoadingProviderSettings = true;
            try
            {
                var config = GetProviderConfig(providerId);
                string legacyBaseUrl = providerId == "deepseek" ? Grasshopper.Instances.Settings.GetValue("AI_API_BaseUrl", config.DefaultBaseUrl) : config.DefaultBaseUrl;
                string legacyModel = providerId == "deepseek" ? Grasshopper.Instances.Settings.GetValue("AI_ModelName", config.DefaultModel) : config.DefaultModel;

                _txtApiKey.Text = ReadResolvedApiKey(providerId);
                _txtApiBaseUrl.Text = Grasshopper.Instances.Settings.GetValue(GetProviderSettingKey(providerId, "BaseUrl"), legacyBaseUrl);
                _txtModel.Text = Grasshopper.Instances.Settings.GetValue(GetProviderSettingKey(providerId, "ModelName"), legacyModel);
            }
            finally
            {
                _isLoadingProviderSettings = false;
            }
        }

        private static void SaveSelectedProviderSettings()
        {
            string providerId = GetSelectedProviderId();
            Grasshopper.Instances.Settings.SetValue("AI_CurrentProvider", providerId);
            PersistApiKey(providerId, _txtApiKey?.Text);
            if (_txtApiBaseUrl != null) Grasshopper.Instances.Settings.SetValue(GetProviderSettingKey(providerId, "BaseUrl"), _txtApiBaseUrl.Text);
            if (_txtModel != null) Grasshopper.Instances.Settings.SetValue(GetProviderSettingKey(providerId, "ModelName"), _txtModel.Text);

            // Keep legacy URL/model keys populated so older builds can still read defaults for DeepSeek.
            if (_txtApiBaseUrl != null) Grasshopper.Instances.Settings.SetValue("AI_API_BaseUrl", _txtApiBaseUrl.Text);
            if (_txtModel != null) Grasshopper.Instances.Settings.SetValue("AI_ModelName", _txtModel.Text);
        }

        private static void SaveSelectedVisionProviderSetting()
        {
            Grasshopper.Instances.Settings.SetValue("AI_VisionProvider", GetSelectedVisionProviderId());
        }
    }
}

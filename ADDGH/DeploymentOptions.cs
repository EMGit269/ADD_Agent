using System;

namespace ADDGH
{
    /// <summary>
    /// 运行环境与分发策略相关的默认值。内测或调试可通过环境变量放宽策略。
    /// </summary>
    public static class DeploymentOptions
    {
        /// <summary>
        /// 设为 "1" 时不再使用 DPAPI 加密 API Key，仍写入 Grasshopper 明文设置（仅建议本地调试）。
        /// </summary>
        public static bool UseDpapiForApiKeys =>
            !string.Equals(Environment.GetEnvironmentVariable("ADDGH_PLAINTEXT_API_KEYS"), "1", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 设为 "1" 时写入调试级日志（噪声较大）。
        /// </summary>
        public static bool EnableVerboseLogging =>
            string.Equals(Environment.GetEnvironmentVariable("ADDGH_LOG_VERBOSE"), "1", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 设为 "1" 时，<see cref="AddGhLog.Error"/> 与 <see cref="AddGhLog.UserAlert"/> 会弹出简单 MessageBox（临时排错；平时勿开）。
        /// </summary>
        public static bool EnableTemporaryErrorPopup =>
            string.Equals(Environment.GetEnvironmentVariable("ADDGH_ERROR_POPUP"), "1", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 会话内存中保留的最大消息条数（含 system / user / assistant / tool）；超出则从紧邻 system 前缀之后丢弃最早条目。
        /// </summary>
        public const int MaxPersistedChatMessages = 320;
    }
}

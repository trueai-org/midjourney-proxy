using System.Reflection;
using System.Text.Json.Serialization;

namespace Midjourney.Base.Models
{

    /// <summary>
    /// 升级状态枚举
    /// </summary>
    public enum UpgradeStatus
    {
        /// <summary>
        /// 空闲状态
        /// </summary>
        Idle,

        /// <summary>
        /// 检查更新中
        /// </summary>
        Checking,

        /// <summary>
        /// 正在下载
        /// </summary>
        Downloading,

        /// <summary>
        /// 下载完成，等待重启
        /// </summary>
        ReadyToRestart,

        /// <summary>
        /// 升级成功
        /// </summary>
        Success,

        /// <summary>
        /// 升级失败
        /// </summary>
        Failed
    }

    /// <summary>
    /// 升级信息
    /// </summary>
    public class UpgradeInfo
    {
        /// <summary>
        /// 状态
        /// </summary>
        public UpgradeStatus Status { get; set; } = UpgradeStatus.Idle;

        /// <summary>
        /// 进度百分比（0-100）
        /// </summary>
        public int Progress { get; set; } = 0;

        /// <summary>
        /// 状态消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 当前版本
        /// </summary>
        public string CurrentVersion { get; set; } = $"v{(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version}";

        /// <summary>
        /// 最新版本
        /// </summary>
        public string LatestVersion { get; set; } = string.Empty;

        /// <summary>
        /// 版本描述
        /// </summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>
        /// 是否有可用更新
        /// </summary>
        public bool HasUpdate { get; set; } = false;

        /// <summary>
        /// 是否支持当前平台
        /// </summary>
        public bool SupportedPlatform { get; set; } = false;

        /// <summary>
        /// 当前平台
        /// </summary>
        public string Platform { get; set; } = string.Empty;

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// GitHub Release 响应模型
    /// </summary>
    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("published_at")]
        public DateTime? PublishedAt { get; set; }

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    /// <summary>
    /// GitHub Asset 模型
    /// </summary>
    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    /// <summary>
    /// 版本信息
    /// </summary>
    public class VersionInfo
    {
        /// <summary>
        /// 版本号
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// 发布时间
        /// </summary>
        public DateTime? PublishedAt { get; set; }

        /// <summary>
        /// 版本描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 是否为预发布版本
        /// </summary>
        public bool IsPrerelease { get; set; }
    }
}

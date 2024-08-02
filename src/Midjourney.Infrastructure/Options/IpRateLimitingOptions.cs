namespace Midjourney.Infrastructure.Options
{
    /// <summary>
    /// 表示 IP 限流配置选项。
    /// </summary>
    public class IpRateLimitingOptions
    {
        /// <summary>
        /// 是否启用限流。
        /// </summary>
        public bool Enable { get; set; }

        /// <summary>
        /// 白名单列表。
        /// </summary>
        public List<string> Whitelist { get; set; } = new List<string>();

        /// <summary>
        /// 黑名单列表。
        /// </summary>
        public List<string> Blacklist { get; set; } = new List<string>();

        /// <summary>
        /// IP 限流规则。
        /// </summary>
        public Dictionary<string, Dictionary<int, int>> IpRules { get; set; } = new Dictionary<string, Dictionary<int, int>>();

        /// <summary>
        /// IP 段限流规则。
        /// </summary>
        public Dictionary<string, Dictionary<int, int>> IpRangeRules { get; set; } = new Dictionary<string, Dictionary<int, int>>();
    }
}

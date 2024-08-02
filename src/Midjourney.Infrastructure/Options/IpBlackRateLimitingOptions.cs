namespace Midjourney.Infrastructure.Options
{
    /// <summary>
    /// IP 黑名单限流配置，触发后自动封锁 IP，支持封锁时间配置
    /// </summary>
    public class IpBlackRateLimitingOptions
    {
        /// <summary>
        /// 是否启用黑名单限流。
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
        /// 封锁时间（分钟）。
        /// </summary>
        public int BlockTime { get; set; } = 1440;

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
using System.Net;

namespace Midjourney.Infrastructure.Options
{
    /// <summary>
    /// 表示 IP 限流配置选项。
    /// </summary>
    public class IpRateLimitingOptions
    {
        /// <summary>
        /// 是否启用限流
        /// </summary>
        public bool Enable { get; set; }

        /// <summary>
        /// 白名单列表
        /// </summary>
        public List<string> Whitelist { get; set; } = new List<string>();

        /// <summary>
        /// 白名单 IP 网络
        /// </summary>
        public List<IPNetwork2> WhitelistNetworks
        {
            get
            {
                // 格式化白名单
                // 如果没有 / , 则默认为 /32
                return Whitelist.Select(ip => !ip.Contains("/") ? IPNetwork2.Parse(ip + "/32") : IPNetwork2.Parse(ip)).ToList();
            }
        }

        /// <summary>
        /// 黑名单列表
        /// </summary>
        public List<string> Blacklist { get; set; } = new List<string>();

        /// <summary>
        /// 黑名单 IP 网络
        /// </summary>
        public List<IPNetwork2> BlacklistNetworks
        {
            get
            {
                // 格式化黑名单
                return Blacklist.Select(ip => IPNetwork2.Parse(ip + "/32")).ToList();
            }
        }

        /// <summary>
        /// IP 限流规则
        /// </summary>
        public Dictionary<string, Dictionary<int, int>> IpRules { get; set; } = new Dictionary<string, Dictionary<int, int>>();

        /// <summary>
        /// IP 段限流规则
        /// </summary>
        public Dictionary<string, Dictionary<int, int>> IpRangeRules { get; set; } = new Dictionary<string, Dictionary<int, int>>();
    }
}

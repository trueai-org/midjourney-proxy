namespace Midjourney.Infrastructure.Options
{
    /// <summary>
    /// IP 黑名单限流配置，触发后自动封锁 IP，支持封锁时间配置
    /// </summary>
    public class IpBlackRateLimitingOptions : IpRateLimitingOptions
    {
        /// <summary>
        /// 封锁时间（分钟）。
        /// </summary>
        public int BlockTime { get; set; } = 1440;
    }
}
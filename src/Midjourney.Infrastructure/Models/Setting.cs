using Midjourney.Infrastructure.Options;

namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// 系统配置
    /// </summary>
    public class Setting : ProxyProperties
    {
        /// <summary>
        /// 全局开启垂直领域
        /// </summary>
        public bool IsVerticalDomain { get; set; }

        /// <summary>
        /// 启用 Swagger
        /// </summary>
        public bool EnableSwagger { get; set; }

        /// <summary>
        /// 限流配置
        /// </summary>
        public IpRateLimitingOptions IpRateLimiting { get; set; }

        /// <summary>
        /// 黑名单限流配置
        /// </summary>
        public IpBlackRateLimitingOptions IpBlackRateLimiting { get; set; }

        /// <summary>
        /// 开启注册
        /// </summary>
        public bool EnableRegister { get; set; }

        /// <summary>
        /// 注册用户默认日绘图限制
        /// </summary>
        public int RegisterUserDefaultDayLimit { get; set; } = -1;

        /// <summary>
        /// 开启访客
        /// </summary>
        public bool EnableGuest { get; set; }

        /// <summary>
        /// 访客默认日绘图限制
        /// </summary>
        public int GuestDefaultDayLimit { get; set; } = -1;
    }
}
using System.ComponentModel.DataAnnotations;

namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// 验证码验证请求
    /// </summary>
    public class CaptchaVerfyRequest
    {
        /// <summary>
        /// CF 弹窗链接
        /// 示例：https://936929561302675456.discordsays.com/captcha/api/c/hIlZOI0ZQI3qQjpXhzS4GTgw_DuRTjYiyyww38dJuTzmqA8pa3OC60yTJbTmK6jd3i6Q0wZNxiuEp2dW/ack?hash=1
        /// </summary>
        [MaxLength(4000)]
        public string Url { get; set; }

        /// <summary>
        /// 自定义参数 = ChannelId
        /// </summary>
        [MaxLength(4000)]
        public string State { get; set; }

        /// <summary>
        /// 通知回调的密钥，防止篡改
        /// </summary>
        [MaxLength(4000)]
        public string Secret { get; set; }

        /// <summary>
        /// 回调地址, 为空时使用全局notifyHook。
        /// </summary>
        [MaxLength(4000)]
        public string NotifyHook { get; set; }

        /// <summary>
        /// 是否验证成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        [MaxLength(4000)]
        public string Message { get; set; }
    }
}

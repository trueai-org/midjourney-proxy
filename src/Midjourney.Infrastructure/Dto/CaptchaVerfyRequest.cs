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
        public string Url { get; set; }

        /// <summary>
        /// 自定义参数。
        /// </summary>
        public string State { get; set; }

        /// <summary>
        /// 回调地址, 为空时使用全局notifyHook。
        /// </summary>
        public string NotifyHook { get; set; }
    }
}

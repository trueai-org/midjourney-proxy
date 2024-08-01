namespace Midjourney.Infrastructure.Options
{
    /// <summary>
    /// 验证码配置项
    /// </summary>
    public class CaptchaOption
    {
        /// <summary>
        /// 2captcha API key
        /// </summary>
        public string TwoCaptchaKey { get; set; }

        /// <summary>
        /// 是否后台运行
        /// </summary>
        public bool Headless { get; set; }
    }
}

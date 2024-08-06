namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// 首页信息
    /// </summary>
    public class HomeDto
    {
        /// <summary>
        /// 是否显示注册入口
        /// </summary>
        public bool IsRegister { get; set; }

        /// <summary>
        /// 是否开启了访客入口
        /// </summary>
        public bool IsGuest { get; set; }
    }
}

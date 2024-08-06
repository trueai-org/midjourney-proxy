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

        /// <summary>
        /// 网站配置为演示模式
        /// </summary>
        public bool IsDemoMode { get; set; }

        /// <summary>
        /// 今日绘图
        /// </summary>
        public int TodayDraw { get; set; }

        /// <summary>
        /// 昨日绘图
        /// </summary>
        public int YesterdayDraw { get; set; }

        /// <summary>
        /// 总绘图
        /// </summary>
        public int TotalDraw { get; set; }

        /// <summary>
        /// 绘图客户端 top 5
        /// </summary>
        public Dictionary<string, int> Tops { get; set; } = new Dictionary<string, int>();
    }
}

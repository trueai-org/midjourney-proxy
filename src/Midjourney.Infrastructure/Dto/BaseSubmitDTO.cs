namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// 基础提交参数类。
    /// </summary>
    public abstract class BaseSubmitDTO
    {
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
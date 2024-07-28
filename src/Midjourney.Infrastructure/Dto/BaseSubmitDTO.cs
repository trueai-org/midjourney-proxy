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

    /// <summary>
    /// 账号筛选
    /// </summary>
    public class AccountFilter
    {
        /// <summary>
        /// 过滤指定实例的账号
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// 账号模式 RELAX | FAST | TURBO
        /// </summary>
        public List<GenerationSpeedMode> Modes { get; set; } = new List<GenerationSpeedMode>();

        /// <summary>
        /// 账号是否 remix（Midjourney Remix）
        /// </summary>
        public bool? Remix { get; set; }

        /// <summary>
        /// 账号是否 remix（Nijiourney Remix）
        /// </summary>
        public bool? NijiRemix { get; set; }

        /// <summary>
        /// 账号过滤时，remix 自动提交视为账号的 remix 为 false
        /// </summary>
        public bool? RemixAutoConsidered { get; set; }
    }
}
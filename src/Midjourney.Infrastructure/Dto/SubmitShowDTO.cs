namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// Show job_id DTO
    /// </summary>
    public class SubmitShowDTO : BaseSubmitDTO
    {
        /// <summary>
        /// bot 类型，mj(默认)或niji
        /// MID_JOURNEY | 枚举值: NIJI_JOURNEY
        /// </summary>
        public string BotType { get; set; }

        /// <summary>
        /// JobId 或 图片 url
        /// </summary>
        public string JobId { get; set; }

        /// <summary>
        /// 账号过滤
        /// </summary>
        public AccountFilter AccountFilter { get; set; }
    }
}
using System.ComponentModel.DataAnnotations;

namespace Midjourney.Base.Dto
{
    /// <summary>
    /// 任务信息查询请求
    /// </summary>
    public class TaskInfoQueryRequest
    {
        /// <summary>
        /// 对象ID。
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 账号实例 ID = 账号渠道 ID = ChannelId
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// 当前绘画的速度模式，选择实例时确定速度，最终速度由任务成功后确定
        /// 1、变化任务时，默认取父级的速度模式
        /// 2、如果任务成功后，依然没有速度，则默认为 FAST
        /// </summary>
        public GenerationSpeedMode? Mode { get; set; }

        /// <summary>
        /// 任务类型。
        /// </summary>
        public TaskAction? Action { get; set; }

        /// <summary>
        /// 任务状态。
        /// </summary>
        public TaskStatus? Status { get; set; }

        /// <summary>
        /// 失败原因。
        /// </summary>
        public string FailReason { get; set; }

        /// <summary>
        /// 提示词/描述。
        /// </summary>
        [StringLength(4000)]
        public string Description { get; set; }

        /// <summary>
        /// 提交开始时间 yyyy-MM-dd
        /// </summary>
        public string SubmitTimeStart { get; set; }

        /// <summary>
        /// 提交结束时间 yyyy-MM-dd
        /// </summary>
        public string SubmitTimeEnd { get; set; }

        /// <summary>
        /// 用户 ID
        /// </summary>
        public string UserId { get; set; }
    }
}

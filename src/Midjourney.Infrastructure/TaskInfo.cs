using Midjourney.Infrastructure.Domain;
using Swashbuckle.AspNetCore.Annotations;

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// 任务类，表示一个任务的基本信息。
    /// </summary>
    [SwaggerSchema("任务")]
    public class TaskInfo : DomainObject
    {
        public TaskInfo()
        {
        }

        /// <summary>
        /// bot 类型，mj(默认)或niji
        /// MID_JOURNEY | 枚举值: NIJI_JOURNEY
        /// </summary>
        public string BotType { get; set; }

        /// <summary>
        /// 提交作业的唯一ID。
        /// </summary>
        public string Nonce { get; set; }

        /// <summary>
        /// 与 MJ 交互成功后消息 ID。
        /// INTERACTION_SUCCESS
        /// </summary>
        public string InteractionMetadataId { get; set; }

        /// <summary>
        /// 消息 ID（MJ 消息 ID，Nonce 与 MessageId 对应）
        /// 最终消息 ID
        /// </summary>
        public string MessageId { get; set; }

        /// <summary>
        /// 消息 ID
        /// 创建消息 ID -> 进度消息 ID -> 完成消息 ID
        /// </summary>
        public List<string> MessageIds { get; set; } = new List<string>();

        /// <summary>
        /// 任务类型。
        /// </summary>
        [SwaggerSchema("任务类型")]
        public TaskAction Action { get; set; }

        /// <summary>
        /// 任务状态。
        /// </summary>
        [SwaggerSchema("任务状态")]
        public TaskStatus Status { get; set; } = TaskStatus.NOT_START;

        /// <summary>
        /// 提示词。
        /// </summary>
        [SwaggerSchema("提示词")]
        public string Prompt { get; set; }

        /// <summary>
        /// 提示词（英文）。
        /// </summary>
        [SwaggerSchema("提示词-英文")]
        public string PromptEn { get; set; }

        /// <summary>
        /// 任务描述。
        /// </summary>
        [SwaggerSchema("任务描述")]
        public string Description { get; set; }

        /// <summary>
        /// 自定义参数。
        /// </summary>
        [SwaggerSchema("自定义参数")]
        public string State { get; set; }

        /// <summary>
        /// 提交时间。
        /// </summary>
        [SwaggerSchema("提交时间")]
        public long? SubmitTime { get; set; }

        /// <summary>
        /// 开始执行时间。
        /// </summary>
        [SwaggerSchema("开始执行时间")]
        public long? StartTime { get; set; }

        /// <summary>
        /// 结束时间。
        /// </summary>
        [SwaggerSchema("结束时间")]
        public long? FinishTime { get; set; }

        /// <summary>
        /// 图片URL。
        /// </summary>
        [SwaggerSchema("图片URL")]
        public string ImageUrl { get; set; }

        /// <summary>
        /// 任务进度。
        /// </summary>
        [SwaggerSchema("任务进度")]
        public string Progress { get; set; }

        /// <summary>
        /// 失败原因。
        /// </summary>
        [SwaggerSchema("失败原因")]
        public string FailReason { get; set; }

        /// <summary>
        /// 按钮
        /// </summary>
        public List<CustomComponentModel> Buttons { get; set; } = new List<CustomComponentModel>();

        /// <summary>
        /// 启动任务。
        /// </summary>
        public void Start()
        {
            StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Status = TaskStatus.SUBMITTED;
            Progress = "0%";
        }

        /// <summary>
        /// 任务成功。
        /// </summary>
        public void Success()
        {
            FinishTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Status = TaskStatus.SUCCESS;
            Progress = "100%";
        }

        /// <summary>
        /// 任务失败。
        /// </summary>
        /// <param name="reason">失败原因。</param>
        public void Fail(string reason)
        {
            FinishTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Status = TaskStatus.FAILURE;
            FailReason = reason;
            Progress = "";
        }
    }
}
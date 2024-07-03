using Swashbuckle.AspNetCore.Annotations;

namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// 执行动作。
    /// </summary>
    [SwaggerSchema("执行动作")]
    public class SubmitActionDTO : BaseSubmitDTO
    {
        /// <summary>
        /// 任务ID。
        /// </summary>
        [SwaggerSchema("任务ID", Description = "\"1320098173412546\"")]
        public string TaskId { get; set; }

        /// <summary>
        /// 动作标识
        /// </summary>
        [SwaggerSchema("MJ::JOB::upsample::2::3dbbd469-36af-4a0f-8f02-df6c579e7011")]
        public string CustomId { get; set; }
    }
}
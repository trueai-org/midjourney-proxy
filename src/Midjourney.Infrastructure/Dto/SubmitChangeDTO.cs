using Swashbuckle.AspNetCore.Annotations;

namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// 变化任务提交参数。
    /// </summary>
    [SwaggerSchema("变化任务提交参数")]
    public class SubmitChangeDTO : BaseSubmitDTO
    {
        /// <summary>
        /// 任务ID。
        /// </summary>
        [SwaggerSchema("任务ID", Description = "\"1320098173412546\"")]
        public string TaskId { get; set; }

        /// <summary>
        /// 操作类型: UPSCALE(放大); VARIATION(变换); REROLL(重新生成)。
        /// </summary>
        [SwaggerSchema("UPSCALE(放大); VARIATION(变换); REROLL(重新生成)", Description = "UPSCALE")]
        public TaskAction Action { get; set; }

        /// <summary>
        /// 序号(1~4), action为UPSCALE, VARIATION时必传。
        /// </summary>
        [SwaggerSchema("序号(1~4), action为UPSCALE,VARIATION时必传", Description = "1")]
        public int? Index { get; set; }
    }
}
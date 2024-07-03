using Swashbuckle.AspNetCore.Annotations;

namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// 简单变化任务提交参数。
    /// </summary>
    [SwaggerSchema("变化任务提交参数-simple")]
    public class SubmitSimpleChangeDTO : BaseSubmitDTO
    {
        /// <summary>
        /// 变化描述: ID $action$index。
        /// </summary>
        [SwaggerSchema("变化描述: ID $action$index", Description = "1320098173412546 U2")]
        public string Content { get; set; }
    }
}
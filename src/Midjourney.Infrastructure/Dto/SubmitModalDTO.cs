using Swashbuckle.AspNetCore.Annotations;

namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// Imagine提交参数。
    /// </summary>
    [SwaggerSchema("Imagine提交参数")]
    public class SubmitModalDTO : BaseSubmitDTO
    {
        /// <summary>
        /// 提示词。
        /// </summary>
        [SwaggerSchema("提示词", Description = "Cat")]
        public string Prompt { get; set; }

        /// <summary>
        /// 任务ID。
        /// </summary>
        [SwaggerSchema("任务ID", Description = "\"1320098173412546\"")]
        public string TaskId { get; set; }

        /// <summary>
        /// 局部重绘的蒙版base64
        /// </summary>
        [SwaggerSchema("图片base64", Description = "data:image/png;base64,xxx")]
        public string MaskBase64 { get; set; }
    }
}
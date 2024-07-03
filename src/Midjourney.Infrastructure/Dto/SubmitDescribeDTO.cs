using Swashbuckle.AspNetCore.Annotations;

namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// Describe提交参数。
    /// </summary>
    [SwaggerSchema("Describe提交参数")]
    public class SubmitDescribeDTO : BaseSubmitDTO
    {
        /// <summary>
        /// 图片base64。
        /// </summary>
        [SwaggerSchema("图片base64", Description = "data:image/png;base64,xxx")]
        public string Base64 { get; set; }
    }
}
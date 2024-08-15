using Swashbuckle.AspNetCore.Annotations;

namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// 上传文件到 discord 的提交参数。
    /// </summary>
    public class SubmitUploadDiscordDto : BaseSubmitDTO
    {
        /// <summary>
        /// 垫图base64数组。
        /// </summary>
        [SwaggerSchema("垫图base64数组")]
        public List<string> Base64Array { get; set; }

        /// <summary>
        /// 账号过滤
        /// </summary>
        public AccountFilter AccountFilter { get; set; }
    }
}
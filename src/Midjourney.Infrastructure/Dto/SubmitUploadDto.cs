namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// Imagine提交参数。
    /// </summary>
    public class SubmitUploadDto : BaseSubmitDTO
    {
        /// <summary>
        /// 图片 urls
        /// </summary>
        public string[] Urls { get; set; } = new string[0];

        /// <summary>
        /// 过滤指定实例的账号
        /// </summary>
        public string InstanceId { get; set; }
    }
}
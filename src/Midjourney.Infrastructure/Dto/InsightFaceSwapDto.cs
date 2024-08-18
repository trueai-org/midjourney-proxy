namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// InsightFace  提交参数。
    /// </summary>
    public class InsightFaceSwapDto : BaseSubmitDTO
    {
        /// <summary>
        /// 人脸源图片base64 或 URL
        /// </summary>
        public string SourceBase64 { get; set; }

        /// <summary>
        /// 人脸源图片URL 或 base64
        /// </summary>
        public string SourceUrl { get; set; }

        /// <summary>
        /// 目标图片base64 或 URL
        /// </summary>
        public string TargetBase64 { get; set; }

        /// <summary>
        /// 目标图片URL 或 base64
        /// </summary>
        public string TargetUrl { get; set; }

        /// <summary>
        /// 账号过滤
        /// </summary>
        public AccountFilter AccountFilter { get; set; }
    }
}
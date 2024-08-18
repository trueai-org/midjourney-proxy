using Microsoft.AspNetCore.Http;

namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// 视频换脸 InsightFace 提交参数。
    /// </summary>
    public class InsightVideoFaceSwapDto : BaseSubmitDTO
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
        /// 目标文件 - 视频文件
        /// </summary>
        public IFormFile TargetFile { get; set; }

        /// <summary>
        /// 目标文件URL - 视频 URL
        /// </summary>
        public string TargetUrl { get; set; }

        /// <summary>
        /// 账号过滤
        /// </summary>
        public AccountFilter AccountFilter { get; set; }
    }
}
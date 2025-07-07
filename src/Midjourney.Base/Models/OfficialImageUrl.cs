using System.Text.Json.Serialization;

namespace Midjourney.Base.Models
{
    public class OfficialImageUrl
    {
        /// <summary>
        /// 图像URL
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; }

        /// <summary>
        /// WebP格式图像URL
        /// </summary>
        [JsonPropertyName("webp")]
        public string Webp { get; set; }

        /// <summary>
        /// 图像编号
        /// </summary>
        [JsonPropertyName("no")]
        public int No { get; set; }

        /// <summary>
        /// 缩略图URL
        /// </summary>
        [JsonPropertyName("thumbnail")]
        public string Thumbnail { get; set; }

        /// <summary>
        /// 图像状态 ok
        /// </summary>
        [JsonPropertyName("status")]
        public string Status { get; set; }

        /// <summary>
        /// 是否已提交到发现页
        /// </summary>
        [JsonPropertyName("isDiscoverySubmitted")]
        public bool IsDiscoverySubmitted { get; set; }

        /// <summary>
        /// 图像类别
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; }

        ///// <summary>
        ///// 喜欢此图像的用户列表
        ///// </summary>
        //[JsonPropertyName("likedUsers")]
        //public List<string> LikedUsers { get; set; }
    }
}

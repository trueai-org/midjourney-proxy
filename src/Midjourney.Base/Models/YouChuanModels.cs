// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
// Copyright (C) 2024 trueai.org

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// Additional Terms:
// This software shall not be used for any illegal activities.
// Users must comply with all applicable laws and regulations,
// particularly those related to image and video processing.
// The use of this software for any form of illegal face swapping,
// invasion of privacy, or any other unlawful purposes is strictly prohibited.
// Violation of these terms may result in termination of the license and may subject the violator to legal action.

using System.Text.Json.Serialization;

namespace Midjourney.Base.Models
{
    /// <summary>
    /// YouChuan API 响应对象
    /// </summary>
    public class YouChuanResponse<T>
    {
        /// <summary>
        /// 响应状态码
        /// </summary>
        [JsonPropertyName("code")]
        public int Code { get; set; }

        /// <summary>
        /// 响应数据内容
        /// </summary>
        [JsonPropertyName("data")]
        public T Data { get; set; }

        /// <summary>
        /// 响应消息
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        [JsonPropertyName("reason")]
        public string Reason { get; set; }
    }

    /// <summary>
    /// YouChuan 数据内容
    /// </summary>
    public class YouChuanStatusData
    {
        /// <summary>
        /// 任务列表
        /// </summary>
        [JsonPropertyName("list")]
        public List<YouChuanTask> List { get; set; } = [];
    }

    /// <summary>
    /// YouChuan 任务信息
    /// </summary>
    public class YouChuanTask
    {
        /// <summary>
        /// 任务ID
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// 任务入队时间
        /// </summary>
        [JsonPropertyName("enqueueTime")]
        public DateTime EnqueueTime { get; set; }

        /// <summary>
        /// 任务类型
        /// </summary>
        [JsonPropertyName("jobType")]
        public string JobType { get; set; }

        /// <summary>
        /// 事件类型
        /// </summary>
        [JsonPropertyName("eventType")]
        public string EventType { get; set; }

        /// <summary>
        /// 父网格索引
        /// </summary>
        [JsonPropertyName("parentGrid")]
        public int ParentGrid { get; set; }

        /// <summary>
        /// 父任务ID
        /// </summary>
        [JsonPropertyName("parentId")]
        public string ParentId { get; set; }

        /// <summary>
        /// 完整命令
        /// </summary>
        [JsonPropertyName("fullCommand")]
        public string FullCommand { get; set; }

        /// <summary>
        /// 批处理大小
        /// </summary>
        [JsonPropertyName("batchSize")]
        public int BatchSize { get; set; }

        /// <summary>
        /// 图像宽度
        /// </summary>
        [JsonPropertyName("width")]
        public int Width { get; set; }

        /// <summary>
        /// 图像高度
        /// </summary>
        [JsonPropertyName("height")]
        public int Height { get; set; }

        /// <summary>
        /// 是否已发布
        /// </summary>
        [JsonPropertyName("published")]
        public bool Published { get; set; }

        /// <summary>
        /// 是否显示
        /// </summary>
        [JsonPropertyName("shown")]
        public bool Shown { get; set; }

        ///// <summary>
        ///// 喜欢列表
        ///// </summary>
        //[JsonPropertyName("likes")]
        //public List<string> Likes { get; set; }

        ///// <summary>
        ///// 评分信息
        ///// </summary>
        //[JsonPropertyName("rating")]
        //public Dictionary<string, object> Rating { get; set; }

        /// <summary>
        /// 图像URL列表
        /// </summary>
        [JsonPropertyName("imgUrls")]
        public List<YouChuanImageUrl> ImgUrls { get; set; } = [];

        /// <summary>
        /// 当前状态 completed
        /// </summary>
        [JsonPropertyName("currentStatus")]
        public string CurrentStatus { get; set; }

        /// <summary>
        /// 服务名称
        /// </summary>
        [JsonPropertyName("service")]
        public string Service { get; set; }

        /// <summary>
        /// 解析的版本
        /// </summary>
        [JsonPropertyName("parsedVersion")]
        public string ParsedVersion { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        [JsonPropertyName("username")]
        public string Username { get; set; }

        /// <summary>
        /// 用户ID
        /// </summary>
        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        /// <summary>
        /// 任务状态 success
        /// </summary>
        [JsonPropertyName("status")]
        public string Status { get; set; }

        /// <summary>
        /// 评论内容
        /// </summary>
        [JsonPropertyName("comment")]
        public string Comment { get; set; }

        ///// <summary>
        ///// 用户资料信息
        ///// </summary>
        //[JsonPropertyName("profile")]
        //public object Profile { get; set; }

        /// <summary>
        /// 房间ID
        /// </summary>
        [JsonPropertyName("roomId")]
        public string RoomId { get; set; }

        /// <summary>
        /// 图像拒绝次数
        /// </summary>
        [JsonPropertyName("imageRejectNum")]
        public int ImageRejectNum { get; set; }

        /// <summary>
        /// 反馈状态
        /// </summary>
        [JsonPropertyName("feedbackStatus")]
        public string FeedbackStatus { get; set; }

        /// <summary>
        /// 反馈时间戳
        /// </summary>
        [JsonPropertyName("feedbackAt")]
        public int FeedbackAt { get; set; }

        /// <summary>
        /// 是否为试用版
        /// </summary>
        [JsonPropertyName("isTrial")]
        public bool IsTrial { get; set; }

        /// <summary>
        /// 视频生成原始图像URL
        /// </summary>
        [JsonPropertyName("videoGenOriginImageUrl")]
        public string VideoGenOriginImageUrl { get; set; }

        /// <summary>
        /// 视频URL
        /// </summary>
        [JsonPropertyName("videoUrl")]
        public string VideoUrl { get; set; }

        /// <summary>
        /// 视频重绘图像URL
        /// </summary>
        [JsonPropertyName("videoRepaintedImageUrl")]
        public string VideoRepaintedImageUrl { get; set; }

        /// <summary>
        /// 视频生成模式
        /// </summary>
        [JsonPropertyName("videoGenMode")]
        public string VideoGenMode { get; set; }

        /// <summary>
        /// 视频帧数
        /// </summary>
        [JsonPropertyName("frame_count")]
        public int? FrameCount { get; set; }
    }

    /// <summary>
    /// YouChuan 图像URL信息
    /// </summary>
    public class YouChuanImageUrl
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
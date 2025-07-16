using System.Text.Json.Serialization;

namespace Midjourney.Base.Models
{
    /// <summary>
    /// 任务状态信息
    /// </summary>
    public class OfficialJobStatus
    {
        /// <summary>
        /// 任务ID
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        ///// <summary>
        ///// 父级网格ID
        ///// </summary>
        //[JsonPropertyName("parent_grid")]
        //public string ParentGrid { get; set; }

        /// <summary>
        /// 父级任务ID
        /// </summary>
        [JsonPropertyName("parent_id")]
        public string ParentId { get; set; }

        /// <summary>
        /// 任务类型
        /// </summary>
        [JsonPropertyName("job_type")]
        public string JobType { get; set; }

        /// <summary>
        /// 事件类型
        /// </summary>
        [JsonPropertyName("event_type")]
        public string EventType { get; set; }

        /// <summary>
        /// 完整命令
        /// </summary>
        [JsonPropertyName("full_command")]
        public string FullCommand { get; set; }

        /// <summary>
        /// 入队时间
        /// </summary>
        [JsonPropertyName("enqueue_time")]
        public string EnqueueTime { get; set; }

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
        /// 批处理大小
        /// </summary>
        [JsonPropertyName("batch_size")]
        public int BatchSize { get; set; }

        /// <summary>
        /// 是否已发布
        /// </summary>
        [JsonPropertyName("published")]
        public bool Published { get; set; }

        /// <summary>
        /// 用户是否点赞
        /// </summary>
        [JsonPropertyName("liked_by_user")]
        public bool LikedByUser { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        [JsonPropertyName("username")]
        public string Username { get; set; }

        /// <summary>
        /// 用户ID
        /// </summary>
        [JsonPropertyName("user_id")]
        public string UserId { get; set; }

        /// <summary>
        /// 当前状态 completed running
        /// </summary>
        [JsonPropertyName("current_status")]
        public string CurrentStatus { get; set; }

        /// <summary>
        /// 图像URL列表
        /// </summary>
        [JsonPropertyName("imgUrls")]
        public List<OfficialImageUrl> ImgUrls { get; set; } = [];

        ///// <summary>
        ///// 在房间内点赞的用户列表
        ///// </summary>
        //[JsonPropertyName("liked_by_user_in_room")]
        //public List<string> LikedByUserInRoom { get; set; } = new List<string>();

        /// <summary>
        /// 房间ID
        /// </summary>
        [JsonPropertyName("room_id")]
        public string RoomId { get; set; }

        /// <summary>
        /// 视频帧数
        /// </summary>
        [JsonPropertyName("vid_framecount")]
        public int? FrameCount { get; set; }
    }
}
namespace Midjourney.Base.Models
{
    /// <summary>
    /// Redis 通知模型
    /// </summary>
    public class RedisNotification
    {
        public RedisNotification()
        {

        }

        public ENotificationType CacheType { get; set; }

        public string ChannelId { get; set; }

        public string TaskInfoId { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 通知类型
    /// </summary>
    public enum ENotificationType
    {
        /// <summary>
        /// 账号缓存清理通知
        /// </summary>
        AccountCache = 0,

        /// <summary>
        /// 取消任务通知
        /// </summary>
        CancelTaskInfo = 1
    }
}
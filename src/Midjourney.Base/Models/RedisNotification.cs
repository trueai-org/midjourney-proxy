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

        /// <summary>
        /// 通知类型
        /// </summary>
        public ENotificationType Type { get; set; }

        /// <summary>
        /// 频道 ID - 账号缓存清理通知 | 入队成功消息 | 任务处理完成并解锁
        /// </summary>
        public string ChannelId { get; set; }

        /// <summary>
        /// 任务信息 ID - 取消任务通知
        /// </summary>
        public string TaskInfoId { get; set; }

        /// <summary>
        /// 任务信息 - 完成作业事件
        /// </summary>
        public TaskInfo TaskInfo { get; set; }

        /// <summary>
        /// 是否成功 - 完成作业事件
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 来源节点，如果是自身发出的消息则不处理
        /// </summary>
        public string Hostname { get; set; } = Environment.MachineName;

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// 减少计数
        /// </summary>
        public int DecreaseCount { get; set; }
    }

    /// <summary>
    /// 通知类型
    /// </summary>
    public enum ENotificationType
    {
        /// <summary>
        /// 账号缓存清理通知/账号更新
        /// </summary>
        AccountCache = 0,

        /// <summary>
        /// 取消任务通知
        /// </summary>
        CancelTaskInfo = 1,

        /// <summary>
        /// 删除任务通知
        /// </summary>
        DeleteTaskInfo = 11,

        /// <summary>
        /// 完成作业事件 - 统计成功/失败
        /// </summary>
        CompleteTaskInfo = 2,

        /// <summary>
        /// 入队成功消息 - 可以继续处理下一个任务
        /// </summary>
        EnqueueTaskInfo = 3,

        /// <summary>
        /// 任务处理完成释放了并行锁 - 可以继续处理下一个任务
        /// </summary>
        DisposeLock = 4,

        /// <summary>
        /// 获取 Seek 种子任务
        /// </summary>
        SeedTaskInfo = 5,

        /// <summary>
        /// 减少慢速计数
        /// </summary>
        DecreaseRelaxCount = 6,

        /// <summary>
        /// 减少快速计数
        /// </summary>
        DecreaseFastCount = 7,

        /// <summary>
        /// 修改配置通知 - 用于 Consul 多节点
        /// </summary>
        SettingChanged = 8,

        /// <summary>
        /// 检查升级通知 - 用于 Consul 多节点
        /// </summary>
        CheckUpdate = 9,

        /// <summary>
        /// 重启通知 - 用于 Consul 多节点
        /// </summary>
        Restart = 10
    }
}
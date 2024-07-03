namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// 通知服务接口，用于通知任务状态变更。
    /// </summary>
    public interface INotifyService
    {
        /// <summary>
        /// 通知任务状态变更。
        /// </summary>
        /// <param name="task">任务实例。</param>
        Task NotifyTaskChange(TaskInfo task);
    }
}
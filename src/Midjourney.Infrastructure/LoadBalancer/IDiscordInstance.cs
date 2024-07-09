using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.Services;

namespace Midjourney.Infrastructure.LoadBalancer
{
    /// <summary>
    /// Discord实例接口，定义了与Discord服务交互的基本方法。
    /// </summary>
    public interface IDiscordInstance : IDiscordService
    {
        /// <summary>
        /// 获取实例ID。
        /// </summary>
        /// <returns>实例ID。</returns>
        string GetInstanceId();

        /// <summary>
        /// 获取Discord账号信息。
        /// </summary>
        /// <returns>Discord账号信息。</returns>
        DiscordAccount Account();

        /// <summary>
        /// 判断实例是否存活。
        /// </summary>
        /// <returns>如果存活返回true，否则返回false。</returns>
        bool IsAlive();

        /// <summary>
        /// 获取正在运行的任务列表。
        /// </summary>
        /// <returns>正在运行的任务列表。</returns>
        List<TaskInfo> GetRunningTasks();

        /// <summary>
        /// 获取排队中的任务列表。
        /// </summary>
        /// <returns>排队中的任务列表。</returns>
        List<TaskInfo> GetQueueTasks();

        /// <summary>
        /// 退出任务。
        /// </summary>
        /// <param name="task">任务实例。</param>
        void ExitTask(TaskInfo task);

        /// <summary>
        /// 获取正在运行的任务的Future字典。
        /// </summary>
        /// <returns>正在运行的任务的Future字典。</returns>
        Dictionary<string, Task> GetRunningFutures();

        /// <summary>
        /// 提交任务。
        /// </summary>
        /// <param name="task">任务实例。</param>
        /// <param name="discordSubmit">提交操作。</param>
        /// <returns>提交结果。</returns>
        SubmitResultVO SubmitTaskAsync(TaskInfo task, Func<Task<Message>> discordSubmit);

        IEnumerable<TaskInfo> FindRunningTask(Func<TaskInfo, bool> condition);

        TaskInfo GetRunningTask(string id);

        TaskInfo GetRunningTaskByNonce(string nonce);

        TaskInfo GetRunningTaskByMessageId(string messageId);
    }
}
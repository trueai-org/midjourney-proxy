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
        /// 获取Discord账号信息。
        /// </summary>
        /// <returns>Discord账号信息。</returns>
        DiscordAccount Account { get; }

        /// <summary>
        /// 获取实例ID。
        /// </summary>
        /// <returns>实例ID。</returns>
        string GetInstanceId { get; }

        /// <summary>
        /// 判断实例是否存活。
        /// </summary>
        /// <returns>如果存活返回true，否则返回false。</returns>
        bool IsAlive { get; }

        /// <summary>
        /// 获取正在运行的任务列表。
        /// </summary>
        /// <returns>正在运行的任务列表。</returns>
        List<TaskInfo> GetRunningTasks();

        /// <summary>
        /// 添加正在运行的任务。
        /// </summary>
        /// <param name="task"></param>
        void AddRunningTask(TaskInfo task);

        /// <summary>
        /// 移除正在运行的任务。
        /// </summary>
        /// <param name="task"></param>
        void RemoveRunningTask(TaskInfo task);

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

        /// <summary>
        /// 查询正在运行的任务
        /// </summary>
        /// <param name="condition"></param>
        /// <returns></returns>
        IEnumerable<TaskInfo> FindRunningTask(Func<TaskInfo, bool> condition);

        /// <summary>
        /// 查询正在运行的任务
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        TaskInfo GetRunningTask(string id);

        /// <summary>
        /// 查询正在运行的任务
        /// </summary>
        /// <param name="nonce"></param>
        /// <returns></returns>
        TaskInfo GetRunningTaskByNonce(string nonce);

        /// <summary>
        /// 查询正在运行的任务
        /// </summary>
        /// <param name="messageId"></param>
        /// <returns></returns>
        TaskInfo GetRunningTaskByMessageId(string messageId);

        /// <summary>
        /// 释放资源
        /// </summary>
        void Dispose();
    }
}
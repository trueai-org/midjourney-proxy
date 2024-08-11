namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// 任务存储服务接口。
    /// </summary>
    public interface ITaskStoreService
    {
        /// <summary>
        /// 保存任务。
        /// </summary>
        /// <param name="task">任务实例。</param>
        void Save(TaskInfo task);

        /// <summary>
        /// 删除任务。
        /// </summary>
        /// <param name="id">任务ID。</param>
        void Delete(string id);

        /// <summary>
        /// 获取任务。
        /// </summary>
        /// <param name="id">任务ID。</param>
        /// <returns>任务实例。</returns>
        TaskInfo Get(string id);
    }
}
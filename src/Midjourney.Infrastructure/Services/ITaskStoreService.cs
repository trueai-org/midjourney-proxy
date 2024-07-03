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

        /// <summary>
        /// 列出所有任务。
        /// </summary>
        /// <returns>任务列表。</returns>
        List<TaskInfo> List();

        ///// <summary>
        ///// 按条件列出任务。
        ///// </summary>
        ///// <param name="condition">任务条件。</param>
        ///// <returns>任务列表。</returns>
        //List<TaskInfo> List(TaskCondition condition);

        ///// <summary>
        ///// 按条件查找一个任务。
        ///// </summary>
        ///// <param name="condition">任务条件。</param>
        ///// <returns>任务实例。</returns>
        //TaskInfo FindOne(TaskCondition condition);
    }
}
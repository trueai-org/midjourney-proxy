using System.Collections.Concurrent;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// 内存任务存储服务实现类。
    /// </summary>
    public class InMemoryTaskStoreServiceImpl : ITaskStoreService
    {
        private readonly ConcurrentDictionary<string, TaskInfo> _taskMap;

        /// <summary>
        /// 初始化内存任务存储服务。
        /// </summary>
        /// <param name="timeout">任务超时时间。</param>
        public InMemoryTaskStoreServiceImpl()
        {
            _taskMap = new ConcurrentDictionary<string, TaskInfo>();
        }

        /// <summary>
        /// 保存任务。
        /// </summary>
        /// <param name="task">任务对象。</param>
        public void Save(TaskInfo task)
        {
            _taskMap[task.Id] = task;
        }

        /// <summary>
        /// 删除任务。
        /// </summary>
        /// <param name="key">任务ID。</param>
        public void Delete(string key)
        {
            _taskMap.TryRemove(key, out _);
        }

        /// <summary>
        /// 获取任务。
        /// </summary>
        /// <param name="key">任务ID。</param>
        /// <returns>任务对象。</returns>
        public TaskInfo Get(string key)
        {
            _taskMap.TryGetValue(key, out var task);
            return task;
        }
    }
}
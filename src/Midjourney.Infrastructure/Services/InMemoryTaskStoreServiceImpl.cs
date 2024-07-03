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

        /// <summary>
        /// 获取所有任务。
        /// </summary>
        /// <returns>任务列表。</returns>
        public List<TaskInfo> List()
        {
            return _taskMap.Values.ToList();
        }

        ///// <summary>
        ///// 根据条件获取任务列表。
        ///// </summary>
        ///// <param name="condition">任务条件。</param>
        ///// <returns>任务列表。</returns>
        //public List<TaskInfo> List(TaskCondition condition)
        //{
        //    return List();

        //    // TODO, 暂时返回全部任务，后续实现条件过滤。
        //    //return _taskMap.Values.Where(condition).ToList();
        //}

        ///// <summary>
        ///// 根据条件获取单个任务。
        ///// </summary>
        ///// <param name="condition">任务条件。</param>
        ///// <returns>任务对象。</returns>
        //public TaskInfo FindOne(TaskCondition condition)
        //{
        //    return _taskMap.Values.FirstOrDefault(condition);
        //}
    }
}
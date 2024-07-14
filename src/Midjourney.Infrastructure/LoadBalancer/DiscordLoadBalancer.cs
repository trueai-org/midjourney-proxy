using System.Collections.Concurrent;

namespace Midjourney.Infrastructure.LoadBalancer
{
    /// <summary>
    /// Discord 负载均衡器。
    /// </summary>
    public class DiscordLoadBalancer
    {
        private readonly IRule _rule;
        private readonly ConcurrentBag<IDiscordInstance> _instances;

        /// <summary>
        /// 初始化 DiscordLoadBalancer 类的新实例。
        /// </summary>
        /// <param name="rule">负载均衡规则。</param>
        public DiscordLoadBalancer(IRule rule)
        {
            _rule = rule;
            _instances = new ConcurrentBag<IDiscordInstance>();
        }

        /// <summary>
        /// 获取所有实例。
        /// </summary>
        /// <returns>所有实例列表。</returns>
        public List<IDiscordInstance> GetAllInstances() => _instances.ToList();

        /// <summary>
        /// 获取存活的实例。
        /// </summary>
        /// <returns>存活的实例列表。</returns>
        public List<IDiscordInstance> GetAliveInstances() => _instances.Where(instance => instance.IsAlive).ToList();

        /// <summary>
        /// 选择一个实例。
        /// </summary>
        /// <returns>选择的实例。</returns>
        public IDiscordInstance ChooseInstance() => _rule.Choose(GetAliveInstances());

        /// <summary>
        /// 获取指定ID的实例。
        /// </summary>
        /// <param name="instanceId">实例ID。</param>
        /// <returns>实例。</returns>
        public IDiscordInstance GetDiscordInstance(string instanceId)
        {
            return string.IsNullOrWhiteSpace(instanceId)
                ? null
                : _instances.FirstOrDefault(instance => instance.GetInstanceId == instanceId);
        }

        /// <summary>
        /// 获取排队任务的ID集合。
        /// </summary>
        /// <returns>排队任务的ID集合。</returns>
        public HashSet<string> GetQueueTaskIds()
        {
            var taskIds = new HashSet<string>();
            foreach (var instance in GetAliveInstances())
            {
                foreach (var taskId in instance.GetRunningFutures().Keys)
                {
                    taskIds.Add(taskId);
                }
            }
            return taskIds;
        }

        /// <summary>
        /// 获取排队任务列表。
        /// </summary>
        /// <returns>排队任务列表。</returns>
        public List<TaskInfo> GetQueueTasks()
        {
            var tasks = new List<TaskInfo>();
            foreach (var instance in GetAliveInstances())
            {
                tasks.AddRange(instance.GetQueueTasks());
            }
            return tasks;
        }

        /// <summary>
        /// 添加 Discord 实例
        /// </summary>
        /// <param name="instance"></param>
        public void AddInstance(IDiscordInstance instance) => _instances.Add(instance);


        /// <summary>
        /// 移除
        /// </summary>
        /// <param name="instance"></param>
        public void RemoveInstance(IDiscordInstance instance) => _instances.TryTake(out instance);
    }
}
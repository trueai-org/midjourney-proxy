using Midjourney.Infrastructure.Dto;

namespace Midjourney.Infrastructure.LoadBalancer
{
    /// <summary>
    /// Discord 负载均衡器。
    /// </summary>
    public class DiscordLoadBalancer
    {
        private readonly IRule _rule;
        private readonly HashSet<IDiscordInstance> _instances;

        /// <summary>
        /// 初始化 DiscordLoadBalancer 类的新实例。
        /// </summary>
        /// <param name="rule">负载均衡规则。</param>
        public DiscordLoadBalancer(IRule rule)
        {
            _rule = rule;
            _instances = new HashSet<IDiscordInstance>();
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
        public IDiscordInstance ChooseInstance(AccountFilter accountFilter = null)
        {
            if (accountFilter == null)
            {
                return _rule.Choose(GetAliveInstances());
            }
            else if (!string.IsNullOrWhiteSpace(accountFilter?.InstanceId))
            {
                return GetDiscordInstance(accountFilter.InstanceId);
            }
            else
            {
                var list = _instances.Where(instance => instance.IsAlive)
                         // 指定速度模式过滤
                         .WhereIf(accountFilter.Modes.Count > 0, c => c.Account.Mode == null || accountFilter.Modes.Contains(c.Account.Mode.Value))

                         // 允许速度模式过滤
                         // 或者有交集的
                         .WhereIf(accountFilter.Modes.Count > 0, c => c.Account.AllowModes == null || c.Account.AllowModes.Count <= 0 || c.Account.AllowModes.Any(x => accountFilter.Modes.Contains(x)))

                         // Midjourney Remix 过滤
                         .WhereIf(accountFilter.Remix == true, c => c.Account.MjRemixOn == accountFilter.Remix || !c.Account.RemixAutoSubmit)
                         .WhereIf(accountFilter.Remix == false, c => c.Account.MjRemixOn == accountFilter.Remix)
                         // Niji Remix 过滤
                         .WhereIf(accountFilter.NijiRemix == true, c => c.Account.NijiRemixOn == accountFilter.NijiRemix || !c.Account.RemixAutoSubmit)
                         .WhereIf(accountFilter.NijiRemix == false, c => c.Account.NijiRemixOn == accountFilter.NijiRemix)
                         // Remix 自动提交过滤
                         .WhereIf(accountFilter.RemixAutoConsidered.HasValue, c => c.Account.RemixAutoSubmit == accountFilter.RemixAutoConsidered)
                         .ToList();

                return _rule.Choose(list);
            }
        }

        /// <summary>
        /// 获取指定ID的实例（不判断是否存活）
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
        /// 获取指定ID的实例（不判断是否存活）
        /// </summary>
        /// <param name="instanceId">实例ID。</param>
        /// <returns>实例。</returns>
        public IDiscordInstance GetDiscordInstanceIsAlive(string instanceId)
        {
            return string.IsNullOrWhiteSpace(instanceId)
                ? null
                : _instances.FirstOrDefault(instance => instance.GetInstanceId == instanceId && instance.IsAlive);
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
        public void RemoveInstance(IDiscordInstance instance) => _instances.Remove(instance);
    }
}
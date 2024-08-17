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
        /// <param name="accountFilter"></param>
        /// <param name="isNewTask">是否过滤只接收新任务的实例</param>
        /// <param name="botType">过滤开启指定机器人的账号</param>
        /// <param name="blend">过滤支持 Blend 的账号</param>
        /// <param name="describe">过滤支持 Describe 的账号</param>
        /// <param name="isDomain">过滤垂直领域的账号</param>
        /// <param name="domainIds">过滤垂直领域 ID</param>
        /// <param name="ids">指定 ids 账号</param>
        public IDiscordInstance ChooseInstance(
            AccountFilter accountFilter = null,
            bool? isNewTask = null,
            EBotType? botType = null,
            bool? blend = null,
            bool? describe = null,
            bool? isDomain = null,
            List<string> domainIds = null,
            List<string> ids = null,
            bool? shorten = null)
        {
            if (accountFilter == null)
            {
                var list = GetAliveInstances()
                     .WhereIf(blend == true, c => c.Account.IsBlend)
                     .WhereIf(describe == true, c => c.Account.IsDescribe)
                     .WhereIf(shorten == true, c => c.Account.IsShorten)
                     .WhereIf(isNewTask == true, c => c.Account.IsAcceptNewTask == true)
                     .WhereIf(botType == EBotType.NIJI_JOURNEY, c => c.Account.EnableNiji == true)
                     .WhereIf(botType == EBotType.MID_JOURNEY, c => c.Account.EnableMj == true)
                     .WhereIf(isDomain == true && domainIds?.Count > 0, c => c.Account.IsVerticalDomain && c.Account.VerticalDomainIds.Any(x => domainIds.Contains(x)))
                     .WhereIf(isDomain == false, c => c.Account.IsVerticalDomain != true)
                     .WhereIf(ids?.Count > 0, c => ids.Contains(c.Account.ChannelId))
                     .ToList();

                return _rule.Choose(list);
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

                         // 过滤只接收新任务的实例
                         .WhereIf(isNewTask == true, c => c.Account.IsAcceptNewTask == true)

                         // 过滤开启 niji mj 的账号
                         .WhereIf(botType == EBotType.NIJI_JOURNEY, c => c.Account.EnableNiji == true)
                         .WhereIf(botType == EBotType.MID_JOURNEY, c => c.Account.EnableMj == true)

                         .WhereIf(blend == true, c => c.Account.IsBlend)
                         .WhereIf(describe == true, c => c.Account.IsDescribe)
                         .WhereIf(shorten == true, c => c.Account.IsShorten)

                         // 领域过滤
                         .WhereIf(isDomain == true && domainIds?.Count > 0, c => c.Account.IsVerticalDomain && c.Account.VerticalDomainIds.Any(x => domainIds.Contains(x)))
                         .WhereIf(isDomain == false, c => c.Account.IsVerticalDomain != true)

                         .WhereIf(ids?.Count > 0, c => ids.Contains(c.Account.ChannelId))
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
        /// 获取指定ID的实例（必须是存活的）
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
namespace Midjourney.Infrastructure.LoadBalancer
{
    /// <summary>
    /// 最少等待空闲选择规则。
    /// </summary>
    public class BestWaitIdleRule : IRule
    {
        private static readonly Random random = new Random();

        /// <summary>
        /// 根据最少等待空闲规则选择一个 Discord 实例。
        /// </summary>
        /// <param name="instances">可用的 Discord 实例列表。</param>
        /// <returns>选择的 Discord 实例。</returns>
        public IDiscordInstance Choose(List<IDiscordInstance> instances)
        {
            if (instances.Count == 0)
            {
                return null;
            }

            // 优先选择空闲的实例
            var idleCandidates = instances
                .Where(c => c.Account.CoreSize - c.GetRunningFutures().Count > 0)
                .GroupBy(c => c.Account.CoreSize - c.GetRunningFutures().Count)
                .OrderByDescending(g => g.Key)
                .FirstOrDefault();

            if (idleCandidates != null)
            {
                // 随机选择一个空闲实例
                return idleCandidates.ElementAt(random.Next(idleCandidates.Count()));
            }

            // 如果没有空闲的实例，则选择 -> (当前队列数 + 执行中的数量) / 核心数, 最小的实例
            var busyCandidates = instances
                .GroupBy(c => (double)(c.GetRunningFutures().Count + c.GetQueueTasks().Count) / c.Account.CoreSize)
                .OrderBy(g => g.Key)
                .FirstOrDefault();

            if (busyCandidates != null)
            {
                // 随机选择一个繁忙实例
                return busyCandidates.ElementAt(random.Next(busyCandidates.Count()));
            }

            return null;
        }
    }

    /// <summary>
    /// 轮询选择规则。
    /// </summary>
    public class RoundRobinRule : IRule
    {
        private int _position = -1;

        /// <summary>
        /// 根据轮询规则选择一个 Discord 实例。
        /// </summary>
        /// <param name="instances">可用的 Discord 实例列表。</param>
        /// <returns>选择的 Discord 实例。</returns>
        public IDiscordInstance Choose(List<IDiscordInstance> instances)
        {
            if (instances.Count == 0)
            {
                return null;
            }

            int pos = Interlocked.Increment(ref _position);
            return instances[pos % instances.Count];
        }
    }

    /// <summary>
    /// 随机规则
    /// </summary>
    public class RandomRule : IRule
    {
        private static readonly Random _random = new Random();

        public IDiscordInstance Choose(List<IDiscordInstance> instances)
        {
            if (instances.Count == 0)
            {
                return null;
            }

            int index = _random.Next(instances.Count);
            return instances[index];
        }
    }

    /// <summary>
    /// 权重规则
    /// </summary>
    public class WeightRule : IRule
    {
        public IDiscordInstance Choose(List<IDiscordInstance> instances)
        {
            if (instances.Count == 0)
            {
                return null;
            }

            int totalWeight = instances.Sum(i => i.Account.Weight);
            int randomWeight = new Random().Next(totalWeight);
            int currentWeight = 0;

            foreach (var instance in instances)
            {
                currentWeight += instance.Account.Weight;
                if (randomWeight < currentWeight)
                {
                    return instance;
                }
            }

            return instances.Last();  // Fallback, should never reach here
        }
    }

}
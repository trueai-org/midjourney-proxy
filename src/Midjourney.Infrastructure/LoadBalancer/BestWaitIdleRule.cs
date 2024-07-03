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

            var map = instances.GroupBy(i =>
            {
                int wait = i.GetRunningFutures().Count - i.Account().CoreSize;
                return wait >= 0 ? wait : -1;
            }).ToDictionary(g => g.Key, g => g.ToList());

            // 找到等待数最少的组
            var minWaitGroup = map.OrderBy(kv => kv.Key).FirstOrDefault().Value;
            if (minWaitGroup == null || minWaitGroup.Count == 0)
            {
                return null;  // 如果最少等待组没有实例，返回 null
            }

            // 从最少等待组中随机选择一个实例
            int index = random.Next(minWaitGroup.Count);
            return minWaitGroup[index];
        }
    }
}
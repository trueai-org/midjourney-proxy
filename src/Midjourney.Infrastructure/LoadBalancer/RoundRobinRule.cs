namespace Midjourney.Infrastructure.LoadBalancer
{
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
}
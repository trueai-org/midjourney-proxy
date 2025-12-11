using Midjourney.Infrastructure.LoadBalancer;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// 定义选择 Discord 实例的规则接口。
    /// </summary>
    public interface IDiscordInstanceRule
    {
        /// <summary>
        /// 根据规则选择一个 Discord 实例。
        /// </summary>
        /// <param name="instances">可用的 Discord 实例列表。</param>
        /// <returns>选择的 Discord 实例。</returns>
        DiscordInstance Choose(List<DiscordInstance> instances);
    }
}

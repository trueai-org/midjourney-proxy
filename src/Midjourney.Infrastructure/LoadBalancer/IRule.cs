namespace Midjourney.Infrastructure.LoadBalancer
{
    /// <summary>
    /// 定义选择 Discord 实例的规则接口。
    /// </summary>
    public interface IRule
    {
        /// <summary>
        /// 根据规则选择一个 Discord 实例。
        /// </summary>
        /// <param name="instances">可用的 Discord 实例列表。</param>
        /// <returns>选择的 Discord 实例。</returns>
        IDiscordInstance Choose(List<IDiscordInstance> instances);
    }
}
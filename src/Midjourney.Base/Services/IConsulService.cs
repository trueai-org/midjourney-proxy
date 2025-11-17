namespace Midjourney.Base.Services
{
    public interface IConsulService
    {
        /// <summary>
        /// 从 Consul 获取当前版本 KV 信息
        /// </summary>
        /// <returns></returns>
        Task<string> GetCurrentVersionAsync();

        Task RegisterServiceAsync();

        Task DeregisterServiceAsync();
    }
}
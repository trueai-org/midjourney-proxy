namespace Midjourney.Base.Services
{
    /// <summary>
    /// 升级服务接口
    /// </summary>
    public interface IUpgradeService
    {
        /// <summary>
        /// 检查最新版本
        /// </summary>
        Task<UpgradeInfo> CheckForUpdatesAsync();

        /// <summary>
        /// 开始下载升级包
        /// </summary>
        Task<bool> StartDownloadAsync();

        /// <summary>
        /// 获取升级状态
        /// </summary>
        UpgradeInfo GetUpgradeStatus();

        /// <summary>
        /// 取消更新
        /// </summary>
        void CancelUpdate();

        /// <summary>
        /// 是否支持当前平台
        /// </summary>
        bool IsSupportedPlatform { get; }

        /// <summary>
        /// 获取升级信息
        /// </summary>
        UpgradeInfo UpgradeInfo { get; }
    }
}

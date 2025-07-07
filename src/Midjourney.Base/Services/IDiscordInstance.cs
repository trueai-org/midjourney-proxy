namespace Midjourney.Base.Services
{
    /// <summary>
    ///
    /// </summary>
    public interface IDiscordInstance
    {
        /// <summary>
        /// 账号信息
        /// </summary>
        DiscordAccount Account { get; }

        /// <summary>
        /// 清理账号缓存
        /// </summary>
        /// <param name="id"></param>
        void ClearAccountCache(string id);

        /// <summary>
        /// 获取格式化后的 prompt 文本
        /// </summary>
        /// <param name="promptEn"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        string GetPrompt(string promptEn, TaskInfo info);

        /// <summary>
        /// 
        /// </summary>
        DiscordHelper DiscordHelper { get; }
    }
}
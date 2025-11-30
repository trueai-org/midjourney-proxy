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
        /// 获取格式化后的 prompt 文本
        /// </summary>
        /// <param name="promptEn"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        string GetPrompt(string promptEn, TaskInfo info);

        /// <summary>
        /// Discord 辅助
        /// </summary>
        DiscordHelper DiscordHelper { get; }
    }
}
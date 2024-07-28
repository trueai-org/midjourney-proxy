namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// Discord服务接口，定义了与Discord服务交互的基本方法。
    /// </summary>
    public interface IDiscordService
    {
        /// <summary>
        /// 提交imagine任务。
        /// </summary>
        /// <param name="prompt">提示词。</param>
        /// <param name="nonce">随机字符串。</param>
        /// <returns>提交结果消息。</returns>
        Task<Message> ImagineAsync(string prompt, string nonce, EBotType botType);

        /// <summary>
        /// 提交放大任务。
        /// </summary>
        /// <param name="messageId">消息ID。</param>
        /// <param name="index">索引。</param>
        /// <param name="messageHash">消息哈希。</param>
        /// <param name="messageFlags">消息标志。</param>
        /// <param name="nonce">随机字符串。</param>
        /// <returns>提交结果消息。</returns>
        Task<Message> UpscaleAsync(string messageId, int index, string messageHash, int messageFlags, string nonce, EBotType botType);

        /// <summary>
        /// 提交变换任务。
        /// </summary>
        /// <param name="messageId">消息ID。</param>
        /// <param name="index">索引。</param>
        /// <param name="messageHash">消息哈希。</param>
        /// <param name="messageFlags">消息标志。</param>
        /// <param name="nonce">随机字符串。</param>
        /// <returns>提交结果消息。</returns>
        Task<Message> VariationAsync(string messageId, int index, string messageHash, int messageFlags, string nonce, EBotType botType);

        /// <summary>
        /// 提交重新生成任务。
        /// </summary>
        /// <param name="messageId">消息ID。</param>
        /// <param name="messageHash">消息哈希。</param>
        /// <param name="messageFlags">消息标志。</param>
        /// <param name="nonce">随机字符串。</param>
        /// <returns>提交结果消息。</returns>
        Task<Message> RerollAsync(string messageId, string messageHash, int messageFlags, string nonce, EBotType botType);

        /// <summary>
        /// 执行动作
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="customId"></param>
        /// <param name="messageFlags"></param>
        /// <param name="nonce"></param>
        /// <returns></returns>
        Task<Message> ActionAsync(string messageId, string customId, int messageFlags, string nonce, EBotType botType);

        /// <summary>
        /// 图片 seed 值
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="nonce"></param>
        /// <returns></returns>
        Task<Message> SeedAsync(string jobId, string nonce, EBotType botType);


        /// <summary>
        /// 图片 seed 值消息
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="nonce"></param>
        /// <returns></returns>
        Task<Message> SeedMessagesAsync(string url);

        /// <summary>
        /// 执行 ZOOM
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <returns></returns>
        Task<Message> ZoomAsync(string messageId, string customId, string prompt, string nonce, EBotType botType);

        /// <summary>
        /// 图生文 - 生图
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        Task<Message> PicReaderAsync(string messageId, string customId, string prompt, string nonce, EBotType botType);

        /// <summary>
        /// Remix 操作
        /// </summary>
        /// <param name="action"></param>
        /// <param name="messageId"></param>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        Task<Message> RemixAsync(TaskAction action, string messageId, string modal, string customId, string prompt, string nonce, EBotType botType);

        /// <summary>
        /// 执行 info 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <returns></returns>
        Task<Message> InfoAsync(string nonce, EBotType botType);

        /// <summary>
        /// 执行 setting 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="isNiji"></param>
        /// <returns></returns>
        Task<Message> SettingAsync(string nonce, EBotType botType);

        /// <summary>
        /// 执行 settings button 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="custom_id"></param>
        /// <returns></returns>
        Task<Message> SettingButtonAsync(string nonce, string custom_id, EBotType botType);

        /// <summary>
        /// 执行 settings select 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        Task<Message> SettingSelectAsync(string nonce, string values);

        /// <summary>
        /// 局部重绘
        /// </summary>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="maskBase64"></param>
        /// <returns></returns>
        Task<Message> InpaintAsync(string customId, string prompt, string maskBase64, EBotType botType);

        /// <summary>
        /// 提交描述任务。
        /// </summary>
        /// <param name="finalFileName">最终文件名。</param>
        /// <param name="nonce">随机字符串。</param>
        /// <returns>提交结果消息。</returns>
        Task<Message> DescribeAsync(string finalFileName, string nonce, EBotType botType);

        /// <summary>
        /// 提交混合任务。
        /// </summary>
        /// <param name="finalFileNames">最终文件名列表。</param>
        /// <param name="dimensions">混合维度。</param>
        /// <param name="nonce">随机字符串。</param>
        /// <returns>提交结果消息。</returns>
        Task<Message> BlendAsync(List<string> finalFileNames, BlendDimensions dimensions, string nonce, EBotType botType);

        /// <summary>
        /// 上传文件。
        /// </summary>
        /// <param name="fileName">文件名。</param>
        /// <param name="dataUrl">数据URL。</param>
        /// <returns>上传结果消息。</returns>
        Task<Message> UploadAsync(string fileName, DataUrl dataUrl);

        /// <summary>
        /// 发送图片消息。
        /// </summary>
        /// <param name="content">内容。</param>
        /// <param name="finalFileName">最终文件名。</param>
        /// <returns>发送结果消息。</returns>
        Task<Message> SendImageMessageAsync(string content, string finalFileName);

        /// <summary>
        /// 自动读 discord 最后一条消息（设置为已读）
        /// </summary>
        /// <param name="lastMessageId"></param>
        /// <returns></returns>
        Task<Message> ReadMessageAsync(string lastMessageId);
    }
}
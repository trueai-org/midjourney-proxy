namespace Midjourney.Infrastructure
{
    /// <summary>
    /// 常量类.
    /// </summary>
    public static class Constants
    {
        // 任务扩展属性 start

        /// <summary>
        /// 通知回调地址.
        /// </summary>
        public const string TASK_PROPERTY_NOTIFY_HOOK = "notifyHook";

        /// <summary>
        /// bot类型，mj(默认)或niji
        /// MID_JOURNEY | NIJI_JOURNEY
        /// </summary>
        public const string TASK_PROPERTY_BOT_TYPE = "botType";

        /// <summary>
        /// 最终提示.
        /// </summary>
        public const string TASK_PROPERTY_FINAL_PROMPT = "finalPrompt";

        /// <summary>
        /// 原始消息内容
        /// </summary>
        public const string TASK_PROPERTY_MESSAGE_CONTENT = "messageContent";

        /// <summary>
        /// 消息ID.
        /// </summary>
        public const string TASK_PROPERTY_MESSAGE_ID = "messageId";

        /// <summary>
        /// 消息哈希.
        /// </summary>
        public const string TASK_PROPERTY_MESSAGE_HASH = "messageHash";

        /// <summary>
        /// 进度消息ID.
        /// </summary>
        public const string TASK_PROPERTY_PROGRESS_MESSAGE_ID = "progressMessageId";

        /// <summary>
        /// 标志.
        /// </summary>
        public const string TASK_PROPERTY_FLAGS = "flags";

        /// <summary>
        /// 随机数.
        /// </summary>
        public const string TASK_PROPERTY_NONCE = "nonce";

        /// <summary>
        /// Discord实例ID.
        /// </summary>
        public const string TASK_PROPERTY_DISCORD_INSTANCE_ID = "discordInstanceId";

        /// <summary>
        /// 引用消息ID.
        /// </summary>
        public const string TASK_PROPERTY_REFERENCED_MESSAGE_ID = "referencedMessageId";

        // 任务扩展属性 end

        /// <summary>
        /// API密钥请求头名称.
        /// </summary>
        public const string API_SECRET_HEADER_NAME = "mj-api-secret";

        /// <summary>
        /// 默认的Discord用户代理.
        /// </summary>
        public const string DEFAULT_DISCORD_USER_AGENT = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/112.0.0.0 Safari/537.36";

        /// <summary>
        /// MJ消息已处理标志.
        /// </summary>
        public const string MJ_MESSAGE_HANDLED = "mj_proxy_handled";
    }
}
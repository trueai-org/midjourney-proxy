namespace Midjourney.Infrastructure
{
    /// <summary>
    /// 返回码常量类.
    /// </summary>
    public static class ReturnCode
    {
        /// <summary>
        /// 成功.
        /// </summary>
        public const int SUCCESS = 1;

        /// <summary>
        /// 数据未找到.
        /// </summary>
        public const int NOT_FOUND = 3;

        /// <summary>
        /// 校验错误.
        /// </summary>
        public const int VALIDATION_ERROR = 4;

        /// <summary>
        /// 系统异常.
        /// </summary>
        public const int FAILURE = 9;

        /// <summary>
        /// 已存在.
        /// </summary>
        public const int EXISTED = 21;

        /// <summary>
        /// 排队中.
        /// </summary>
        public const int IN_QUEUE = 22;

        /// <summary>
        /// 队列已满.
        /// </summary>
        public const int QUEUE_REJECTED = 23;

        /// <summary>
        /// prompt包含敏感词.
        /// </summary>
        public const int BANNED_PROMPT = 24;
    }
}
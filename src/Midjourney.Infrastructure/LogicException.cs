namespace Midjourney.Infrastructure
{
    /// <summary>
    /// 逻辑异常
    /// </summary>
    public class LogicException : Exception
    {
        public LogicException()
        { }

        public LogicException(string message)
            : base(message)
        { }

        public LogicException(string message, Exception innerException)
            : base(message, innerException)
        { }

        public LogicException(int code, string message)
           : base(message)
        {
            Code = code;
        }

        /// <summary>
        /// 自定义错误编码
        /// </summary>
        public int Code { get; private set; }
    }

    /// <summary>
    /// 参数异常
    /// </summary>
    public class LogicParamException : LogicException
    {
        public LogicParamException(string message = "参数异常")
           : base(message)
        {

        }
    }
}

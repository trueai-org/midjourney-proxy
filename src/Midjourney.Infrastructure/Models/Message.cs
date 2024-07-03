namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// 通用消息类，用于封装返回结果。
    /// </summary>
    public class Message
    {
        /// <summary>
        /// 状态码。
        /// </summary>
        public int Code { get; }

        /// <summary>
        /// 描述信息。
        /// </summary>
        public string Description { get; }

        protected Message(int code, string description)
        {
            Code = code;
            Description = description;
        }

        /// <summary>
        /// 返回成功的消息。
        /// </summary>
        public static Message Success() => new Message(ReturnCode.SUCCESS, "成功");


        /// <summary>
        /// 返回成功的消息。
        /// </summary>
        public static Message Success(string message) => new Message(ReturnCode.SUCCESS, message);

        /// <summary>
        /// 返回未找到的消息。
        /// </summary>
        public static Message NotFound() => new Message(ReturnCode.NOT_FOUND, "数据未找到");

        /// <summary>
        /// 返回校验错误的消息。
        /// </summary>
        public static Message ValidationError() => new Message(ReturnCode.VALIDATION_ERROR, "校验错误");

        /// <summary>
        /// 返回系统异常的消息。
        /// </summary>
        public static Message Failure() => new Message(ReturnCode.FAILURE, "系统异常");

        /// <summary>
        /// 返回带自定义描述的系统异常消息。
        /// </summary>
        public static Message Failure(string description) => new Message(ReturnCode.FAILURE, description);

        /// <summary>
        /// 返回自定义状态码和描述的消息。
        /// </summary>
        public static Message Of(int code, string description) => new Message(code, description);
    }

    /// <summary>
    /// 通用消息类，用于封装返回结果。
    /// </summary>
    /// <typeparam name="T">消息类型。</typeparam>
    public class Message<T> : Message
    {
        /// <summary>
        /// 返回结果。
        /// </summary>
        public T Result { get; }

        protected Message(int code, string description, T result = default)
            : base(code, description)
        {
            Result = result;
        }

        /// <summary>
        /// 返回成功的消息。
        /// </summary>
        /// <param name="result">结果。</param>
        public static Message<T> Success(T result) => new Message<T>(ReturnCode.SUCCESS, "成功", result);

        /// <summary>
        /// 返回带自定义状态码和描述的成功消息。
        /// </summary>
        public static Message<T> Success(int code, string description, T result) => new Message<T>(code, description, result);

        /// <summary>
        /// 返回自定义状态码、描述和结果的消息。
        /// </summary>
        public static Message<T> Of(int code, string description, T result) => new Message<T>(code, description, result);
    }
}
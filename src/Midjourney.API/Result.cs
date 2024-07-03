namespace Midjourney.API
{
    /// <summary>
    /// 结果
    /// </summary>
    public class Result
    {
        public bool Success { get; set; }

        public int Code { get; set; }

        public string Message { get; set; }

        public string Timestamp { get; set; } = DateTime.Now.Ticks.ToString();

        public Result()
        {

        }

        protected Result(bool success)
        {
            Success = success;
        }

        protected Result(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        protected Result(bool success, string message, int code)
            : this(success, message)
        {
            Code = code;
        }

        public static Result Ok()
        {
            return new Result(true);
        }

        public static Result Ok(string message)
        {
            return new Result(true, message);
        }

        public static Result Fail(string error)
        {
            return new Result(false, error, -1);
        }

        public static Result<TValue> Ok<TValue>(int code, TValue value) where TValue : class
        {
            return new Result<TValue>(value, code, true, null);
        }

        public static Result<TValue> Ok<TValue>(TValue value) where TValue : class
        {
            return new Result<TValue>(value, true, null);
        }

        public static Result<TValue> Ok<TValue>(TValue value, string message)
        {
            return new Result<TValue>(value, true, message);
        }

        public static Result<TValue> Fail<TValue>(string error)
        {
            return new Result<TValue>(default, false, error);
        }

        public static Result<TValue> Fail<TValue>(TValue value, string error)
        {
            return new Result<TValue>(value, false, error);
        }
    }

    public class Result<TValue> : Result
    {
        public TValue Data { get; set; }

        public Result()
        {

        }

        protected internal Result(TValue value, bool success, string message)
        : base(success, message, !success ? -1 : 0)
        {
            Data = value;
        }

        protected internal Result(TValue value, int code, bool success, string message)
            : base(success, message, !success ? -1 : 0)
        {
            Data = value;
            Code = code;
        }
    }
}

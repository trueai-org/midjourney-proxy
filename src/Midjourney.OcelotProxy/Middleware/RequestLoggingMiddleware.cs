namespace Midjourney.OcelotProxy.Middleware
{
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var requestId = Guid.NewGuid().ToString("N")[..8];
            var startTime = DateTime.UtcNow;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // 记录请求开始
            _logger.LogInformation("[{RequestId}] {Method} {Path} 开始处理",
                requestId, context.Request.Method, context.Request.Path);

            try
            {
                // 执行下一个中间件
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{RequestId}] {Method} {Path} 处理异常",
                    requestId, context.Request.Method, context.Request.Path);
                throw;
            }
            finally
            {
                stopwatch.Stop();

                // 记录请求完成（不尝试修改响应）
                _logger.LogInformation("[{RequestId}] {Method} {Path} 处理完成 - {StatusCode} ({ElapsedMs}ms)",
                    requestId,
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    stopwatch.Elapsed.TotalMilliseconds);
            }

            //var startTime = DateTime.UtcNow;
            //var requestId = Guid.NewGuid().ToString("N")[..8];

            //// 添加请求 ID 到响应头
            //context.Response.Headers.Append("X-Request-Id", requestId);

            //_logger.LogInformation("[{RequestId}] {Method} {Path} 开始处理",
            //    requestId, context.Request.Method, context.Request.Path);

            //try
            //{
            //    await _next(context);
            //}
            //finally
            //{
            //    var duration = DateTime.UtcNow - startTime;
            //    _logger.LogInformation("[{RequestId}] {Method} {Path} 处理完成 - {StatusCode} ({Duration}ms)",
            //        requestId, context.Request.Method, context.Request.Path,
            //        context.Response.StatusCode, duration.TotalMilliseconds);
            //}
        }
    }
}
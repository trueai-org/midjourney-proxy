namespace Midjourney.YarpProxy.Middleware
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
            // 收集所有 IP 相关信息
            var request = context.Request;
            var ipInfo = new
            {
                RemoteIp = context.Connection.RemoteIpAddress?.ToString(),
                XForwardedFor = request.Headers["X-Forwarded-For"],
                XRealIp = request.Headers["X-Real-IP"],
                XOriginalFor = request.Headers["X-Original-For"],
                CFConnectingIp = request.Headers["CF-Connecting-IP"], // Cloudflare
                XClientIp = request.Headers["X-Client-IP"],
                UserAgent = request.Headers["User-Agent"]
            };

            _logger.LogDebug("Request IP Info: {Method} {Path} - {@IpInfo}",
                request.Method,
                request.Path,
                ipInfo);

            var startTime = DateTime.UtcNow;
            var requestId = Guid.NewGuid().ToString("N")[..8];

            // 添加请求 ID 到响应头
            context.Response.Headers.Append("X-Request-Id", requestId);

            _logger.LogInformation("[{RequestId}] {Method} {Path} 开始处理",
                requestId, context.Request.Method, context.Request.Path);

            try
            {
                await _next(context);
            }
            finally
            {
                var duration = DateTime.UtcNow - startTime;
                _logger.LogInformation("[{RequestId}] {Method} {Path} 处理完成 - {StatusCode} ({Duration}ms)",
                    requestId, context.Request.Method, context.Request.Path,
                    context.Response.StatusCode, duration.TotalMilliseconds);
            }
        }
    }
}
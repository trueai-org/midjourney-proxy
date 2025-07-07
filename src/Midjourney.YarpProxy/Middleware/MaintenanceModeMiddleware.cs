using System.Text;
using Yarp.ReverseProxy.Configuration;

namespace Midjourney.YarpProxy.Middleware
{
    public class MaintenanceModeMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<MaintenanceModeMiddleware> _logger;
        private readonly IProxyConfigProvider _configProvider;

        public MaintenanceModeMiddleware(
            RequestDelegate next,
            ILogger<MaintenanceModeMiddleware> logger,
            IProxyConfigProvider configProvider)
        {
            _next = next;
            _logger = logger;
            _configProvider = configProvider;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 如果是调试端点，直接放行
            if (context.Request.Path.StartsWithSegments("/debug") ||
                context.Request.Path.StartsWithSegments("/health"))
            {
                await _next(context);
                return;
            }

            // 检查是否有可用的后端服务
            var config = _configProvider.GetConfig();
            var hasHealthyDestinations = config.Clusters.Any(c =>
                c.Destinations.Any());

            if (!hasHealthyDestinations)
            {
                _logger.LogWarning("没有可用的后端服务，返回维护模式响应");

                context.Response.StatusCode = 503; // Service Unavailable
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsync($$"""
                {
                    "error": "Service Unavailable",
                    "message": "所有后端服务当前不可用，系统正在维护中",
                    "timestamp": "{{DateTime.UtcNow:O}}",
                    "retryAfter": 30
                }
                """, Encoding.UTF8);

                return;
            }

            await _next(context);
        }
    }
}

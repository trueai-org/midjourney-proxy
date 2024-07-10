using Microsoft.AspNetCore.Authorization;

namespace Midjourney.API
{
    /// <summary>
    /// 简单的授权中间件
    /// </summary>
    public class SimpleAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _authToken;

        public SimpleAuthMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _authToken = configuration["AuthToken"]; // 从配置中获取预期的令牌
        }

        public async Task Invoke(HttpContext context)
        {
            var path = context.Request.Path.Value;

            // 检查是否有 AllowAnonymous 特性
            var endpoint = context.GetEndpoint();
            var allowAnonymous = endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null;

            if (allowAnonymous)
            {
                await _next(context);
                return;
            }

            // 获取 Authorization 或 Mj-Api-Secret 头部
            var hasAuthHeader = context.Request.Headers.TryGetValue("Authorization", out var authHeader);
            var hasApiSecretHeader = context.Request.Headers.TryGetValue("Mj-Api-Secret", out var apiSecretHeader);

            if (!hasAuthHeader && !hasApiSecretHeader)
            {
                context.Response.StatusCode = 401; // Unauthorized
                await context.Response.WriteAsync("Authorization header missing.");
                return;
            }

            var token = hasAuthHeader ? authHeader.ToString() : apiSecretHeader.ToString();
            if (token != _authToken)
            {
                context.Response.StatusCode = 401; // Unauthorized
                await context.Response.WriteAsync("Invalid token.");
                return;
            }

            await _next(context);
        }
    }

}

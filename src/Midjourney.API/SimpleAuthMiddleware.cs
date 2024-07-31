using Microsoft.AspNetCore.Authorization;

namespace Midjourney.API
{
    /// <summary>
    /// 简单的授权中间件
    /// </summary>
    public class SimpleAuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _userToken;
        private readonly string _adminToken;

        public SimpleAuthMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _userToken = configuration["UserToken"]; // 从配置中获取用户令牌
            _adminToken = configuration["AdminToken"]; // 从配置中获取管理员令牌
        }

        public async Task Invoke(HttpContext context)
        {
            var path = context.Request.Path.Value;
            if (path.StartsWith("/mj-turbo"))
            {
                context.Items["Mode"] = "turbo";
            }
            else if (path.StartsWith("/mj-relax"))
            {
                context.Items["Mode"] = "relax";
            }
            else if (path.StartsWith("/mj-fast"))
            {
                context.Items["Mode"] = "fast";
            }
            else
            {
                context.Items["Mode"] = "";
            }

            // 演示模式下不需要验证
            if (GlobalConfiguration.IsDemoMode == true)
            {
                await _next(context);
                return;
            }

            // 如果都为空，则不需要验证
            if (string.IsNullOrWhiteSpace(_userToken) && string.IsNullOrWhiteSpace(_adminToken))
            {
                await _next(context);
                return;
            }

            // 获取 Authorization 或 Mj-Api-Secret 头部
            var hasAuthHeader = context.Request.Headers.TryGetValue("Authorization", out var authHeader);
            var hasApiSecretHeader = context.Request.Headers.TryGetValue("Mj-Api-Secret", out var apiSecretHeader);

            // 检查是否有 AllowAnonymous 特性
            var endpoint = context.GetEndpoint();
            var allowAnonymous = endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null;

            // 如果没有这两个头部，且不允许匿名访问，则返回 401
            if (!allowAnonymous && !hasAuthHeader && !hasApiSecretHeader)
            {
                context.Response.StatusCode = 401; // Unauthorized
                await context.Response.WriteAsync("Authorization header missing.");
                return;
            }

            // 验证令牌是否正确
            if (!allowAnonymous)
            {
                var token = hasAuthHeader ? authHeader.ToString() : apiSecretHeader.ToString();
                if (token != _userToken && token != _adminToken)
                {
                    context.Response.StatusCode = 401; // Unauthorized
                    await context.Response.WriteAsync("Invalid token.");
                    return;
                }

                // 如果是管理员接口，需要管理员令牌
                if (context.Request.Path.StartsWithSegments("/mj/admin") && token != _adminToken)
                {
                    context.Response.StatusCode = 403; // Forbidden
                    await context.Response.WriteAsync("Forbidden: Admin access required.");
                    return;
                }
            }

            await _next(context);
        }
    }

}

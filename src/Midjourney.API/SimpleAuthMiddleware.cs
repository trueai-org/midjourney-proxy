using Microsoft.AspNetCore.Authorization;

namespace Midjourney.API
{
    /// <summary>
    /// 简单的授权中间件
    /// </summary>
    public class SimpleAuthMiddleware
    {
        private readonly RequestDelegate _next;

        public SimpleAuthMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, WorkContext workContext)
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

            // 检查是否有 AllowAnonymous 特性
            var endpoint = context.GetEndpoint();
            var allowAnonymous = endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null;
            if (!allowAnonymous)
            {
                var user = workContext.GetUser();

                // 如果用户被禁用
                if (user?.Status == EUserStatus.DISABLED)
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Forbidden: User is disabled.");
                    return;
                }

                // 如果是管理员接口，需要管理员角色
                if (context.Request.Path.StartsWithSegments("/mj/admin"))
                {
                    if (user?.Role != EUserRole.ADMIN)
                    {
                        context.Response.StatusCode = 401;
                        await context.Response.WriteAsync("Forbidden: Admin access required.");
                        return;
                    }
                }
                else
                {
                    // 非管理员接口，只要登录即可或开启访客模式
                    // 未开启访客
                    // 并且不允许匿名访问，则返回 401
                    if (user == null && !GlobalConfiguration.Setting.EnableGuest)
                    {
                        context.Response.StatusCode = 401;
                        await context.Response.WriteAsync("Authorization header missing.");
                        return;
                    }
                }
            }

            await _next(context);
        }
    }
}
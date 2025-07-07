using System.Runtime;
using System.Text;
using Consul;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Midjourney.YarpProxy.Middleware;
using Midjourney.YarpProxy.Models;
using Midjourney.YarpProxy.Services;
using Serilog;
using Yarp.ReverseProxy.Configuration;

// 配置 Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .Build())
    .CreateLogger();

try
{
    Log.Information("启动 YARP 网关...");

    var builder = WebApplication.CreateBuilder(args);

    // 配置转发头
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                                  ForwardedHeaders.XForwardedProto |
                                  ForwardedHeaders.XForwardedHost;

        // 如果您知道代理服务器的 IP，可以添加到已知代理列表
        // options.KnownProxies.Add(IPAddress.Parse("192.168.1.1"));

        // 如果在 Docker 或 Kubernetes 环境中，可能需要清空已知网络
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

    // 添加 Consul 配置
    builder.Services.Configure<ConsulOptions>(builder.Configuration.GetSection(nameof(ConsulOptions)));

    // 添加 YARP 服务
    builder.Services.AddSingleton<IProxyConfigProvider>(new InMemoryConfigProvider([], []));
    builder.Services.AddReverseProxy().LoadFromMemory([], []);

    // 添加 Consul 服务发现
    builder.Services.AddSingleton<IConsulClient>(sp =>
    {
        var consulOptions = sp.GetRequiredService<IOptions<ConsulOptions>>().Value;
        return new ConsulClient(config =>
        {
            config.Address = new Uri(consulOptions.ConsulUrl);
        });
    });

    builder.Services.AddHealthChecks()
        .AddCheck("YARP Gateway Health Check", () => HealthCheckResult.Healthy("YARP 网关运行正常"));

    // 添加 Consul 服务发现后台服务
    builder.Services.AddHostedService<ConsulServiceDiscoveryHostedService>();

    var app = builder.Build();

    // 使用转发头中间件（应该在其他中间件之前）
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.All
    });

    // 配置中间件管道
    app.UseMiddleware<RequestLoggingMiddleware>();

    // 添加维护模式中间件
    app.UseMiddleware<MaintenanceModeMiddleware>();

    app.UseRouting();

    // 健康检查端点
    app.MapHealthChecks("/health");

    // 添加一个调试路由
    app.MapGet("/debug/routes", (IProxyConfigProvider provider) =>
    {
        var config = provider.GetConfig();
        return Results.Ok(new
        {
            Routes = config.Routes.Select(r => new
            {
                r.RouteId,
                Path = r.Match.Path,
                ClusterId = r.ClusterId
            }),
            Clusters = config.Clusters.Select(c => new
            {
                c.ClusterId,
                c.Destinations.Count,
                Destinations = c.Destinations.Select(d => new
                {
                    d.Key,
                    Address = d.Value.Address
                })
            })
        });
    });

    app.MapGet("/debug/headers", (HttpContext context) =>
    {
        var headers = context.Request.Headers
            .ToDictionary(h => h.Key, h => h.Value.ToArray());

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

        return Results.Ok(new
        {
            request.Method,
            request.Path,
            IpInfo = ipInfo,
            RemoteIpAddress = context.Connection.RemoteIpAddress?.ToString(),
            LocalIpAddress = context.Connection.LocalIpAddress?.ToString(),
            Headers = headers,
            UserAgent = context.Request.Headers["User-Agent"].FirstOrDefault()
        });
    });

    // YARP 反向代理
    app.MapReverseProxy();

    //// YARP 反向代理（优先处理）
    //app.MapReverseProxy(proxyPipeline => {
    //    // 可以在这里添加额外的前置或后置处理
    //    proxyPipeline.Use((context, next) => {
    //        // 记录转发前的信息
    //        return next();
    //    });
    //});

    // 默认路由（用于未匹配的请求）
    app.MapFallback(async context =>
    {
        context.Response.StatusCode = 404;
        //context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync($$"""
        {
            "error": "Not Found",
            "message": "请求的路径未找到匹配的服务",
            "path": "{{context.Request.Path}}",
            "timestamp": "{{DateTime.UtcNow:O}}"
        }
        """, Encoding.UTF8);
    });

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "YARP 网关启动失败");
}
finally
{
    Log.CloseAndFlush();
}
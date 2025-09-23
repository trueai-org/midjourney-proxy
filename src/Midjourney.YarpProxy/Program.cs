using System.Collections.Concurrent;
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
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Model;

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

    builder.Services.AddSingleton<ILoadBalancingPolicy, WeightedRandomLoadBalancingPolicy>();

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

    // 暴露一个 API 路由显示连接数
    app.MapGet("/debug/connection-counts", () =>
    {
        var connectionCounts = ProxyConnectionCountMiddleware.GetConnectionCounts();
        return Results.Ok(connectionCounts);
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

    //// 注册代理中间件
    //app.MapReverseProxy(proxyPipeline =>
    //{
    //    // 1. 可选：按规则手动选择 cluster
    //    proxyPipeline.Use((context, next) =>
    //    {
    //        var lookup = context.RequestServices.GetRequiredService<IProxyStateLookup>();
    //        if (lookup.TryGetCluster(ChooseCluster(context), out var cluster))
    //        {
    //            context.ReassignProxyRequest(cluster);
    //        }

    //        return next();
    //    });

    //    proxyPipeline.UseLoadBalancing();

    //    // 必须在 LoadBalancing 之后读取已选 destination 并计数
    //    // 使用 ProxyRequest 中间件（在目标被选定后触发）
    //    proxyPipeline.Use(async (context, next) =>
    //    {
    //        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
    //        if (proxyFeature?.ProxiedDestination != null)
    //        {
    //        }

    //        var destinationAddress = proxyFeature?.ProxiedDestination?.Model?.Config?.Address;

    //        try
    //        {
    //            // 请求开始时增加连接数
    //            ProxyConnectionCountMiddleware.IncrementConnection(destinationAddress);

    //            // 等待代理目标被选择（此时 ProxiedDestination 应该不为 null）
    //            await next(); // 继续执行代理管道
    //        }
    //        finally
    //        {
    //            ProxyConnectionCountMiddleware.DecrementConnection(destinationAddress);
    //        }
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

//string ChooseCluster(HttpContext context)
//{
//    // Decide which cluster to use. This could be random, weighted, based on headers, etc.
//    //return Random.Shared.Next(2) == 1 ? "midjourney-cluster" : "cluster2";

//    //// 随机获取一个可用的集群
//    //var clusterNames = destinations
//    //    .Select(d => d.Model.Config.Address)
//    //    .Distinct()
//    //    .ToList();

//    //return clusterNames.Count > 0
//    //    ? clusterNames[Random.Shared.Next(clusterNames.Count)]
//    //    : "midjourney-cluster"; // 默认集群

//    var proxyFeature = context.Features.Get<IReverseProxyFeature>();
//    return proxyFeature?.Route?.Config.ClusterId ?? "midjourney-cluster";
//}

public class ProxyConnectionCountMiddleware
{
    private static readonly ConcurrentDictionary<string, int> _connectionCounts = new();

    public static void IncrementConnection(string destinationAddress)
    {
        if (string.IsNullOrEmpty(destinationAddress))
            return;

        _connectionCounts.AddOrUpdate(destinationAddress, 1, (_, count) => count + 1);
    }

    public static void DecrementConnection(string destinationAddress)
    {
        if (string.IsNullOrEmpty(destinationAddress))
            return;

        _connectionCounts.AddOrUpdate(destinationAddress, 0, (_, count) => Math.Max(0, count - 1));
    }

    public static IReadOnlyDictionary<string, int> GetConnectionCounts()
    {
        return _connectionCounts;
    }
}

/// <summary>
/// 加权随机负载均衡策略实现
/// - 从每个 destination 的 metadata 中读取 "Weight"（不区分大小写）。
/// - 权重 <= 0 的 destination 会被视为不可选（跳过）。
/// - 如果解析不到任何权重或所有权重为 0，会退化为返回第一个可用 destination。
///
/// 使用：
///   builder.Services.AddSingleton<ILoadBalancingPolicy, WeightedRandomLoadBalancingPolicy>();
/// 在 Cluster 的配置中指定 LoadBalancing.Policy = "WeightedRandom"（或使用 Name 字符串 "WeightedRandom"）
/// </summary>
public class WeightedRandomLoadBalancingPolicy : ILoadBalancingPolicy
{
    private readonly Random _random = Random.Shared;

    /// <summary>
    /// 策略名称（配置中引用此名称以使用该策略）
    /// </summary>
    public string Name => "WeightedRandom";

    /// <summary>
    /// 选择一个 destination。
    /// </summary>
    /// <param name="context">当前 HttpContext（可用于按请求上下文做额外决策）</param>
    /// <param name="cluster">集群状态</param>
    /// <param name="availableDestinations">可用目标列表</param>
    /// <returns>选中的 DestinationState，如果没有可选项返回 null</returns>
    public DestinationState PickDestination(HttpContext context, ClusterState cluster, IReadOnlyList<DestinationState> availableDestinations)
    {
        if (availableDestinations == null || availableDestinations.Count == 0)
            return null;

        var weighted = new List<(DestinationState dest, int weight)>(availableDestinations.Count);

        var cfg = context.RequestServices.GetRequiredService<IProxyConfigProvider>().GetConfig();

        foreach (var dest in availableDestinations)
        {
            var metadata = cfg.Clusters.FirstOrDefault(x => x.ClusterId == cluster.ClusterId)
                ?.Destinations.FirstOrDefault(c => c.Key == dest.DestinationId).Value.Metadata;
            int weight = 1;
            if (metadata != null)
            {
                if (metadata.TryGetValue("Weight", out var ws) && int.TryParse(ws, out var parsed))
                {
                    weight = Math.Max(0, parsed);
                }
            }

            if (weight > 0)
                weighted.Add((dest, weight));
        }

        DestinationState selected;

        if (weighted.Count == 0)
        {
            selected = availableDestinations[0];
        }
        else
        {
            var total = weighted.Sum(t => t.weight);
            var r = _random.Next(0, total);
            int acc = 0;
            selected = weighted[0].dest;
            foreach (var (dest, w) in weighted)
            {
                acc += w;
                if (r < acc)
                {
                    selected = dest;
                    break;
                }
            }
        }

        try
        {
            // --- 在这里（选中时）进行计数，并注册请求完成时的回调以减计数 ---
            var address = selected.Model.Config.Address;
            ProxyConnectionCountMiddleware.IncrementConnection(address);

            // 注册回调，确保请求结束时减计数
            // 注意：如果多个策略/中间件也注册 OnCompleted，这个回调仍会被执行
            context.Response.OnCompleted(() =>
            {
                ProxyConnectionCountMiddleware.DecrementConnection(address);
                return Task.CompletedTask;
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to register connection counting for selected destination.");
        }

        return selected;
    }
}
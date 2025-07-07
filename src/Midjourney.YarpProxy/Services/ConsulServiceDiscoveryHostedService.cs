using Consul;
using Microsoft.Extensions.Options;
using Midjourney.YarpProxy.Models;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.LoadBalancing;

using DestinationConfig = Yarp.ReverseProxy.Configuration.DestinationConfig;
using RouteConfig = Yarp.ReverseProxy.Configuration.RouteConfig;

namespace Midjourney.YarpProxy.Services
{
    /// <summary>
    /// Consul 服务发现后台服务
    /// </summary>
    public class ConsulServiceDiscoveryHostedService : BackgroundService
    {
        private readonly IConsulClient _consulClient;
        private readonly ILogger<ConsulServiceDiscoveryHostedService> _logger;
        private readonly ConsulOptions _consulOptions;
        private ulong _lastIndex;
        private readonly InMemoryConfigProvider _yarpConfigProvider;

        public ConsulServiceDiscoveryHostedService(
            IConsulClient consulClient,
            ILogger<ConsulServiceDiscoveryHostedService> logger,
            IProxyConfigProvider proxyConfigProvider,
            IOptions<ConsulOptions> consulOptions,
            InMemoryConfigProvider yarpConfigProvider)
        {
            _consulClient = consulClient;
            _logger = logger;
            _consulOptions = consulOptions.Value;
            _yarpConfigProvider = yarpConfigProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("正在从 Consul 获取服务信息...");

                    // 首次启动时立即更新一次配置
                    await UpdateYarpConfig(stoppingToken);

                    // 启动长轮询监听Consul服务变更
                    await WatchServiceChanges(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "更新 YARP 配置时出错");
                }

                // 每30秒更新一次配置
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        private async Task WatchServiceChanges(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("开始监听Consul服务变更...");

                    // 使用Consul的阻塞查询（长轮询）监听服务变化
                    var queryOptions = new QueryOptions
                    {
                        WaitIndex = _lastIndex,  // 上次查询的索引
                        WaitTime = TimeSpan.FromMinutes(5) // 长轮询超时时间
                    };

                    // 监听健康状态变化
                    var result = await _consulClient.Health.Service(
                        _consulOptions.ServiceName,
                        string.Empty,
                        false, // 获取所有状态，不仅是健康的
                        queryOptions,
                        cancellationToken);

                    _lastIndex = result.LastIndex;

                    _logger.LogInformation("检测到服务变更或健康状态变化，更新YARP配置...");

                    await UpdateYarpConfig(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "监听Consul服务变更时出错，将在10秒后重试");
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                }
            }
        }

        private async Task UpdateYarpConfig(CancellationToken cancellationToken)
        {
            // 从 Consul 获取服务信息
            var servicesResult = await _consulClient.Agent.Services(cancellationToken);

            // 获取健康检查状态
            var healthChecks = await _consulClient.Health.State(HealthStatus.Any, cancellationToken);
            var checksDict = healthChecks.Response.ToDictionary(c => c.ServiceID);

            // 过滤出目标服务，并且必须是健康的服务
            var midjourneyServices = servicesResult.Response.Values
                .Where(s => s.Service == _consulOptions.ServiceName)
                .Where(s => !checksDict.TryGetValue(s.ID, out var check) ||
                            check.Status == HealthStatus.Passing)
                .ToList();

            _logger.LogInformation("找到 {Count} 个健康的 {ServiceName} 服务实例",
                midjourneyServices.Count, _consulOptions.ServiceName);

            if (midjourneyServices.Count == 0)
            {
                _logger.LogInformation("没有找到健康的 {ServiceName} 服务实例，清空路由配置", _consulOptions.ServiceName);
                _yarpConfigProvider.Update([], []);
                return;
            }


            // 创建 YARP 路由配置 - 简化但更有效的配置
            var routes = new List<RouteConfig>
            {
                new RouteConfig
                {
                    RouteId = "midjourney-route",
                    ClusterId = "midjourney-cluster",
                    Match = new RouteMatch
                    {
                        //Path = "/{**catch-all}"
                        Path = "{**catch-all}" // 匹配所有路径 
                    },
                    Transforms = new[]
                    {
                        //// 保持原有的 X-Forwarded-For 头，不要被 YARP 覆盖
                        //new Dictionary<string, string>
                        //{
                        //    ["RequestHeadersCopy"] = "true"
                        //},
                        //// 如果没有 X-Forwarded-For，则添加；如果有，则追加
                        //new Dictionary<string, string>
                        //{
                        //    ["RequestHeader"] = "X-Forwarded-For:{header:x-forwarded-for:},{remote_addr}"
                        //},
                        //// 保持其他转发头
                        //new Dictionary<string, string>
                        //{
                        //    ["RequestHeader"] = "X-Forwarded-Proto:{scheme}"
                        //},
                        //new Dictionary<string, string>
                        //{
                        //    ["RequestHeader"] = "X-Forwarded-Host:{header:host}"
                        //},

                        // 简单粗暴：完全禁用 YARP 对转发头的自动处理
                        new Dictionary<string, string>
                        {
                            ["X-Forwarded"] = "Off"
                        }
                    }
                }
            };

            // 创建 YARP 集群配置
            var destinations = new Dictionary<string, DestinationConfig>();
            foreach (var service in midjourneyServices)
            {
                var destination = new DestinationConfig
                {
                    Address = $"http://{service.Address}:{service.Port}"
                };
                destinations.Add($"midjourney-{service.ID}", destination);
            }

            var clusters = new List<ClusterConfig>
            {
                new ClusterConfig
                {
                    ClusterId = "midjourney-cluster",
                    LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin,
                    HealthCheck = new HealthCheckConfig
                    {
                        Active = new ActiveHealthCheckConfig
                        {
                            Enabled = true,
                            Interval = TimeSpan.FromSeconds(10),
                            Timeout = TimeSpan.FromSeconds(5),
                            Path = "/health"
                        }
                    },
                    Destinations = destinations
                }
            };

            //var clusters = new List<ClusterConfig>
            //{
            //    new ClusterConfig
            //    {
            //        ClusterId = "midjourney-cluster",
            //        LoadBalancingPolicy = LoadBalancingPolicies.RoundRobin,
            //        HealthCheck = new HealthCheckConfig
            //        {
            //            Active = new ActiveHealthCheckConfig
            //            {
            //                Enabled = true,
            //                Interval = TimeSpan.FromSeconds(10),
            //                Timeout = TimeSpan.FromSeconds(5),
            //                Path = "/health",
            //                Policy = HealthCheckConstants.ActivePolicy.ConsecutiveFailures
            //            },
            //            Passive = new PassiveHealthCheckConfig
            //            {
            //                Enabled = true,
            //                Policy = HealthCheckConstants.PassivePolicy.TransportFailureRate,
            //                ReactivationPeriod = TimeSpan.FromSeconds(30)
            //            }
            //        },
            //        HttpRequest = new ForwarderRequestConfig
            //        {
            //            ActivityTimeout = TimeSpan.FromSeconds(30),
            //            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            //        },
            //        Destinations = destinations
            //    }
            //};


            _yarpConfigProvider.Update(routes, clusters);
            

            _logger.LogInformation("已更新 YARP 配置，路由数量: {RouteCount}, 集群数量: {ClusterCount}, 目标服务数量: {DestinationCount}",
                routes.Count, clusters.Count, destinations.Count);
        }
    }
}
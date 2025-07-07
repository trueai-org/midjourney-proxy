using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Consul;
using Microsoft.Extensions.Logging;

namespace Midjourney.Infrastructure.Services
{
    public class ConsulService : IConsulService, IDisposable
    {
        private readonly ConsulClient _consulClient;
        private readonly ConsulOptions _consulOptions;
        private readonly ILogger<ConsulService> _logger;
        private string _serviceId;
        private readonly string _uniqueInstanceId;

        public ConsulService(ILogger<ConsulService> logger)
        {
            _consulOptions = GlobalConfiguration.Setting.ConsulOptions;
            _logger = logger;

            var consulClientConfiguration = new ConsulClientConfiguration
            {
                Address = new Uri(_consulOptions.ConsulUrl)
            };

            _consulClient = new ConsulClient(consulClientConfiguration);

            // 生成唯一实例ID（基于机器名+进程ID+时间戳）
            _uniqueInstanceId = $"{Environment.MachineName}-{Environment.ProcessId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        }

        public async Task RegisterServiceAsync()
        {
            try
            {
                var localIp = PrivateNetworkHelper.GetPrimaryPrivateIP();

                _logger.LogInformation($"尝试注册服务到 Consul: {_consulOptions.ServiceName} at {localIp}:{_consulOptions.ServicePort}");

                // 先清理可能存在的同IP同端口的旧实例
                await CleanupOldInstancesAsync(localIp, _consulOptions.ServicePort);

                // 生成新的服务ID
                _serviceId = $"{_consulOptions.ServiceName}-{localIp.Replace(".", "-")}-{_consulOptions.ServicePort}-{_uniqueInstanceId}";

                var tags = new List<string>() {
                    "traefik.enable=true",

                    //// 最基础的 Traefik 配置
                    //"traefik.enable=true",
                    //"traefik.http.routers.midjourney.rule=PathPrefix(`/mj`)",
                    //"traefik.http.routers.midjourney.entrypoints=web",
                    //$"traefik.http.services.midjourney.loadbalancer.server.port={_consulOptions.ServicePort}",

                    //// 基础标签
                    //"api", "midjourney", "v1",

                     // 使用统一的路由名称，避免冲突 =PathPrefix(`/`) ||  PathPrefix(`/mj`) || PathPrefix(`/health`)
                    "traefik.http.routers.midjourney-api.rule=PathPrefix(`/`)",
                    "traefik.http.routers.midjourney-api.entrypoints=web",
                    "traefik.http.routers.midjourney-api.service=midjourney-service",
                    //"traefik.http.routers.midjourney-api.priority=100", // 较低优先级

                    //// 专门的 /test 路由
                    //"traefik.http.routers.midjourney-test.rule=PathPrefix(`/test`)",
                    //"traefik.http.routers.midjourney-test.entrypoints=web",
                    //"traefik.http.routers.midjourney-test.service=midjourney-service",
                    //"traefik.http.routers.midjourney-test.middlewares=test-rewrite",
                    //"traefik.http.routers.midjourney-test.priority=200", // 设置优先级

                    //// 路径重写中间件
                    //"traefik.http.middlewares.test-rewrite.replacepathregex.regex=^/test(.*)",
                    //"traefik.http.middlewares.test-rewrite.replacepathregex.replacement=/$1",

                    // 统一的服务配置
                    $"traefik.http.services.midjourney-service.loadbalancer.server.port={_consulOptions.ServicePort}",
                    "traefik.http.services.midjourney-service.loadbalancer.server.scheme=http",

                    //// 负载均衡策略 - 正确的语法
                    //"traefik.http.services.midjourney-service.loadbalancer.sticky=false",

                    // 健康检查
                    "traefik.http.services.midjourney-service.loadbalancer.healthcheck.path=/health",
                    "traefik.http.services.midjourney-service.loadbalancer.healthcheck.interval=10s",
                    "traefik.http.services.midjourney-service.loadbalancer.healthcheck.timeout=5s",

                    // 实例元数据
                    $"instance.id={_serviceId}",
                    $"instance.unique={_uniqueInstanceId}",
                    $"instance.started={DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}",
                    "version=1.0.0",
                    "api",
                    "midjourney",
                    "v1"

                    //// Traefik 标签 - 关键配置
                    //"traefik.enable=true",

                    //// HTTP 路由器配置
                    //"traefik.http.routers.midjourney.rule=PathPrefix(`/mj`) || PathPrefix(`/health`)",
                    //"traefik.http.routers.midjourney.entrypoints=web",

                    //// 服务配置
                    //$"traefik.http.services.midjourney.loadbalancer.server.port={_consulOptions.ServicePort}",
                    //"traefik.http.services.midjourney.loadbalancer.server.scheme=http",

                    //// 健康检查配置
                    //"traefik.http.services.midjourney.loadbalancer.healthcheck.path=/health",
                    //"traefik.http.services.midjourney.loadbalancer.healthcheck.interval=10s",

                    //// 可选：中间件
                    //"traefik.http.routers.midjourney.middlewares=cors-headers"
                };
                //tags.AddRange(_consulOptions.ServiceTags ?? Array.Empty<string>());

                var registration = new AgentServiceRegistration
                {
                    ID = _serviceId,
                    Name = _consulOptions.ServiceName, // 服务名保持一致用于负载均衡
                    Address = localIp,
                    Port = _consulOptions.ServicePort,
                    Tags = tags.Distinct().ToArray(),
                    Check = new AgentServiceCheck
                    {
                        HTTP = $"http://{localIp}:{_consulOptions.ServicePort}{_consulOptions.HealthCheckUrl}",
                        Interval = _consulOptions.HealthCheckInterval,
                        Timeout = _consulOptions.HealthCheckTimeout,
                        DeregisterCriticalServiceAfter = _consulOptions.DeregisterCriticalServiceAfter
                    }
                };

                await _consulClient.Agent.ServiceRegister(registration);
                _logger.LogInformation($"服务已注册到 Consul: {_serviceId} at {localIp}:{_consulOptions.ServicePort}");
                _logger.LogInformation($"实例唯一标识: {_uniqueInstanceId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册服务到 Consul 失败");
            }
        }

        private async Task CleanupOldInstancesAsync(string currentIp, int currentPort)
        {
            try
            {
                _logger.LogInformation("检查并清理旧的服务实例...");

                // 获取当前机器上同端口的所有实例
                var services = await _consulClient.Agent.Services();
                var oldInstances = services.Response.Values
                    .Where(s => s.Service == _consulOptions.ServiceName &&
                               s.Address == currentIp &&
                               s.Port == currentPort)
                    .ToList();

                foreach (var oldInstance in oldInstances)
                {
                    try
                    {
                        // 检查进程是否还存在
                        if (IsProcessStillRunning(oldInstance.ID))
                        {
                            _logger.LogInformation($"实例 {oldInstance.ID} 对应的进程仍在运行，跳过清理");
                            continue;
                        }

                        _logger.LogInformation($"清理旧实例: {oldInstance.ID}");
                        await _consulClient.Agent.ServiceDeregister(oldInstance.ID);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"清理旧实例失败: {oldInstance.ID}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理旧实例时发生错误");
            }
        }

        private bool IsProcessStillRunning(string serviceId)
        {
            try
            {
                // 从服务ID中提取进程ID
                var parts = serviceId.Split('-');
                if (parts.Length >= 2 && int.TryParse(parts[^1], out int processId))
                {
                    var process = System.Diagnostics.Process.GetProcessById(processId);
                    return process != null && !process.HasExited;
                }
            }
            catch
            {
                // 进程不存在或访问被拒绝
            }
            return false;
        }

        public async Task DeregisterServiceAsync()
        {
            try
            {
                if (!string.IsNullOrEmpty(_serviceId))
                {
                    await _consulClient.Agent.ServiceDeregister(_serviceId);
                    _logger.LogInformation($"服务已从 Consul 注销: {_serviceId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从 Consul 注销服务失败");
            }
        }

        private string GetLocalIPAddress()
        {


            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                socket.Connect("223.5.5.5", 65530);
                var endPoint = socket.LocalEndPoint as IPEndPoint;
                var primaryIP = endPoint?.Address.ToString();
                if (!string.IsNullOrEmpty(primaryIP) && primaryIP != "127.0.0.1")
                {
                    return primaryIP;
                }
            }
            catch { }

            try
            {
                // 方法2：获取第一个非回环的网络接口 IP
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork &&
                                       !IPAddress.IsLoopback(ip));

                if (ip != null)
                {
                    return ip.ToString();
                }
            }
            catch { }

            // 方法3：通过网络接口获取
            try
            {
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var networkInterface in networkInterfaces)
                {
                    if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                        networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var props = networkInterface.GetIPProperties();
                        var ip = props.UnicastAddresses
                            .FirstOrDefault(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                                                  !IPAddress.IsLoopback(addr.Address))
                            ?.Address.ToString();

                        if (!string.IsNullOrEmpty(ip))
                        {
                            return ip;
                        }
                    }
                }
            }
            catch { }

            return "127.0.0.1";
        }

        public void Dispose()
        {
            _consulClient?.Dispose();
        }
    }
}
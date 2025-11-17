namespace Midjourney.Base.Options
{
    public class ConsulOptions
    {
        /// <summary>
        /// 是否启用 Consul 服务注册
        /// </summary>
        public bool Enable { get; set; } = false;

        public string ConsulUrl { get; set; } = "http://localhost:8500";

        public string ServiceName { get; set; } = "midjourney-proxy";

        public int ServicePort { get; set; } = 8080;

        /// <summary>
        /// 启用版本对比更新检查，启用时以注册中心的服务版本为准，如果版本过低则执行更新检查，然后退出应用程序
        /// </summary>
        public bool EnableVersionCheck { get; set; } = true;

        public string HealthCheckUrl { get; set; } = "/health";

        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(10);

        public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan DeregisterCriticalServiceAfter { get; set; } = TimeSpan.FromMinutes(1);
    }
}
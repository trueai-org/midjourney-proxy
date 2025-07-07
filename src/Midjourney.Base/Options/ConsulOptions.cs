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

        public string HealthCheckUrl { get; set; } = "/health";

        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(10);

        public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan DeregisterCriticalServiceAfter { get; set; } = TimeSpan.FromMinutes(1);
    }
}
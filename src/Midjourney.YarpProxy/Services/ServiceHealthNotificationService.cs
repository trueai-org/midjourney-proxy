using Yarp.ReverseProxy.Configuration;

namespace Midjourney.YarpProxy.Services
{
    public class ServiceHealthNotificationService : IHostedService
    {
        private readonly ILogger<ServiceHealthNotificationService> _logger;
        private readonly IProxyConfigProvider _configProvider;
        private Timer _timer;
        private bool _wasHealthy = true;

        public ServiceHealthNotificationService(
            ILogger<ServiceHealthNotificationService> logger,
            IProxyConfigProvider configProvider)
        {
            _logger = logger;
            _configProvider = configProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _timer = new Timer(CheckServiceHealth, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
            return Task.CompletedTask;
        }

        private void CheckServiceHealth(object state)
        {
            var config = _configProvider.GetConfig();
            var hasHealthyServices = config.Clusters.Any(c => c.Destinations.Any());

            if (hasHealthyServices && !_wasHealthy)
            {
                _logger.LogInformation("🎉 服务已恢复正常，所有后端服务重新可用");
                _wasHealthy = true;
            }
            else if (!hasHealthyServices && _wasHealthy)
            {
                _logger.LogError("⚠️  所有后端服务不可用，系统进入维护模式");
                _wasHealthy = false;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}

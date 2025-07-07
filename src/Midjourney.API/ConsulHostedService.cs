namespace Midjourney.API
{
    public class ConsulHostedService : IHostedService
    {
        private readonly IConsulService _consulService;
        private readonly ILogger<ConsulHostedService> _logger;
        private readonly IHostApplicationLifetime _applicationLifetime;

        public ConsulHostedService(
            IConsulService consulService,
            ILogger<ConsulHostedService> logger,
            IHostApplicationLifetime applicationLifetime)
        {
            _consulService = consulService;
            _logger = logger;
            _applicationLifetime = applicationLifetime;
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                // 在应用程序启动时注册 Consul 服务
                _applicationLifetime.ApplicationStarted.Register(() =>
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            _logger.LogInformation("正在注册 Consul 服务...");
                            _consulService.RegisterServiceAsync();
                            _logger.LogInformation("Consul 服务注册完成");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "注册 Consul 服务注册失败");
                        }
                    });
                });

                // 确保在应用程序停止时注销服务
                _applicationLifetime.ApplicationStopping.Register(async () =>
                {
                    await _consulService.DeregisterServiceAsync();
                });

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动 Consul 服务注册失败");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _consulService.DeregisterServiceAsync();
                _logger.LogInformation("Consul 服务注销完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止 Consul 服务注销失败");
            }
        }
    }
}
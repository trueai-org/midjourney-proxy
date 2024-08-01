using PuppeteerSharp;

namespace Midjourney.Captcha.API
{
    public class BrowserDownloadService : IHostedService
    {
        private readonly ILogger<BrowserDownloadService> _logger;

        public BrowserDownloadService(ILogger<BrowserDownloadService> logger)
        {
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("正在下载浏览器...");
            await CloudflareHelper.DownloadBrowser();
            _logger.LogInformation("浏览器下载完成");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // 可以在这里处理服务停止时的逻辑
            return Task.CompletedTask;
        }
    }

}

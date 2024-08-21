// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
// Copyright (C) 2024 trueai.org

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// Additional Terms:
// This software shall not be used for any illegal activities. 
// Users must comply with all applicable laws and regulations,
// particularly those related to image and video processing. 
// The use of this software for any form of illegal face swapping,
// invasion of privacy, or any other unlawful purposes is strictly prohibited. 
// Violation of these terms may result in termination of the license and may subject the violator to legal action.

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

            _logger.LogInformation("服务运行中...");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // 可以在这里处理服务停止时的逻辑
            return Task.CompletedTask;
        }
    }

}

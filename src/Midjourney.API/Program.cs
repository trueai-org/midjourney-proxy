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

using Midjourney.License;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;

namespace Midjourney.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                // 创建并运行主机
                var host = CreateHostBuilder(args).Build();

                // 确保在应用程序结束时关闭并刷新日志
                AppDomain.CurrentDomain.ProcessExit += (s, e) => Log.CloseAndFlush();

                // 记录当前目录
                Log.Information($"Current directory: {Directory.GetCurrentDirectory()}");

                // 机器标识
                LicenseKeyHelper.Startup();

                host.Run();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "应用程序启动失败");
            }
            finally
            {
                Log.Information("应用程序即将关闭");

                // 确保日志被刷新和关闭
                Log.CloseAndFlush();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
          Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                // 配置读取完成后，可以在主机配置完成前访问配置
                var configuration = config.Build();

                // 可以在这里调整日志器，使用配置信息
                ConfigureInitialLogger(configuration, hostingContext.HostingEnvironment.IsDevelopment());
            })
            .ConfigureLogging((hostContext, loggingBuilder) =>
            {
                // 禁用默认日志提供程序，完全依赖 Serilog
                loggingBuilder.ClearProviders();
            })
            .UseSerilog()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            });

        /// <summary>
        /// 读取配置并更新初始日志器
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="isDevelopment"></param>
        private static void ConfigureInitialLogger(IConfiguration configuration, bool isDevelopment)
        {
            // 基本日志配置
            //var loggerConfiguration = new LoggerConfiguration()
            //      .ReadFrom.Configuration(configuration)
            //      .Enrich.FromLogContext();

            // 写死配置，而不是读取配置文件
            // 单文件最大 10MB
            var fileSizeLimitBytes = 10 * 1024 * 1024;
            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Default", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.File("logs/log.txt",
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: fileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 31);


            // 开发环境特定配置
            if (isDevelopment)
            {
                loggerConfiguration.MinimumLevel.Debug();

                //// 如果配置中没有设置控制台日志，则添加
                //// 否则，不要在代码中添加，避免重复
                //bool hasConsoleInConfig = configuration
                //    .GetSection("Serilog:WriteTo")
                //    .GetChildren()
                //    .Any(section => section["Name"]?.Equals("Console", StringComparison.OrdinalIgnoreCase) == true);

                //if (!hasConsoleInConfig)
                //{
                //    loggerConfiguration.WriteTo.Console();
                //}

                loggerConfiguration.WriteTo.Console();

                // 启用 Serilog 自我诊断
                SelfLog.Enable(Console.Error);
            }

            // 所有环境都记录错误到单独文件
            loggerConfiguration.WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(evt => evt.Level >= LogEventLevel.Error)
                .WriteTo.File("logs/error.txt",
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: fileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 31));

            Log.Logger = loggerConfiguration.CreateLogger();
        }
    }
}
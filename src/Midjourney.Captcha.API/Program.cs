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
using RestSharp;
using Serilog;
using Serilog.Debugging;
using System.Net;
using System.Net.Security;

namespace Midjourney.Captcha.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            ServicePointManager.ServerCertificateValidationCallback = new RemoteCertificateValidationCallback(delegate { return true; });
            //ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            //ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault;

            var builder = CreateHostBuilder(args).Build();
            var env = builder.Services.GetService<IWebHostEnvironment>();

            // 配置 Serilog
            var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Services.GetService<IConfiguration>());

            if (env.IsDevelopment())
            {
                logger.MinimumLevel.Debug()
                      .Enrich.FromLogContext();

                // 使用 Serilog.Debugging.SelfLog.Enable(Console.Error) 来启用 Serilog 的自我诊断，这将帮助诊断配置问题。
                SelfLog.Enable(Console.Error);
            }

            Log.Logger = logger.CreateLogger();

            // 确保在应用程序结束时关闭并刷新日志
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Log.CloseAndFlush();

            try
            {
                Log.Information($"Current: {Directory.GetCurrentDirectory()}");

                //// 使用 Serilog
                //builder.Host.UseSerilog();

                var app = builder;

                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "应用启动失败");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                })
                .UseSerilog();
    }
}
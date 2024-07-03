using Serilog;
using Serilog.Debugging;

namespace Midjourney.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
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
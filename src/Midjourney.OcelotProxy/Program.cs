using Midjourney.OcelotProxy;
using Midjourney.OcelotProxy.Middleware;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Provider.Consul;
using Ocelot.Provider.Polly;
using Serilog;

// 配置 Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .Build())
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // 添加 Ocelot 配置文件
    builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);

    // 添加 Ocelot 服务
    builder.Services.AddOcelot(builder.Configuration)
        // 添加 Consul 服务发现
        // 使用自定义的 ConsulServiceBuilder
        .AddConsul<MyConsulServiceBuilder>()
        .AddPolly();     // 添加 Polly 熔断降级

    var app = builder.Build();

    // 将日志中间件放在最外层，但不修改响应
    app.UseMiddleware<RequestLoggingMiddleware>();

    // 使用 Ocelot 中间件
    await app.UseOcelot();

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "网关启动失败");
}
finally
{
    Log.CloseAndFlush();
}
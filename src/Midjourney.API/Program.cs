// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
// Copyright (C) 2024 trueai.org
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

global using Midjourney.Base;
global using Midjourney.Base.Data;
global using Midjourney.Base.Dto;
global using Midjourney.Base.Models;
global using Midjourney.Base.Services;
global using Midjourney.Base.StandardTable;
global using Midjourney.Base.Util;
global using Midjourney.Infrastructure;

global using ILogger = Serilog.ILogger;
global using TaskStatus = Midjourney.Base.TaskStatus;

using System.Reflection;
using System.Text.Json.Serialization;
using CSRedis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi.Models;
using Midjourney.Infrastructure.Services;
using Midjourney.License;
using Midjourney.License.YouChuan;
using MongoDB.Driver;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;

namespace Midjourney.API
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                // 创建并运行主机
                var builder = WebApplication.CreateBuilder(args);

                // 清除默认日志提供器，使用 Serilog 全面替代
                builder.Logging.ClearProviders();

                // 初始化 Serilog
                ConfigureInitialLogger(builder.Configuration, builder.Environment.IsDevelopment());

                // 让 Host 使用 Serilog
                builder.Host.UseSerilog();

                // --- 服务注册与初始化 ---

                // 初始化全局配置项（可能需要访问磁盘或 DB），必须在添加依赖前执行以确保 Setting 可用
                await SettingHelper.InitializeAsync();

                var setting = SettingHelper.Instance.Current;

                // 是否需要重新保存配置
                var isSaveSetting = false;
                if (setting.DatabaseType == DatabaseType.NONE)
                {
                    setting.DatabaseType = DatabaseType.LiteDB;
                    isSaveSetting = true;
                }

                // 验证数据库是否可连接
                if (!DbHelper.VerifyConfigure())
                {
                    // 切换为本地数据库
                    setting.DatabaseType = DatabaseType.LiteDB;
                    isSaveSetting = true;

                    Log.Error("数据库连接失败，自动切换为 LiteDB 数据库");
                }

                Log.Information("数据库类型：{0}", setting.DatabaseType);

                // 初始化 Redis 验证是否可连接
                if (setting.IsValidRedis)
                {
                    try
                    {
                        var csredis = new CSRedisClient(setting.RedisConnectionString);
                        if (!csredis.Ping())
                        {
                            setting.EnableRedis = false;
                            isSaveSetting = true;
                            Log.Error("Redis 连接失败，已自动禁用 Redis 功能");
                        }
                    }
                    catch (Exception ex)
                    {
                        setting.EnableRedis = false;
                        isSaveSetting = true;
                        Log.Error(ex, "Redis 连接异常，已自动禁用 Redis 功能");
                    }
                }

                // 需要重新保存配置，注意：如果版本过旧，重新保存配置可能会覆盖新的业务，需谨慎处理
                if (isSaveSetting)
                {
                    await SettingHelper.Instance.SaveAsync(setting);
                }

                // 应用配置项
                SettingHelper.Instance.ApplySettings();

                // 机器标识
                LicenseKeyHelper.Startup();

                // 注册服务（原 Startup.ConfigureServices 中的服务）
                var services = builder.Services;
                var configuration = builder.Configuration;

                services.AddMemoryCache();
                services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
                services.AddTransient<WorkContext>();

                // API 异常过滤器 / 方法过滤器
                services.AddControllers(options =>
                {
                    options.Filters.Add<CustomLogicExceptionFilterAttribute>();
                    options.Filters.Add<CustomActionFilterAttribute>();
                }).AddJsonOptions(options =>
                {
                    // 配置枚举序列化为字符串
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                });

                // 添加授权服务
                services.AddAuthorization();

                // 自定义配置 API 行为选项 (400 模型验证处理)
                services.Configure<ApiBehaviorOptions>(options =>
                {
                    options.InvalidModelStateResponseFactory = (context) =>
                    {
                        var error = context.ModelState.Values.FirstOrDefault()?.Errors?.FirstOrDefault()?.ErrorMessage ?? "参数异常";
                        Log.Warning("参数异常 {@0} - {@1}", context.HttpContext?.Request?.GetUrl() ?? "", error);
                        return new JsonResult(Result.Fail(error));
                    };
                });

                // 注册 IHttpClientFactory 复用 HttpClient 实例
                services.AddHttpClient();
                services.AddYouChuanHttpClient();

                // 注册升级服务
                services.TryAddSingleton<IUpgradeService, UpgradeService>();

                // 注册 Midjourney 服务（扩展方法）
                services.AddMidjourneyServices(setting);

                // 注册 Consul
                services.AddSingleton<IConsulService, ConsulService>();

                // 注册 Discord 账号初始化器 并作为 HostedService 启动
                services.AddSingleton<DiscordAccountInitializer>();
                services.AddHostedService(provider => provider.GetRequiredService<DiscordAccountInitializer>());

                // 添加健康检查
                services.AddHealthChecks();

                // Swagger / OpenAPI
                services.AddEndpointsApiExplorer();
                services.AddSwaggerGen(c =>
                {
                    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Midjourney API", Version = "v1" });

                    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
                    {
                        Description = "在下框中输入请求头中需要添加的授权 Authorization: {Token}",
                        Name = "Authorization",
                        In = ParameterLocation.Header,
                        Type = SecuritySchemeType.ApiKey,
                        Scheme = "ApiKeyScheme"
                    });

                    c.AddSecurityRequirement(new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference
                                {
                                    Type = ReferenceType.SecurityScheme,
                                    Id = "ApiKey"
                                }
                            },
                            new string[] { }
                        }
                    });

                    var xmls = new string[] { "Midjourney.Infrastructure.xml" };
                    foreach (var xmlModel in xmls)
                    {
                        var baseDirectory = AppContext.BaseDirectory;
                        if (!File.Exists(Path.Combine(baseDirectory, xmlModel)))
                        {
                            baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                        }

                        var xmlSubPath = Path.Combine(baseDirectory, xmlModel);
                        if (File.Exists(xmlSubPath))
                        {
                            c.IncludeXmlComments(xmlSubPath, true);
                        }
                    }

                    // 当前程序集名称
                    var assemblyMame = Assembly.GetExecutingAssembly().GetName().Name;
                    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyMame}.xml");
                    if (File.Exists(xmlPath))
                    {
                        c.IncludeXmlComments(xmlPath, true);
                    }
                });

                // --- 构建 app 并配置中间件 ---
                var app = builder.Build();

                // 确保应用退出时刷新并关闭日志
                AppDomain.CurrentDomain.ProcessExit += (s, e) => Log.CloseAndFlush();

                // 记录当前目录
                Log.Information($"Current directory: {Directory.GetCurrentDirectory()}");

                // 在这里把 ServiceProvider 传给静态门面（必须在 Build 之后）
                MediatorProvider.SetServiceProvider(app.Services);

                if (app.Environment.IsDevelopment())
                {
                    app.UseDeveloperExceptionPage();
                }

                if (app.Environment.IsDevelopment() || GlobalConfiguration.IsDemoMode == true || GlobalConfiguration.Setting?.EnableSwagger == true)
                {
                    app.UseSwagger();
                    app.UseSwaggerUI(c =>
                    {
                        c.EnablePersistAuthorization();
                        c.DisplayRequestDuration();
                    });
                }

                SystemInfo.GetCurrentSystemInfo();

                GlobalConfiguration.ContentRootPath = app.Environment.ContentRootPath;

                // 从配置中读取任务最大并行数限制  -e CONCURRENT=10
                // 优先使用环境变量
                var concurrent = configuration.GetSection("CONCURRENT").Get<int?>();
                var concurrentEnv = Environment.GetEnvironmentVariable("CONCURRENT");
                if (!string.IsNullOrWhiteSpace(concurrentEnv) && int.TryParse(concurrentEnv, out var maxConcurrent))
                {
                    concurrent = maxConcurrent;
                }
                if (concurrent.HasValue)
                {
                    // 初始化全局锁
                    if (concurrent.Value > 0)
                    {
                        GlobalConfiguration.GlobalLock = new AsyncParallelLock(concurrent.Value);
                    }
                    else if (concurrent.Value <= -1)
                    {
                        // 不限制
                        concurrent = -1;
                    }
                    else
                    {
                        // 不处理任务
                        concurrent = 0;
                    }
                    GlobalConfiguration.GlobalMaxConcurrent = concurrent.Value;
                    Log.Information("环境变量设置当前节点全局最大任务并行处理上限：{0}", concurrent.Value);
                }

                app.UseDefaultFiles(); // 启用默认文件（index.html）
                app.UseStaticFiles(); // 配置提供静态文件

                app.UseCors(builderCors =>
                {
                    builderCors.AllowAnyMethod().AllowAnyHeader().SetIsOriginAllowed(origin => true).AllowCredentials();
                });

                app.UseRouting();

                // 使用自定义中间件
                app.UseMiddleware<SimpleAuthMiddleware>();

                // 限流
                app.UseMiddleware<RateLimitingMiddleware>();

                app.UseAuthorization();

                // 映射控制器与健康检查
                app.MapControllers();
                app.MapHealthChecks("/health");

                // 启动应用
                await app.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "应用程序启动失败");
            }
            finally
            {
                // 确保日志被刷新和关闭
                Log.Information("应用程序即将关闭");
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// 读取配置并更新初始日志器
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="isDevelopment"></param>
        private static void ConfigureInitialLogger(IConfiguration configuration, bool isDevelopment)
        {
            // 设置初始日志级别
            SettingHelper.LogLevelSwitch.MinimumLevel = isDevelopment ? LogEventLevel.Debug : LogEventLevel.Information;

            // 基本日志配置
            //var loggerConfiguration = new LoggerConfiguration()
            //      .ReadFrom.Configuration(configuration)
            //      .Enrich.FromLogContext();

            // 写死配置，而不是读取配置文件
            // 单文件最大 10MB
            var fileSizeLimitBytes = 10 * 1024 * 1024;
            var loggerConfiguration = new LoggerConfiguration()
                //.MinimumLevel.Information()
                .MinimumLevel.ControlledBy(SettingHelper.LogLevelSwitch) // 使用 LoggingLevelSwitch 控制日志级别
                .MinimumLevel.Override("Default", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File("logs/log.txt",
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: fileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 31);

            // 开发环境特定配置
            if (isDevelopment)
            {
                //loggerConfiguration.MinimumLevel.Debug();

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

                //loggerConfiguration.WriteTo.Console();

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
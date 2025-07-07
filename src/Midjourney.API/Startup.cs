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
global using Midjourney.Base.Options;
global using Midjourney.Base.Services;
global using Midjourney.Base.StandardTable;
global using Midjourney.Base.Storage;
global using Midjourney.Base.Util;
global using Midjourney.Infrastructure;

global using ILogger = Serilog.ILogger;
global using TaskStatus = Midjourney.Base.TaskStatus;

using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Midjourney.Infrastructure.Services;
using Midjourney.License;
using MongoDB.Driver;
using Serilog;

namespace Midjourney.API
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            // 初始化全局配置项
            var setting = LiteDBHelper.SettingStore.Get(Constants.DEFAULT_SETTING_ID);
            if (setting == null)
            {
                setting = new Setting
                {
                    Id = Constants.DEFAULT_SETTING_ID,

                    EnableRegister = true,
                    EnableGuest = true,

                    RegisterUserDefaultDayLimit = -1,
                    RegisterUserDefaultCoreSize = -1,
                    RegisterUserDefaultQueueSize = -1,
                    RegisterUserDefaultTotalLimit = -1,

                    GuestDefaultDayLimit = -1,
                    GuestDefaultCoreSize = -1,
                    GuestDefaultQueueSize = -1,
                };
                LiteDBHelper.SettingStore.Save(setting);
            }
            GlobalConfiguration.Setting = setting;

            // 原始 Mongo 配置，旧版数据库配置
            if (setting.DatabaseType == DatabaseType.NONE)
            {
                if (MongoHelper.OldVerify())
                {
                    // 将原始 Mongo 配置转换为新配置
                    setting.DatabaseConnectionString = setting.MongoDefaultConnectionString;
                    setting.DatabaseName = setting.MongoDefaultDatabase;
                    setting.DatabaseType = DatabaseType.MongoDB;
                }
            }

            // 如果未配置则为 None
            if (setting.DatabaseType == DatabaseType.NONE)
            {
                setting.DatabaseType = DatabaseType.LiteDB;
            }

            // 验证数据库是否可连接
            GlobalConfiguration.Setting = setting;
            if (!DbHelper.Verify())
            {
                // 切换为本地数据库
                setting.DatabaseType = DatabaseType.LiteDB;
                Log.Error("数据库连接失败，自动切换为 LiteDB 数据库");
            }

            Log.Information("数据库类型：{0}", setting.DatabaseType);

            if (string.IsNullOrWhiteSpace(setting.LicenseKey))
            {
                // 默认授权
                setting.LicenseKey = "trueai.org";
            }

            LicenseKeyHelper.LicenseKey = setting.LicenseKey;
            LiteDBHelper.SettingStore.Save(setting);
            GlobalConfiguration.Setting = setting;

            services.AddMemoryCache();
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddTransient<WorkContext>();

            // API 异常过滤器
            // API 方法/模型过滤器
            services.AddControllers(options =>
            {
                options.Filters.Add<CustomLogicExceptionFilterAttribute>();
                options.Filters.Add<CustomActionFilterAttribute>();
            }).AddJsonOptions(options =>
            {
                // 配置枚举序列化为字符串
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());

                //// 全局配置
                //options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                //options.JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
            });

            // 添加授权服务
            services.AddAuthorization();

            // 自定义配置 API 行为选项
            // 配置 api 视图模型验证 400 错误处理，需要在 AddControllers 之后配置
            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = (context) =>
                {
                    var error = context.ModelState.Values.FirstOrDefault()?.Errors?.FirstOrDefault()?.ErrorMessage ?? "参数异常";
                    Log.Warning("参数异常 {@0} - {@1}", context.HttpContext?.Request?.GetUrl() ?? "", error);
                    return new JsonResult(Result.Fail(error));
                };
            });

            // 注册 HttpClient
            services.AddHttpClient();

            // 升级服务
            services.TryAddSingleton<IUpgradeService, UpgradeService>();

            // 注册 Midjourney 服务
            services.AddMidjourneyServices(setting);

            // 配置 Consul
            //var consulOpt = Configuration.GetSection(nameof(ConsulOptions));
            //var consulOptions = consulOpt.Get<ConsulOptions>();
            if (GlobalConfiguration.Setting.ConsulOptions?.Enable == true)
            {
                //services.Configure<ConsulOptions>(consulOpt);
                //var opt = Options.Create(GlobalConfiguration.Setting.ConsulOptions);

                // 注册服务
                services.AddSingleton<IConsulService, ConsulService>();
                services.AddHostedService<ConsulHostedService>();
            }

            // 注册 Discord 账号初始化器
            services.AddSingleton<DiscordAccountInitializer>();
            services.AddHostedService(provider => provider.GetRequiredService<DiscordAccountInitializer>());

            // 添加健康检查
            services.AddHealthChecks();

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Midjourney API", Version = "v1" });

                c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
                {
                    Description = "在下框中输入请求头中需要添加的授权 Authorization: {Token}",
                    Name = "Authorization", // 或者 "Mj-Api-Secret" 视具体需求而定
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
        }

        public void Configure(IApplicationBuilder app, IHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            if (env.IsDevelopment() || GlobalConfiguration.IsDemoMode == true || GlobalConfiguration.Setting?.EnableSwagger == true)
            {
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.EnablePersistAuthorization();
                    c.DisplayRequestDuration();
                });
            }

            SystemInfo.GetCurrentSystemInfo();
            GlobalConfiguration.ContentRootPath = env.ContentRootPath;

            app.UseDefaultFiles(); // 启用默认文件（index.html）
            app.UseStaticFiles(); // 配置提供静态文件

            app.UseCors(builder =>
            {
                builder.AllowAnyMethod().AllowAnyHeader().SetIsOriginAllowed(origin => true).AllowCredentials();
            });

            app.UseRouting();

            // 使用自定义中间件
            app.UseMiddleware<SimpleAuthMiddleware>();

            // 限流
            app.UseMiddleware<RateLimitingMiddleware>();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();

                endpoints.MapHealthChecks("/health");
            });

            //// 优雅关闭处理
            //// 在应用程序停止时注销 Consul 服务
            //// 判断是否启用 Consul
            //// if (Configuration.GetSection(nameof(ConsulOptions)).Get<ConsulOptions>()?.Enable == true)
            //if (GlobalConfiguration.Setting.ConsulOptions?.Enable == true)
            //{
            //    var lifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
            //    lifetime.ApplicationStopping.Register(async () =>
            //    {
            //        var consulService = app.ApplicationServices.GetRequiredService<IConsulService>();
            //        await consulService.DeregisterServiceAsync();
            //    });
            //}
        }
    }
}
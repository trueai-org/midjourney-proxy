global using Midjourney.Infrastructure;
global using Midjourney.Infrastructure.Models;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi.Models;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Options;
using Serilog;
using System.Reflection;
using System.Text.Json.Serialization;

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
            // 启动时，优先初始化全局配置项
            var configSec = Configuration.GetSection("mj");
            var configOpt = configSec.Get<ProxyProperties>();
            services.Configure<ProxyProperties>(configSec);

            var ipSec = Configuration.GetSection("IpRateLimiting");
            var ipRateOpt = ipSec.Get<IpRateLimitingOptions>();
            services.Configure<IpRateLimitingOptions>(ipSec);

            var ipBlackSec = Configuration.GetSection("IpBlackRateLimiting");
            var ipBlackOpt = ipBlackSec.Get<IpBlackRateLimitingOptions>();
            services.Configure<IpBlackRateLimitingOptions>(ipBlackSec);

            var setting = DbHelper.SettingStore.Get(Constants.DEFAULT_SETTING_ID);
            if (setting == null)
            {
                setting = new Setting
                {
                    Id = Constants.DEFAULT_SETTING_ID,
                    IpRateLimiting = ipRateOpt,
                    IpBlackRateLimiting = ipBlackOpt,
                    EnableRegister = true,
                    EnableGuest = true,
                    RegisterUserDefaultDayLimit = -1,
                    GuestDefaultDayLimit = -1,

                    AccountChooseRule = configOpt.AccountChooseRule,
                    BaiduTranslate = configOpt.BaiduTranslate,
                    CaptchaNotifyHook = configOpt.CaptchaNotifyHook,
                    CaptchaServer = configOpt.CaptchaServer,
                    NgDiscord = configOpt.NgDiscord,
                    NotifyHook = configOpt.NotifyHook,
                    NotifyPoolSize = configOpt.NotifyPoolSize,
                    Openai = configOpt.Openai,
                    Proxy = configOpt.Proxy,
                    TranslateWay = configOpt.TranslateWay,
                    Smtp = configOpt.Smtp
                };
                DbHelper.SettingStore.Save(setting);

                Thread.Sleep(5 * 1000);
            }
            GlobalConfiguration.Setting = setting;

            // 缓存
            services.AddMemoryCache();

            // 是否为演示模式
            var isDemoMode = Configuration.GetSection("Demo").Get<bool?>();
            if (isDemoMode != true)
            {
                if (bool.TryParse(Environment.GetEnvironmentVariable("DEMO"), out var demo) && demo)
                {
                    isDemoMode = demo;
                }
            }
            GlobalConfiguration.IsDemoMode = isDemoMode;

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
                    Log.Logger.Warning("参数异常 {@0} - {@1}", context.HttpContext?.Request?.GetUrl() ?? "", error);
                    return new JsonResult(Result.Fail(error));
                };
            });

            // 注册 HttpClient
            services.AddHttpClient();

            // 注册 Midjourney 服务
            services.AddMidjourneyServices(setting);

            // 注册 Discord 账号初始化器
            services.AddSingleton<DiscordAccountInitializer>();
            services.AddHostedService(provider => provider.GetRequiredService<DiscordAccountInitializer>());

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
            if (env.IsDevelopment() || GlobalConfiguration.IsDemoMode == true)
            {
                app.UseDeveloperExceptionPage();

                app.UseSwagger();
                app.UseSwaggerUI();
            }

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
            });
        }
    }
}
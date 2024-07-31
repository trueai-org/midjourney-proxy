global using Midjourney.Infrastructure;
global using Midjourney.Infrastructure.Models;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi.Models;
using Midjourney.Infrastructure.Options;
using Serilog;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Midjourney.Captcha.API
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
            services.Configure<CaptchaOption>(Configuration.GetSection("Captcha"));

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

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Midjourney Captcha API", Version = "v1" });

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

            app.UseCors(builder =>
            {
                builder.AllowAnyMethod().AllowAnyHeader().SetIsOriginAllowed(origin => true).AllowCredentials();
            });

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
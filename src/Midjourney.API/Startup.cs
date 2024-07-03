global using Midjourney.Infrastructure;

using Microsoft.AspNetCore.Mvc;
using Serilog;
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
            services.Configure<ProxyProperties>(Configuration.GetSection("mj"));

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
            }); ;

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
            services.AddMidjourneyServices();

            // 注册 Discord 账号初始化器
            services.AddSingleton<DiscordAccountInitializer>();
            services.AddHostedService(provider => provider.GetRequiredService<DiscordAccountInitializer>());

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen();
        }

        public void Configure(IApplicationBuilder app, IHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();

                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseStaticFiles();

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
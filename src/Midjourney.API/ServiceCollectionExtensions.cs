using Midjourney.Infrastructure.Handle;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;

namespace Midjourney.API
{
    public static class ServiceCollectionExtensions
    {
        public static void AddMidjourneyServices(this IServiceCollection services)
        {
            // 注册所有的处理程序
            services.AddTransient<MessageHandler, ErrorMessageHandler>();
            services.AddTransient<MessageHandler, ImagineSuccessHandler>();
            services.AddTransient<MessageHandler, RerollSuccessHandler>();
            services.AddTransient<MessageHandler, StartAndProgressHandler>();
            services.AddTransient<MessageHandler, UpscaleSuccessHandler>();
            services.AddTransient<MessageHandler, VariationSuccessHandler>();
            services.AddTransient<MessageHandler, DescribeSuccessHandler>();
            services.AddTransient<MessageHandler, ActionSuccessHandler>();

            // 通知服务
            services.AddSingleton<INotifyService, NotifyServiceImpl>();

            // 翻译服务
            services.AddSingleton<ITranslateService, BaiduTranslateService>();

            // 存储服务
            // 内存
            //services.AddSingleton<ITaskStoreService, InMemoryTaskStoreServiceImpl>();
            // LiteDB
            services.AddSingleton<ITaskStoreService>(new LiteDBRepository("data/mj.db"));

            // 账号负载均衡服务
            services.AddSingleton<IRule, RoundRobinRule>();

            // Discord 负载均衡器
            services.AddSingleton<DiscordLoadBalancer>();

            // Discord 账号助手
            services.AddSingleton<DiscordAccountHelper>();

            // Discord 助手
            services.AddSingleton<DiscordHelper>();

            // 任务服务
            services.AddSingleton<ITaskService, TaskServiceImpl>();
        }
    }
}
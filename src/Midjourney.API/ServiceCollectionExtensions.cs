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

using System.Reflection;
using Midjourney.Infrastructure.Handle;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;

namespace Midjourney.API
{
    public static class ServiceCollectionExtensions
    {
        public static void AddMidjourneyServices(this IServiceCollection services, Setting config)
        {
            // 注册所有的处理程序

            // 机器人消息处理程序
            services.AddTransient<BotMessageHandler, BotErrorMessageHandler>();
            services.AddTransient<BotMessageHandler, BotImagineSuccessHandler>();
            services.AddTransient<BotMessageHandler, BotRerollSuccessHandler>();
            services.AddTransient<BotMessageHandler, BotStartAndProgressHandler>();
            services.AddTransient<BotMessageHandler, BotUpscaleSuccessHandler>();
            services.AddTransient<BotMessageHandler, BotVariationSuccessHandler>();
            services.AddTransient<BotMessageHandler, BotDescribeSuccessHandler>();
            services.AddTransient<BotMessageHandler, BotActionSuccessHandler>();
            services.AddTransient<BotMessageHandler, BotBlendSuccessHandler>();
            services.AddTransient<BotMessageHandler, BotShowSuccessHandler>();

            // 用户消息处理程序
            services.AddTransient<UserMessageHandler, UserErrorMessageHandler>();
            services.AddTransient<UserMessageHandler, UserImagineSuccessHandler>();
            services.AddTransient<UserMessageHandler, UserActionSuccessHandler>();
            services.AddTransient<UserMessageHandler, UserUpscaleSuccessHandler>();
            services.AddTransient<UserMessageHandler, UserBlendSuccessHandler>();
            services.AddTransient<UserMessageHandler, UserDescribeSuccessHandler>();
            services.AddTransient<UserMessageHandler, UserShowSuccessHandler>();
            services.AddTransient<UserMessageHandler, UserVariationSuccessHandler>();
            services.AddTransient<UserMessageHandler, UserStartAndProgressHandler>();
            services.AddTransient<UserMessageHandler, UserRerollSuccessHandler>();
            services.AddTransient<UserMessageHandler, UserShortenSuccessHandler>();

            // 换脸服务
            services.AddSingleton<FaceSwapInstance>();
            services.AddSingleton<VideoFaceSwapInstance>();

            // 通知服务
            services.AddSingleton<INotifyService, NotifyServiceImpl>();

            // 任务服务
            services.AddSingleton<ITaskStoreService, TaskRepository>();

            // 账号负载均衡服务
            switch (config.AccountChooseRule)
            {
                case AccountChooseRule.BestWaitIdle:
                    services.AddSingleton<IDiscordInstanceRule, BestWaitIdleRule>();
                    break;

                case AccountChooseRule.Random:
                    services.AddSingleton<IDiscordInstanceRule, RandomRule>();
                    break;

                case AccountChooseRule.Weight:
                    services.AddSingleton<IDiscordInstanceRule, WeightRule>();
                    break;

                case AccountChooseRule.Polling:
                    services.AddSingleton<IDiscordInstanceRule, RoundRobinRule>();
                    break;

                default:
                    services.AddSingleton<IDiscordInstanceRule, BestWaitIdleRule>();
                    break;
            }

            // Discord 负载均衡器
            services.AddSingleton<DiscordLoadBalancer>();

            // Discord 账号助手
            services.AddSingleton<DiscordAccountHelper>();

            // Discord 助手
            services.AddSingleton<DiscordHelper>();

            // 任务服务
            services.AddSingleton<ITaskService, TaskService>();

            // 注册 MediatR
            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

            services.AddBaseServices(config);
            services.AddInfrastructureServices(config);
        }
    }
}
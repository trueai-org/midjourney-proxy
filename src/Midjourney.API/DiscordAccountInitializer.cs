using Microsoft.Extensions.Options;
using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.LoadBalancer;
using Serilog;

using ILogger = Serilog.ILogger;

namespace Midjourney.API
{
    /// <summary>
    /// Discord账号初始化器，用于初始化Discord账号实例。
    /// </summary>
    public class DiscordAccountInitializer : IHostedService
    {
        private readonly DiscordLoadBalancer _discordLoadBalancer;
        private readonly DiscordAccountHelper _discordAccountHelper;
        private readonly ProxyProperties _properties;
        private readonly ILogger _logger;

        /// <summary>
        /// 初始化 DiscordAccountInitializer 类的新实例。
        /// </summary>
        /// <param name="discordLoadBalancer">Discord负载均衡器实例。</param>
        /// <param name="discordAccountHelper">Discord账号辅助类实例。</param>
        /// <param name="options">ProxyProperties 配置选项。</param>
        public DiscordAccountInitializer(DiscordLoadBalancer discordLoadBalancer, DiscordAccountHelper discordAccountHelper, IOptions<ProxyProperties> options)
        {
            _discordLoadBalancer = discordLoadBalancer;
            _discordAccountHelper = discordAccountHelper;
            _properties = options.Value;

            _logger = Log.Logger;
        }

        /// <summary>
        /// 启动服务并初始化Discord账号实例。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var proxy = _properties.Proxy;
            if (!string.IsNullOrEmpty(proxy.Host))
            {
                Environment.SetEnvironmentVariable("http_proxyHost", proxy.Host);
                Environment.SetEnvironmentVariable("http_proxyPort", proxy.Port.ToString());
                Environment.SetEnvironmentVariable("https_proxyHost", proxy.Host);
                Environment.SetEnvironmentVariable("https_proxyPort", proxy.Port.ToString());
            }

            var configAccounts = _properties.Accounts;
            if (!string.IsNullOrEmpty(_properties.Discord.ChannelId))
            {
                configAccounts.Add(_properties.Discord);
            }

            var instances = _discordLoadBalancer.GetAllInstances();
            foreach (var configAccount in configAccounts)
            {
                var account = new DiscordAccount
                {
                    GuildId = configAccount.GuildId,
                    ChannelId = configAccount.ChannelId,
                    UserToken = configAccount.UserToken,
                    UserAgent = string.IsNullOrEmpty(configAccount.UserAgent) ? Constants.DEFAULT_DISCORD_USER_AGENT : configAccount.UserAgent,
                    Enable = configAccount.Enable,
                    CoreSize = configAccount.CoreSize,
                    QueueSize = configAccount.QueueSize,
                    BotToken = configAccount.BotToken,
                    TimeoutMinutes = configAccount.TimeoutMinutes
                };

                try
                {
                    var instance = await _discordAccountHelper.CreateDiscordInstance(account);
                    if (!account.Enable)
                    {
                        continue;
                    }

                    //// TODO, 暂时不添加验证和监听
                    //instance.StartWss();
                    //var lockObject = await AsyncLockUtils.WaitForLockAsync("wss:" + account.ChannelId, TimeSpan.FromSeconds(10));
                    //if (lockObject.GetProperty<int>("code", 0) != ReturnCode.SUCCESS)
                    //{
                    //    throw new ValidationException(lockObject.GetProperty<string>("description"));
                    //}

                    instances.Add(instance);

                    // TODO 临时方案，后续需要优化
                    // 先添加到负载均衡器，再启动
                    _discordLoadBalancer.AddInstance(instance);
                }
                catch (Exception ex)
                {
                    _logger.Error("Account({@0}) init fail, disabled: {@1}", account.GetDisplay(), ex.Message);
                    account.Enable = false;
                }
            }

            var enableInstanceIds = instances.Where(instance => instance.IsAlive())
                                             .Select(instance => instance.GetInstanceId())
                                             .ToHashSet();

            _logger.Information("当前可用账号数 [{@0}] - {@1}", enableInstanceIds.Count, string.Join(", ", enableInstanceIds));

            await Task.CompletedTask;
        }

        /// <summary>
        /// 停止服务。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            // 此处可以添加服务停止时的逻辑
            return Task.CompletedTask;
        }
    }
}
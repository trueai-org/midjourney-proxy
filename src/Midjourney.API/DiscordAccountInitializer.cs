using IdGen;
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
        /// 初始化 DiscordAccountInitializer 类的新实例
        /// </summary>
        /// <param name="discordLoadBalancer"></param>
        /// <param name="discordAccountHelper"></param>
        /// <param name="options"></param>
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
            await Initialize();
        }

        /// <summary>
        /// 初始化所有账号
        /// </summary>
        /// <returns></returns>
        public async Task Initialize(params DiscordAccountConfig[] appends)
        {
            var proxy = _properties.Proxy;
            if (!string.IsNullOrEmpty(proxy.Host))
            {
                Environment.SetEnvironmentVariable("http_proxyHost", proxy.Host);
                Environment.SetEnvironmentVariable("http_proxyPort", proxy.Port.ToString());
                Environment.SetEnvironmentVariable("https_proxyHost", proxy.Host);
                Environment.SetEnvironmentVariable("https_proxyPort", proxy.Port.ToString());
            }

            var db = DbHelper.AccountStore;
            var accounts = db.GetAll();

            // 将启动配置中的 account 添加到数据库
            var configAccounts = _properties.Accounts.ToList();
            if (!string.IsNullOrEmpty(_properties.Discord.ChannelId))
            {
                configAccounts.Add(_properties.Discord);
            }

            if (appends?.Length > 0)
            {
                configAccounts.AddRange(appends);
            }

            foreach (var configAccount in configAccounts)
            {
                var account = accounts.FirstOrDefault(c => c.ChannelId == configAccount.ChannelId);
                if (account == null)
                {
                    account = new DiscordAccount
                    {
                        Id = configAccount.ChannelId,
                        ChannelId = configAccount.ChannelId,

                        GuildId = configAccount.GuildId,
                        UserToken = configAccount.UserToken,
                        UserAgent = string.IsNullOrEmpty(configAccount.UserAgent) ? Constants.DEFAULT_DISCORD_USER_AGENT : configAccount.UserAgent,
                        Enable = configAccount.Enable,
                        CoreSize = configAccount.CoreSize,
                        QueueSize = configAccount.QueueSize,
                        BotToken = configAccount.BotToken,
                        TimeoutMinutes = configAccount.TimeoutMinutes,
                        PrivateChannelId = configAccount.PrivateChannelId,
                        NijiBotChannelId = configAccount.NijiBotChannelId,
                        MaxQueueSize = configAccount.MaxQueueSize,
                        Mode = configAccount.Mode,
                        Weight = configAccount.Weight,
                        Remark = configAccount.Remark
                    };

                    db.Add(account);
                    accounts.Add(account);
                }
            }

            var instances = _discordLoadBalancer.GetAllInstances();
            foreach (var account in accounts)
            {
                if (!account.Enable)
                {
                    continue;
                }

                try
                {
                    var disInstance = _discordLoadBalancer.GetDiscordInstance(account.ChannelId);
                    if (disInstance == null)
                    {
                        disInstance = await _discordAccountHelper.CreateDiscordInstance(account);
                        instances.Add(disInstance);
                        _discordLoadBalancer.AddInstance(disInstance);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("Account({@0}) init fail, disabled: {@1}", account.GetDisplay(), ex.Message);

                    account.Enable = false;

                    db.Update(account);
                }
            }

            var enableInstanceIds = instances.Where(instance => instance.IsAlive)
                .Select(instance => instance.GetInstanceId)
                .ToHashSet();

            _logger.Information("当前可用账号数 [{@0}] - {@1}", enableInstanceIds.Count, string.Join(", ", enableInstanceIds));
        }

        /// <summary>
        /// 更新账号信息
        /// </summary>
        /// <param name="account"></param>
        public void UpdateAccount(DiscordAccount account)
        {
            DiscordAccount model = null;

            var disInstance = _discordLoadBalancer.GetDiscordInstance(account.Id);
            if (disInstance != null)
            {
                model = disInstance.Account;
            }

            if (model == null)
            {
                model = DbHelper.AccountStore.Get(account.Id);
            }

            if (model == null)
            {
                throw new LogicException("账号不存在");
            }

            model.Enable = account.Enable;
            model.ChannelId = account.ChannelId;
            model.GuildId = account.GuildId;
            model.PrivateChannelId = account.PrivateChannelId;
            model.NijiBotChannelId = account.NijiBotChannelId;
            model.UserAgent = account.UserAgent;
            model.RemixAutoSubmit = account.RemixAutoSubmit;
            model.CoreSize = account.CoreSize;
            model.QueueSize = account.QueueSize;
            model.MaxQueueSize = account.MaxQueueSize;
            model.TimeoutMinutes = account.TimeoutMinutes;
            model.Weight = account.Weight;
            model.Remark = account.Remark;
            model.BotToken = account.BotToken;
            model.UserToken = account.UserToken;
            model.Mode = account.Mode;

            DbHelper.AccountStore.Update(model);
        }

        /// <summary>
        /// 更新并重新连接账号
        /// </summary>
        /// <param name="account"></param>
        public async Task ReconnectAccount(DiscordAccount account)
        {
            try
            {
                UpdateAccount(account);

                var disInstance = _discordLoadBalancer.GetDiscordInstance(account.Id);
                if (disInstance != null)
                {
                    disInstance.Dispose();
                }
            }
            catch
            {

            }

            await Initialize();
        }

        /// <summary>
        /// 停止连接并删除账号
        /// </summary>
        /// <param name="id"></param>
        public void DeleteAccount(string id)
        {
            try
            {
                var disInstance = _discordLoadBalancer.GetDiscordInstance(id);
                if (disInstance != null)
                {
                    disInstance.Dispose();
                }
            }
            catch
            {

            }

            var model = DbHelper.AccountStore.Get(id);
            if (model != null)
            {
                DbHelper.AccountStore.Delete(id);
            }
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
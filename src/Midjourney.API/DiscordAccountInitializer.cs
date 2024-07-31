using Microsoft.Extensions.Options;
using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using Serilog;

using ILogger = Serilog.ILogger;

namespace Midjourney.API
{
    /// <summary>
    /// Discord账号初始化器，用于初始化Discord账号实例。
    /// </summary>
    public class DiscordAccountInitializer : IHostedService
    {
        private readonly ITaskService _taskService;
        private readonly DiscordLoadBalancer _discordLoadBalancer;
        private readonly DiscordAccountHelper _discordAccountHelper;
        private readonly ProxyProperties _properties;
        private readonly ILogger _logger;


        public DiscordAccountInitializer(
            DiscordLoadBalancer discordLoadBalancer,
            DiscordAccountHelper discordAccountHelper,
            IOptions<ProxyProperties> options,
            ITaskService taskService)
        {
            _discordLoadBalancer = discordLoadBalancer;
            _discordAccountHelper = discordAccountHelper;
            _properties = options.Value;
            _taskService = taskService;

            _logger = Log.Logger;
        }

        /// <summary>
        /// 启动服务并初始化Discord账号实例。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _ = Task.Run(async () =>
            {
                await Initialize();
            });

            await Task.CompletedTask;
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
            var accounts = db.GetAll().OrderBy(c => c.Sort).ToList();

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
                    if (configAccount.Interval < 1.2m)
                    {
                        configAccount.Interval = 1.2m;
                    }

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
                        Remark = configAccount.Remark,
                        RemixAutoSubmit = configAccount.RemixAutoSubmit,
                        Sponsor = configAccount.Sponsor,
                        Sort = configAccount.Sort,
                        Interval = configAccount.Interval
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

                IDiscordInstance disInstance = null;
                try
                {
                    disInstance = _discordLoadBalancer.GetDiscordInstance(account.ChannelId);
                    if (disInstance == null)
                    {
                        disInstance = await _discordAccountHelper.CreateDiscordInstance(account);
                        instances.Add(disInstance);
                        _discordLoadBalancer.AddInstance(disInstance);

                        // TODO 这里应该等待初始化完成，并获取用户信息验证，获取用户成功后设置为可用状态
                        // 多账号启动时，等待一段时间再启动下一个账号
                        await Task.Delay(1000 * 5);

                        // 启动后执行 info setting 操作
                        await _taskService.InfoSetting(account.ChannelId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("Account({@0}) init fail, disabled: {@1}", account.GetDisplay(), ex.Message);

                    account.Enable = false;

                    db.Update(account);
                    disInstance?.ClearAccountCache(account.Id);
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
        /// <param name="param"></param>
        public void UpdateAccount(DiscordAccount param)
        {
            DiscordAccount model = null;

            var disInstance = _discordLoadBalancer.GetDiscordInstance(param.ChannelId);
            if (disInstance != null)
            {
                model = disInstance.Account;
            }

            if (model == null)
            {
                model = DbHelper.AccountStore.Get(param.Id);
            }

            if (model == null)
            {
                throw new LogicException("账号不存在");
            }

            // 渠道 ID 和 服务器 ID 禁止修改
            //model.ChannelId = account.ChannelId;
            //model.GuildId = account.GuildId;

            // 更新账号重连时，自动解锁
            model.Lock = false;
            model.CfHashCreated = null;
            model.CfHashUrl = null;
            model.CfUrl = null;

            if (param.Interval < 1.2m)
            {
                param.Interval = 1.2m;
            }

            model.Interval = param.Interval;
            model.Sort = param.Sort;
            model.Enable = param.Enable;
            model.PrivateChannelId = param.PrivateChannelId;
            model.NijiBotChannelId = param.NijiBotChannelId;
            model.UserAgent = param.UserAgent;
            model.RemixAutoSubmit = param.RemixAutoSubmit;
            model.CoreSize = param.CoreSize;
            model.QueueSize = param.QueueSize;
            model.MaxQueueSize = param.MaxQueueSize;
            model.TimeoutMinutes = param.TimeoutMinutes;
            model.Weight = param.Weight;
            model.Remark = param.Remark;
            model.BotToken = param.BotToken;
            model.UserToken = param.UserToken;
            model.Mode = param.Mode;
            model.Sponsor = param.Sponsor;

            DbHelper.AccountStore.Update(model);

            disInstance?.ClearAccountCache(model.Id);
        }

        /// <summary>
        /// 更新并重新连接账号
        /// </summary>
        /// <param name="account"></param>
        public async Task ReconnectAccount(DiscordAccount account)
        {
            try
            {
                // 如果正在执行则释放
                var disInstance = _discordLoadBalancer.GetDiscordInstance(account.ChannelId);
                if (disInstance != null)
                {
                    _discordLoadBalancer.RemoveInstance(disInstance);

                    disInstance.Dispose();
                }
            }
            catch
            {

            }

            UpdateAccount(account);

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
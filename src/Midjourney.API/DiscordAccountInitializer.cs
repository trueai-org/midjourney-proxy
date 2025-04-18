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

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.Util;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;
using RestSharp;
using Serilog;

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
        private readonly DiscordHelper _discordHelper;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _memoryCache;
        private readonly Serilog.ILogger _logger = Log.Logger;

        private Timer _timer;
        private DateTime? _userDayReset = null;

        public DiscordAccountInitializer(
            DiscordLoadBalancer discordLoadBalancer,
            DiscordAccountHelper discordAccountHelper,
            IConfiguration configuration,
            IOptions<ProxyProperties> options,
            ITaskService taskService,
            IMemoryCache memoryCache,
            DiscordHelper discordHelper)
        {
            // 配置全局缓存
            GlobalConfiguration.MemoryCache = memoryCache;

            _discordLoadBalancer = discordLoadBalancer;
            _discordAccountHelper = discordAccountHelper;
            _properties = options.Value;
            _taskService = taskService;
            _configuration = configuration;
            _memoryCache = memoryCache;
            _discordHelper = discordHelper;
        }

        /// <summary>
        /// 启动服务并初始化Discord账号实例。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var setting = GlobalConfiguration.Setting;

            // 初始化环境变量
            var proxy = setting.Proxy;
            if (!string.IsNullOrEmpty(proxy.Host))
            {
                Environment.SetEnvironmentVariable("http_proxyHost", proxy.Host);
                Environment.SetEnvironmentVariable("http_proxyPort", proxy.Port.ToString());
                Environment.SetEnvironmentVariable("https_proxyHost", proxy.Host);
                Environment.SetEnvironmentVariable("https_proxyPort", proxy.Port.ToString());
            }

            // 初始化数据库索引
            DbHelper.Instance.IndexInit();

            // 是否开启 LiteDB 自动迁移
            if (setting.DatabaseType != DatabaseType.NONE && setting.DatabaseType != DatabaseType.LiteDB && setting.IsAutoMigrate)
            {
                // 迁移 account user domain banded
                try
                {
                    // 如果 liteAccountIds 的数据在 mongoAccountIds 不存在，则迁移到 mongodb
                    // account 迁移
                    var liteAccountIds = LiteDBHelper.AccountStore.GetAllIds();
                    var accountStore = DbHelper.Instance.AccountStore;
                    var mongoAccountIds = accountStore.GetAllIds();
                    var accountIds = liteAccountIds.Except(mongoAccountIds).ToList();
                    if (accountIds.Count > 0)
                    {
                        var liteAccounts = LiteDBHelper.AccountStore.GetAll();
                        foreach (var id in accountIds)
                        {
                            var model = liteAccounts.FirstOrDefault(c => c.Id == id);
                            if (model != null)
                            {
                                accountStore.Add(model);
                            }
                        }
                    }

                    // user 迁移
                    var liteUserIds = LiteDBHelper.UserStore.GetAllIds();
                    var userStore = DbHelper.Instance.UserStore;
                    var mongoUserIds = userStore.GetAllIds();
                    var userIds = liteUserIds.Except(mongoUserIds).ToList();
                    if (userIds.Count > 0)
                    {
                        var liteUsers = LiteDBHelper.UserStore.GetAll();
                        foreach (var id in userIds)
                        {
                            var model = liteUsers.FirstOrDefault(c => c.Id == id);
                            if (model != null)
                            {
                                userStore.Add(model);
                            }
                        }
                    }

                    // domain 迁移
                    var liteDomainIds = LiteDBHelper.DomainStore.GetAllIds();
                    var domainStore = DbHelper.Instance.DomainStore;
                    var mongoDomainIds = domainStore.GetAllIds();
                    var domainIds = liteDomainIds.Except(mongoDomainIds).ToList();
                    if (domainIds.Count > 0)
                    {
                        var liteDomains = LiteDBHelper.DomainStore.GetAll();
                        foreach (var id in domainIds)
                        {
                            var model = liteDomains.FirstOrDefault(c => c.Id == id);
                            if (model != null)
                            {
                                domainStore.Add(model);
                            }
                        }
                    }

                    // banded 迁移
                    var liteBannedIds = LiteDBHelper.BannedWordStore.GetAllIds();
                    var bannedStore = DbHelper.Instance.BannedWordStore;
                    var mongoBannedIds = bannedStore.GetAllIds();
                    var bannedIds = liteBannedIds.Except(mongoBannedIds).ToList();
                    if (bannedIds.Count > 0)
                    {
                        var liteBanneds = LiteDBHelper.BannedWordStore.GetAll();
                        foreach (var id in bannedIds)
                        {
                            var model = liteBanneds.FirstOrDefault(c => c.Id == id);
                            if (model != null)
                            {
                                bannedStore.Add(model);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "LiteDB 数据迁移基本信息异常");
                }

                // 迁移 task
                try
                {
                    _ = Task.Run(() =>
                    {
                        TaskInfoAutoMigrate();
                    });
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "LiteDB 数据迁移任务信息异常");
                }
            }

            // 迁移完后，再验证初始化数据
            try
            {
                // 初始化管理员用户
                // 判断超管是否存在
                var admin = DbHelper.Instance.UserStore.Get(Constants.ADMIN_USER_ID);
                if (admin == null)
                {
                    admin = new User
                    {
                        Id = Constants.ADMIN_USER_ID,
                        Name = Constants.ADMIN_USER_ID,
                        Token = _configuration["AdminToken"],
                        Role = EUserRole.ADMIN,
                        Status = EUserStatus.NORMAL,
                        IsWhite = true
                    };

                    if (string.IsNullOrWhiteSpace(admin.Token))
                    {
                        admin.Token = "admin";
                    }

                    DbHelper.Instance.UserStore.Add(admin);
                }

                // 初始化普通用户
                var user = DbHelper.Instance.UserStore.Get(Constants.DEFAULT_USER_ID);
                var userToken = _configuration["UserToken"];
                if (user == null && !string.IsNullOrWhiteSpace(userToken))
                {
                    user = new User
                    {
                        Id = Constants.DEFAULT_USER_ID,
                        Name = Constants.DEFAULT_USER_ID,
                        Token = userToken,
                        Role = EUserRole.USER,
                        Status = EUserStatus.NORMAL,
                        IsWhite = true
                    };
                    DbHelper.Instance.UserStore.Add(user);
                }

                // 初始化领域标签
                var defaultDomain = DbHelper.Instance.DomainStore.Get(Constants.DEFAULT_DOMAIN_ID);
                if (defaultDomain == null)
                {
                    defaultDomain = new DomainTag
                    {
                        Id = Constants.DEFAULT_DOMAIN_ID,
                        Name = "默认标签",
                        Description = "",
                        Sort = 0,
                        Enable = true,
                        Keywords = WordsUtils.GetWords()
                    };
                    DbHelper.Instance.DomainStore.Add(defaultDomain);
                }

                // 完整标签
                var fullDomain = DbHelper.Instance.DomainStore.Get(Constants.DEFAULT_DOMAIN_FULL_ID);
                if (fullDomain == null)
                {
                    fullDomain = new DomainTag
                    {
                        Id = Constants.DEFAULT_DOMAIN_FULL_ID,
                        Name = "默认完整标签",
                        Description = "",
                        Sort = 0,
                        Enable = true,
                        Keywords = WordsUtils.GetWordsFull()
                    };
                    DbHelper.Instance.DomainStore.Add(fullDomain);
                }

                // 违规词
                var bannedWord = DbHelper.Instance.BannedWordStore.Get(Constants.DEFAULT_BANNED_WORD_ID);
                if (bannedWord == null)
                {
                    bannedWord = new BannedWord
                    {
                        Id = Constants.DEFAULT_BANNED_WORD_ID,
                        Name = "默认违规词",
                        Description = "",
                        Sort = 0,
                        Enable = true,
                        Keywords = BannedPromptUtils.GetStrings()
                    };
                    DbHelper.Instance.BannedWordStore.Add(bannedWord);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "初始化基本信息异常");
            }

            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

            await Task.CompletedTask;
        }

        /// <summary>
        /// 自动迁移绘图任务数据
        /// </summary>
        public void TaskInfoAutoMigrate()
        {
            try
            {
                LocalLock.TryLock("TaskInfoAutoMigrate", TimeSpan.FromSeconds(10), () =>
                {
                    // 判断最后一条是否存在
                    var success = 0;
                    var last = LiteDBHelper.TaskStore.GetCollection().Query().OrderByDescending(c => c.SubmitTime).FirstOrDefault();
                    if (last != null)
                    {
                        var taskStore = DbHelper.Instance.TaskStore;

                        var lastModel = taskStore.Single(c => c.Id == last.Id);
                        if (lastModel == null)
                        {
                            // 迁移数据
                            var taskIds = LiteDBHelper.TaskStore.GetCollection().Query().Select(c => c.Id).ToList();
                            foreach (var tid in taskIds)
                            {
                                var info = LiteDBHelper.TaskStore.Get(tid);
                                if (info != null)
                                {
                                    // 判断是否存在
                                    var exist = taskStore.Any(c => c.Id == info.Id);
                                    if (!exist)
                                    {
                                        taskStore.Add(info);
                                        success++;
                                    }
                                }
                            }

                            _logger.Information("自动迁移绘图任务数据 success: {@0}", success);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "自动迁移绘图任务数据 error");
            }
        }

        private async void DoWork(object state)
        {
            _logger.Information("开始例行检查");

            try
            {
                var isLock = await AsyncLocalLock.TryLockAsync("DoWork", TimeSpan.FromSeconds(10), async () =>
                {
                    try
                    {
                        // 每日 0 点清除用户日绘图统计
                        if (_userDayReset == null || _userDayReset.Value.Date != DateTime.Now.Date)
                        {
                            // 获取日绘图数量 > 0 的用户
                            var users = DbHelper.Instance.UserStore.Where(c => c.DayDrawCount > 0).ToList();
                            foreach (var user in users)
                            {
                                user.DayDrawCount = 0;
                                DbHelper.Instance.UserStore.Update("DayDrawCount", user);
                            }

                            _userDayReset = DateTime.Now.Date;
                        }


                        // 本地配置中的默认账号
                        var configAccounts = _properties.Accounts.ToList();
                        if (!string.IsNullOrEmpty(_properties.Discord?.ChannelId)
                        && !_properties.Discord.ChannelId.Contains("*"))
                        {
                            configAccounts.Add(_properties.Discord);
                        }

                        await Initialize(configAccounts.ToArray());

                        // 检查并删除旧的文档
                        CheckAndDeleteOldDocuments();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "执行例行检查时发生异常");
                    }

                    _logger.Information("例行检查完成");
                });

                if (!isLock)
                {
                    _logger.Information("例行检查中，请稍后重试...");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "例行检查执行异常");
            }
        }

        /// <summary>
        /// 检查并删除旧文档
        /// </summary>
        public static void CheckAndDeleteOldDocuments()
        {
            var setting = GlobalConfiguration.Setting;
            if (setting.MaxCount <= 0)
            {
                return;
            }

            var maxCount = setting.MaxCount;

            // 如果超过 x 条，删除最早插入的数据
            switch (setting.DatabaseType)
            {
                case DatabaseType.NONE:
                    break;
                case DatabaseType.LiteDB:
                    {
                        var documentCount = DbHelper.Instance.TaskStore.Count();
                        if (documentCount > maxCount)
                        {
                            var documentsToDelete = (int)documentCount - maxCount;
                            var ids = LiteDBHelper.TaskStore.GetCollection().Query().OrderBy(c => c.SubmitTime)
                                .Limit(documentsToDelete)
                                .ToList()
                                .Select(c => c.Id);

                            if (ids.Any())
                            {
                                LiteDBHelper.TaskStore.GetCollection().DeleteMany(c => ids.Contains(c.Id));
                            }
                        }
                    }
                    break;
                case DatabaseType.MongoDB:
                    {
                        var coll = MongoHelper.GetCollection<TaskInfo>();
                        var documentCount = coll.CountDocuments(Builders<TaskInfo>.Filter.Empty);
                        if (documentCount > maxCount)
                        {
                            var documentsToDelete = documentCount - maxCount;
                            var ids = coll.Find(c => true).SortBy(c => c.SubmitTime).Limit((int)documentsToDelete).Project(c => c.Id).ToList();
                            if (ids.Any())
                            {
                                coll.DeleteMany(c => ids.Contains(c.Id));
                            }
                        }
                    }
                    break;
                case DatabaseType.SQLite:
                case DatabaseType.MySQL:
                case DatabaseType.PostgreSQL:
                case DatabaseType.SQLServer:
                    {
                        var freeSql = FreeSqlHelper.FreeSql;
                        if (freeSql != null)
                        {
                            var documentCount = freeSql.Queryable<TaskInfo>().Count();
                            if (documentCount > maxCount)
                            {
                                var documentsToDelete = (int)documentCount - maxCount;
                                var ids = freeSql.Queryable<TaskInfo>().OrderBy(c => c.SubmitTime)
                                    .Take(documentsToDelete)
                                    .ToList()
                                    .Select(c => c.Id);
                                if (ids.Any())
                                {
                                    freeSql.Delete<TaskInfo>().Where(c => ids.Contains(c.Id)).ExecuteAffrows();
                                }
                            }
                        }
                    }
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// 初始化所有账号
        /// </summary>
        /// <returns></returns>
        public async Task Initialize(params DiscordAccountConfig[] appends)
        {
            var isLock = await AsyncLocalLock.TryLockAsync("initialize:all", TimeSpan.FromSeconds(10), async () =>
            {
                var db = DbHelper.Instance.AccountStore;

                var accounts = db.GetAll().OrderBy(c => c.Sort).ToList();

                // 将启动配置中的 account 添加到数据库
                var configAccounts = new List<DiscordAccountConfig>();
                if (appends?.Length > 0)
                {
                    configAccounts.AddRange(appends);
                }

                foreach (var configAccount in configAccounts)
                {
                    var account = accounts.FirstOrDefault(c => c.ChannelId == configAccount.ChannelId);
                    if (account == null)
                    {
                        account = DiscordAccount.Create(configAccount);
                        db.Add(account);

                        accounts.Add(account);
                    }
                }

                foreach (var account in accounts)
                {
                    try
                    {
                        await StartCheckAccount(account, false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Account({@0}) init fail, disabled: {@1}", account.GetDisplay(), ex.Message);

                        account.Enable = false;
                        account.DisabledReason = "初始化失败";

                        db.Update(account);
                    }
                }

                var enableInstanceIds = _discordLoadBalancer.GetAllInstances()
                .Where(instance => instance.IsAlive)
                .Select(instance => instance.ChannelId)
                .ToHashSet();

                _logger.Information("当前可用账号数 [{@0}] - {@1}", enableInstanceIds.Count, string.Join(", ", enableInstanceIds));
            });
            if (!isLock)
            {
                _logger.Warning("初始化所有账号中，请稍后重试...");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 检查并启动连接
        /// </summary>
        public async Task StartCheckAccount(DiscordAccount account, bool isValidateLock = true)
        {
            if (account == null || account.Enable != true)
            {
                return;
            }

            var isLock = await AsyncLocalLock.TryLockAsync($"initialize:{account.Id}", TimeSpan.FromSeconds(5), async () =>
            {
                var setting = GlobalConfiguration.Setting;

                var sw = new Stopwatch();
                var swAll = new Stopwatch();

                swAll.Start();
                sw.Start();

                var info = new StringBuilder();
                info.AppendLine($"{account.Id}初始化中...");

                var db = DbHelper.Instance.AccountStore;
                DiscordInstance disInstance = null;

                try
                {
                    // 获取获取值
                    account = db.Get(account.Id)!;

                    // 如果账号处于登录中
                    if (account.IsAutoLogining)
                    {
                        // 如果超过 10 分钟
                        if (account.LoginStart.HasValue && account.LoginStart.Value.AddMinutes(10) < DateTime.Now)
                        {
                            account.IsAutoLogining = false;
                            account.LoginMessage = "登录超时";

                            db.Update("IsAutoLogining,LoginMessage", account);
                        }
                    }

                    if (account.Enable != true)
                    {
                        return;
                    }

                    disInstance = _discordLoadBalancer.GetDiscordInstance(account.ChannelId);

                    // 判断是否在工作时间内
                    var now = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();
                    var dayCount = (int)DbHelper.Instance.TaskStore.Count(c => c.InstanceId == account.ChannelId && c.SubmitTime >= now);

                    sw.Stop();
                    info.AppendLine($"{account.Id}初始化中... 获取任务数耗时: {sw.ElapsedMilliseconds}ms");
                    sw.Restart();

                    // 随机延期token
                    if (setting.EnableAutoExtendToken)
                    {
                        await RandomSyncToken(account);
                        sw.Stop();
                        info.AppendLine($"{account.Id}初始化中... 随机延期token耗时: {sw.ElapsedMilliseconds}ms");
                        sw.Restart();
                    }

                    // 只要在工作时间内，就创建实例
                    if (DateTime.Now.IsInWorkTime(account.WorkTime))
                    {
                        if (disInstance == null)
                        {
                            // 初始化子频道
                            account.InitSubChannels();

                            // 快速时长校验
                            // 如果 fastTime <= 0.1，则标记为快速用完
                            var fastTime = account.FastTimeRemaining?.ToString()?.Split('/')?.FirstOrDefault()?.Trim();
                            if (!string.IsNullOrWhiteSpace(fastTime) && double.TryParse(fastTime, out var ftime) && ftime <= 0.1)
                            {
                                account.FastExhausted = true;
                            }
                            else
                            {
                                account.FastExhausted = false;
                            }

                            // 自动设置慢速，如果快速用完
                            if (account.FastExhausted == true && account.EnableAutoSetRelax == true)
                            {
                                account.AllowModes = new List<GenerationSpeedMode>() { GenerationSpeedMode.RELAX };

                                if (account.CoreSize > 3)
                                {
                                    account.CoreSize = 3;
                                }
                            }

                            // 启用自动获取私信 ID
                            if (setting.EnableAutoGetPrivateId)
                            {
                                try
                                {
                                    Thread.Sleep(500);
                                    var id = await _discordAccountHelper.GetBotPrivateId(account, EBotType.MID_JOURNEY);
                                    if (!string.IsNullOrWhiteSpace(id))
                                    {
                                        account.PrivateChannelId = id;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.Error(ex, "获取 MJ 私聊频道 ID 异常 {@0}", account.ChannelId);

                                    info.AppendLine($"{account.Id}初始化中... 获取 MJ 私聊频道 ID 异常");
                                }

                                sw.Stop();
                                info.AppendLine($"{account.Id}初始化中... 获取 MJ 私聊频道 ID 耗时: {sw.ElapsedMilliseconds}ms");
                                sw.Restart();

                                try
                                {
                                    Thread.Sleep(500);
                                    var id = await _discordAccountHelper.GetBotPrivateId(account, EBotType.NIJI_JOURNEY);
                                    if (!string.IsNullOrWhiteSpace(id))
                                    {
                                        account.NijiBotChannelId = id;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.Error(ex, "获取 NIJI 私聊频道 ID 异常 {@0}", account.ChannelId);

                                    info.AppendLine($"{account.Id}初始化中... 获取 NIJI 私聊频道 ID 异常");
                                }

                                sw.Stop();
                                info.AppendLine($"{account.Id}初始化中... 获取 NIJI 私聊频道 ID 耗时: {sw.ElapsedMilliseconds}ms");
                                sw.Restart();
                            }

                            account.DayDrawCount = dayCount;
                            db.Update("NijiBotChannelId,PrivateChannelId,AllowModes,SubChannels,SubChannelValues,FastExhausted,DayDrawCount", account);

                            // 清除缓存
                            ClearAccountCache(account.Id);

                            // 启用自动验证账号功能
                            // 连接前先判断账号是否正常
                            if (setting.EnableAutoVerifyAccount)
                            {
                                var success = await _discordAccountHelper.ValidateAccount(account);
                                if (!success)
                                {
                                    throw new Exception("账号不可用");
                                }

                                sw.Stop();
                                info.AppendLine($"{account.Id}初始化中... 验证账号耗时: {sw.ElapsedMilliseconds}ms");
                                sw.Restart();
                            }

                            disInstance = await _discordAccountHelper.CreateDiscordInstance(account)!;
                            disInstance.IsInit = true;
                            _discordLoadBalancer.AddInstance(disInstance);

                            sw.Stop();
                            info.AppendLine($"{account.Id}初始化中... 创建实例耗时: {sw.ElapsedMilliseconds}ms");
                            sw.Restart();

                            // 这里应该等待初始化完成，并获取用户信息验证，获取用户成功后设置为可用状态
                            // 多账号启动时，等待一段时间再启动下一个账号
                            await Task.Delay(1000 * 5);


                            try
                            {
                                // 启动后执行 info setting 操作
                                await _taskService.InfoSetting(account.Id);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "同步 info 异常 {@0}", account.ChannelId);

                                info.AppendLine($"{account.Id}初始化中... 同步 info 异常");
                            }

                            sw.Stop();
                            info.AppendLine($"{account.Id}初始化中... 同步 info 耗时: {sw.ElapsedMilliseconds}ms");
                            sw.Restart();

                        }

                        // 慢速切换快速模式检查
                        if (account.EnableRelaxToFast == true)
                        {
                            await disInstance?.RelaxToFastValidate();
                            sw.Stop();
                            info.AppendLine($"{account.Id}初始化中... 慢速切换快速模式检查耗时: {sw.ElapsedMilliseconds}ms");
                            sw.Restart();
                        }

                        // 启用自动同步信息和设置
                        if (setting.EnableAutoSyncInfoSetting)
                        {
                            // 每 6~12 小时，同步账号信息
                            await disInstance?.RandomSyncInfo();
                            sw.Stop();
                            info.AppendLine($"{account.Id}初始化中... 随机同步信息耗时: {sw.ElapsedMilliseconds}ms");
                            sw.Restart();
                        }
                    }
                    else
                    {
                        sw.Stop();
                        info.AppendLine($"{account.Id}初始化中... 非工作时间，不创建实例耗时: {sw.ElapsedMilliseconds}ms");
                        sw.Restart();

                        // 非工作时间内，如果存在实例则释放
                        if (disInstance != null)
                        {
                            _discordLoadBalancer.RemoveInstance(disInstance);
                            disInstance.Dispose();
                        }

                        sw.Stop();
                        info.AppendLine($"{account.Id}初始化中... 非工作时间，释放实例耗时: {sw.ElapsedMilliseconds}ms");
                        sw.Restart();
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    info.AppendLine($"{account.Id}初始化中... 异常: {ex.Message} 耗时: {sw.ElapsedMilliseconds}ms");
                    sw.Restart();

                    _logger.Error(ex, "Account({@0}) init fail, disabled: {@1}", account.ChannelId, ex.Message);

                    if (setting.EnableAutoLogin)
                    {
                        sw.Stop();
                        info.AppendLine($"{account.Id}尝试自动登录...");
                        sw.Restart();

                        try
                        {
                            // 开始尝试自动登录
                            var suc = DiscordAccountHelper.AutoLogin(account, true);

                            if (suc)
                            {
                                sw.Stop();
                                info.AppendLine($"{account.Id}自动登录请求成功...");
                                sw.Restart();
                            }
                            else
                            {
                                sw.Stop();
                                info.AppendLine($"{account.Id}自动登录请求失败...");
                                sw.Restart();
                            }
                        }
                        catch (Exception exa)
                        {
                            _logger.Error(exa, "Account({@0}) auto login fail, disabled: {@1}", account.ChannelId, exa.Message);

                            sw.Stop();
                            info.AppendLine($"{account.Id}自动登录请求异常...");
                            sw.Restart();
                        }
                    }

                    account.Enable = false;
                    account.DisabledReason = ex.Message ?? "初始化失败";

                    db.Update(account);

                    disInstance?.ClearAccountCache(account.Id);
                    disInstance = null;

                    // 清除缓存
                    ClearAccountCache(account.Id);

                    sw.Stop();
                    info.AppendLine($"{account.Id}初始化中... 异常，禁用账号耗时: {sw.ElapsedMilliseconds}ms");
                    sw.Restart();
                }
                finally
                {
                    swAll.Stop();
                    info.AppendLine($"{account.Id}初始化完成, 总耗时: {swAll.ElapsedMilliseconds}ms");

                    _logger.Information(info.ToString());
                }
            });

            // 未获取到锁时，是否抛出异常
            // 如果验证锁，但未获取锁时，抛出异常
            if (isValidateLock && !isLock)
            {
                throw new LogicException("初始化中，请稍后重试");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 随机 60~600s 延期 token
        /// </summary>
        /// <returns></returns>
        public async Task RandomSyncToken(DiscordAccount account)
        {
            var key = $"random_token_{account.ChannelId}";
            await _memoryCache.GetOrCreateAsync(key, async c =>
            {
                try
                {
                    _logger.Information("随机对 token 进行延期 {@0}", account.ChannelId);

                    // 随机 60~600s
                    var random = new Random();
                    var sec = random.Next(60, 600);
                    c.SetAbsoluteExpiration(TimeSpan.FromSeconds(sec));

                    var options = new RestClientOptions(_discordHelper.GetServer())
                    {
                        MaxTimeout = -1,
                        UserAgent = account.UserAgent ?? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36",
                    };
                    var client = new RestClient(options);
                    var request = new RestRequest("/api/v9/content-inventory/users/@me", Method.Get);
                    request.AddHeader("authorization", account.UserToken);

                    // base64 编码
                    // "eyJvcyI6IldpbmRvd3MiLCJicm93c2VyIjoiQ2hyb21lIiwiZGV2aWNlIjoiIiwic3lzdGVtX2xvY2FsZSI6InpoLUNOIiwiYnJvd3Nlcl91c2VyX2FnZW50IjoiTW96aWxsYS81LjAgKFdpbmRvd3MgTlQgMTAuMDsgV2luNjQ7IHg2NCkgQXBwbGVXZWJLaXQvNTM3LjM2IChLSFRNTCwgbGlrZSBHZWNrbykgQ2hyb21lLzEyOS4wLjAuMCBTYWZhcmkvNTM3LjM2IiwiYnJvd3Nlcl92ZXJzaW9uIjoiMTI5LjAuMC4wIiwib3NfdmVyc2lvbiI6IjEwIiwicmVmZXJyZXIiOiJodHRwczovL2Rpc2NvcmQuY29tLz9kaXNjb3JkdG9rZW49TVRJM056TXhOVEEyT1RFMU1UQXlNekUzTlEuR1k2U2RpLm9zdl81cVpOcl9xeVdxVDBtTW0tYkJ4RVRXQzgwQzVPbzU4WlJvIiwicmVmZXJyaW5nX2RvbWFpbiI6ImRpc2NvcmQuY29tIiwicmVmZXJyZXJfY3VycmVudCI6IiIsInJlZmVycmluZ19kb21haW5fY3VycmVudCI6IiIsInJlbGVhc2VfY2hhbm5lbCI6InN0YWJsZSIsImNsaWVudF9idWlsZF9udW1iZXIiOjM0Mjk2OCwiY2xpZW50X2V2ZW50X3NvdXJjZSI6bnVsbH0="

                    var str = "{\"os\":\"Windows\",\"browser\":\"Chrome\",\"device\":\"\",\"system_locale\":\"zh-CN\",\"browser_user_agent\":\"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36\",\"browser_version\":\"129.0.0.0\",\"os_version\":\"10\",\"referrer\":\"https://discord.com/?discordtoken={@token}\",\"referring_domain\":\"discord.com\",\"referrer_current\":\"\",\"referring_domain_current\":\"\",\"release_channel\":\"stable\",\"client_build_number\":342968,\"client_event_source\":null}";
                    str = str.Replace("{@token}", account.UserToken);

                    // str 转 base64
                    var bs64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(str));

                    request.AddHeader("x-super-properties", bs64);
                    var response = await client.ExecuteAsync(request);

                    //{
                    //    "request_id": "62a56587a8964dfa9cbb81c234a9a962",
                    //    "entries": [],
                    //    "entries_hash": 0,
                    //    "expired_at": "2024-11-08T02:50:21.323000+00:00",
                    //    "refresh_stale_inbox_after_ms": 30000,
                    //    "refresh_token": "eyJjcmVhdGVkX2F0IjogIjIwMjQtMTEtMDhUMDI6Mzk6MjguNDY4MzcyKzAwOjAwIiwgImNvbnRlbnRfaGFzaCI6ICI0N0RFUXBqOEhCU2ErL1RJbVcrNUpDZXVRZVJrbTVOTXBKV1pHM2hTdUZVPSJ9",
                    //    "wait_ms_until_next_fetch": 652856
                    //}

                    var obj = JObject.Parse(response.Content);
                    if (obj.ContainsKey("refresh_token"))
                    {
                        var refreshToken = obj["refresh_token"].ToString();
                        if (!string.IsNullOrWhiteSpace(refreshToken))
                        {
                            _logger.Information("随机对 token 进行延期成功 {@0}", account.ChannelId);
                            return true;
                        }
                    }

                    _logger.Information("随机对 token 进行延期失败 {@0}, {@1}", account.ChannelId, response.Content);

                    return false;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "随机对 token 进行延期异常 {@0}", account.ChannelId);
                }

                return false;
            });
        }

        /// <summary>
        /// 更新账号信息
        /// </summary>
        /// <param name="param"></param>
        public async Task UpdateAccount(DiscordAccount param)
        {
            var model = DbHelper.Instance.AccountStore.Get(param.Id);
            if (model == null)
            {
                throw new LogicException("账号不存在");
            }

            // 更新一定要加锁，因为其他进程会修改 account 值，导致值覆盖
            var isLock = await AsyncLocalLock.TryLockAsync($"initialize:{model.Id}", TimeSpan.FromSeconds(5), async () =>
            {
                model = DbHelper.Instance.AccountStore.Get(model.Id)!;

                // 渠道 ID 和 服务器 ID 禁止修改
                //model.ChannelId = account.ChannelId;
                //model.GuildId = account.GuildId;

                // 更新账号重连时，自动解锁
                model.Lock = false;
                model.CfHashCreated = null;
                model.CfHashUrl = null;
                model.CfUrl = null;

                // 验证 Interval
                if (param.Interval < 1.2m)
                {
                    param.Interval = 1.2m;
                }

                // 最大并行数
                if (param.CoreSize > 12)
                {
                    param.CoreSize = 12;
                }

                // 验证 WorkTime
                if (!string.IsNullOrEmpty(param.WorkTime))
                {
                    var ts = param.WorkTime.ToTimeSlots();
                    if (ts.Count == 0)
                    {
                        param.WorkTime = null;
                    }
                }

                // 验证 FishingTime
                if (!string.IsNullOrEmpty(param.FishingTime))
                {
                    var ts = param.FishingTime.ToTimeSlots();
                    if (ts.Count == 0)
                    {
                        param.FishingTime = null;
                    }
                }

                model.LoginAccount = param.LoginAccount?.Trim();
                model.LoginPassword = param.LoginPassword?.Trim();
                model.Login2fa = param.Login2fa?.Trim();
                model.IsAutoLogining = false; // 重置自动登录状态
                model.LoginStart = null;
                model.LoginEnd = null;
                model.LoginMessage = null;


                model.EnableAutoSetRelax = param.EnableAutoSetRelax;
                model.EnableRelaxToFast = param.EnableRelaxToFast;
                model.EnableFastToRelax = param.EnableFastToRelax;
                model.IsBlend = param.IsBlend;
                model.IsDescribe = param.IsDescribe;
                model.IsShorten = param.IsShorten;
                model.DayDrawLimit = param.DayDrawLimit;
                model.IsVerticalDomain = param.IsVerticalDomain;
                model.VerticalDomainIds = param.VerticalDomainIds;
                model.SubChannels = param.SubChannels;

                model.PermanentInvitationLink = param.PermanentInvitationLink;
                model.FishingTime = param.FishingTime;
                model.EnableNiji = param.EnableNiji;
                model.EnableMj = param.EnableMj;
                model.AllowModes = param.AllowModes;
                model.WorkTime = param.WorkTime;
                model.Interval = param.Interval;
                model.AfterIntervalMin = param.AfterIntervalMin;
                model.AfterIntervalMax = param.AfterIntervalMax;
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

                DbHelper.Instance.AccountStore.Update(model);

                var disInstance = _discordLoadBalancer.GetDiscordInstance(model.ChannelId);
                disInstance?.ClearAccountCache(model.Id);

                // 清除缓存
                ClearAccountCache(model.Id);

                await Task.CompletedTask;
            });
            if (!isLock)
            {
                throw new LogicException("作业执行中，请稍后重试");
            }
        }

        /// <summary>
        /// 清理账号缓存
        /// </summary>
        /// <param name="id"></param>
        public void ClearAccountCache(string id)
        {
            _memoryCache.Remove($"account:{id}");
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

            await UpdateAccount(account);

            // 异步执行
            _ = StartCheckAccount(account);
        }

        /// <summary>
        /// 停止连接并删除账号
        /// </summary>
        /// <param name="id"></param>
        public void DeleteAccount(string id)
        {
            var model = DbHelper.Instance.AccountStore.Get(id);

            if (model != null)
            {
                try
                {
                    var disInstance = _discordLoadBalancer.GetDiscordInstance(model.ChannelId);
                    if (disInstance != null)
                    {
                        _discordLoadBalancer.RemoveInstance(disInstance);
                        disInstance.Dispose();
                    }
                }
                catch
                {
                }

                DbHelper.Instance.AccountStore.Delete(id);
            }
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.Information("例行检查服务已停止");

            _timer?.Change(Timeout.Infinite, 0);

            await Task.CompletedTask;
        }
    }
}
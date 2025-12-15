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
using LiteDB;
using Microsoft.Extensions.Caching.Memory;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using Midjourney.License;
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
        private readonly Setting _properties;
        private readonly DiscordHelper _discordHelper;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger _logger = Log.Logger;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly IUpgradeService _upgradeService;
        private readonly IConsulService _consulService;

        private Timer _timer;
        private DateTime? _upgradeTime = null;
        private bool _isUpgrading = false; // 是否更新中，避免长时间运行更新

        public DiscordAccountInitializer(
            DiscordLoadBalancer discordLoadBalancer,
            DiscordAccountHelper discordAccountHelper,
            IConfiguration configuration,
            ITaskService taskService,
            IMemoryCache memoryCache,
            DiscordHelper discordHelper,
            IHostApplicationLifetime applicationLifetime,
            IUpgradeService upgradeService,
            IConsulService consulService)
        {
            // 配置全局缓存
            GlobalConfiguration.MemoryCache = memoryCache;

            _properties = GlobalConfiguration.Setting;
            _discordLoadBalancer = discordLoadBalancer;
            _discordAccountHelper = discordAccountHelper;
            _taskService = taskService;
            _configuration = configuration;
            _memoryCache = memoryCache;
            _discordHelper = discordHelper;
            _applicationLifetime = applicationLifetime;
            _upgradeService = upgradeService;
            _consulService = consulService;
        }

        /// <summary>
        /// 启动服务并初始化Discord账号实例。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _applicationLifetime.ApplicationStarted.Register(async () =>
                {
                    var setting = GlobalConfiguration.Setting;

                    // 启用配置中心
                    if (setting.ConsulOptions?.Enable == true)
                    {
                        // 启用版本对比更新检查，启用时以注册中心的服务版本为准，如果版本过低则执行更新检查，然后退出应用程序
                        if (setting.ConsulOptions.EnableVersionCheck)
                        {
                            try
                            {
                                var currentVersion = GlobalConfiguration.Version;
                                var consulVersion = await _consulService.GetCurrentVersionAsync();
                                if (!string.IsNullOrWhiteSpace(consulVersion) && !string.IsNullOrWhiteSpace(consulVersion) && currentVersion != consulVersion)
                                {
                                    var exeVer = new Version(currentVersion.TrimStart('v'));
                                    var conVer = new Version(consulVersion.TrimStart('v'));
                                    if (exeVer < conVer)
                                    {
                                        _logger.Information("注册中心检测到新版本，当前版本: {@0}，注册中心版本: {@1}，开始更新检查...", currentVersion, consulVersion);

                                        // 检查更新，当有可用更新时
                                        var downloding = await UpgradeCheck();
                                        if (downloding)
                                        {
                                            // 最多等待 5 分钟，获取下载状态
                                            var downlodingTask = new Task(() =>
                                            {
                                                var sw = new Stopwatch();
                                                sw.Start();
                                                while (sw.Elapsed.TotalMinutes < 5)
                                                {
                                                    var status = _upgradeService.GetUpgradeStatus();
                                                    if (status.Status == UpgradeStatus.ReadyToRestart)
                                                    {
                                                        break;
                                                    }
                                                    Thread.Sleep(1000);
                                                }
                                            });
                                            downlodingTask.Start();
                                            downlodingTask.Wait();

                                            // 再次获取状态
                                            var finalStatus = _upgradeService.GetUpgradeStatus();
                                            if (finalStatus.Status == UpgradeStatus.ReadyToRestart)
                                            {
                                                _logger.Information("注册中心版本更新检查完成，应用程序即将退出以完成更新。");

                                                // 使用非 0 退出码，表示需要重启应用程序
                                                Environment.Exit(101);

                                                return;
                                            }
                                            else
                                            {
                                                _logger.Warning("注册中心版本更新检查完成，但更新未能成功完成，状态：{@0}，请手动检查更新。", finalStatus.Status);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Consul 版本对比更新检查执行失败");
                            }
                        }
                    }
                    else
                    {
                        // 后台更新检查
                        _ = UpgradeCheck();
                    }

                    // 官方下载下载器
                    if (setting.EnableOfficial)
                    {
                        try
                        {
                            _ = LicenseKeyHelper.InitDonwloader();
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "初始化官方业务异常");
                        }
                    }

                    // 启用视频功能，视频下载器
                    if (setting.EnableVideo)
                    {
                        try
                        {
                            _ = new VideoToWebPConverter().ConfigureFFMpeg();
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "初始化视频业务异常");
                        }
                    }

                    // 初始化数据库索引
                    DbHelper.Instance.IndexInit();

                    // 迁移 account user domain banded
                    // 是否开启 LiteDB 自动迁移
                    if (setting.DatabaseType != DatabaseType.LiteDB && setting.DatabaseType != DatabaseType.MongoDB && setting.IsAutoMigrate)
                    {
                        try
                        {
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
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await AdaptiveLock.ExecuteWithLock("TaskInfoAutoMigrate", TimeSpan.FromSeconds(10), () =>
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
                                                    try
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
                                                    catch (Exception ex)
                                                    {
                                                        _logger.Error(ex, "LiteDB 自动迁移绘图任务数异常 TaskId: {@0}", tid);
                                                    }
                                                }

                                                _logger.Information("LiteDB 自动迁移绘图任务数据 success: {@0}", success);
                                            }
                                        }
                                    });
                                }
                                catch (Exception ex)
                                {
                                    _logger.Error(ex, "LiteDB 自动迁移绘图任务数据 error");
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "LiteDB 数据迁移任务信息异常");
                        }
                    }

                    // 迁移 account user domain banded
                    // 是否开启 MongoDB 自动迁移
                    if (setting.DatabaseType != DatabaseType.LiteDB && setting.DatabaseType != DatabaseType.MongoDB && setting.IsAutoMigrateMongo)
                    {
                        try
                        {
                            // 创建 mongodb 实例
                            if (!string.IsNullOrWhiteSpace(setting.MongoDefaultConnectionString) && !string.IsNullOrWhiteSpace(setting.MongoDefaultDatabase))
                            {
                                var mongo = MongoDbFactory.Create(setting.MongoDefaultConnectionString, setting.MongoDefaultDatabase);
                                if (mongo.VerifyConnection())
                                {
                                    // account 迁移
                                    var sourceAccountIds = mongo.GetCollection<DiscordAccount>().Find(x => true).Project(c => c.Id).ToList();
                                    var accountStore = DbHelper.Instance.AccountStore;
                                    var targetAccountIds = accountStore.GetAllIds();
                                    var accountIds = sourceAccountIds.Except(targetAccountIds).ToList();
                                    if (accountIds.Count > 0)
                                    {
                                        var sourceAccounts = mongo.GetCollection<DiscordAccount>().Find(x => true).ToList();
                                        foreach (var id in accountIds)
                                        {
                                            var model = sourceAccounts.FirstOrDefault(c => c.Id == id);
                                            if (model != null)
                                            {
                                                accountStore.Add(model);
                                            }
                                        }
                                    }

                                    // user 迁移
                                    var sourceUserIds = mongo.GetCollection<User>().Find(x => true).Project(c => c.Id).ToList();
                                    var userStore = DbHelper.Instance.UserStore;
                                    var targetUserIds = userStore.GetAllIds();
                                    var userIds = sourceUserIds.Except(targetUserIds).ToList();
                                    if (userIds.Count > 0)
                                    {
                                        var sourceUsers = mongo.GetCollection<User>().Find(x => true).ToList();
                                        foreach (var id in userIds)
                                        {
                                            var model = sourceUsers.FirstOrDefault(c => c.Id == id);
                                            if (model != null)
                                            {
                                                userStore.Add(model);
                                            }
                                        }
                                    }

                                    // domain 迁移
                                    var sourceDomainIds = mongo.GetCollection<DomainTag>().Find(x => true).Project(c => c.Id).ToList();
                                    var domainStore = DbHelper.Instance.DomainStore;
                                    var targetDomainIds = domainStore.GetAllIds();
                                    var domainIds = sourceDomainIds.Except(targetDomainIds).ToList();
                                    if (domainIds.Count > 0)
                                    {
                                        var sourceDomains = mongo.GetCollection<DomainTag>().Find(x => true).ToList();
                                        foreach (var id in domainIds)
                                        {
                                            var model = sourceDomains.FirstOrDefault(c => c.Id == id);
                                            if (model != null)
                                            {
                                                domainStore.Add(model);
                                            }
                                        }
                                    }

                                    // banded 迁移
                                    var sourceBannedIds = mongo.GetCollection<BannedWord>().Find(x => true).Project(c => c.Id).ToList();
                                    var bannedStore = DbHelper.Instance.BannedWordStore;
                                    var targetBannedIds = bannedStore.GetAllIds();
                                    var bannedIds = sourceBannedIds.Except(targetBannedIds).ToList();
                                    if (bannedIds.Count > 0)
                                    {
                                        var sourceBanneds = mongo.GetCollection<BannedWord>().Find(x => true).ToList();
                                        foreach (var id in bannedIds)
                                        {
                                            var model = sourceBanneds.FirstOrDefault(c => c.Id == id);
                                            if (model != null)
                                            {
                                                bannedStore.Add(model);
                                            }
                                        }
                                    }

                                    // 迁移 task
                                    try
                                    {
                                        _ = Task.Run(async () =>
                                        {
                                            try
                                            {
                                                await AdaptiveLock.ExecuteWithLock("TaskInfoAutoMigrate", TimeSpan.FromSeconds(10), () =>
                                                {
                                                    // 判断最后一条是否存在
                                                    var success = 0;
                                                    var error = 0;
                                                    var last = mongo.GetCollection<TaskInfo>().Find(x => true).SortByDescending(c => c.SubmitTime).FirstOrDefault();
                                                    if (last != null)
                                                    {
                                                        var taskStore = DbHelper.Instance.TaskStore;
                                                        var lastModel = taskStore.Single(c => c.Id == last.Id);
                                                        if (lastModel == null)
                                                        {
                                                            // 迁移数据
                                                            var taskIds = mongo.GetCollection<TaskInfo>().Find(x => true).Project(c => c.Id).ToList();
                                                            foreach (var tid in taskIds)
                                                            {
                                                                try
                                                                {
                                                                    var info = mongo.GetCollection<TaskInfo>().Find(x => x.Id == tid).FirstOrDefault();
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
                                                                catch (Exception ex)
                                                                {
                                                                    error++;

                                                                    _logger.Error(ex, "MongoDB 自动迁移绘图任务数异常 TaskId: {@0}", tid);
                                                                }
                                                            }

                                                            _logger.Information("MongoDB 自动迁移绘图任务数据 success: {@0}, error: {@1}", success, error);
                                                        }
                                                    }
                                                });
                                            }
                                            catch (Exception ex)
                                            {
                                                _logger.Error(ex, "MongoDB 自动迁移绘图任务数据 error");
                                            }
                                        });
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.Error(ex, "MongoDB 数据迁移任务信息异常");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "MongoDB 数据迁移基本信息异常");
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

                    // 执行作业
                    _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

                    // 最后注册 Consul 服务
                    if (setting.ConsulOptions?.Enable == true)
                    {
                        try
                        {
                            _logger.Information("正在注册 Consul 服务...");
                            await _consulService.RegisterServiceAsync();
                            _logger.Information("Consul 服务注册完成");
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "注册 Consul 服务注册失败");
                        }
                    }

                    // 订阅 redis 消息
                    if (setting.IsValidRedis)
                    {
                        RedisHelper.Subscribe((RedisHelper.Prefix + Constants.REDIS_NOTIFY_CHANNEL, async msg =>
                        {
                            try
                            {
                                var notification = msg.Body.ToObject<RedisNotification>();
                                await OnRedisReceived(notification);
                            }
                            catch (Exception ex)
                            {
                                // 记录日志
                                Log.Error(ex, $"处理缓存清除通知时发生错误");
                            }
                        }
                        ));
                    }
                });

                // 确保在应用程序停止时注销服务
                _applicationLifetime.ApplicationStopping.Register(async () =>
                {
                    try
                    {
                        await _consulService.DeregisterServiceAsync();
                        _logger.Information("Consul 服务注销完成");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "停止 Consul 服务注销失败");
                    }
                });

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "启动异常");
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
                        // 异步更新检查
                        _ = UpgradeCheck();

                        var now = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();

                        GlobalConfiguration.TodayDraw = (int)DbHelper.Instance.TaskStore.Count(x => x.SubmitTime >= now);
                        GlobalConfiguration.TotalDraw = (int)DbHelper.Instance.TaskStore.Count(x => true);

                        // 用户绘图统计
                        UserStat();

                        // 验证许可
                        await LicenseKeyHelper.Validate();

                        // 初始化
                        await Initialize();

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
                    _logger.Debug("例行检查中...");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "例行检查执行异常");
            }
        }

        /// <summary>
        /// 用户绘图统计
        /// </summary>
        public void UserStat()
        {
            // 今日用户绘图统计
            var setting = GlobalConfiguration.Setting;
            if (setting.EnableUserDrawStatistics)
            {
                var now = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();

                var userTotalCount = new Dictionary<string, int>();
                var userTodayCount = new Dictionary<string, int>();
                if (setting.DatabaseType == DatabaseType.MongoDB)
                {
                    var taskColl = MongoHelper.GetCollection<TaskInfo>();
                    var userColl = MongoHelper.GetCollection<User>();

                    userTotalCount = taskColl.AsQueryable()
                           .GroupBy(c => c.UserId)
                           .Select(g => new
                           {
                               UserId = g.Key,
                               TotalCount = g.Count()
                           })
                           .ToList()
                           .Where(c => !string.IsNullOrWhiteSpace(c.UserId))
                           .ToDictionary(c => c.UserId, c => c.TotalCount);

                    userTodayCount = taskColl.AsQueryable()
                           .Where(c => c.SubmitTime >= now)
                           .GroupBy(c => c.UserId)
                           .Select(g => new
                           {
                               UserId = g.Key,
                               TotalCount = g.Count()
                           })
                           .ToList()
                           .Where(c => !string.IsNullOrWhiteSpace(c.UserId))
                           .ToDictionary(c => c.UserId, c => c.TotalCount);

                    foreach (var item in userTotalCount)
                    {
                        if (!userTodayCount.ContainsKey(item.Key))
                        {
                            userTodayCount[item.Key] = 0;
                        }

                        var update = Builders<User>.Update
                        .Set(c => c.TotalDrawCount, item.Value)
                        .Set(c => c.DayDrawCount, userTodayCount[item.Key]);
                        userColl.UpdateOne(c => c.Id == item.Key, update);
                    }
                }
                else if (setting.DatabaseType == DatabaseType.LiteDB)
                {
                    userTotalCount = LiteDBHelper.TaskStore.GetCollection()
                        .Query()
                        .Select(c => c.UserId)
                        .ToList()
                        .GroupBy(c => c)
                        .Select(g => new
                        {
                            UserId = g.Key,
                            TotalCount = g.Count()
                        })
                        .Where(c => !string.IsNullOrWhiteSpace(c.UserId))
                        .ToDictionary(c => c.UserId, c => c.TotalCount);

                    userTodayCount = LiteDBHelper.TaskStore.GetCollection()
                        .Query()
                        .Where(c => c.SubmitTime >= now)
                        .Select(c => c.UserId)
                        .ToList()
                        .GroupBy(c => c)
                        .Select(g => new
                        {
                            UserId = g.Key,
                            TotalCount = g.Count()
                        })
                        .Where(c => !string.IsNullOrWhiteSpace(c.UserId))
                        .ToDictionary(c => c.UserId, c => c.TotalCount);

                    foreach (var item in userTotalCount)
                    {
                        if (!userTodayCount.ContainsKey(item.Key))
                        {
                            userTodayCount[item.Key] = 0;
                        }

                        var userColl = LiteDBHelper.TaskStore.LiteDatabase.GetCollection<User>();
                        userColl.UpdateMany($"{{ TotalDrawCount: {item.Value}, DayDrawCount: {userTodayCount[item.Key]} }}", $"_id = '{item.Key}'");
                    }
                }
                else
                {
                    var freeSql = FreeSqlHelper.FreeSql;
                    if (freeSql != null)
                    {
                        userTotalCount = freeSql.Select<TaskInfo>()
                            .GroupBy(c => c.UserId)
                            .ToList(c => new
                            {
                                UserId = c.Key,
                                TotalCount = c.Count()
                            })
                            .Where(c => !string.IsNullOrWhiteSpace(c.UserId))
                            .ToDictionary(c => c.UserId, c => c.TotalCount);

                        userTodayCount = freeSql.Select<TaskInfo>()
                            .Where(c => c.SubmitTime >= now)
                            .GroupBy(c => c.UserId)
                            .ToList(c => new
                            {
                                UserId = c.Key,
                                TotalCount = c.Count()
                            })
                            .Where(c => !string.IsNullOrWhiteSpace(c.UserId))
                            .ToDictionary(c => c.UserId, c => c.TotalCount);

                        foreach (var item in userTotalCount)
                        {
                            if (!userTodayCount.ContainsKey(item.Key))
                            {
                                userTodayCount[item.Key] = 0;
                            }

                            freeSql.Update<User>()
                                .Set(c => c.TotalDrawCount, item.Value)
                                .Set(c => c.DayDrawCount, userTodayCount[item.Key])
                                .Where(c => c.Id == item.Key)
                                .ExecuteAffrows();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 执行更新检查并下载最新版本
        /// </summary>
        /// <returns></returns>
        public async Task<bool> UpgradeCheck()
        {
            try
            {
                if (GlobalConfiguration.Setting.EnableUpdateCheck)
                {
                    // 更新超过 24 小时，强制调整状态
                    if (_isUpgrading && _upgradeTime != null && (DateTime.Now - _upgradeTime.Value).TotalHours > 24)
                    {
                        _isUpgrading = false;
                    }

                    if (!_isUpgrading && (_upgradeTime == null || (DateTime.Now - _upgradeTime.Value).TotalHours > 6))
                    {
                        _upgradeTime = DateTime.Now;
                        _isUpgrading = true;

                        try
                        {
                            _logger.Information("开始检查更新...");

                            var last = await _upgradeService.CheckForUpdatesAsync();
                            if (last.HasUpdate)
                            {
                                return await _upgradeService.StartDownloadAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "更新检查失败");
                        }
                        finally
                        {
                            _isUpgrading = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "更新检查执行失败");
            }

            return false;
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
        public async Task Initialize()
        {
            var isLock = await AsyncLocalLock.TryLockAsync("initialize:all", TimeSpan.FromSeconds(10), async () =>
            {
                var db = DbHelper.Instance.AccountStore;

                var accounts = db.GetAll().OrderBy(c => c.Sort).ToList();

                foreach (var account in accounts)
                {
                    try
                    {
                        DrawCounter.InitAccountTodayCounter(account.ChannelId);

                        await StartAccount(account);
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
                _logger.Debug("初始化所有账号中，请稍后重试...");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 检查并启动连接
        /// </summary>
        public async Task StartAccount(DiscordAccount account)
        {
            if (account == null || account.Enable != true)
            {
                return;
            }

            // 自适应锁
            await using var lockHandle = await AdaptiveLock.LockAsync(account.InitializationLockKey, 10);
            if (!lockHandle.IsAcquired)
            {
                return;
            }

            var setting = GlobalConfiguration.Setting;

            var swAll = new Stopwatch();
            swAll.Start();

            var sw = new Stopwatch();
            sw.Start();

            var info = new StringBuilder();

            var db = DbHelper.Instance.AccountStore;
            DiscordInstance disInstance = null;

            try
            {
                // 获取获取值
                account = db.Get(account.Id);

                if (account == null)
                {
                    return;
                }

                // discord 如果账号处于登录中
                if (account.IsDiscord && account.IsAutoLogining)
                {
                    // 如果超过 10 分钟
                    if (account.LoginStart.HasValue && account.LoginStart.Value.AddMinutes(10) < DateTime.Now)
                    {
                        account.IsAutoLogining = false;
                        account.LoginMessage = "登录超时";
                        db.Update("IsAutoLogining,LoginMessage", account);
                        account.ClearCache();
                    }
                }

                if (account.Enable != true)
                {
                    return;
                }

                disInstance = _discordLoadBalancer.GetDiscordInstance(account.ChannelId);

                // 判断是否在工作时间内
                // 只要在工作时间内，就创建实例
                var now = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();
                if (DateTime.Now.IsInWorkTime(account.WorkTime))
                {
                    if (disInstance == null)
                    {
                        // 初始化子频道
                        if (account.IsDiscord)
                        {
                            account.InitSubChannels();
                        }

                        // 快速时长校验
                        // 如果 fastTime <= 0.1，则标记为快速用完
                        var fastTime = account.FastTimeRemaining?.ToString()?.Split('/')?.FirstOrDefault()?.Trim();
                        if (!string.IsNullOrWhiteSpace(fastTime) && double.TryParse(fastTime, out var ftime) && ftime <= 0.2)
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
                            account.AllowModes = [GenerationSpeedMode.RELAX];
                            if (account.CoreSize > 3)
                            {
                                account.CoreSize = 3;
                            }
                        }

                        // discord 启用自动获取私信 ID
                        if (account.IsDiscord && setting.EnableAutoGetPrivateId)
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

                        db.Update("NijiBotChannelId,PrivateChannelId,AllowModes,SubChannels,SubChannelValues,FastExhausted", account);
                        account.ClearCache();

                        // discord 启用自动验证账号功能, 连接前先判断账号是否正常
                        if (account.IsDiscord && setting.EnableAutoVerifyAccount)
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
                        _discordLoadBalancer.AddInstance(disInstance);

                        sw.Stop();
                        info.AppendLine($"{account.Id}初始化中... 创建实例耗时: {sw.ElapsedMilliseconds}ms");
                        sw.Restart();

                        // 首次创建实例后同步账号信息
                        // 高频同步 info setting 一定会封号
                        try
                        {
                            // 这里应该等待初始化完成，并获取用户信息验证，获取用户成功后设置为可用状态
                            // 多账号启动时，等待一段时间再启动下一个账号
                            await Task.Delay(1000 * 5);

                            // 启动后强制同步
                            var success = await disInstance.SyncInfoSetting(true);
                            if (success)
                            {
                                // 设置初始化完成
                                disInstance.IsInit = true;
                            }

                            sw.Stop();
                            info.AppendLine($"{account.Id}初始化中... 同步 info {(success ? "成功" : "失败")} 耗时: {sw.ElapsedMilliseconds}ms");
                            sw.Restart();
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "同步 info 异常 {@0}", account.ChannelId);

                            info.AppendLine($"{account.Id}初始化中... 同步 info 异常");
                        }
                    }

                    // 无最大并行限制
                    if (GlobalConfiguration.GlobalMaxConcurrent != 0)
                    {
                        // 非强制同步获取成功
                        // 账号信息自动同步
                        var success = await disInstance?.SyncInfoSetting();
                        if (success == true)
                        {
                            if (disInstance != null && disInstance.IsInit == false)
                            {
                                // 设置初始化完成
                                disInstance.IsInit = true;
                            }
                        }

                        // discord 随机延期 token
                        if (account.IsDiscord && setting.EnableAutoExtendToken)
                        {
                            await RandomSyncToken(account);
                            sw.Stop();
                            info.AppendLine($"{account.ChannelId}初始化中... 随机延期token耗时: {sw.ElapsedMilliseconds}ms");
                            sw.Restart();
                        }
                    }
                    else
                    {
                        // 并行为 0 标记
                        // 设置初始化完成
                        disInstance.IsInit = true;
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
                info.AppendLine($"{account.ChannelId} 初始化中... 异常: {ex.Message} 耗时: {sw.ElapsedMilliseconds}ms");
                sw.Restart();

                _logger.Error(ex, $"{account.ChannelId} 初始化失败");

                if (setting.EnableAutoLogin && !account.IsYouChuan && !account.IsOfficial)
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
                account.ClearCache();

                disInstance = null;

                sw.Stop();
                info.AppendLine($"{account.Id}初始化中... 异常，禁用账号耗时: {sw.ElapsedMilliseconds}ms");
                sw.Restart();
            }
            finally
            {
                swAll.Stop();

                info.Append($"{account.ChannelId} 检查完成, 总耗时: {swAll.ElapsedMilliseconds}ms");

                _logger.Information(info.ToString());
            }
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
        public async Task<DiscordAccount> UpdateAccount(DiscordAccount param)
        {
            var model = DbHelper.Instance.AccountStore.Get(param.Id);
            if (model == null)
            {
                throw new LogicException("账号不存在");
            }

            // 更新一定要加锁，因为其他进程会修改 account 值，导致值覆盖
            await using var lockHandle = await AdaptiveLock.LockAsync(model.InitializationLockKey, 5);
            if (!lockHandle.IsAcquired)
            {
                throw new LogicException("作业执行中，请稍后重试");
            }

            model = DbHelper.Instance.AccountStore.Get(model.Id)!;

            // 清空禁用原因
            if (model.IsYouChuan || model.IsOfficial)
            {
                model.DisabledReason = null;
            }

            // 更新账号重连时，自动解锁
            model.Lock = false;
            model.CfHashCreated = null;
            model.CfHashUrl = null;
            model.CfUrl = null;

            // 清除风控状态
            model.RiskControlUnlockTime = null;

            // 验证 Interval
            if (param.Interval < 0m)
            {
                param.Interval = 0m;
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

            model.IsDraft = param.IsDraft;
            model.LoginAccount = param.LoginAccount?.Trim();
            model.LoginPassword = param.LoginPassword?.Trim();
            model.Login2fa = param.Login2fa?.Trim();
            model.IsAutoLogining = false; // 重置自动登录状态
            model.LoginStart = null;
            model.LoginEnd = null;
            model.LoginMessage = null;

            model.EnableAutoSetRelax = param.EnableAutoSetRelax;
            model.IsBlend = param.IsBlend;
            model.IsDescribe = param.IsDescribe;
            model.IsShorten = param.IsShorten;
            model.DayDrawLimit = param.DayDrawLimit;
            model.DayRelaxDrawLimit = param.DayRelaxDrawLimit;
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
            model.RelaxQueueSize = param.RelaxQueueSize;
            model.RelaxCoreSize = param.RelaxCoreSize;

            model.TimeoutMinutes = param.TimeoutMinutes;
            model.Weight = param.Weight;
            model.Remark = param.Remark;
            model.BotToken = param.BotToken;
            model.UserToken = param.UserToken;
            model.Mode = param.Mode;
            model.Sponsor = param.Sponsor;
            model.IsHdVideo = param.IsHdVideo;
            model.IsRelaxVideo = param.IsRelaxVideo;
            model.OfficialEnablePersonalize = param.OfficialEnablePersonalize;
            model.YouChuanEnablePreferRelax = param.YouChuanEnablePreferRelax;

            DbHelper.Instance.AccountStore.Update(model);

            model.ClearCache();

            return model;
        }

        /// <summary>
        /// 释放账号连接
        /// </summary>
        /// <param name="account"></param>
        public void DisposeAccount(DiscordAccount account)
        {
            try
            {
                // 如果是悠船或官方，只有修改令牌才释放
                if (account.Enable == true && (account.IsYouChuan || account.IsOfficial))
                {
                    // 如果正在执行则释放
                    var disInstance = _discordLoadBalancer.GetDiscordInstance(account.ChannelId);
                    if (disInstance != null)
                    {
                        // 如果令牌修改了，则必须移除
                        if (account.UserToken != disInstance?.Account.UserToken)
                        {
                            _discordLoadBalancer.RemoveInstance(disInstance);
                            disInstance.Dispose();
                        }
                    }
                }
                else
                {
                    // 如果正在执行则释放
                    var disInstance = _discordLoadBalancer.GetDiscordInstance(account.ChannelId);
                    if (disInstance != null)
                    {
                        _discordLoadBalancer.RemoveInstance(disInstance);
                        disInstance.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "账号重连释放失败 {@0}", account.ChannelId);
            }
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

                model.ClearCache();
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
            try
            {
                await _consulService.DeregisterServiceAsync();

                _logger.Information("Consul 服务注销完成");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "停止 Consul 服务注销失败");
            }

            _logger.Information("例行检查服务已停止");

            _timer?.Change(Timeout.Infinite, 0);

            await Task.CompletedTask;
        }

        /// <summary>
        /// 处理 Redis 消息通知
        /// </summary>
        /// <param name="notification"></param>
        public async Task OnRedisReceived(RedisNotification notification)
        {
            try
            {
                var isSelf = notification.Hostname == Environment.MachineName;
                var use = (int)(DateTime.Now - notification.Timestamp).TotalMilliseconds;

                _logger.Information("收到订阅消息, 用时: {@7} ms, 来源: {@5} -> {@6}, self: {@0}, type: {@1}, cid: {@2}, tid: {@3}, tiId: {@4}",
                    isSelf,
                    notification.Type, notification.ChannelId, notification.TaskInfoId, notification.TaskInfo?.Id,
                    notification.Hostname, Environment.MachineName,
                    use);

                switch (notification.Type)
                {
                    case ENotificationType.AccountCache:
                        {
                            // 清除账号缓存消息
                            var instance = _discordLoadBalancer.GetDiscordInstance(notification.ChannelId);
                            if (instance != null)
                            {
                                // 仅清理本地缓存
                                instance.Account.ClearCache(false);

                                // 判断账号是否被删除了
                                if (!string.IsNullOrWhiteSpace(instance.Account.Id))
                                {
                                    var account = DbHelper.Instance.AccountStore.Get(instance.Account.Id);
                                    if (account == null)
                                    {
                                        instance?.Dispose(false);
                                    }
                                }
                            }
                        }
                        break;

                    case ENotificationType.CancelTaskInfo:
                        {
                            // 判断是否自身发出的
                            if (isSelf)
                            {
                                return;
                            }

                            var targetTask = _discordLoadBalancer.GetRunningTasks().FirstOrDefault(t => t.Id == notification.TaskInfoId);

                            // 如果任务不在队列中，则从存储中获取
                            if (targetTask == null)
                            {
                                targetTask = DbHelper.Instance.TaskStore.Get(notification.TaskInfoId);
                            }

                            if (targetTask != null)
                            {
                                if (!targetTask.IsCompleted)
                                {
                                    targetTask.Fail("取消任务");

                                    DbHelper.Instance.TaskStore.Update(targetTask);
                                }
                            }
                        }
                        break;

                    case ENotificationType.CompleteTaskInfo:
                        {
                            // 判断是否自身发出的
                            if (isSelf)
                            {
                                return;
                            }

                            DrawCounter.Complete(notification.TaskInfo, notification.IsSuccess, false);
                        }
                        break;

                    case ENotificationType.DisposeLock:
                    case ENotificationType.EnqueueTaskInfo:
                        {
                            // 通知任务可以立即执行
                            var instance = _discordLoadBalancer.GetDiscordInstance(notification.ChannelId);
                            instance?.NotifyRedisJob();
                        }
                        break;

                    case ENotificationType.SeedTaskInfo:
                        {
                            // 收到获取种子任务请求
                            var instance = _discordLoadBalancer.GetDiscordInstance(notification.ChannelId);
                            await instance?.GetSeed(notification.TaskInfo);
                        }
                        break;

                    case ENotificationType.DecreaseFastCount:
                        {
                            var instance = _discordLoadBalancer.GetDiscordInstance(notification.ChannelId);
                            instance?.DecreaseFastAvailableCount(notification.DecreaseCount);
                        }
                        break;

                    case ENotificationType.DecreaseRelaxCount:
                        {
                            var instance = _discordLoadBalancer.GetDiscordInstance(notification.ChannelId);
                            instance?.DecreaseYouchuanRelaxAvailableCount(notification.DecreaseCount);
                        }
                        break;

                    case ENotificationType.Restart:
                        {
                            // 判断是否自身发出的
                            if (isSelf)
                            {
                                return;
                            }

                            Log.Information("收到重启应用程序通知，10秒后自动重启容器");

                            // 异步执行重启，避免阻塞当前请求
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    // 等待一段时间让响应返回给客户端
                                    await Task.Delay(1000 * 10);

                                    // 执行重启逻辑
                                    await RestartApplicationAsync();
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, "重启应用程序时发生错误");
                                }
                            });
                        }
                        break;

                    case ENotificationType.CheckUpdate:
                        {
                            // 判断是否自身发出的
                            if (isSelf)
                            {
                                return;
                            }

                            var upgradeInfo = await _upgradeService.CheckForUpdatesAsync();
                            if (upgradeInfo.HasUpdate)
                            {
                                await _upgradeService.StartDownloadAsync();
                            }
                        }
                        break;

                    case ENotificationType.SettingChanged:
                        {
                            // 判断是否自身发出的
                            if (isSelf)
                            {
                                return;
                            }

                            await SettingHelper.Instance.LoadAsync();
                        }
                        break;

                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "处理 Redis 消息通知异常 {@0}", notification.ToJson());
            }
        }

        /// <summary>
        /// 执行应用程序重启
        /// </summary>
        /// <returns></returns>
        private async Task RestartApplicationAsync()
        {
            try
            {
                var isInContainer = IsDockerEnvironment();
                if (isInContainer)
                {
                    // Docker 环境重启
                    await RestartInDockerAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "重启应用程序失败");
                throw;
            }
        }

        private bool IsDockerEnvironment()
        {
            try
            {
                return System.IO.File.Exists("/.dockerenv") ||
                Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ||
                Environment.GetEnvironmentVariable("DOCKER_CONTAINER") != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Docker 环境重启
        /// </summary>
        /// <returns></returns>
        private async Task RestartInDockerAsync()
        {
            Log.Information("检测到 Docker 环境，准备重启容器");

            // 在 Docker 环境中，最安全的方式是退出应用程序
            // 让容器的重启策略来处理重启
            await Task.Delay(1000);

            // 优雅关闭应用程序
            Environment.Exit(0);
        }
    }
}
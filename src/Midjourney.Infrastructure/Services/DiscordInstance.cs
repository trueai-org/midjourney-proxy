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

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Midjourney.Infrastructure.Services;
using Midjourney.License;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Midjourney.Infrastructure.LoadBalancer
{
    /// <summary>
    /// Discord 实例
    /// 实现了IDiscordInstance接口，负责处理Discord相关的任务管理和执行。
    /// </summary>
    public class DiscordInstance : IDiscordInstance
    {
        /// <summary>
        /// 全局正在运行的任务列表
        /// </summary>
        public static readonly ConcurrentDictionary<string, TaskInfo> GlobalRunningTasks = new();

        private readonly object _accountLock = new();
        private readonly ILogger _logger = Log.Logger;

        private readonly ITaskStoreService _taskStoreService;
        private readonly INotifyService _notifyService;

        private readonly IFreeSql _freeSql = FreeSqlHelper.FreeSql;

        /// <summary>
        /// 当前实例正在运行的任务列表
        /// key: TaskInfo.Id, value: TaskInfo
        /// </summary>
        private readonly ConcurrentDictionary<string, TaskInfo> _runningTasks = [];

        private readonly Task _longTask;
        private readonly CancellationTokenSource _longToken;

        private readonly HttpClient _httpClient;
        private readonly DiscordHelper _discordHelper;
        private readonly Dictionary<string, string> _paramsMap;

        private readonly string _discordInteractionUrl;
        private readonly string _discordAttachmentUrl;
        private readonly string _discordMessageUrl;

        private DiscordAccount _account;

        private readonly IYmTaskService _ymTaskService;

        private readonly IHttpClientFactory _httpClientFactory;

        /// <summary>
        /// Relax 队列
        /// </summary>
        private readonly RedisQueue<TaskInfoQueue> _relaxQueue;

        /// <summary>
        /// 默认或快速队列
        /// </summary>
        private readonly RedisQueue<TaskInfoQueue> _defaultOrFastQueue;

        /// <summary>
        /// 放大专属队列（不限制并发）
        /// </summary>
        private readonly RedisQueue<TaskInfoQueue> _upscaleQueue;

        /// <summary>
        /// 图生文专属队列 = 默认队列数
        /// </summary>
        private readonly RedisQueue<TaskInfoQueue> _describeQueue;

        /// <summary>
        /// 图生文专属并发 = 默认并发数
        /// </summary>
        private readonly RedisConcurrent _describeConcurrent;

        /// <summary>
        /// Relax 并发控制
        /// </summary>
        private readonly RedisConcurrent _relaxConcurrent;

        /// <summary>
        /// 默认或快速并发控制
        /// </summary>
        private readonly RedisConcurrent _defaultOrFastConcurrent;

        /// <summary>
        /// redis 执行信号锁, 收到到通知后立即执行
        /// </summary>
        private readonly object _redisJobLock = new();

        /// <summary>
        /// redis 执行信号量
        /// </summary>
        private readonly SemaphoreSlim _redisJobSignal = new(0, 1);

        /// <summary>
        /// 种子获取任务锁，单节点单账号最大并行任务不超过，默认 12
        /// </summary>
        private readonly AsyncParallelLock _seekParallelLock = new(12);

        public DiscordInstance(
            DiscordAccount account,
            ITaskStoreService taskStoreService,
            INotifyService notifyService,
            DiscordHelper discordHelper,
            Dictionary<string, string> paramsMap,
            IWebProxy webProxy,
            IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
            _logger = Log.Logger;

            var hch = new HttpClientHandler
            {
                UseProxy = webProxy != null,
                Proxy = webProxy
            };

            _httpClient = new HttpClient(hch)
            {
                Timeout = TimeSpan.FromMinutes(10),
            };

            _paramsMap = paramsMap;
            _discordHelper = discordHelper;

            _account = account;
            SubscribeToAccount(_account);

            _taskStoreService = taskStoreService;
            _notifyService = notifyService;

            var discordServer = _discordHelper.GetServer();

            _discordInteractionUrl = $"{discordServer}/api/v9/interactions";
            _discordAttachmentUrl = $"{discordServer}/api/v9/channels/{account.ChannelId}/attachments";
            _discordMessageUrl = $"{discordServer}/api/v9/channels/{account.ChannelId}/messages";

            // 放大队列
            _upscaleQueue = new RedisQueue<TaskInfoQueue>(RedisHelper.Instance, $"upscale:{account.ChannelId}");

            // 慢速队列
            _relaxQueue = new RedisQueue<TaskInfoQueue>(RedisHelper.Instance, $"relax:{account.ChannelId}", account.RelaxQueueSize);

            // 默认/快速队列
            _defaultOrFastQueue = new RedisQueue<TaskInfoQueue>(RedisHelper.Instance, $"fast:{account.ChannelId}", account.QueueSize);

            // 图生文队列
            _describeQueue = new RedisQueue<TaskInfoQueue>(RedisHelper.Instance, $"describe:{account.ChannelId}", account.QueueSize);

            // 慢速并发
            _relaxConcurrent = new RedisConcurrent(RedisHelper.Instance, $"relax:{account.ChannelId}");

            // 默认/快速并发
            _defaultOrFastConcurrent = new RedisConcurrent(RedisHelper.Instance, $"fast:{account.ChannelId}");

            // 图生文并发
            _describeConcurrent = new RedisConcurrent(RedisHelper.Instance, $"describe:{account.ChannelId}");

            // 后台任务
            // 后台任务取消 token
            _longToken = new CancellationTokenSource();
            _longTask = new Task(Running, _longToken.Token, TaskCreationOptions.LongRunning);
            _longTask.Start();

            if (account.IsYouChuan || account.IsOfficial)
            {
                _ymTaskService = new YmTaskService(account, this, _httpClientFactory);
            }
        }

        private void SubscribeToAccount(DiscordAccount account)
        {
            // 把 handler 保存到局部变量以便 later 能取消订阅
            Action handler = null!;

            // 使用捕获的 localOld 保证处理时能定位到当时订阅的实例
            var localOld = account;
            handler = () =>
            {
                try
                {
                    // 从存储取最新数据
                    var acc = _freeSql.Get<DiscordAccount>(localOld.Id);
                    if (acc != null && !ReferenceEquals(localOld, acc))
                    {
                        // 在替换订阅之前，先从旧实例取消订阅
                        lock (_accountLock)
                        {
                            localOld.ClearCacheEvent -= handler;
                            _account = acc; // 替换字段

                            // 为新实例订阅同一个 handler（注意这里订阅的是 acc）
                            acc.ClearCacheEvent += handler;

                            _logger.Information("账号信息已更新订阅 {@0}", acc.ChannelId);
                        }
                    }
                    // 如果账号被删除了
                    else if (acc == null)
                    {
                        IsInit = false;
                    }
                }
                catch (Exception ex)
                {
                    // 记录异常，避免抛出导致发布者中断
                    _logger.Error(ex, "订阅账号信息变更失败。 {@0}", localOld.Id);
                }
            };

            // 进行初次订阅
            account.ClearCacheEvent += handler;
        }

        /// <summary>
        /// 默认会话ID。
        /// </summary>
        public string DefaultSessionId { get; set; } = "f1a313a09ce079ce252459dc70231f30";

        /// <summary>
        ///
        /// </summary>
        public DiscordHelper DiscordHelper => _discordHelper;

        public BotMessageListener BotMessageListener { get; set; }

        public WebSocketManager WebSocketManager { get; set; }

        /// <summary>
        /// 获取Discord账号信息。
        /// </summary>
        /// <returns>Discord账号</returns>
        public DiscordAccount Account => _account;

        /// <summary>
        /// 获取实例ID（由于 ChannelId 不可修改，因此使用原始值即可）
        /// </summary>
        /// <returns>实例ID</returns>
        public string ChannelId => Account.ChannelId;

        /// <summary>
        /// 快速可用剩余次数
        /// </summary>
        public int FastAvailableCount => CounterHelper.GetFastTaskAvailableCount(_account.ChannelId);

        /// <summary>
        /// 判断悠船是否允许慢速
        /// </summary>
        /// <returns></returns>
        public bool IsYouChuanAllowRelax()
        {
            var acc = Account;
            if (acc.IsYouChuan)
            {
                if (acc.YouChuanRelaxedReset > DateTime.Now.Date)
                {
                    // 已上限
                    return false;
                }

                // 悠船每日限制慢速
                var relaxCount = CounterHelper.GetYouchuanRelaxCount(acc.ChannelId);
                var limit = acc.YouChuanRelaxDailyLimit;

                if (relaxCount < limit - acc.RelaxCoreSize - acc.RelaxQueueSize)
                {
                    return true;
                }
                else if (relaxCount >= limit)
                {
                    return false;
                }
                else
                {
                    // 精确计算剩余次数
                    var currentCore = _relaxConcurrent.GetConcurrency(acc.RelaxCoreSize);
                    var currentQueue = _relaxQueue.Count();
                    if (relaxCount + currentCore + currentQueue < limit)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 计数器验证，通过速度模式精确判断，判断是否快速/慢速次数是否足够
        /// </summary>
        /// <returns></returns>
        public bool IsValidAvailableCount(GenerationSpeedMode? mode)
        {
            // 如果是快速模式，判断快速是否足够次数
            if (mode == GenerationSpeedMode.FAST)
            {
                // 如果超过 3 次快速
                return FastAvailableCount > 3;
            }
            else if (mode == GenerationSpeedMode.TURBO)
            {
                // 如果超过 6 次快速
                return FastAvailableCount > 6;
            }
            // 如果慢速模式，只有悠船才判断慢速次数
            else if (mode == GenerationSpeedMode.RELAX && Account.IsYouChuan)
            {
                return IsYouChuanAllowRelax();
            }

            // 没有速度要求
            if (mode == null)
            {
                // 有快速或或慢速
                if (Account.IsYouChuan)
                {
                    return FastAvailableCount > 3 || IsYouChuanAllowRelax();
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断是否允许图生文
        /// </summary>
        /// <returns></returns>
        public bool IsAllowDescribe()
        {
            var acc = Account;
            if (acc.IsDescribe)
            {
                // 官方/discord 账号不限制图生文
                if (acc.IsOfficial || acc.IsDiscord)
                {
                    return true;
                }

                // 悠船每日 200 限制
                var limit = 200;
                if (acc.IsYouChuan)
                {
                    if (acc.YouChuanPicreadReset > DateTime.Now.Date)
                    {
                        // 已上限
                        return false;
                    }

                    var describeCount = CounterHelper.GetDescribeCount(acc.ChannelId);
                    if (describeCount < limit - acc.CoreSize - acc.QueueSize)
                    {
                        return true;
                    }
                    else if (describeCount >= limit)
                    {
                        return false;
                    }
                    else
                    {
                        // 精确计算剩余次数
                        var currentCore = _describeConcurrent.GetConcurrency(acc.CoreSize);
                        var currentQueue = _describeQueue.Count();
                        if (describeCount + currentCore + currentQueue < limit)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 扣减快速
        /// </summary>
        /// <param name="value"></param>
        public void DecreaseFastAvailableCount(int value)
        {
            //if (value <= 0 || FastAvailableCount <= 0)
            //{
            //    return;
            //}

            //var newValue = FastAvailableCount - value;
            //FastAvailableCount = Math.Max(0, newValue);

            //_logger.Information("账号 {@0} 扣减快速剩余次数 {@1}，预估当前剩余次数 {@2}", Account.ChannelId, value, FastAvailableCount);
        }

        /// <summary>
        /// 扣减悠船慢速
        /// </summary>
        /// <param name="value"></param>
        public void DecreaseYouchuanRelaxAvailableCount(int value)
        {
            //if (!Account.IsYouChuan || YouchuanRelaxAvailableCount <= 0 || value <= 0)
            //{
            //    return;
            //}

            //var newValue = YouchuanRelaxAvailableCount - value;

            //YouchuanRelaxAvailableCount = Math.Max(0, newValue);

            //_logger.Information("账号 {@0} 扣减悠船慢速剩余次数 {@1}，预估当前剩余次数 {@2}", Account.ChannelId, value, YouchuanRelaxAvailableCount);
        }

        /// <summary>
        /// 是否已初始化完成
        /// </summary>
        public bool IsInit { get; set; }

        /// <summary>
        /// 判断实例是否存活
        /// </summary>
        /// <returns>是否存活</returns>
        public bool IsAlive
        {
            get
            {
                var acc = Account;

                if (acc == null || acc.Enable != true || !IsInit)
                {
                    return false;
                }

                if (acc.IsYouChuan)
                {
                    return !string.IsNullOrWhiteSpace(_ymTaskService?.YouChuanToken);
                }

                if (acc.IsOfficial)
                {
                    return !string.IsNullOrWhiteSpace(_ymTaskService?.OfficialToken);
                }

                if (acc.IsDiscord)
                {
                    return WebSocketManager != null && WebSocketManager.Running == true && acc.Lock == false;
                }

                return false;
            }
        }

        /// <summary>
        /// 悠船 / 官方任务处理
        /// </summary>
        public IYmTaskService YmTaskService => _ymTaskService;

        /// <summary>
        /// 悠船登录
        /// </summary>
        /// <returns></returns>
        public async Task YouChuanLogin()
        {
            await _ymTaskService.YouChuanLogin();
        }

        /// <summary>
        /// 获取正在运行的任务列表。
        /// </summary>
        /// <returns>正在运行的任务列表</returns>
        public List<TaskInfo> GetRunningTasks() => _runningTasks.Values.ToList();

        /// <summary>
        /// 获取正在运行的任务数量。
        /// </summary>
        public int GetRunningTaskCount
        {
            get
            {
                var count = 0;
                var acc = Account;

                count += _defaultOrFastConcurrent.GetConcurrency(acc.CoreSize);

                if (acc.IsYouChuan)
                {
                    count += _relaxConcurrent.GetConcurrency(acc.RelaxCoreSize);
                }

                count += _describeConcurrent.GetConcurrency(acc.CoreSize);

                return count;
            }
        }

        ///// <summary>
        ///// 获取队列中的任务列表。
        ///// </summary>
        ///// <returns>队列中的任务列表</returns>
        //public List<TaskInfo> GetQueueTasks()
        //{
        //    var list = new List<TaskInfo>();
        //    var defaultOrFastQueueTasks = _defaultOrFastQueue.Items();
        //    if (defaultOrFastQueueTasks != null && defaultOrFastQueueTasks.Count > 0)
        //    {
        //        list.AddRange(defaultOrFastQueueTasks.Select(c => c.Info));
        //    }

        //    if (Account.IsYouChuan)
        //    {
        //        var relaxQueueTasks = _relaxQueue.Items();
        //        if (relaxQueueTasks != null && relaxQueueTasks.Count > 0)
        //        {
        //            list.AddRange(relaxQueueTasks.Select(c => c.Info));
        //        }
        //    }

        //    return list;
        //}

        /// <summary>
        /// 获取队列中的任务数量。
        /// </summary>
        public int GetQueueTaskCount
        {
            get
            {
                var count = _defaultOrFastQueue.Count();

                if (Account.IsYouChuan)
                {
                    count += _relaxQueue.Count();
                }

                count += _describeQueue.Count();

                return count;
            }
        }

        ///// <summary>
        ///// 是否存在空闲队列，即：队列是否已满，是否可加入新的任务
        ///// </summary>
        //public bool IsIdleQueue(GenerationSpeedMode? mode = null)
        //{
        //    mode ??= GenerationSpeedMode.FAST;

        //    var acc = Account;

        //    // 悠船慢速队列验证
        //    if (acc.IsYouChuan && mode == GenerationSpeedMode.RELAX)
        //    {
        //        var queueCount = _relaxQueue.Count();

        //        // 判断 RELAX 队列是否有空闲
        //        return acc.RelaxQueueSize <= 0 || queueCount < acc.RelaxQueueSize;
        //    }

        //    // 快速队列验证
        //    var defaultOrFastQueueCount = _defaultOrFastQueue.Count();
        //    return acc.QueueSize <= 0 || defaultOrFastQueueCount < acc.QueueSize;
        //}

        /// <summary>
        /// 根据速度模式判断是否允许继续绘图，不判断放大任务
        /// </summary>
        /// <param name="mode"></param>
        /// <returns></returns>
        public bool IsAllowContinue(GenerationSpeedMode mode)
        {
            var acc = Account;

            var isContinue = true;

            // 使用数据库判断指定模式是否允许绘图
            if (isContinue)
            {
                // 首先判断允许速度
                if (acc.AllowModes.Count > 0 && !acc.AllowModes.Contains(mode))
                {
                    isContinue = false;
                }
                else
                {
                    // 如果是固定速度，则强制修改速度为固定速度
                    if (acc.Mode != null)
                    {
                        mode = acc.Mode.Value;
                    }

                    // 判断速度对应的额度是否满足
                    switch (mode)
                    {
                        case GenerationSpeedMode.RELAX:
                            {
                                if (acc.IsYouChuan)
                                {
                                    isContinue = acc.YouChuanRelaxedReset == null || acc.YouChuanRelaxedReset <= DateTime.Now.Date;
                                }
                                else
                                {
                                    isContinue = true;
                                }
                            }
                            break;

                        case GenerationSpeedMode.FAST:
                            {
                                if (acc.IsYouChuan)
                                {
                                    isContinue = acc.YouChuanFastRemaining > 360;
                                }
                                if (acc.IsOfficial)
                                {
                                    isContinue = acc.OfficialFastRemaining > 360;
                                }
                                if (acc.IsDiscord)
                                {
                                    isContinue = acc.FastExhausted == false;
                                }
                            }
                            break;

                        case GenerationSpeedMode.TURBO:
                            {
                                if (acc.IsYouChuan)
                                {
                                    isContinue = acc.YouChuanFastRemaining > 720;
                                }

                                if (acc.IsOfficial)
                                {
                                    isContinue = acc.OfficialFastRemaining > 720;
                                }

                                if (acc.IsDiscord)
                                {
                                    isContinue = acc.FastExhausted == false;
                                }
                            }
                            break;

                        default:
                            break;
                    }
                }
            }

            // 判断空闲队列
            if (isContinue)
            {
                // 悠船慢速队列验证
                if (acc.IsYouChuan && mode == GenerationSpeedMode.RELAX)
                {
                    var queueCount = _relaxQueue.Count();

                    // 判断 RELAX 队列是否有空闲
                    isContinue = acc.RelaxQueueSize <= 0 || queueCount < acc.RelaxQueueSize;
                }
                else
                {
                    // 快速队列验证
                    var defaultOrFastQueueCount = _defaultOrFastQueue.Count();
                    isContinue = acc.QueueSize <= 0 || defaultOrFastQueueCount < acc.QueueSize;
                }
            }

            // 判断日绘图限制
            if (isContinue)
            {
                // 慢速
                if (mode == GenerationSpeedMode.RELAX)
                {
                    if (acc.DayRelaxDrawLimit <= -1)
                    {
                        isContinue = true;
                    }
                    else
                    {
                        var todayCount = CounterHelper.GetAccountTodayTotalCount(ChannelId, mode);
                        isContinue = todayCount < acc.DayRelaxDrawLimit;
                    }
                }
                // 快速 | 极速
                else
                {
                    if (acc.DayDrawLimit <= -1)
                    {
                        isContinue = true;
                    }
                    else
                    {
                        var todayCount = CounterHelper.GetAccountTodayTotalCount(ChannelId, mode);
                        isContinue = todayCount < acc.DayDrawLimit;
                    }
                }
            }

            // 根据计数器精确判断
            if (isContinue)
            {
                // 如果慢速模式，只有悠船才判断慢速次数
                if (mode == GenerationSpeedMode.RELAX)
                {
                    if (acc.IsYouChuan)
                    {
                        isContinue = IsYouChuanAllowRelax();
                    }
                    else
                    {
                        isContinue = true;
                    }
                }
                else
                {
                    // 如果是快速，则判断快速是否足够次数
                    // 快速至少 > 3 次
                    // 暂不考虑极速的次数、暂不考虑视频的次数

                    if (FastAvailableCount > 3 && FastAvailableCount > acc.CoreSize + acc.QueueCount)
                    {
                        isContinue = true;
                    }
                    else
                    {
                        // 精确计算
                        var currentCore = _defaultOrFastConcurrent.GetConcurrency(acc.CoreSize);
                        var currentQueue = _defaultOrFastQueue.Count();
                        if (FastAvailableCount > 3 && FastAvailableCount > currentCore + currentQueue)
                        {
                            isContinue = true;
                        }
                        else
                        {
                            isContinue = false;
                        }
                    }
                }
            }

            return isContinue;
        }

        /// <summary>
        /// 获取图片 seed
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        public async Task GetSeed(TaskInfo task)
        {
            if (task == null || string.IsNullOrWhiteSpace(task.Id))
            {
                return;
            }

            // 确保最大并行数，不超过 N
            await _seekParallelLock.LockAsync();

            try
            {
                await using var lockHandle = await AdaptiveLock.LockAsync($"GetSeed:{task.Id}", 30);
                if (!lockHandle.IsAcquired)
                {
                    // 没有获取到锁，直接返回
                    return;
                }

                task = _taskStoreService.Get(task.Id);
                if (task == null)
                {
                    return;
                }
                if (!string.IsNullOrWhiteSpace(task.Seed))
                {
                    return;
                }

                // 如果是悠船或官方
                if (task.IsPartner || task.IsOfficial)
                {
                    var seek = await YmTaskService.GetSeed(task);
                    if (!string.IsNullOrWhiteSpace(seek))
                    {
                        task.Seed = seek;
                    }
                    else
                    {
                        task.SeedError = "未找到 seed";
                    }
                    return;
                }

                // 请配置私聊频道
                var privateChannelId = string.Empty;
                if ((task.RealBotType ?? task.BotType) == EBotType.MID_JOURNEY)
                {
                    privateChannelId = Account.PrivateChannelId;
                }
                else
                {
                    privateChannelId = Account.NijiBotChannelId;
                }

                if (string.IsNullOrWhiteSpace(privateChannelId))
                {
                    task.SeedError = "请配置私聊频道";
                    return;
                }

                _runningTasks.TryAdd(task.Id, task);
                GlobalRunningTasks.TryAdd(task.Id, task);

                var hash = task.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_HASH, default);

                var nonce = SnowFlake.NextId();
                task.Nonce = nonce;
                task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                // /show job_id
                // https://discord.com/api/v9/interactions
                var res = await SeedAsync(hash, nonce, task.RealBotType ?? task.BotType);
                if (res.Code != ReturnCode.SUCCESS)
                {
                    task.SeedError = res.Description;
                    return;
                }

                // 等待获取 seed messageId
                // 等待最大超时 5min
                var sw = new Stopwatch();
                sw.Start();

                do
                {
                    Thread.Sleep(100);
                    task = GetRunningTask(task.Id);

                    if (string.IsNullOrWhiteSpace(task.SeedMessageId))
                    {
                        if (sw.ElapsedMilliseconds > 1000 * 60 * 3)
                        {
                            task.SeedError = "超时，未找到 seed messageId";
                            return;
                        }
                    }
                } while (string.IsNullOrWhiteSpace(task.SeedMessageId));

                // 添加反应
                // https://discord.com/api/v9/channels/1256495659683676190/messages/1260598192333127701/reactions/✉️/@me?location=Message&type=0
                var url = $"https://discord.com/api/v9/channels/{privateChannelId}/messages/{task.SeedMessageId}/reactions/%E2%9C%89%EF%B8%8F/%40me?location=Message&type=0";
                var msgRes = await SeedMessagesAsync(url);
                if (msgRes.Code != ReturnCode.SUCCESS)
                {
                    task.SeedError = res.Description;
                    return;
                }

                sw.Start();
                do
                {
                    Thread.Sleep(500);
                    task = GetRunningTask(task.Id);

                    if (string.IsNullOrWhiteSpace(task.Seed))
                    {
                        if (sw.ElapsedMilliseconds > 1000 * 60 * 3)
                        {
                            task.SeedError = "超时，未找到 seed";
                            return;
                        }
                    }
                } while (string.IsNullOrWhiteSpace(task.Seed));
            }
            finally
            {
                _seekParallelLock.Unlock();

                _runningTasks.TryRemove(task.Id, out _);
                GlobalRunningTasks.TryRemove(task.Id, out _);

                _freeSql.Update("Seed,SeedError,SeedMessageId", task);
            }
        }

        /// <summary>
        /// 通知 redis 信号可以立即继续了
        /// </summary>
        public void NotifyRedisJob()
        {
            lock (_redisJobLock)
            {
                // 释放信号量
                if (_redisJobSignal.CurrentCount == 0)
                {
                    _redisJobSignal.Release();
                }
            }
        }

        /// <summary>
        /// 提交任务前休眠
        /// </summary>
        /// <returns></returns>
        private async Task AccountBeforeDelay()
        {
            // 账号休眠
            var preSleep = Account.Interval;
            if (preSleep <= 0m)
            {
                preSleep = 0m;
            }

            // 提交任务前间隔
            // 当一个作业完成后，是否先等待一段时间再提交下一个作业
            if (preSleep > 0)
            {
                await Task.Delay((int)(preSleep * 1000));
            }
        }

        /// <summary>
        /// 提交任务后休眠
        /// </summary>
        /// <param name="isPicReader"></param>
        /// <returns></returns>
        private async Task AccountAfterDelay(bool isPicReader = false)
        {
            // 计算执行后的间隔
            var acc = Account;

            var min = acc.AfterIntervalMin;
            var max = acc.AfterIntervalMax;

            // 计算 min ~ max随机数
            var afterInterval = 1200;
            if (max > min && min >= 0m)
            {
                afterInterval = new Random().Next((int)(min * 1000), (int)(max * 1000));
            }
            else if (max == min && min >= 0m)
            {
                afterInterval = (int)(min * 1000);
            }

            if (isPicReader)
            {
                // 批量任务操作提交间隔 1.2s + 6.8s
                await Task.Delay(afterInterval + 6800);
            }
            else
            {
                await Task.Delay(afterInterval);
            }
        }

        /// <summary>
        /// 后台服务执行任务
        /// </summary>
        private void Running()
        {
            // 程序启动后，如果开启了 redis 则将未开始、已提交、执行中 最近12小时的任务重新加入到队列
            Task.Run(async () =>
            {
                try
                {
                    // 预估恢复队列数 * 超时时间的任务
                    var hour = -1 * 12;
                    var agoTime = new DateTimeOffset(DateTime.Now.AddHours(hour)).ToUnixTimeMilliseconds();
                    var list = _freeSql.Select<TaskInfo>()
                    .Where(c => c.InstanceId == Account.ChannelId && c.SubmitTime >= agoTime && c.Status != TaskStatus.CANCEL && c.Status != TaskStatus.FAILURE && c.Status != TaskStatus.MODAL && c.Status != TaskStatus.SUCCESS)
                    .ToList();

                    _logger.Information("重启恢复作业账号 {@0} 任务数 {@1}", Account.ChannelId, list.Count);

                    if (list.Count > 0)
                    {
                        // 获取所有慢速队列任务
                        var relaxItems = _relaxQueue.Items();
                        var fastItems = _defaultOrFastQueue.Items();

                        foreach (var item in list)
                        {
                            if (item.IsPartnerRelax)
                            {
                                if (relaxItems.Any(c => c?.Info?.Id == item.Id))
                                {
                                    continue;
                                }

                                // 强制加入慢速队列
                                var success = await _relaxQueue.EnqueueAsync(new TaskInfoQueue()
                                {
                                    Info = item,
                                    Function = TaskInfoQueueFunction.REFRESH,
                                }, Account.RelaxQueueSize, 10, true);

                                _logger.Information("重启加入慢速队列任务 {@0} - {@1} - {@2}", Account.ChannelId, item.Id, success);
                            }
                            else
                            {
                                if (fastItems.Any(c => c?.Info?.Id == item.Id))
                                {
                                    continue;
                                }

                                // 强制加入快速队列
                                var success = await _defaultOrFastQueue.EnqueueAsync(new TaskInfoQueue()
                                {
                                    Info = item,
                                    Function = TaskInfoQueueFunction.REFRESH,
                                }, Account.QueueSize, 10, true);

                                _logger.Information("重启加入快速队列任务 {@0} - {@1} - {@2}", Account.ChannelId, item.Id, success);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "重启首次恢复作业异常 {@0}", ChannelId);
                }
            });

            if (GlobalConfiguration.GlobalMaxConcurrent != 0)
            {
                var redisTask = RedisJobRun(_longToken.Token);
            }

            while (true)
            {
                try
                {
                    if (_longToken.Token.IsCancellationRequested)
                    {
                        // 清理资源（如果需要）
                        break;
                    }
                }
                catch { }

                Thread.Sleep(5000);
            }
        }

        /// <summary>
        /// 执行 Redis 后台作业
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task RedisJobRun(CancellationToken token)
        {
            var globalLock = GlobalConfiguration.GlobalLock;
            var globalLimit = GlobalConfiguration.GlobalMaxConcurrent;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 判断当前实例是否可用，实例不可用时，不消费作业
                    while (!IsAlive)
                    {
                        await Task.Delay(1000 * 10, token);
                    }

                    // 是否立即执行下一个任务
                    var isContinueNext = false;

                    // 放大任务队列，不判断并发数
                    if (Account.IsYouChuan || Account.IsOfficial)
                    {
                        // 悠船 | 官方立即执行全部放大任务
                        var upscaleQueueCount = 0;
                        do
                        {
                            upscaleQueueCount = await _upscaleQueue.CountAsync();
                            if (upscaleQueueCount > 0)
                            {
                                var req = await _upscaleQueue.DequeueAsync(token);
                                if (req?.Info != null)
                                {
                                    // 使用 Task.Run 启动后台任务，避免阻塞主线程
                                    _ = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            await RedisQueueUpdateProgress(req);
                                        }
                                        finally
                                        {
                                            // 由于不占用并发锁，所以不需要释放锁通知
                                        }
                                    }, token);
                                }
                            }
                        } while (upscaleQueueCount > 1);
                    }
                    else
                    {
                        // Discord 放大任务插队优先执行
                        var upscaleQueueCount = await _upscaleQueue.CountAsync();
                        if (upscaleQueueCount > 0)
                        {
                            var req = await _upscaleQueue.DequeueAsync(token);
                            if (req?.Info != null)
                            {
                                // 如果队列中还有任务
                                isContinueNext = isContinueNext || upscaleQueueCount > 1;

                                // 在执行前休眠，由于消息已经取出来了，但是还没有消费或提交，如果服务器突然宕机，可能会导致提交参数丢失
                                await AccountBeforeDelay();

                                // 使用 Task.Run 启动后台任务，避免阻塞主线程
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await RedisQueueUpdateProgress(req);
                                    }
                                    finally
                                    {
                                        // 由于不占用并发锁，所以不需要释放锁通知
                                    }
                                }, token);

                                await AccountAfterDelay();
                            }
                        }
                    }

                    // 图生文队列任务处理
                    var describeQueueCount = await _describeQueue.CountAsync();
                    if (describeQueueCount > 0)
                    {
                        if (globalLimit > 0)
                        {
                            await globalLock.LockAsync(token);
                        }
                        // 先尝试获取并发锁
                        var lockObj = _describeConcurrent.TryLock(Account.CoreSize);
                        if (lockObj != null)
                        {
                            // 如果队列中还有任务，并且获取到锁
                            isContinueNext = isContinueNext || describeQueueCount > 1;
                            // 内部已经控制了并发和阻塞，这里只需循环调用
                            var req = await _describeQueue.DequeueAsync(token);
                            if (req?.Info != null)
                            {
                                // 在执行前休眠，由于消息已经取出来了，但是还没有消费或提交，如果服务器突然宕机，可能会导致提交参数丢失
                                await AccountBeforeDelay();
                                // 使用 Task.Run 启动后台任务，避免阻塞主线程
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await RedisQueueUpdateProgress(req);
                                    }
                                    finally
                                    {
                                        // 释放锁
                                        lockObj.Dispose();
                                        if (globalLimit > 0)
                                        {
                                            globalLock.Unlock();
                                        }
                                        // 发送锁我已经释放了
                                        var notification = new RedisNotification
                                        {
                                            Type = ENotificationType.DisposeLock,
                                            ChannelId = ChannelId,
                                            TaskInfoId = req?.Info?.Id
                                        };
                                        RedisHelper.Publish(RedisHelper.Prefix + Constants.REDIS_NOTIFY_CHANNEL, notification.ToJson());
                                    }
                                }, token);
                                await AccountAfterDelay();
                            }
                            else
                            {
                                // 释放锁
                                lockObj.Dispose();
                                if (globalLimit > 0)
                                {
                                    globalLock.Unlock();
                                }
                                // 发送锁我已经释放了
                                var notification = new RedisNotification
                                {
                                    Type = ENotificationType.DisposeLock,
                                    ChannelId = ChannelId,
                                    TaskInfoId = req?.Info?.Id
                                };
                                RedisHelper.Publish(RedisHelper.Prefix + Constants.REDIS_NOTIFY_CHANNEL, notification.ToJson());

                                // 中文日志
                                Log.Warning("Redis 图生文队列出队为空 {@0}", Account.ChannelId);
                            }
                        }
                        else
                        {
                            if (globalLimit > 0)
                            {
                                globalLock.Unlock();
                            }
                        }
                    }

                    // 从快速队列获取任务
                    var queueCount = await _defaultOrFastQueue.CountAsync();
                    if (queueCount > 0)
                    {
                        if (globalLimit > 0)
                        {
                            await globalLock.LockAsync(token);
                        }

                        // 先尝试获取并发锁
                        var lockObj = _defaultOrFastConcurrent.TryLock(Account.CoreSize);
                        if (lockObj != null)
                        {
                            // 如果队列中还有任务，并且获取到锁
                            isContinueNext = isContinueNext || queueCount > 1;

                            // 内部已经控制了并发和阻塞，这里只需循环调用
                            var req = await _defaultOrFastQueue.DequeueAsync(token);
                            if (req?.Info != null)
                            {
                                // 在执行前休眠，由于消息已经取出来了，但是还没有消费或提交，如果服务器突然宕机，可能会导致提交参数丢失
                                await AccountBeforeDelay();

                                // 使用 Task.Run 启动后台任务，避免阻塞主线程
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await RedisQueueUpdateProgress(req);
                                    }
                                    finally
                                    {
                                        // 释放锁
                                        lockObj.Dispose();

                                        if (globalLimit > 0)
                                        {
                                            globalLock.Unlock();
                                        }

                                        // 发送锁我已经释放了
                                        var notification = new RedisNotification
                                        {
                                            Type = ENotificationType.DisposeLock,
                                            ChannelId = ChannelId,
                                            TaskInfoId = req?.Info?.Id
                                        };
                                        RedisHelper.Publish(RedisHelper.Prefix + Constants.REDIS_NOTIFY_CHANNEL, notification.ToJson());
                                    }
                                }, token);

                                await AccountAfterDelay();
                            }
                            else
                            {
                                // 释放锁
                                lockObj.Dispose();

                                if (globalLimit > 0)
                                {
                                    globalLock.Unlock();
                                }

                                // 发送锁我已经释放了
                                var notification = new RedisNotification
                                {
                                    Type = ENotificationType.DisposeLock,
                                    ChannelId = ChannelId,
                                    TaskInfoId = req?.Info?.Id
                                };
                                RedisHelper.Publish(RedisHelper.Prefix + Constants.REDIS_NOTIFY_CHANNEL, notification.ToJson());

                                // 中文日志
                                Log.Warning("Redis 默认队列出队为空 {@0}", Account.ChannelId);
                            }
                        }
                        else
                        {
                            if (globalLimit > 0)
                            {
                                globalLock.Unlock();
                            }
                        }
                    }

                    // 只有悠船才有慢速队列
                    // 是否立即执行下一个慢速任务
                    var relaxQueueCount = 0;
                    if (Account.IsYouChuan)
                    {
                        if (globalLimit > 0)
                        {
                            await globalLock.LockAsync(token);
                        }

                        // 从放松队列获取任务
                        relaxQueueCount = await _relaxQueue.CountAsync();
                        if (relaxQueueCount > 0)
                        {
                            // 先尝试获取并发锁
                            var lockObj = _relaxConcurrent.TryLock(Account.RelaxCoreSize);
                            if (lockObj != null)
                            {
                                // 如果队列中还有任务，并且获取到锁
                                isContinueNext = isContinueNext || relaxQueueCount > 1;

                                // 内部已经控制了并发和阻塞，这里只需循环调用
                                var req = await _relaxQueue.DequeueAsync(token);
                                if (req?.Info != null)
                                {
                                    // 使用 Task.Run 启动后台任务，避免阻塞主线程
                                    _ = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            await RedisQueueUpdateProgress(req);
                                        }
                                        finally
                                        {
                                            // 释放锁
                                            lockObj.Dispose();

                                            if (globalLimit > 0)
                                            {
                                                globalLock.Unlock();
                                            }

                                            // 发送锁我已经释放了
                                            var notification = new RedisNotification
                                            {
                                                Type = ENotificationType.DisposeLock,
                                                ChannelId = ChannelId,
                                                TaskInfoId = req?.Info?.Id
                                            };
                                            RedisHelper.Publish(RedisHelper.Prefix + Constants.REDIS_NOTIFY_CHANNEL, notification.ToJson());
                                        }
                                    }, token);

                                    await AccountAfterDelay();
                                }
                                else
                                {
                                    // 释放锁
                                    lockObj.Dispose();

                                    if (globalLimit > 0)
                                    {
                                        globalLock.Unlock();
                                    }

                                    // 发送锁我已经释放了
                                    var notification = new RedisNotification
                                    {
                                        Type = ENotificationType.DisposeLock,
                                        ChannelId = ChannelId,
                                        TaskInfoId = req?.Info?.Id
                                    };
                                    RedisHelper.Publish(RedisHelper.Prefix + Constants.REDIS_NOTIFY_CHANNEL, notification.ToJson());

                                    // 中文日志
                                    Log.Warning("Redis 慢速队列出队为空 {@0}", Account.ChannelId);
                                }
                            }
                        }
                        else
                        {
                            if (globalLimit > 0)
                            {
                                globalLock.Unlock();
                            }
                        }
                    }

                    // 队列可能还有任务时，立即执行下一次判断
                    if (isContinueNext)
                    {
                        await Task.Delay(200, token);
                    }
                    else
                    {
                        // 等待 10s 或收到信号唤醒
                        var signaled = await _redisJobSignal.WaitAsync(1000 * 10, token);
                        if (signaled)
                        {
                            // 由完成时/新任务信号量释放唤醒，立即执行
                            await Task.Delay(200, token);
                        }
                        else
                        {
                            // 超时唤醒，继续循环
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break; // 停止信号
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Redis Queue Worker Error");

                    // 防止死循环报错导致 CPU 飙升，加一个短暂延迟
                    await Task.Delay(1000 * 10, token);
                }
            }
        }

        /// <summary>
        /// 提交任务到 Redis 队列（入队前不保存到数据库）
        /// </summary>
        /// <param name="info"></param>
        /// <param name="discordSubmit"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> RedisEnqueue(TaskInfoQueue req)
        {
            if (req?.Info == null)
            {
                return SubmitResultVO.Fail(ReturnCode.FAILURE, "未知错误，请稍后重试");
            }

            var info = req.Info;
            info.InstanceId = ChannelId;

            try
            {
                var currentWaitNumbers = 0;

                // 如果是放大任务，则加入放大队列
                if (info.Action == TaskAction.UPSCALE)
                {
                    // 先保存到数据库，再加入到队列
                    _taskStoreService.Save(info);

                    // 放大队列允许溢出
                    var success = await _upscaleQueue.EnqueueAsync(req, -1, ignoreFull: true);
                    if (!success)
                    {
                        _taskStoreService.Delete(info.Id);

                        return SubmitResultVO.Fail(ReturnCode.FAILURE, "提交失败，队列已满，请稍后重试")
                            .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ChannelId);
                    }
                }
                else if (info.Action == TaskAction.DESCRIBE)
                {
                    // 在任务提交时，前面的的任务数量
                    currentWaitNumbers = await _describeQueue.CountAsync();
                    if (Account.QueueSize > 0 && currentWaitNumbers >= Account.QueueSize)
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, "提交失败，队列已满，请稍后重试")
                            .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ChannelId);
                    }

                    // 先保存到数据库，再加入到队列
                    _taskStoreService.Save(info);
                    var success = await _describeQueue.EnqueueAsync(req, Account.QueueSize);
                    if (!success)
                    {
                        _taskStoreService.Delete(info.Id);

                        return SubmitResultVO.Fail(ReturnCode.FAILURE, "提交失败，队列已满，请稍后重试")
                            .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ChannelId);
                    }
                }
                else if (info.IsPartnerRelax)
                {
                    // 在任务提交时，前面的的任务数量
                    currentWaitNumbers = await _relaxQueue.CountAsync();

                    if (Account.RelaxQueueSize > 0 && currentWaitNumbers >= Account.RelaxQueueSize)
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, "提交失败，队列已满，请稍后重试")
                            .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ChannelId);
                    }

                    // 先保存到数据库，再加入到队列
                    _taskStoreService.Save(info);
                    var success = await _relaxQueue.EnqueueAsync(req, Account.RelaxQueueSize);
                    if (!success)
                    {
                        _taskStoreService.Delete(info.Id);

                        return SubmitResultVO.Fail(ReturnCode.FAILURE, "提交失败，队列已满，请稍后重试")
                            .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ChannelId);
                    }
                }
                else
                {
                    // 在任务提交时，前面的的任务数量
                    currentWaitNumbers = await _defaultOrFastQueue.CountAsync();
                    if (Account.QueueSize > 0 && currentWaitNumbers >= Account.QueueSize)
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, "提交失败，队列已满，请稍后重试")
                            .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ChannelId);
                    }

                    // 先保存到数据库，再加入到队列
                    _taskStoreService.Save(info);
                    var success = await _defaultOrFastQueue.EnqueueAsync(req, Account.QueueSize);
                    if (!success)
                    {
                        _taskStoreService.Delete(info.Id);

                        return SubmitResultVO.Fail(ReturnCode.FAILURE, "提交失败，队列已满，请稍后重试")
                            .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ChannelId);
                    }
                }

                // 发送入队提醒
                var notification = new RedisNotification
                {
                    Type = ENotificationType.EnqueueTaskInfo,
                    ChannelId = ChannelId,
                    TaskInfoId = info.Id
                };
                RedisHelper.Publish(RedisHelper.Prefix + Constants.REDIS_NOTIFY_CHANNEL, notification.ToJson());

                if (currentWaitNumbers == 0)
                {
                    return SubmitResultVO.Of(ReturnCode.SUCCESS, "提交成功", info.Id)
                        .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ChannelId);
                }
                else
                {
                    return SubmitResultVO.Of(ReturnCode.IN_QUEUE, $"排队中，前面还有{currentWaitNumbers}个任务", info.Id)
                        .SetProperty("numberOfQueues", currentWaitNumbers)
                        .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ChannelId);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "submit task error");

                _taskStoreService.Delete(info.Id);

                return SubmitResultVO.Fail(ReturnCode.FAILURE, "提交失败，系统异常")
                    .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ChannelId);
            }
        }

        /// <summary>
        /// 更新 Redis 队列任务进度
        /// </summary>
        /// <param name="queue"></param>
        /// <returns></returns>
        public async Task RedisQueueUpdateProgress(TaskInfoQueue queue)
        {
            var info = queue?.Info;

            if (info == null || string.IsNullOrWhiteSpace(info?.Id))
            {
                Log.Error("任务信息无效，跳过处理。 {@0}", queue);
                return;
            }

            try
            {
                // 开始
                Log.Information("开始处理任务。 {@0} - {@1} - {@2}", info.Id, info.InstanceId, info.Status);

                // 同一个任务锁
                using var infoLock = RedisHelper.Instance.Lock($"UpdateProgress:{info.Id}", 15);
                if (infoLock == null)
                {
                    // 从队列取出后，但是没有消费，可能会造成任务 NOT_START
                    Log.Warning("获取任务锁失败，跳过处理。 {@0} - {@1} - {@2}", info.Id, info.InstanceId, info.Status);
                    return;
                }

                // 重新获取 info
                var oldId = info.Id;

                info = _taskStoreService.Get(info.Id);
                if (info == null)
                {
                    Log.Warning("任务不存在，跳过处理。 {@0}", oldId);
                    return;
                }

                // 如果任务完成
                if (info.IsCompleted)
                {
                    Log.Information("任务已完成，跳过处理。 {@0} - {@1} - {@2}", info.Id, info.InstanceId, info.Status);
                    return;
                }

                _runningTasks.TryAdd(info.Id, info);
                GlobalRunningTasks.TryAdd(info.Id, info);

                // 判断当前实例是否可用
                if (!IsAlive)
                {
                    info.Fail("实例不可用");
                    SaveAndNotify(info);
                    return;
                }

                if (info.Status == TaskStatus.NOT_START || (info.Status == TaskStatus.MODAL && queue.Function == TaskInfoQueueFunction.MODAL))
                {
                    // 判断提交时间是否大于超时 * 最大队列数
                    var subTime = DateTimeOffset.FromUnixTimeMilliseconds(info.SubmitTime.Value).ToLocalTime();
                    if ((DateTime.Now - subTime).TotalMinutes > Account.TimeoutMinutes * 12)
                    {
                        info.Fail("任务提交超时");
                        SaveAndNotify(info);
                        return;
                    }

                    // 刷新作业需要特殊处理，一般是重启服务器恢复的作业
                    if (info.Status == TaskStatus.NOT_START && queue.Function == TaskInfoQueueFunction.REFRESH)
                    {
                        // 如果是 imagine 任务
                        if (info.Action == TaskAction.IMAGINE)
                        {
                            queue.Function = TaskInfoQueueFunction.SUBMIT;
                        }
                        else if (info.Action == TaskAction.BLEND)
                        {
                            queue.Function = TaskInfoQueueFunction.BLEND;
                        }
                        else if (info.Action == TaskAction.DESCRIBE)
                        {
                            queue.Function = TaskInfoQueueFunction.DESCRIBE;
                        }
                        else if (info.Action == TaskAction.EDIT)
                        {
                            queue.Function = TaskInfoQueueFunction.EDIT;
                        }
                        else if (info.Action == TaskAction.RETEXTURE)
                        {
                            queue.Function = TaskInfoQueueFunction.RETEXTURE;
                        }
                        else if (info.Action == TaskAction.VIDEO)
                        {
                            queue.Function = TaskInfoQueueFunction.VIDEO;
                        }
                        else
                        {
                            // 其他暂不处理，因为没参数
                        }
                    }

                    switch (queue.Function)
                    {
                        case TaskInfoQueueFunction.SUBMIT:
                            {
                                // 绘画任务未提交
                                if (info.IsPartner || info.IsOfficial)
                                {
                                    var result = await YmTaskService.SubmitTaskAsync(info, _taskStoreService, this);
                                    if (result?.Code != ReturnCode.SUCCESS)
                                    {
                                        info.Fail(result?.Description ?? "未知错误");
                                        SaveAndNotify(info);
                                        return;
                                    }
                                }
                                else
                                {
                                    var result = await ImagineAsync(info, info.PromptEn, info.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default));
                                    if (result?.Code != ReturnCode.SUCCESS)
                                    {
                                        info.Fail(result?.Description ?? "未知错误");
                                        SaveAndNotify(info);
                                        return;
                                    }
                                }

                                if (!info.IsCompleted)
                                {
                                    info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                    info.Status = TaskStatus.SUBMITTED;
                                    info.Progress = "0%";
                                }

                                SaveAndNotify(info);
                            }
                            break;

                        case TaskInfoQueueFunction.ACTION:
                            // 变化任务未提交
                            {
                                if (info.IsPartner || info.IsOfficial)
                                {
                                    var result = await YmTaskService.SubmitActionAsync(info, queue.ActionParam.Dto, queue.ActionParam.TargetTask, _taskStoreService, this);
                                    if (result?.Code != ReturnCode.SUCCESS)
                                    {
                                        info.Fail(result?.Description ?? "未知错误");
                                        SaveAndNotify(info);
                                        return;
                                    }
                                }
                                else
                                {
                                    var result = await ActionAsync(queue.ActionParam.MessageId,
                                        queue.ActionParam.CustomId,
                                        queue.ActionParam.MessageFlags,
                                        queue.ActionParam.Nonce, info);
                                    if (result?.Code != ReturnCode.SUCCESS)
                                    {
                                        info.Fail(result?.Description ?? "未知错误");
                                        SaveAndNotify(info);
                                        return;
                                    }
                                }

                                if (!info.IsCompleted)
                                {
                                    info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                    info.Status = TaskStatus.SUBMITTED;
                                    info.Progress = "0%";
                                }

                                SaveAndNotify(info);
                            }
                            break;

                        case TaskInfoQueueFunction.REFRESH:
                            {
                            }
                            break;

                        case TaskInfoQueueFunction.MODAL:
                            {
                                // 弹窗任务未提交
                                if (info.IsPartner || info.IsOfficial)
                                {
                                    var result = await YmTaskService.SubmitModal(info, queue.ModalParam.TargetTask, queue.ModalParam.Dto, _taskStoreService);
                                    if (result?.Code != ReturnCode.SUCCESS)
                                    {
                                        info.Fail(result?.Description ?? "未知错误");
                                        SaveAndNotify(info);
                                        return;
                                    }
                                }
                                else
                                {
                                    var task = info;
                                    var submitAction = queue.ModalParam.Dto;

                                    var customId = task.GetProperty<string>(Constants.TASK_PROPERTY_CUSTOM_ID, default);
                                    var messageFlags = task.GetProperty<string>(Constants.TASK_PROPERTY_FLAGS, default)?.ToInt() ?? 0;
                                    var messageId = task.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_ID, default);
                                    var nonce = task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default);

                                    // 弹窗确认
                                    task.RemixModaling = true;

                                    var res = await ActionAsync(messageId, customId, messageFlags, nonce, task);
                                    if (res?.Code != ReturnCode.SUCCESS)
                                    {
                                        info.Fail(res?.Description ?? "未知错误");
                                        SaveAndNotify(info);
                                        return;
                                    }

                                    // 等待获取 messageId 和交互消息 id
                                    // 等待最大超时 5min
                                    var sw = new Stopwatch();
                                    sw.Start();
                                    do
                                    {
                                        // 等待 2.5s
                                        Thread.Sleep(2500);
                                        task = GetRunningTask(task.Id);

                                        if (string.IsNullOrWhiteSpace(task.RemixModalMessageId) || string.IsNullOrWhiteSpace(task.InteractionMetadataId))
                                        {
                                            if (sw.ElapsedMilliseconds > 300000)
                                            {
                                                info.Fail("超时，未找到消息");
                                                SaveAndNotify(info);
                                                return;
                                            }
                                        }
                                    } while (string.IsNullOrWhiteSpace(task.RemixModalMessageId) || string.IsNullOrWhiteSpace(task.InteractionMetadataId));

                                    // 等待 1.2s
                                    Thread.Sleep(1200);

                                    task.RemixModaling = false;

                                    // 自定义变焦
                                    if (customId.StartsWith("MJ::CustomZoom::"))
                                    {
                                        nonce = SnowFlake.NextId();
                                        task.Nonce = nonce;
                                        task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                                        var result = await ZoomAsync(task, task.RemixModalMessageId, customId, task.PromptEn, nonce);
                                        if (result?.Code != ReturnCode.SUCCESS)
                                        {
                                            info.Fail(result?.Description ?? "未知错误");
                                            SaveAndNotify(info);
                                            return;
                                        }
                                    }
                                    // 局部重绘
                                    else if (customId.StartsWith("MJ::Inpaint::"))
                                    {
                                        var ifarmeCustomId = task.GetProperty<string>(Constants.TASK_PROPERTY_IFRAME_MODAL_CREATE_CUSTOM_ID, default);
                                        var result = await InpaintAsync(task, ifarmeCustomId, task.PromptEn, submitAction.MaskBase64);
                                        if (result?.Code != ReturnCode.SUCCESS)
                                        {
                                            info.Fail(result?.Description ?? "未知错误");
                                            SaveAndNotify(info);
                                            return;
                                        }
                                    }
                                    // 图生文 -> 文生图
                                    else if (customId.StartsWith("MJ::Job::PicReader::"))
                                    {
                                        nonce = SnowFlake.NextId();
                                        task.Nonce = nonce;
                                        task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                                        var result = await PicReaderAsync(task, task.RemixModalMessageId, customId, task.PromptEn, nonce, task.RealBotType ?? task.BotType);
                                        if (result?.Code != ReturnCode.SUCCESS)
                                        {
                                            info.Fail(result?.Description ?? "未知错误");
                                            SaveAndNotify(info);
                                            return;
                                        }
                                    }
                                    // prompt shorten -> 生图
                                    else if (customId.StartsWith("MJ::Job::PromptAnalyzer::"))
                                    {
                                        nonce = SnowFlake.NextId();
                                        task.Nonce = nonce;
                                        task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                                        // MJ::ImagineModal::1265485889606516808
                                        customId = $"MJ::ImagineModal::{messageId}";
                                        var modal = "MJ::ImagineModal::new_prompt";

                                        var result = await RemixAsync(task, task.Action.Value, task.RemixModalMessageId, modal,
                                            customId, task.PromptEn, nonce, task.RealBotType ?? task.BotType);
                                        if (result?.Code != ReturnCode.SUCCESS)
                                        {
                                            info.Fail(result?.Description ?? "未知错误");
                                            SaveAndNotify(info);
                                            return;
                                        }
                                    }
                                    // Remix mode
                                    else if (task.Action == TaskAction.VARIATION || task.Action == TaskAction.REROLL || task.Action == TaskAction.PAN)
                                    {
                                        nonce = SnowFlake.NextId();
                                        task.Nonce = nonce;
                                        task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                                        var action = task.Action;

                                        TaskInfo parentTask = null;
                                        if (!string.IsNullOrWhiteSpace(task.ParentId))
                                        {
                                            parentTask = _taskStoreService.Get(task.ParentId);
                                            if (parentTask == null)
                                            {
                                                info.Fail("未找到父级任务");
                                                SaveAndNotify(info);
                                                return;
                                            }
                                        }

                                        var prevCustomId = parentTask?.GetProperty<string>(Constants.TASK_PROPERTY_REMIX_CUSTOM_ID, default);
                                        var prevModal = parentTask?.GetProperty<string>(Constants.TASK_PROPERTY_REMIX_MODAL, default);

                                        var modal = "MJ::RemixModal::new_prompt";
                                        if (action == TaskAction.REROLL)
                                        {
                                            // 如果是首次提交，则使用交互 messageId
                                            if (string.IsNullOrWhiteSpace(prevCustomId))
                                            {
                                                // MJ::ImagineModal::1265485889606516808
                                                customId = $"MJ::ImagineModal::{messageId}";
                                                modal = "MJ::ImagineModal::new_prompt";
                                            }
                                            else
                                            {
                                                modal = prevModal;

                                                if (prevModal.Contains("::PanModal"))
                                                {
                                                    // 如果是 pan, pan 是根据放大图片的 CUSTOM_ID 进行重绘处理
                                                    var cus = parentTask?.GetProperty<string>(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, default);
                                                    if (string.IsNullOrWhiteSpace(cus))
                                                    {
                                                        info.Fail("未找到目标图片的 U 操作");
                                                        SaveAndNotify(info);
                                                        return;
                                                    }

                                                    // MJ::JOB::upsample::3::10f78893-eddb-468f-a0fb-55643a94e3b4
                                                    var arr = cus.Split("::");
                                                    var hash = arr[4];
                                                    var i = arr[3];

                                                    var prevArr = prevCustomId.Split("::");
                                                    var convertedString = $"MJ::PanModal::{prevArr[2]}::{hash}::{i}";
                                                    customId = convertedString;

                                                    // 在进行 U 时，记录目标图片的 U 的 customId
                                                    task.SetProperty(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, parentTask?.GetProperty<string>(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, default));
                                                }
                                                else
                                                {
                                                    customId = prevCustomId;
                                                }

                                                task.SetProperty(Constants.TASK_PROPERTY_REMIX_CUSTOM_ID, customId);
                                                task.SetProperty(Constants.TASK_PROPERTY_REMIX_MODAL, modal);
                                            }
                                        }
                                        else if (action == TaskAction.VARIATION)
                                        {
                                            var suffix = "0";

                                            // 如果全局开启了高变化，则高变化
                                            if ((task.RealBotType ?? task.BotType) == EBotType.MID_JOURNEY)
                                            {
                                                if (Account.Buttons.Any(x => x.CustomId == "MJ::Settings::HighVariabilityMode::1" && x.Style == 3))
                                                {
                                                    suffix = "1";
                                                }
                                            }
                                            else
                                            {
                                                if (Account.NijiButtons.Any(x => x.CustomId == "MJ::Settings::HighVariabilityMode::1" && x.Style == 3))
                                                {
                                                    suffix = "1";
                                                }
                                            }

                                            // 低变化
                                            if (customId.Contains("low_variation"))
                                            {
                                                suffix = "0";
                                            }
                                            // 如果是高变化
                                            else if (customId.Contains("high_variation"))
                                            {
                                                suffix = "1";
                                            }

                                            var parts = customId.Split("::");
                                            var convertedString = $"MJ::RemixModal::{parts[4]}::{parts[3]}::{suffix}";
                                            customId = convertedString;

                                            task.SetProperty(Constants.TASK_PROPERTY_REMIX_CUSTOM_ID, customId);
                                            task.SetProperty(Constants.TASK_PROPERTY_REMIX_MODAL, modal);
                                        }
                                        else if (action == TaskAction.PAN)
                                        {
                                            modal = "MJ::PanModal::prompt";

                                            // MJ::JOB::pan_left::1::f58e98cb-e76b-4ffa-9ed2-74f0c3fefa5c::SOLO
                                            // to
                                            // MJ::PanModal::left::f58e98cb-e76b-4ffa-9ed2-74f0c3fefa5c::1

                                            var parts = customId.Split("::");
                                            var convertedString = $"MJ::PanModal::{parts[2].Split('_')[1]}::{parts[4]}::{parts[3]}";
                                            customId = convertedString;

                                            task.SetProperty(Constants.TASK_PROPERTY_REMIX_CUSTOM_ID, customId);
                                            task.SetProperty(Constants.TASK_PROPERTY_REMIX_MODAL, modal);

                                            // 在进行 U 时，记录目标图片的 U 的 customId
                                            task.SetProperty(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, parentTask?.GetProperty<string>(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, default));
                                        }
                                        else
                                        {
                                            info.Fail("未知操作");
                                            SaveAndNotify(info);
                                            return;
                                        }

                                        var result = await RemixAsync(task, task.Action.Value, task.RemixModalMessageId, modal,
                                            customId, task.PromptEn, nonce, task.RealBotType ?? task.BotType);
                                        if (result?.Code != ReturnCode.SUCCESS)
                                        {
                                            info.Fail(result?.Description ?? "未知错误");
                                            SaveAndNotify(info);
                                            return;
                                        }
                                    }
                                    else
                                    {
                                        info.Fail("不支持的操作");
                                        SaveAndNotify(info);
                                        return;
                                    }
                                }

                                if (!info.IsCompleted)
                                {
                                    info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                    info.Status = TaskStatus.SUBMITTED;
                                    info.Progress = "0%";
                                }

                                SaveAndNotify(info);
                            }
                            break;

                        case TaskInfoQueueFunction.DESCRIBE:
                            {
                                if (info.IsPartner || info.IsOfficial)
                                {
                                    if (!info.IsCompleted)
                                    {
                                        info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                        info.Status = TaskStatus.SUBMITTED;
                                        info.Progress = "0%";
                                    }

                                    await YmTaskService.Describe(info);

                                    if (info.Buttons.Count > 0)
                                    {
                                        await info.SuccessAsync();
                                    }
                                    else
                                    {
                                        info.Fail("操作失败，请稍后重试");
                                    }

                                    SaveAndNotify(info);

                                    return;
                                }
                                else
                                {
                                    var result = await DescribeByLinkAsync(info.ImageUrl,
                                        info.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default),
                                        info.RealBotType ?? info.BotType);

                                    if (result?.Code != ReturnCode.SUCCESS)
                                    {
                                        info.Fail(result?.Description ?? "未知错误");
                                        SaveAndNotify(info);
                                        return;
                                    }

                                    if (!info.IsCompleted)
                                    {
                                        info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                        info.Status = TaskStatus.SUBMITTED;
                                        info.Progress = "0%";
                                    }

                                    SaveAndNotify(info);
                                }
                            }
                            break;

                        case TaskInfoQueueFunction.SHORTEN:
                            {
                                if (info.IsPartner || info.IsOfficial)
                                {
                                }
                                else
                                {
                                    var result = await ShortenAsync(info, info.PromptEn,
                                        info.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default),
                                        info.RealBotType ?? info.BotType);
                                    if (result?.Code != ReturnCode.SUCCESS)
                                    {
                                        info.Fail(result?.Description ?? "未知错误");
                                        SaveAndNotify(info);
                                        return;
                                    }

                                    if (!info.IsCompleted)
                                    {
                                        info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                        info.Status = TaskStatus.SUBMITTED;
                                        info.Progress = "0%";
                                    }

                                    SaveAndNotify(info);
                                }
                            }
                            break;

                        case TaskInfoQueueFunction.BLEND:
                            {
                                if (info.IsPartner || info.IsOfficial)
                                {
                                    var result = await YmTaskService.SubmitTaskAsync(info, _taskStoreService, this);
                                    if (result?.Code != ReturnCode.SUCCESS)
                                    {
                                        info.Fail(result?.Description ?? "未知错误");
                                        SaveAndNotify(info);
                                        return;
                                    }
                                }
                                else
                                {
                                    var result = await BlendAsync(
                                        queue.BlendParam.FinalFileNames,
                                        queue.BlendParam.Dimensions,
                                        info.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default),
                                        info.RealBotType ?? info.BotType);
                                    if (result?.Code != ReturnCode.SUCCESS)
                                    {
                                        info.Fail(result?.Description ?? "未知错误");
                                        SaveAndNotify(info);
                                        return;
                                    }
                                }

                                if (!info.IsCompleted)
                                {
                                    info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                    info.Status = TaskStatus.SUBMITTED;
                                    info.Progress = "0%";
                                }

                                SaveAndNotify(info);
                            }
                            break;

                        case TaskInfoQueueFunction.EDIT:
                            {
                                if (info.IsPartner || info.IsOfficial)
                                {
                                    var result = await YmTaskService.SubmitTaskAsync(info, _taskStoreService, this);
                                    if (result?.Code != ReturnCode.SUCCESS)
                                    {
                                        info.Fail(result?.Description ?? "未知错误");
                                        SaveAndNotify(info);
                                        return;
                                    }

                                    if (!info.IsCompleted)
                                    {
                                        info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                        info.Status = TaskStatus.SUBMITTED;
                                        info.Progress = "0%";
                                    }

                                    SaveAndNotify(info);
                                }
                                else
                                {
                                }
                            }
                            break;

                        case TaskInfoQueueFunction.RETEXTURE:
                            {
                                if (info.IsPartner || info.IsOfficial)
                                {
                                    var result = await YmTaskService.SubmitTaskAsync(info, _taskStoreService, this);
                                    if (result?.Code != ReturnCode.SUCCESS)
                                    {
                                        info.Fail(result?.Description ?? "未知错误");
                                        SaveAndNotify(info);
                                        return;
                                    }

                                    if (!info.IsCompleted)
                                    {
                                        info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                        info.Status = TaskStatus.SUBMITTED;
                                        info.Progress = "0%";
                                    }

                                    SaveAndNotify(info);
                                }
                                else
                                {
                                }
                            }
                            break;

                        case TaskInfoQueueFunction.VIDEO:
                            {
                                if (info.IsPartner || info.IsOfficial)
                                {
                                    var result = await YmTaskService.SubmitTaskAsync(info, _taskStoreService, this);
                                    if (result?.Code != ReturnCode.SUCCESS)
                                    {
                                        info.Fail(result?.Description ?? "未知错误");
                                        SaveAndNotify(info);
                                        return;
                                    }

                                    if (!info.IsCompleted)
                                    {
                                        info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                        info.Status = TaskStatus.SUBMITTED;
                                        info.Progress = "0%";
                                    }

                                    SaveAndNotify(info);
                                }
                                else
                                {
                                    var result = await ImagineAsync(info, info.PromptEn,
                                        info.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default));
                                    if (result?.Code != ReturnCode.SUCCESS)
                                    {
                                        info.Fail(result?.Description ?? "未知错误");
                                        SaveAndNotify(info);
                                        return;
                                    }

                                    if (!info.IsCompleted)
                                    {
                                        info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                        info.Status = TaskStatus.SUBMITTED;
                                        info.Progress = "0%";
                                    }

                                    SaveAndNotify(info);
                                }
                            }
                            break;

                        default:
                            break;
                    }
                }

                // 超时处理
                var timeoutMin = Account.TimeoutMinutes;
                if (info.StartTime == null || info.StartTime == 0)
                {
                    info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                }

                // 转本地时间
                var startTime = DateTimeOffset.FromUnixTimeMilliseconds(info.StartTime.Value).ToLocalTime();

                if (info.Status == TaskStatus.SUBMITTED || info.Status == TaskStatus.IN_PROGRESS)
                {
                    while (info.Status == TaskStatus.SUBMITTED || info.Status == TaskStatus.IN_PROGRESS)
                    {
                        // 悠船/官方
                        if (info.IsPartner || info.IsOfficial)
                        {
                            await _ymTaskService.UpdateStatus(info, _taskStoreService, Account);
                        }

                        SaveAndNotify(info);

                        if ((DateTime.Now - startTime).TotalMinutes > timeoutMin)
                        {
                            info.Fail($"执行超时 {timeoutMin} 分钟");
                            SaveAndNotify(info);
                            return;
                        }

                        await Task.Delay(3000);
                    }

                    // 任务完成后，自动读消息
                    try
                    {
                        // 成功才都消息
                        if (info.Status == TaskStatus.SUCCESS && !info.IsPartner && !info.IsOfficial)
                        {
                            var res = await ReadMessageAsync(info.MessageId);
                            if (res.Code == ReturnCode.SUCCESS)
                            {
                                _logger.Debug("自动读消息成功 {@0} - {@1}", info.InstanceId, info.Id);
                            }
                            else
                            {
                                _logger.Warning("自动读消息失败 {@0} - {@1}", info.InstanceId, info.Id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "自动读消息异常 {@0} - {@1}", info.InstanceId, info.Id);
                    }
                }

                // 任务执行完成
                _logger.Information("任务执行结束 {@0} - {@1} - {@2}", info.Id, info.InstanceId, info.Status);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "更新任务进度异常 {@0} - {@1}", info.Id, info.InstanceId);

                info.Fail("服务异常，请稍后重试");

                SaveAndNotify(info);
            }
            finally
            {
                try
                {
                    if (info.Status == TaskStatus.SUCCESS)
                    {
                        // 图生文
                        if (info.Action == TaskAction.DESCRIBE)
                        {
                            CounterHelper.DescribeIncrement(Account);
                        }
                        // 记录今日 relax / fast 次数
                        else if (info.Action != TaskAction.UPSCALE)
                        {
                            if (info.Mode == GenerationSpeedMode.RELAX)
                            {
                                // 只有悠船慢速计数
                                if (info.IsPartnerRelax)
                                {
                                    // 记录慢速使用次数
                                    var value = 1;
                                    if (info.Action == TaskAction.VIDEO)
                                    {
                                        var bs = info.GetVideoBatchSize();
                                        value *= bs * 2;
                                    }
                                    var count = CounterHelper.YouchuanRelaxIncrement(Account, value);
                                    var relaxAvailable = Math.Max(0, Account.YouChuanRelaxDailyLimit - count);
                                    Log.Information("悠船慢速任务完成，扣减 relax 次数: TaskId={@0}, InstanceId={@1}, 扣除={@2}, 预估慢速剩余次数={@3}, 今日慢速计数={@4}",
                                        info.Id, info.InstanceId, value, relaxAvailable, count);
                                }
                            }
                            else
                            {
                                // 今日总成功绘图
                                var totalCount = CounterHelper.GetAccountTodaySuccessTotalCount(Account.ChannelId);

                                var value = 1;
                                if (info.Mode == GenerationSpeedMode.TURBO)
                                {
                                    value *= 2;
                                }
                                if (info.Action == TaskAction.VIDEO)
                                {
                                    var bs = info.GetVideoBatchSize();
                                    value *= bs * 2;
                                }

                                // 扣减快速
                                var currentCount = CounterHelper.FastTaskAvailableDecrementCount(Account.ChannelId, value);
                                var fastAvailable = Math.Max(0, currentCount);

                                Log.Information("任务完成，扣减 fast 次数: TaskId={@0}, InstanceId={@1}, 扣除={@2}, 预估快速剩余次数={@3}, 今日总绘图计数={@4}",
                                    info.Id, info.InstanceId, value, fastAvailable, totalCount);

                                // 如果是快速模式，且触发最低阈值，则立即同步一次 info
                                // 每 200 次执行一次同步，或 低于 12 次执行一次同步
                                if (fastAvailable % 200 == 0 || fastAvailable <= 12)
                                {
                                    await SyncInfoSetting(true);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "记录 relax/fast 次数异常 {@0} - {@1}", info.Id, info.InstanceId);
                }

                _runningTasks.TryRemove(info.Id, out _);
                GlobalRunningTasks.TryRemove(info.Id, out _);

                // 如果任务执行结束，仍然处于未开始状态
                if (!info.IsCompleted)
                {
                    _logger.Error("警告：未知错误，任务执行结束仍未完成 {@0} - {@1} - {@2}", info.Id, info.InstanceId, info.Status);
                }

                SaveAndNotify(info);
            }
        }

        /// <summary>
        /// 保存并通知任务状态变化。
        /// </summary>
        /// <param name="task">任务信息</param>
        private void SaveAndNotify(TaskInfo task)
        {
            try
            {
                if (task == null)
                {
                    return;
                }

                _taskStoreService.Save(task);
                _notifyService.NotifyTaskChange(task);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "作业通知执行异常 {@0}", task.Id);
            }
        }

        /// <summary>
        /// 查找符合条件的正在运行的任务。
        /// </summary>
        /// <param name="condition">条件</param>
        /// <returns>符合条件的正在运行的任务列表</returns>
        public IEnumerable<TaskInfo> FindRunningTask(Func<TaskInfo, bool> condition)
        {
            return GetRunningTasks().Where(condition);
        }

        /// <summary>
        /// 根据ID获取正在运行的任务。
        /// </summary>
        /// <param name="id">任务ID</param>
        /// <returns>任务信息</returns>
        public TaskInfo GetRunningTask(string id)
        {
            return GetRunningTasks().FirstOrDefault(t => id == t.Id);
        }

        /// <summary>
        /// 根据 ID 获取历史任务
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public TaskInfo GetTask(string id)
        {
            return _taskStoreService.Get(id);
        }

        /// <summary>
        /// 根据随机数获取正在运行的任务。
        /// </summary>
        /// <param name="nonce">随机数</param>
        /// <returns>任务信息</returns>
        public TaskInfo GetRunningTaskByNonce(string nonce)
        {
            if (string.IsNullOrWhiteSpace(nonce))
            {
                return null;
            }

            return FindRunningTask(c => c.Nonce == nonce).FirstOrDefault();
        }

        /// <summary>
        /// 根据消息ID获取正在运行的任务。
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <returns>任务信息</returns>
        public TaskInfo GetRunningTaskByMessageId(string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                return null;
            }

            return FindRunningTask(c => c.MessageId == messageId).FirstOrDefault();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose(bool isPublishToRedis = true)
        {
            var accountId = Account?.Id;

            try
            {
                BotMessageListener?.Dispose();
                WebSocketManager?.Dispose();

                // 任务取消
                _longToken.Cancel();

                // 清理后台任务
                if (_longTask != null && !_longTask.IsCompleted)
                {
                    try
                    {
                        _longTask.Wait();
                    }
                    catch
                    {
                        // Ignore exceptions from logging task
                    }
                }

                // 释放未完成的任务
                foreach (var runningTask in _runningTasks)
                {
                    runningTask.Value.Fail("强制取消"); // 取消任务（假设TaskInfo有Cancel方法）
                }

                _runningTasks.Clear();
                GlobalRunningTasks.Clear();
            }
            catch
            {
            }
            finally
            {
                // 最后清除缓存
                Account.ClearCache(isPublishToRedis);
            }
        }

        /// <summary>
        /// 绘画
        /// </summary>
        /// <param name="info"></param>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <returns></returns>
        public async Task<Message> ImagineAsync(TaskInfo info, string prompt, string nonce)
        {
            prompt = GetPrompt(prompt, info);

            var json = (info.RealBotType ?? info.BotType) == EBotType.MID_JOURNEY ? _paramsMap["imagine"] : _paramsMap["imagineniji"];
            var paramsStr = ReplaceInteractionParams(json, nonce);

            JObject paramsJson = JObject.Parse(paramsStr);
            paramsJson["data"]["options"][0]["value"] = prompt;

            return await PostJsonAndCheckStatusAsync(paramsJson.ToString());
        }

        /// <summary>
        /// 放大
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="index"></param>
        /// <param name="messageHash"></param>
        /// <param name="messageFlags"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> UpscaleAsync(string messageId, int index, string messageHash, int messageFlags, string nonce, EBotType botType)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["upscale"], nonce, botType)
                .Replace("$message_id", messageId)
                .Replace("$index", index.ToString())
                .Replace("$message_hash", messageHash);

            var obj = JObject.Parse(paramsStr);

            if (obj.ContainsKey("message_flags"))
            {
                obj["message_flags"] = messageFlags;
            }
            else
            {
                obj.Add("message_flags", messageFlags);
            }

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 变化
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="index"></param>
        /// <param name="messageHash"></param>
        /// <param name="messageFlags"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> VariationAsync(string messageId, int index, string messageHash, int messageFlags, string nonce, EBotType botType)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["variation"], nonce, botType)
                .Replace("$message_id", messageId)
                .Replace("$index", index.ToString())
                .Replace("$message_hash", messageHash);
            var obj = JObject.Parse(paramsStr);

            if (obj.ContainsKey("message_flags"))
            {
                obj["message_flags"] = messageFlags;
            }
            else
            {
                obj.Add("message_flags", messageFlags);
            }

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 执行动作
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="customId"></param>
        /// <param name="messageFlags"></param>
        /// <param name="nonce"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        public async Task<Message> ActionAsync(
            string messageId,
            string customId,
            int messageFlags,
            string nonce,
            TaskInfo info)
        {
            var botType = info.RealBotType ?? info.BotType;

            string guid = null;
            string channelId = null;
            if (!string.IsNullOrWhiteSpace(info.SubInstanceId))
            {
                if (Account.SubChannelValues.ContainsKey(info.SubInstanceId))
                {
                    guid = Account.SubChannelValues[info.SubInstanceId];
                    channelId = info.SubInstanceId;
                }
            }

            var paramsStr = ReplaceInteractionParams(_paramsMap["action"], nonce, botType,
                guid, channelId)
                .Replace("$message_id", messageId);

            var obj = JObject.Parse(paramsStr);

            if (obj.ContainsKey("message_flags"))
            {
                obj["message_flags"] = messageFlags;
            }
            else
            {
                obj.Add("message_flags", messageFlags);
            }

            obj["data"]["custom_id"] = customId;

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 图片 seed 值
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> SeedAsync(string jobId, string nonce, EBotType botType)
        {
            // 私聊频道
            var json = botType == EBotType.MID_JOURNEY ? _paramsMap["seed"] : _paramsMap["seedniji"];
            var paramsStr = json
              .Replace("$channel_id", botType == EBotType.MID_JOURNEY ? Account.PrivateChannelId : Account.NijiBotChannelId)
              .Replace("$session_id", DefaultSessionId)
              .Replace("$nonce", nonce)
              .Replace("$job_id", jobId);

            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 图片 seed 值消息
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public async Task<Message> SeedMessagesAsync(string url)
        {
            try
            {
                //if (!JwtTokenValidate(Account.UserToken))
                //{
                //    return Message.Of(ReturnCode.VALIDATION_ERROR, "令牌错误");
                //}

                // 解码
                url = System.Web.HttpUtility.UrlDecode(url);

                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Content = new StringContent("", Encoding.UTF8, "application/json")
                };

                request.Headers.UserAgent.ParseAdd(Account.UserAgent);

                // 设置 request Authorization 为 UserToken，不需要 Bearer 前缀
                request.Headers.Add("Authorization", Account.UserToken);

                var response = await _httpClient.SendAsync(request);
                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return Message.Success();
                }

                _logger.Error("Seed Http 请求执行失败 {@0}, {@1}, {@2}", url, response.StatusCode, response.Content);

                return Message.Of((int)response.StatusCode, "请求失败");
            }
            catch (HttpRequestException e)
            {
                _logger.Error(e, "Seed Http 请求执行异常 {@0}", url);

                return Message.Of(ReturnCode.FAILURE, e.Message?.Substring(0, Math.Min(e.Message.Length, 100)) ?? "未知错误");
            }
        }

        /// <summary>
        /// 自定义变焦
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <returns></returns>
        public async Task<Message> ZoomAsync(TaskInfo info, string messageId, string customId, string prompt, string nonce)
        {
            customId = customId.Replace("MJ::CustomZoom::", "MJ::OutpaintCustomZoomModal::");
            prompt = GetPrompt(prompt, info);

            string paramsStr = ReplaceInteractionParams(_paramsMap["zoom"], nonce, info.RealBotType ?? info.BotType)
                .Replace("$message_id", messageId);
            //.Replace("$prompt", prompt);

            var obj = JObject.Parse(paramsStr);

            obj["data"]["custom_id"] = customId;
            obj["data"]["components"][0]["components"][0]["value"] = prompt;

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 图生文 - 生图
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> PicReaderAsync(TaskInfo info, string messageId, string customId, string prompt, string nonce, EBotType botType)
        {
            var index = customId.Split("::").LastOrDefault();
            prompt = GetPrompt(prompt, info);

            string paramsStr = ReplaceInteractionParams(_paramsMap["picreader"], nonce, botType)
                .Replace("$message_id", messageId)
                .Replace("$index", index);

            var obj = JObject.Parse(paramsStr);
            obj["data"]["components"][0]["components"][0]["value"] = prompt;
            paramsStr = obj.ToString();

            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// Remix 操作
        /// </summary>
        /// <param name="action"></param>
        /// <param name="messageId"></param>
        /// <param name="modal"></param>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> RemixAsync(TaskInfo info, TaskAction action, string messageId, string modal, string customId, string prompt, string nonce, EBotType botType)
        {
            prompt = GetPrompt(prompt, info);

            string paramsStr = ReplaceInteractionParams(_paramsMap["remix"], nonce, botType)
                .Replace("$message_id", messageId)
                .Replace("$custom_id", customId)
                .Replace("$modal", modal);

            var obj = JObject.Parse(paramsStr);
            obj["data"]["components"][0]["components"][0]["value"] = prompt;
            paramsStr = obj.ToString();

            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 执行 info 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> InfoAsync(string nonce, EBotType botType)
        {
            var content = botType == EBotType.MID_JOURNEY ? _paramsMap["info"] : _paramsMap["infoniji"];

            var paramsStr = ReplaceInteractionParams(content, nonce);
            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 执行 settings button 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="custom_id"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> SettingButtonAsync(string nonce, string custom_id, EBotType botType)
        {
            var paramsStr = ReplaceInteractionParams(_paramsMap["settingbutton"], nonce)
                .Replace("$custom_id", custom_id);

            if (botType == EBotType.NIJI_JOURNEY)
            {
                paramsStr = paramsStr
                    .Replace("$application_id", Constants.NIJI_APPLICATION_ID)
                    .Replace("$message_id", Account.NijiSettingsMessageId);
            }
            else if (botType == EBotType.MID_JOURNEY)
            {
                paramsStr = paramsStr
                    .Replace("$application_id", Constants.MJ_APPLICATION_ID)
                    .Replace("$message_id", Account.SettingsMessageId);
            }

            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// MJ 执行 settings select 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        public async Task<Message> SettingSelectAsync(string nonce, string values)
        {
            var paramsStr = ReplaceInteractionParams(_paramsMap["settingselect"], nonce)
              .Replace("$message_id", Account.SettingsMessageId)
              .Replace("$values", values);
            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 执行 setting 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> SettingAsync(string nonce, EBotType botType)
        {
            var content = botType == EBotType.NIJI_JOURNEY ? _paramsMap["settingniji"] : _paramsMap["setting"];

            var paramsStr = ReplaceInteractionParams(content, nonce);

            //var obj = JObject.Parse(paramsStr);
            //paramsStr = obj.ToString();

            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 根据 job id 显示任务信息
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> ShowAsync(string jobId, string nonce, EBotType botType)
        {
            var content = botType == EBotType.MID_JOURNEY ? _paramsMap["show"] : _paramsMap["showniji"];

            var paramsStr = ReplaceInteractionParams(content, nonce)
                .Replace("$value", jobId);

            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 获取 prompt 格式化
        /// </summary>
        /// <param name="prompt"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        public string GetPrompt(string prompt, TaskInfo info)
        {
            var acc = Account;

            if (string.IsNullOrWhiteSpace(prompt))
            {
                return prompt;
            }

            // 如果开启 niji 转 mj
            if (info.RealBotType == EBotType.MID_JOURNEY && info.BotType == EBotType.NIJI_JOURNEY)
            {
                if (!prompt.Contains("--niji"))
                {
                    prompt += " --niji";
                }
            }

            // 将 2 个空格替换为 1 个空格
            // 将 " -- " 替换为 " "
            prompt = prompt.Replace(" -- ", " ").Replace("  ", " ").Replace("  ", " ").Replace("  ", " ").Trim();

            // 使用正则替换超过 --- 3个 - 的替换为 " "
            prompt = Regex.Replace(prompt, @"-{3,}", " ");

            // 任务指定速度模式
            if (info != null && info.Mode != null)
            {
                prompt = prompt.AppendSpeedMode(info.Mode);
            }

            // 允许速度模式
            if (acc.AllowModes?.Count > 0)
            {
                // 计算不允许的速度模式，并删除相关参数
                var notAllowModes = new List<string>();
                if (!acc.AllowModes.Contains(GenerationSpeedMode.RELAX))
                {
                    notAllowModes.Add("--relax");
                }
                if (!acc.AllowModes.Contains(GenerationSpeedMode.FAST))
                {
                    notAllowModes.Add("--fast");
                }
                if (!acc.AllowModes.Contains(GenerationSpeedMode.TURBO))
                {
                    notAllowModes.Add("--turbo");
                }

                // 移除 prompt 可能的的参数
                foreach (var mode in notAllowModes)
                {
                    prompt = prompt.Replace(mode, "");
                }
            }

            // 如果快速模式用完了，且启用自动切换慢速
            if (acc.FastExhausted && acc.EnableAutoSetRelax == true)
            {
                prompt = prompt.AppendSpeedMode(GenerationSpeedMode.RELAX);
            }

            // 指定生成速度模式
            if (acc.Mode != null)
            {
                prompt = prompt.AppendSpeedMode(acc.Mode);
            }
            else
            {
                // 如果悠船账号, 开启慢速优先模式
                if (Account.IsYouChuan && Account.YouChuanEnablePreferRelax
                    && info.Mode != GenerationSpeedMode.RELAX
                    && info.Action != TaskAction.UPSCALE
                    && info.Action != TaskAction.DESCRIBE
                    && info.Action != TaskAction.VIDEO)
                {
                    // 如果有慢速和快速，且前台允许快速和慢速
                    if (acc.AllowModes == null || (acc.AllowModes.Contains(GenerationSpeedMode.FAST) && acc.AllowModes.Contains(GenerationSpeedMode.RELAX)))
                    {
                        if (IsYouChuanAllowRelax())
                        {
                            prompt = prompt.AppendSpeedMode(GenerationSpeedMode.RELAX);
                        }
                    }
                }

                // 仅适用于官方账号
                // 未指定速度
                // 如果用户开启了清除提示词，且前台没有请求速度，则清除速度
                if (Account.IsOfficial && GlobalConfiguration.Setting.PrivateRemoveRequestSpeedMode && info.RequestMode == null)
                {
                    prompt = prompt.RemoveSpeedMode();
                }
            }

            // 草稿处理
            if (Account.IsDraft && !prompt.Contains("--draft", StringComparison.OrdinalIgnoreCase))
            {
                prompt += " --draft";
            }

            //// 处理转义字符引号等
            //return prompt.Replace("\\\"", "\"").Replace("\\'", "'").Replace("\\\\", "\\");

            prompt = FormatUrls(prompt, info).ConfigureAwait(false).GetAwaiter().GetResult();

            return prompt;
        }

        /// <summary>
        /// 对 prompt 中含有 url 的进行转换为官方 url 处理
        /// 同一个 url 1 小时内有效缓存
        /// </summary>
        /// <param name="prompt"></param>
        /// <returns></returns>
        public async Task<string> FormatUrls(string prompt, TaskInfo info)
        {
            var setting = GlobalConfiguration.Setting;
            if (!setting.EnableConvertOfficialLink)
            {
                return prompt;
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                return prompt;
            }

            // 使用正则提取所有的 url
            var urls = Regex.Matches(prompt, @"(https?|ftp|file)://[-A-Za-z0-9+&@#/%?=~_|!:,.;]+[-A-Za-z0-9+&@#/%=~_|]")
                .Select(c => c.Value).Distinct().ToList();

            if (urls?.Count > 0)
            {
                var urlDic = new Dictionary<string, string>();
                foreach (var url in urls)
                {
                    try
                    {
                        // 如果是悠船任务，并且链接包含悠船，则不处理
                        if (info.IsPartner)
                        {
                            if (url.Contains("youchuan"))
                            {
                                continue;
                            }

                            // 未启用链接转换
                            if (!setting.EnableYouChuanPromptLink)
                            {
                                continue;
                            }
                        }

                        // url 缓存默认 24 小时有效
                        var okUrl = await AdaptiveCache.GetOrCreateAsync($"fetch:{url}", async () =>
                        {
                            var ff = new FileFetchHelper();
                            var res = await ff.FetchFileAsync(url);
                            if (res.Success && !string.IsNullOrWhiteSpace(res.Url))
                            {
                                return res.Url;
                            }
                            else if (res.Success && res.FileBytes.Length > 0)
                            {
                                if (info.IsPartner)
                                {
                                    // 悠船链接转换
                                    var youchuanUrl = await _ymTaskService.UploadFile(info, res.FileBytes, res.FileName);
                                    if (!string.IsNullOrWhiteSpace(youchuanUrl))
                                    {
                                        return youchuanUrl;
                                    }
                                    else
                                    {
                                        throw new LogicException(ReturnCode.FAILURE, "悠船链接转换失败");
                                    }
                                }
                                else
                                {
                                    // 上传到 Discord 服务器
                                    var uploadResult = await UploadAsync(res.FileName, new DataUrl(res.ContentType, res.FileBytes));
                                    if (uploadResult.Code != ReturnCode.SUCCESS)
                                    {
                                        throw new LogicException(uploadResult.Code, uploadResult.Description);
                                    }

                                    if (uploadResult.Description.StartsWith("http"))
                                    {
                                        return uploadResult.Description;
                                    }
                                    else
                                    {
                                        var finalFileName = uploadResult.Description;
                                        var sendImageResult = await SendImageMessageAsync("upload image: " + finalFileName, finalFileName);
                                        if (sendImageResult.Code != ReturnCode.SUCCESS)
                                        {
                                            throw new LogicException(sendImageResult.Code, sendImageResult.Description);
                                        }

                                        return sendImageResult.Description;
                                    }
                                }
                            }

                            throw new LogicException($"解析链接失败 {url}, {res?.Msg}");
                        }, TimeSpan.FromDays(1));

                        urlDic[url] = okUrl;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "解析 url 异常 {0}", url);

                        // 转换失败跳过
                    }
                }

                // 替换 url
                foreach (var item in urlDic)
                {
                    prompt = prompt.Replace(item.Key, item.Value);
                }
            }

            return prompt;
        }

        /// <summary>
        /// 局部重绘
        /// </summary>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="maskBase64"></param>
        /// <returns></returns>
        public async Task<Message> InpaintAsync(TaskInfo info, string customId, string prompt, string maskBase64)
        {
            try
            {
                prompt = GetPrompt(prompt, info);

                customId = customId?.Replace("MJ::iframe::", "");

                // mask.replace(/^data:.+?;base64,/, ''),
                maskBase64 = maskBase64?.Replace("data:image/png;base64,", "");

                var obj = new
                {
                    customId = customId,
                    //full_prompt = null,
                    mask = maskBase64,
                    prompt = prompt,
                    userId = "0",
                    username = "0",
                };
                var paramsStr = Newtonsoft.Json.JsonConvert.SerializeObject(obj);

                // NIJI 也是这个链接
                var response = await PostJsonAsync("https://936929561302675456.discordsays.com/inpaint/api/submit-job",
                    paramsStr);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return Message.Success();
                }

                return Message.Of((int)response.StatusCode, "提交失败");
            }
            catch (HttpRequestException e)
            {
                _logger.Error(e, "局部重绘请求执行异常 {@0}", info);

                return Message.Of(ReturnCode.FAILURE, e.Message?.Substring(0, Math.Min(e.Message.Length, 100)) ?? "未知错误");
            }
        }

        /// <summary>
        /// 重新生成
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="messageHash"></param>
        /// <param name="messageFlags"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> RerollAsync(string messageId, string messageHash, int messageFlags, string nonce, EBotType botType)
        {
            string paramsStr = ReplaceInteractionParams(_paramsMap["reroll"], nonce, botType)
                .Replace("$message_id", messageId)
                .Replace("$message_hash", messageHash);
            var obj = JObject.Parse(paramsStr);

            if (obj.ContainsKey("message_flags"))
            {
                obj["message_flags"] = messageFlags;
            }
            else
            {
                obj.Add("message_flags", messageFlags);
            }

            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 解析描述
        /// </summary>
        /// <param name="finalFileName"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> DescribeAsync(string finalFileName, string nonce, EBotType botType)
        {
            string fileName = finalFileName.Substring(finalFileName.LastIndexOf("/") + 1);

            var json = botType == EBotType.NIJI_JOURNEY ? _paramsMap["describeniji"] : _paramsMap["describe"];
            string paramsStr = ReplaceInteractionParams(json, nonce)
                .Replace("$file_name", fileName)
                .Replace("$final_file_name", finalFileName);
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 解析描述
        /// </summary>
        /// <param name="link"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> DescribeByLinkAsync(string link, string nonce, EBotType botType)
        {
            var json = botType == EBotType.NIJI_JOURNEY ? _paramsMap["describenijilink"] : _paramsMap["describelink"];
            string paramsStr = ReplaceInteractionParams(json, nonce)
                .Replace("$link", link);
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 上传一个较长的提示词，mj 可以返回一组简要的提示词
        /// </summary>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> ShortenAsync(TaskInfo info, string prompt, string nonce, EBotType botType)
        {
            prompt = GetPrompt(prompt, info);

            var json = botType == EBotType.MID_JOURNEY || prompt.Contains("--niji") ? _paramsMap["shorten"] : _paramsMap["shortenniji"];
            var paramsStr = ReplaceInteractionParams(json, nonce);

            var obj = JObject.Parse(paramsStr);
            obj["data"]["options"][0]["value"] = prompt;
            paramsStr = obj.ToString();

            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 合成
        /// </summary>
        /// <param name="finalFileNames"></param>
        /// <param name="dimensions"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> BlendAsync(List<string> finalFileNames, BlendDimensions dimensions, string nonce, EBotType botType)
        {
            var json = botType == EBotType.MID_JOURNEY || GlobalConfiguration.Setting.EnableConvertNijiToMj ? _paramsMap["blend"] : _paramsMap["blendniji"];

            string paramsStr = ReplaceInteractionParams(json, nonce);
            JObject paramsJson = JObject.Parse(paramsStr);
            JArray options = (JArray)paramsJson["data"]["options"];
            JArray attachments = (JArray)paramsJson["data"]["attachments"];
            for (int i = 0; i < finalFileNames.Count; i++)
            {
                string finalFileName = finalFileNames[i];
                string fileName = finalFileName.Substring(finalFileName.LastIndexOf("/") + 1);
                JObject attachment = new JObject
                {
                    ["id"] = i.ToString(),
                    ["filename"] = fileName,
                    ["uploaded_filename"] = finalFileName
                };
                attachments.Add(attachment);
                JObject option = new JObject
                {
                    ["type"] = 11,
                    ["name"] = $"image{i + 1}",
                    ["value"] = i
                };
                options.Add(option);
            }
            options.Add(new JObject
            {
                ["type"] = 3,
                ["name"] = "dimensions",
                ["value"] = $"--ar {dimensions.GetValue()}"
            });
            return await PostJsonAndCheckStatusAsync(paramsJson.ToString());
        }

        private string ReplaceInteractionParams(string paramsStr, string nonce,
            string guid = null, string channelId = null)
        {
            return paramsStr.Replace("$guild_id", guid ?? Account.GuildId)
                .Replace("$channel_id", channelId ?? Account.ChannelId)
                .Replace("$session_id", DefaultSessionId)
                .Replace("$nonce", nonce);
        }

        private string ReplaceInteractionParams(string paramsStr, string nonce, EBotType botType,
            string guid = null, string channelId = null)
        {
            var str = ReplaceInteractionParams(paramsStr, nonce, guid, channelId);

            if (botType == EBotType.MID_JOURNEY)
            {
                str = str.Replace("$application_id", Constants.MJ_APPLICATION_ID);
            }
            else if (botType == EBotType.NIJI_JOURNEY)
            {
                str = str.Replace("$application_id", Constants.NIJI_APPLICATION_ID);
            }

            return str;
        }

        /// <summary>
        /// 上传文件到 Discord 或 文件存储
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="dataUrl"></param>
        /// <param name="useDiscordUpload"></param>
        /// <returns></returns>
        public async Task<Message> UploadAsync(string fileName, DataUrl dataUrl, bool useDiscordUpload = false)
        {
            // 保存用户上传的 base64 到文件存储
            if (GlobalConfiguration.Setting.EnableSaveUserUploadBase64 && !useDiscordUpload)
            {
                try
                {
                    var localPath = $"attachments/{DateTime.Now:yyyyMMdd}/{fileName}";

                    var mt = MimeKit.MimeTypes.GetMimeType(Path.GetFileName(localPath));
                    if (string.IsNullOrWhiteSpace(mt))
                    {
                        mt = "image/png";
                    }

                    var stream = new MemoryStream(dataUrl.Data);
                    var res = StorageHelper.Instance?.SaveAsync(stream, localPath, dataUrl.MimeType ?? mt);
                    if (string.IsNullOrWhiteSpace(res?.Url))
                    {
                        throw new Exception("上传图片到加速站点失败");
                    }

                    var url = res.Url;

                    return Message.Success(url);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "上传图片到加速站点异常");

                    return Message.Of(ReturnCode.FAILURE, "上传图片到加速站点异常");
                }
            }
            else
            {
                try
                {
                    JObject fileObj = new JObject
                    {
                        ["filename"] = fileName,
                        ["file_size"] = dataUrl.Data.Length,
                        ["id"] = "0"
                    };
                    JObject paramsJson = new JObject
                    {
                        ["files"] = new JArray { fileObj }
                    };
                    HttpResponseMessage response = await PostJsonAsync(_discordAttachmentUrl, paramsJson.ToString());
                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        _logger.Error("上传图片到discord失败, status: {StatusCode}, msg: {Body}", response.StatusCode, await response.Content.ReadAsStringAsync());
                        return Message.Of(ReturnCode.VALIDATION_ERROR, "上传图片到discord失败");
                    }
                    JArray array = JObject.Parse(await response.Content.ReadAsStringAsync())["attachments"] as JArray;
                    if (array == null || array.Count == 0)
                    {
                        return Message.Of(ReturnCode.VALIDATION_ERROR, "上传图片到discord失败");
                    }
                    string uploadUrl = array[0]["upload_url"].ToString();
                    string uploadFilename = array[0]["upload_filename"].ToString();

                    await PutFileAsync(uploadUrl, dataUrl);

                    return Message.Success(uploadFilename);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "上传图片到discord失败");

                    return Message.Of(ReturnCode.FAILURE, "上传图片到discord失败");
                }
            }
        }

        public async Task<Message> SendImageMessageAsync(string content, string finalFileName)
        {
            string fileName = finalFileName.Substring(finalFileName.LastIndexOf("/") + 1);
            string paramsStr = _paramsMap["message"]
                .Replace("$content", content)
                .Replace("$channel_id", Account.ChannelId)
                .Replace("$file_name", fileName)
                .Replace("$final_file_name", finalFileName);
            HttpResponseMessage response = await PostJsonAsync(_discordMessageUrl, paramsStr);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                _logger.Error("发送图片消息到discord失败, status: {StatusCode}, msg: {Body}", response.StatusCode, await response.Content.ReadAsStringAsync());
                return Message.Of(ReturnCode.VALIDATION_ERROR, "发送图片消息到discord失败");
            }
            JObject result = JObject.Parse(await response.Content.ReadAsStringAsync());
            JArray attachments = result["attachments"] as JArray;
            if (attachments != null && attachments.Count > 0)
            {
                return Message.Success(attachments[0]["url"].ToString());
            }
            return Message.Failure("发送图片消息到discord失败: 图片不存在");
        }

        /// <summary>
        /// 自动读 discord 最后一条消息（设置为已读）
        /// </summary>
        /// <param name="lastMessageId"></param>
        /// <returns></returns>
        public async Task<Message> ReadMessageAsync(string lastMessageId)
        {
            if (string.IsNullOrWhiteSpace(lastMessageId))
            {
                return Message.Of(ReturnCode.VALIDATION_ERROR, "lastMessageId 不能为空");
            }

            var paramsStr = @"{""token"":null,""last_viewed"":3496}";
            var url = $"{_discordMessageUrl}/{lastMessageId}/ack";

            HttpResponseMessage response = await PostJsonAsync(url, paramsStr);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                _logger.Error("自动读discord消息失败, status: {StatusCode}, msg: {Body}", response.StatusCode, await response.Content.ReadAsStringAsync());
                return Message.Of(ReturnCode.VALIDATION_ERROR, "自动读discord消息失败");
            }
            return Message.Success();
        }

        private async Task PutFileAsync(string uploadUrl, DataUrl dataUrl)
        {
            uploadUrl = _discordHelper.GetDiscordUploadUrl(uploadUrl);
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
            {
                Content = new ByteArrayContent(dataUrl.Data)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(dataUrl.MimeType);
            request.Content.Headers.ContentLength = dataUrl.Data.Length;
            request.Headers.UserAgent.ParseAdd(Account.UserAgent);
            await _httpClient.SendAsync(request);
        }

        private async Task<HttpResponseMessage> PostJsonAsync(string url, string paramsStr)
        {
            //if (!JwtTokenValidate(Account.UserToken))
            //{
            //    throw new LogicException(ReturnCode.VALIDATION_ERROR, "令牌错误");
            //}

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(paramsStr, Encoding.UTF8, "application/json")
            };

            request.Headers.UserAgent.ParseAdd(Account.UserAgent);

            // 设置 request Authorization 为 UserToken，不需要 Bearer 前缀
            request.Headers.Add("Authorization", Account.UserToken);

            return await _httpClient.SendAsync(request);
        }

        private async Task<Message> PostJsonAndCheckStatusAsync(string paramsStr)
        {
            // 如果 TooManyRequests 请求失败，则重拾最多 3 次
            var count = 5;

            // 已处理的 message id
            var messageIds = new List<string>();
            do
            {
                HttpResponseMessage response = null;
                try
                {
                    response = await PostJsonAsync(_discordInteractionUrl, paramsStr);
                    if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                    {
                        return Message.Success();
                    }
                    else if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        count--;
                        if (count > 0)
                        {
                            // 等待 3~6 秒
                            var random = new Random();
                            var seconds = random.Next(3, 6);
                            await Task.Delay(seconds * 1000);

                            _logger.Warning("Http 请求执行频繁，等待重试 {@0}, {@1}, {@2}", paramsStr, response.StatusCode, response.Content);
                            continue;
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        count--;

                        if (count > 0)
                        {
                            // 等待 3~6 秒
                            var random = new Random();
                            var seconds = random.Next(3, 6);
                            await Task.Delay(seconds * 1000);

                            // 当是 NotFound 时
                            // 可能是 message id 错乱导致
                            if (paramsStr.Contains("message_id") && paramsStr.Contains("nonce"))
                            {
                                var obj = JObject.Parse(paramsStr);
                                if (obj.ContainsKey("message_id") && obj.ContainsKey("nonce"))
                                {
                                    var nonce = obj["nonce"].ToString();
                                    var message_id = obj["message_id"].ToString();
                                    if (!string.IsNullOrEmpty(nonce) && !string.IsNullOrWhiteSpace(message_id))
                                    {
                                        messageIds.Add(message_id);

                                        var t = GetRunningTaskByNonce(nonce);
                                        if (t != null && !string.IsNullOrWhiteSpace(t.ParentId))
                                        {
                                            var p = GetTask(t.ParentId);
                                            if (p != null)
                                            {
                                                var newMessageId = p.MessageIds.Where(c => !messageIds.Contains(c)).FirstOrDefault();
                                                if (!string.IsNullOrWhiteSpace(newMessageId))
                                                {
                                                    obj["message_id"] = newMessageId;

                                                    var oldStr = paramsStr;
                                                    paramsStr = obj.ToString();

                                                    _logger.Warning("Http 可能消息错乱，等待重试 {@0}, {@1}, {@2}, {@3}", oldStr, paramsStr, response.StatusCode, response.Content);
                                                    continue;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    _logger.Error("Http 请求执行失败 {@0}, {@1}, {@2}", paramsStr, response.StatusCode, response.Content);

                    var error = $"{response.StatusCode}: {paramsStr.Substring(0, Math.Min(paramsStr.Length, 1000))}";

                    // 如果是 403 则直接禁用账号
                    if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        _logger.Error("Http 请求没有操作权限，禁用账号 {@0}", paramsStr);

                        Account.Enable = false;
                        Account.DisabledReason = "Http 请求没有操作权限，禁用账号";
                        _freeSql.Update(Account);
                        Account.ClearCache();

                        return Message.Of(ReturnCode.FAILURE, "请求失败，禁用账号");
                    }

                    return Message.Of((int)response.StatusCode, error);
                }
                catch (HttpRequestException e)
                {
                    _logger.Error(e, "Http 请求执行异常 {@0}", paramsStr);

                    return Message.Of(ReturnCode.FAILURE, e.Message?.Substring(0, Math.Min(e.Message.Length, 100)) ?? "未知错误");
                }
            } while (true);
        }

        /// <summary>
        /// 全局切换 fast 模式
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> FastAsync(string nonce, EBotType botType)
        {
            var acc = Account;

            if (botType == EBotType.NIJI_JOURNEY && acc.EnableNiji != true)
            {
                return Message.Success("忽略提交，未开启 niji");
            }

            if (botType == EBotType.MID_JOURNEY && acc.EnableMj != true)
            {
                return Message.Success("忽略提交，未开启 mj");
            }

            var json = botType == EBotType.MID_JOURNEY ? _paramsMap["fast"] : _paramsMap["fastniji"];
            var paramsStr = ReplaceInteractionParams(json, nonce);
            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 全局切换 relax 模式
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task<Message> RelaxAsync(string nonce, EBotType botType)
        {
            if (botType == EBotType.NIJI_JOURNEY && Account.EnableNiji != true)
            {
                return Message.Success("忽略提交，未开启 niji");
            }

            if (botType == EBotType.MID_JOURNEY && Account.EnableMj != true)
            {
                return Message.Success("忽略提交，未开启 mj");
            }

            var json = botType == EBotType.NIJI_JOURNEY ? _paramsMap["relax"] : _paramsMap["relaxniji"];
            var paramsStr = ReplaceInteractionParams(json, nonce);
            var obj = JObject.Parse(paramsStr);
            paramsStr = obj.ToString();
            return await PostJsonAndCheckStatusAsync(paramsStr);
        }

        /// <summary>
        /// 自动同步 Info Setting
        /// </summary>
        /// <param name="isClearCache"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        public async Task<bool> SyncInfoSetting(bool isClearCache = false)
        {
            try
            {
                var acc = Account;

                await using var lockHandle = await AdaptiveLock.LockAsync(acc.InfoLockKey, 30);
                if (!lockHandle.IsAcquired)
                {
                    Log.Warning("同步信息被锁定，跳过执行，ChannelId={0}", acc.ChannelId);
                    return false;
                }

                var success = false;

                // 悠船立即同步，不缓存
                if (acc.IsYouChuan)
                {
                    var sw = Stopwatch.StartNew();
                    success = await YmTaskService.SyncYouchuanInfo();
                    sw.Stop();
                    if (success)
                    {
                        // 计算快速可用次数
                        var fastAvailableCount = (int)Math.Ceiling(Account.YouChuanFastRemaining / 60D);

                        CounterHelper.SetFastTaskAvailableCount(Account.ChannelId, fastAvailableCount);

                        // 计算慢速总数
                        var count = CounterHelper.GetYouchuanRelaxCount(acc.ChannelId);
                        var youchuanRelaxAvailableCount = Account.YouChuanRelaxedReset > DateTime.Now.Date ? 0 : Math.Max(0, Account.YouChuanRelaxDailyLimit - count);

                        _logger.Information("悠船同步信息完成，ChannelId={@0}, 预估快速剩余次数={@1}, 预估慢速剩余次数={@2}, 用时={@3}ms",
                            ChannelId, fastAvailableCount, youchuanRelaxAvailableCount, sw.ElapsedMilliseconds);
                    }

                    return success;
                }

                var cacheKey = $"info_setting_sync_cache:{acc.ChannelId}";
                var cacheValue = AdaptiveCache.Get<bool?>(cacheKey);

                // 不清理缓存，且有缓存时，直接返回成功
                if (!isClearCache && cacheValue == true)
                {
                    return true;
                }

                // 清理缓存，或缓存不存在时，执行同步频率验证
                if (isClearCache || cacheValue != true)
                {
                    AdaptiveCache.Remove(cacheKey);

                    // 只有强制同步时才需要添加限制规则
                    // 最新规则：
                    // 每 1 分钟最多同步 1 次
                    // 每 5 分钟最多同步 1 次
                    // 每 10 分钟最多同步 2 次
                    // 每 30 分钟最多同步 3 次
                    // 每 60 分钟最多同步 6 次

                    var keyPrefix = $"syncinfo_limit_{DateTime.Now:yyyyMMdd}:";

                    if (!RateLimiter.Check(keyPrefix, acc.ChannelId, 1, 1) ||
                        !RateLimiter.Check(keyPrefix, acc.ChannelId, 5, 1) ||
                        !RateLimiter.Check(keyPrefix, acc.ChannelId, 10, 2) ||
                        !RateLimiter.Check(keyPrefix, acc.ChannelId, 30, 3) ||
                        !RateLimiter.Check(keyPrefix, acc.ChannelId, 60, 6))
                    {
                        Log.Warning("同步信息调用过于频繁，ChannelId={0}", acc.ChannelId);
                        return false;
                    }
                }

                if (acc.IsOfficial)
                {
                    // 官方 60-180 分钟
                    var cacheMinutes = Random.Shared.Next(60, 180);
                    success = await AdaptiveCache.GetOrCreateAsync(cacheKey, async () =>
                    {
                        var sw = Stopwatch.StartNew();
                        var ok = await YmTaskService.SyncOfficialInfo();
                        sw.Stop();
                        if (!ok)
                        {
                            throw new LogicException("同步官方信息失败");
                        }

                        // 计算快速可用次数
                        var fastAvailableCount = (int)Math.Ceiling(Account.OfficialFastRemaining / 60D);
                        CounterHelper.SetFastTaskAvailableCount(Account.ChannelId, fastAvailableCount);

                        _logger.Information("官方同步信息完成，ChannelId={@0}, 预估快速剩余次数={@1}, 用时={@2}ms",
                            ChannelId, fastAvailableCount, sw.ElapsedMilliseconds);

                        return ok;
                    }, TimeSpan.FromMinutes(cacheMinutes));
                }

                if (acc.IsDiscord)
                {
                    // discord 60-180 分钟
                    var cacheMinutes = Random.Shared.Next(60, 180);
                    success = await AdaptiveCache.GetOrCreateAsync(cacheKey, async () =>
                    {
                        var sw = Stopwatch.StartNew();
                        if (Account.EnableMj == true)
                        {
                            try
                            {
                                var settingRes = await SettingAsync(SnowFlake.NextId(), EBotType.MID_JOURNEY);
                                if (settingRes.Code != ReturnCode.SUCCESS)
                                {
                                    throw new LogicException(settingRes.Description);
                                }
                                Thread.Sleep(2500);

                                var infoRes = await InfoAsync(SnowFlake.NextId(), EBotType.MID_JOURNEY);
                                if (infoRes.Code != ReturnCode.SUCCESS)
                                {
                                    throw new LogicException(infoRes.Description);
                                }
                                Thread.Sleep(2500);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "同步 MJ 信息异常，ChannelId={@0}", ChannelId);
                                throw;
                            }
                        }

                        if (Account.EnableNiji == true)
                        {
                            try
                            {
                                // 如果没有开启 NIJI 转 MJ
                                if (GlobalConfiguration.Setting.EnableConvertNijiToMj == false)
                                {
                                    var settingRes = await SettingAsync(SnowFlake.NextId(), EBotType.NIJI_JOURNEY);
                                    if (settingRes.Code != ReturnCode.SUCCESS)
                                    {
                                        throw new LogicException(settingRes.Description);
                                    }
                                    Thread.Sleep(2500);

                                    var infoRes = await InfoAsync(SnowFlake.NextId(), EBotType.NIJI_JOURNEY);
                                    if (infoRes.Code != ReturnCode.SUCCESS)
                                    {
                                        throw new LogicException(infoRes.Description);
                                    }
                                    Thread.Sleep(2500);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "同步 Niji 信息异常，ChannelId={@0}", ChannelId);
                                throw;
                            }
                        }
                        sw.Stop();

                        // 快速时长校验
                        // 如果 fastTime <= 0.2，则标记为快速用完
                        var fastTime = Account.FastTimeRemaining?.ToString()?.Split('/')?.FirstOrDefault()?.Trim();

                        // 0.2h = 12 分钟 = 12 次
                        var ftime = 0.0;
                        if (!string.IsNullOrWhiteSpace(fastTime) && double.TryParse(fastTime, out ftime) && ftime <= 0.2)
                        {
                            Account.FastExhausted = true;
                        }
                        else if (ftime > 0.5)
                        {
                            Account.FastExhausted = false;
                        }

                        // 自动设置慢速，如果快速用完
                        if (Account.FastExhausted == true && Account.EnableAutoSetRelax == true)
                        {
                            Account.AllowModes = [GenerationSpeedMode.RELAX];
                            if (Account.CoreSize > 3)
                            {
                                Account.CoreSize = 3;
                            }
                        }

                        // 计算快速可用次数
                        var fastAvailableCount = (int)Math.Ceiling(ftime * 60);

                        CounterHelper.SetFastTaskAvailableCount(Account.ChannelId, fastAvailableCount);

                        _freeSql.Update("FastExhausted,AllowModes,CoreSize", acc);

                        acc.ClearCache();

                        _logger.Information("Discord 同步信息完成，ChannelId={@0}, 预估快速剩余次数={@1}, 用时={@2}ms",
                            ChannelId, fastAvailableCount, sw.ElapsedMilliseconds);

                        return true;
                    }, TimeSpan.FromMinutes(cacheMinutes));
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "同步账号信息异常 {@0}", ChannelId);
            }

            return false;
        }
    }

    /// <summary>
    /// 速率限制器
    /// </summary>
    public static class RateLimiter
    {
        /// <summary>
        /// 检查速率限制是否通过（通过则自动+1）
        /// </summary>
        /// <param name="prefix">键前缀</param>
        /// <param name="identifier">标识符（如 ChannelId）</param>
        /// <param name="windowMinutes">时间窗口（分钟）</param>
        /// <param name="maxCount">最大允许次数</param>
        /// <returns>true=允许执行，false=已超限</returns>
        public static bool Check(string prefix, string identifier, int windowMinutes, int maxCount)
        {
            var key = $"{prefix}:{identifier}:{windowMinutes}m:{DateTime.Now.Ticks / TimeSpan.TicksPerMinute / windowMinutes}";
            var count = AdaptiveCache.GetCounter(key);
            if (count >= maxCount)
            {
                return false;
            }
            AdaptiveCache.Increment(key, 1, TimeSpan.FromMinutes(windowMinutes + 1));
            return true;
        }

        /// <summary>
        /// 异步检查速率限制是否通过
        /// </summary>
        public static async Task<bool> CheckAsync(string prefix, string identifier, int windowMinutes, int maxCount)
        {
            var key = $"{prefix}:{identifier}:{windowMinutes}m:{DateTime.Now.Ticks / TimeSpan.TicksPerMinute / windowMinutes}";
            var count = await AdaptiveCache.GetCounterAsync(key);
            if (count >= maxCount)
            {
                return false;
            }
            await AdaptiveCache.IncrementAsync(key, 1, TimeSpan.FromMinutes(windowMinutes + 1));
            return true;
        }
    }

    /// <summary>
    /// 任务信息队列
    /// </summary>
    public class TaskInfoQueue
    {
        /// <summary>
        /// 任务信息
        /// </summary>
        public TaskInfo Info { get; set; }

        /// <summary>
        /// 请求功能
        /// </summary>
        public TaskInfoQueueFunction Function { get; set; }

        /// <summary>
        /// Action 操作请求参数
        /// </summary>
        public TaskInfoQueueActionParam ActionParam { get; set; }

        /// <summary>
        /// Action 操作请求参数
        /// </summary>
        public class TaskInfoQueueActionParam
        {
            /// <summary>
            /// 消息 ID
            /// </summary>
            public string MessageId { get; set; }

            /// <summary>
            /// 自定义
            /// </summary>
            public string CustomId { get; set; }

            /// <summary>
            ///
            /// </summary>
            public int MessageFlags { get; set; }

            /// <summary>
            ///
            /// </summary>
            public string Nonce { get; set; }

            /// <summary>
            /// 提交动作 DTO
            /// </summary>
            public SubmitActionDTO Dto { get; set; }

            /// <summary>
            /// 父级任务
            /// </summary>
            public TaskInfo TargetTask { get; set; }
        }

        /// <summary>
        /// Modal 操作请求参数
        /// </summary>
        public TaskInfoQueueModalParam ModalParam { get; set; }

        /// <summary>
        /// Modal 操作请求参数
        /// </summary>
        public class TaskInfoQueueModalParam
        {
            /// <summary>
            /// 自定义
            /// </summary>
            public string CustomId { get; set; }

            /// <summary>
            ///
            /// </summary>
            public int MessageFlags { get; set; }

            /// <summary>
            ///
            /// </summary>
            public string Nonce { get; set; }

            /// <summary>
            /// 提交动作 DTO
            /// </summary>
            public SubmitModalDTO Dto { get; set; }

            /// <summary>
            /// 父级任务
            /// </summary>
            public TaskInfo TargetTask { get; set; }
        }

        /// <summary>
        /// 自定义变焦任务 请求参数
        /// </summary>
        public TaskInfoQueueZoomParam ZoomParam { get; set; }

        /// <summary>
        /// 自定义变焦任务 请求参数
        /// </summary>
        public class TaskInfoQueueZoomParam
        {
            /// <summary>
            /// 自定义
            /// </summary>
            public string CustomId { get; set; }

            /// <summary>
            ///
            /// </summary>
            public string Nonce { get; set; }

            /// <summary>
            /// 模态消息 ID
            /// </summary>
            public string ModalMessageId { get; set; }
        }

        /// <summary>
        /// 局部重绘任务 请求参数
        /// </summary>
        public TaskInfoQueueInpaintParam InpaintParam { get; set; }

        /// <summary>
        /// 局部重绘任务 请求参数
        /// </summary>
        public class TaskInfoQueueInpaintParam
        {
            public string ModalCreateCustomId { get; set; }
            public string MaskBase64 { get; set; }
        }

        /// <summary>
        /// 图生文 -> 文生图 请求参数
        /// </summary>
        public TaskInfoQueuePicReaderParam PicReaderParam { get; set; }

        /// <summary>
        /// 图生文 -> 文生图 请求参数
        /// </summary>
        public class TaskInfoQueuePicReaderParam
        {
            /// <summary>
            /// 自定义
            /// </summary>
            public string CustomId { get; set; }

            /// <summary>
            ///
            /// </summary>
            public string Nonce { get; set; }

            public string ModalMessageId { get; set; }

            public EBotType BotType { get; set; }
        }

        /// <summary>
        /// 提示词解析器 请求参数
        /// </summary>
        public TaskInfoQueuePromptAnalyzerParam PromptAnalyzerParam { get; set; }

        /// <summary>
        /// 混合任务 请求参数
        /// </summary>
        public TaskInfoQueueRemixParam RemixParam { get; set; }

        /// <summary>
        /// 混图合成任务 请求参数
        /// </summary>

        public TaskInfoQueueBlendParam BlendParam { get; set; }

        /// <summary>
        /// 混图合成任务 请求参数
        /// </summary>
        public class TaskInfoQueueBlendParam
        {
            public List<string> FinalFileNames { get; set; } = [];

            public BlendDimensions Dimensions { get; set; }
        }

        /// <summary>
        /// 提示词解析器 请求参数
        /// </summary>
        public class TaskInfoQueuePromptAnalyzerParam
        {
            /// <summary>
            /// 自定义
            /// </summary>
            public string CustomId { get; set; }

            /// <summary>
            ///
            /// </summary>
            public string Nonce { get; set; }

            public EBotType BotType { get; set; }
            public string ModalMessageId { get; set; }
            public string Modal { get; set; }
        }

        /// <summary>
        /// 混合任务 请求参数
        /// </summary>
        public class TaskInfoQueueRemixParam
        {
            public TaskAction Action { get; set; }
            public string ModalMessageId { get; set; }
            public string Modal { get; set; }
            public string CustomId { get; set; }
            public string Nonce { get; set; }
            public EBotType BotType { get; set; }
        }
    }

    /// <summary>
    /// 提交请求功能
    /// </summary>
    public enum TaskInfoQueueFunction
    {
        /// <summary>
        /// 提交任务
        /// </summary>
        SUBMIT = 0,

        /// <summary>
        /// 变化任务
        /// </summary>
        ACTION = 1,

        /// <summary>
        /// 图生文任务
        /// </summary>
        DESCRIBE = 2,

        /// <summary>
        /// 混图合成任务
        /// </summary>
        BLEND = 3,

        /// <summary>
        /// 弹窗任务
        /// </summary>
        MODAL = 4,

        /// <summary>
        /// 刷新任务
        /// </summary>
        REFRESH = 5,

        ///// <summary>
        ///// 自定义变焦任务 - 仅Discord
        ///// </summary>
        //ZOOM,

        ///// <summary>
        ///// 局部重绘任务 - 仅Discord
        ///// </summary>
        //INPAINT,

        ///// <summary>
        ///// 图生文 -> 文生图 - 仅Discord
        ///// </summary>
        //PIC_READER,

        ///// <summary>
        ///// 提示词解析器 - 仅Discord
        ///// </summary>
        //PROMPT_ANALYZER,

        ///// <summary>
        ///// 混合任务 - 仅Discord
        ///// </summary>
        //REMIX,

        /// <summary>
        /// 提示词简化器 - 仅Discord
        /// </summary>
        SHORTEN = 10,

        /// <summary>
        /// 高级编辑 - 仅悠船/官网
        /// </summary>
        EDIT = 11,

        /// <summary>
        /// 高级转绘 - 仅悠船/官网
        /// </summary>
        RETEXTURE = 12,

        /// <summary>
        /// 视频生成 - 仅悠船/官网
        /// </summary>
        VIDEO = 13
    }
}
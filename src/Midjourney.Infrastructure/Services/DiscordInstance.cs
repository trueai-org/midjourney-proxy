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

using Microsoft.Extensions.Caching.Memory;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.Storage;
using Midjourney.Infrastructure.Util;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

using ILogger = Serilog.ILogger;

namespace Midjourney.Infrastructure.LoadBalancer
{
    /// <summary>
    /// Discord 实例
    /// 实现了IDiscordInstance接口，负责处理Discord相关的任务管理和执行。
    /// </summary>
    public class DiscordInstance
    {

        private readonly object _lockAccount = new object();

        private readonly ILogger _logger = Log.Logger;

        private readonly ITaskStoreService _taskStoreService;
        private readonly INotifyService _notifyService;

        private readonly List<TaskInfo> _runningTasks = [];
        private readonly ConcurrentDictionary<string, Task> _taskFutureMap = [];
        private readonly AsyncParallelLock _semaphoreSlimLock;

        private readonly Task _longTask;
        private readonly Task _longTaskCache;

        private readonly CancellationTokenSource _longToken;
        private readonly ManualResetEvent _mre; // 信号

        private readonly HttpClient _httpClient;
        private readonly DiscordHelper _discordHelper;
        private readonly Dictionary<string, string> _paramsMap;

        private readonly string _discordInteractionUrl;
        private readonly string _discordAttachmentUrl;
        private readonly string _discordMessageUrl;
        private readonly IMemoryCache _cache;
        private readonly ITaskService _taskService;

        /// <summary>
        /// 当前队列任务
        /// </summary>
        private ConcurrentQueue<(TaskInfo, Func<Task<Message>>)> _queueTasks = [];

        private DiscordAccount _account;

        public DiscordInstance(
            IMemoryCache memoryCache,
            DiscordAccount account,
            ITaskStoreService taskStoreService,
            INotifyService notifyService,
            DiscordHelper discordHelper,
            Dictionary<string, string> paramsMap,
            IWebProxy webProxy,
            ITaskService taskService)
        {
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

            _taskService = taskService;
            _cache = memoryCache;
            _paramsMap = paramsMap;
            _discordHelper = discordHelper;

            _account = account;
            _taskStoreService = taskStoreService;
            _notifyService = notifyService;

            // 最小 1, 最大 12
            _semaphoreSlimLock = new AsyncParallelLock(Math.Max(1, Math.Min(account.CoreSize, 12)));

            // 初始化信号器
            _mre = new ManualResetEvent(false);

            var discordServer = _discordHelper.GetServer();

            _discordInteractionUrl = $"{discordServer}/api/v9/interactions";
            _discordAttachmentUrl = $"{discordServer}/api/v9/channels/{account.ChannelId}/attachments";
            _discordMessageUrl = $"{discordServer}/api/v9/channels/{account.ChannelId}/messages";

            // 后台任务
            // 后台任务取消 token
            _longToken = new CancellationTokenSource();
            _longTask = new Task(Running, _longToken.Token, TaskCreationOptions.LongRunning);
            _longTask.Start();

            _longTaskCache = new Task(RuningCache, _longToken.Token, TaskCreationOptions.LongRunning);
            _longTaskCache.Start();
        }

        /// <summary>
        /// 默认会话ID。
        /// </summary>
        public string DefaultSessionId { get; set; } = "f1a313a09ce079ce252459dc70231f30";

        /// <summary>
        /// 获取实例ID。
        /// </summary>
        /// <returns>实例ID</returns>
        public string ChannelId => Account.ChannelId;

        public BotMessageListener BotMessageListener { get; set; }

        public WebSocketManager WebSocketManager { get; set; }

        /// <summary>
        /// 获取Discord账号信息。
        /// </summary>
        /// <returns>Discord账号</returns>
        public DiscordAccount Account
        {
            get
            {
                try
                {
                    lock (_lockAccount)
                    {
                        if (!string.IsNullOrWhiteSpace(_account?.Id))
                        {
                            _account = _cache.GetOrCreate($"account:{_account.Id}", (c) =>
                            {
                                c.SetAbsoluteExpiration(TimeSpan.FromMinutes(2));

                                // 必须数据库中存在
                                var acc = DbHelper.Instance.AccountStore.Get(_account.Id);
                                if (acc != null)
                                {
                                    return acc;
                                }

                                // 如果账号被删除了
                                IsInit = false;

                                return _account;
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to get account. {@0}", _account?.Id ?? "unknown");
                }

                return _account;
            }
        }

        /// <summary>
        /// 清理账号缓存
        /// </summary>
        /// <param name="id"></param>
        public void ClearAccountCache(string id)
        {
            _cache.Remove($"account:{id}");
        }

        /// <summary>
        /// 是否已初始化完成
        /// </summary>
        public bool IsInit { get; set; }

        /// <summary>
        /// 判断实例是否存活
        /// </summary>
        /// <returns>是否存活</returns>
        public bool IsAlive => IsInit && Account != null
             && Account.Enable != null && Account.Enable == true
             && WebSocketManager != null
             && WebSocketManager.Running == true
             && Account.Lock == false;

        /// <summary>
        /// 获取正在运行的任务列表。
        /// </summary>
        /// <returns>正在运行的任务列表</returns>
        public List<TaskInfo> GetRunningTasks() => _runningTasks;

        /// <summary>
        /// 获取队列中的任务列表。
        /// </summary>
        /// <returns>队列中的任务列表</returns>
        public List<TaskInfo> GetQueueTasks() => new List<TaskInfo>(_queueTasks.Select(c => c.Item1) ?? []);

        /// <summary>
        /// 是否存在空闲队列，即：队列是否已满，是否可加入新的任务
        /// </summary>
        public bool IsIdleQueue
        {
            get
            {
                if (_queueTasks.Count <= 0)
                {
                    return true;
                }

                if (Account.MaxQueueSize <= 0)
                {
                    return true;
                }

                return _queueTasks.Count < Account.MaxQueueSize;
            }
        }

        /// <summary>
        /// 后台服务执行任务
        /// </summary>
        private void Running()
        {
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
                catch
                {

                }

                try
                {
                    //if (_longToken.Token.IsCancellationRequested)
                    //{
                    //    // 清理资源（如果需要）
                    //    break;
                    //}

                    // 等待信号通知
                    _mre.WaitOne();

                    // 判断是否还有资源可用
                    while (!_semaphoreSlimLock.IsLockAvailable())
                    {
                        //if (_longToken.Token.IsCancellationRequested)
                        //{
                        //    // 清理资源（如果需要）
                        //    break;
                        //}

                        // 等待
                        Thread.Sleep(100);
                    }

                    //// 允许同时执行 N 个信号量的任务
                    //while (_queueTasks.TryDequeue(out var info))
                    //{
                    //    // 判断是否还有资源可用
                    //    while (!_semaphoreSlimLock.TryWait(100))
                    //    {
                    //        // 等待
                    //        Thread.Sleep(100);
                    //    }

                    //    _taskFutureMap[info.Item1.Id] = ExecuteTaskAsync(info.Item1, info.Item2);
                    //}

                    // 允许同时执行 N 个信号量的任务
                    //while (true)
                    //{
                    //if (_longToken.Token.IsCancellationRequested)
                    //{
                    //    // 清理资源（如果需要）
                    //    break;
                    //}

                    while (_queueTasks.TryPeek(out var info))
                    {
                        // 判断是否还有资源可用
                        if (_semaphoreSlimLock.IsLockAvailable())
                        {
                            var preSleep = Account.Interval;
                            if (preSleep <= 1.2m)
                            {
                                preSleep = 1.2m;
                            }

                            // 提交任务前间隔
                            // 当一个作业完成后，是否先等待一段时间再提交下一个作业
                            Thread.Sleep((int)(preSleep * 1000));

                            // 从队列中移除任务，并开始执行
                            if (_queueTasks.TryDequeue(out info))
                            {
                                _taskFutureMap[info.Item1.Id] = ExecuteTaskAsync(info.Item1, info.Item2);

                                // 计算执行后的间隔
                                var min = Account.AfterIntervalMin;
                                var max = Account.AfterIntervalMax;

                                // 计算 min ~ max随机数
                                var afterInterval = 1200;
                                if (max > min && min >= 1.2m)
                                {
                                    afterInterval = new Random().Next((int)(min * 1000), (int)(max * 1000));
                                }
                                else if (max == min && min >= 1.2m)
                                {
                                    afterInterval = (int)(min * 1000);
                                }

                                // 如果是图生文操作
                                if (info.Item1.GetProperty<string>(Constants.TASK_PROPERTY_CUSTOM_ID, default)?.Contains("PicReader") == true)
                                {
                                    // 批量任务操作提交间隔 1.2s + 6.8s
                                    Thread.Sleep(afterInterval + 6800);
                                }
                                else
                                {
                                    // 队列提交间隔
                                    Thread.Sleep(afterInterval);
                                }
                            }
                        }
                        else
                        {
                            // 如果没有可用资源，等待
                            Thread.Sleep(100);
                        }
                    }

                    //else
                    //{
                    //    // 队列为空，退出循环
                    //    break;
                    //}
                    //}

                    //if (_longToken.Token.IsCancellationRequested)
                    //{
                    //    // 清理资源（如果需要）
                    //    break;
                    //}

                    //// 等待
                    //Thread.Sleep(100);



                    // 重新设置信号
                    _mre.Reset();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"后台作业执行异常 {Account?.ChannelId}");

                    // 停止 1min
                    Thread.Sleep(1000 * 60);
                }
            }
        }

        /// <summary>
        /// 缓存处理
        /// </summary>
        private void RuningCache()
        {
            while (true)
            {
                if (_longToken.Token.IsCancellationRequested)
                {
                    // 清理资源（如果需要）
                    break;
                }

                try
                {
                    // 当前时间转为 Unix 时间戳
                    // 今日 0 点 Unix 时间戳
                    var now = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();
                    var count = (int)DbHelper.Instance.TaskStore.Count(c => c.SubmitTime >= now && c.InstanceId == Account.ChannelId);

                    if (Account.DayDrawCount != count)
                    {
                        Account.DayDrawCount = count;

                        DbHelper.Instance.AccountStore.Update("DayDrawCount", Account);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "RuningCache 异常");
                }

                // 每 2 分钟执行一次
                Thread.Sleep(60 * 1000 * 2);
            }
        }

        /// <summary>
        /// 退出任务并进行保存和通知。
        /// </summary>
        /// <param name="task">任务信息</param>
        public void ExitTask(TaskInfo task)
        {
            _taskFutureMap.TryRemove(task.Id, out _);
            SaveAndNotify(task);

            // 判断 _queueTasks 队列中是否存在指定任务，如果有则移除
            //if (_queueTasks.Any(c => c.Item1.Id == task.Id))
            //{
            //    _queueTasks = new ConcurrentQueue<(TaskInfo, Func<Task<Message>>)>(_queueTasks.Where(c => c.Item1.Id != task.Id));
            //}

            // 判断 _queueTasks 队列中是否存在指定任务，如果有则移除
            // 使用线程安全的方式移除
            if (_queueTasks.Any(c => c.Item1.Id == task.Id))
            {
                // 移除 _queueTasks 队列中指定的任务
                var tempQueue = new ConcurrentQueue<(TaskInfo, Func<Task<Message>>)>();

                // 将不需要移除的元素加入到临时队列中
                while (_queueTasks.TryDequeue(out var item))
                {
                    if (item.Item1.Id != task.Id)
                    {
                        tempQueue.Enqueue(item);
                    }
                }

                // 交换队列引用
                _queueTasks = tempQueue;
            }
        }

        /// <summary>
        /// 获取正在运行的任务Future映射。
        /// </summary>
        /// <returns>任务Future映射</returns>
        public Dictionary<string, Task> GetRunningFutures() => new Dictionary<string, Task>(_taskFutureMap);

        /// <summary>
        /// 提交任务。
        /// </summary>
        /// <param name="info">任务信息</param>
        /// <param name="discordSubmit">Discord提交任务的委托</param>
        /// <returns>任务提交结果</returns>
        public SubmitResultVO SubmitTaskAsync(TaskInfo info, Func<Task<Message>> discordSubmit)
        {
            // 在任务提交时，前面的的任务数量
            var currentWaitNumbers = _queueTasks.Count;
            if (Account.MaxQueueSize > 0 && currentWaitNumbers >= Account.MaxQueueSize)
            {
                return SubmitResultVO.Fail(ReturnCode.FAILURE, "提交失败，队列已满，请稍后重试")
                    .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ChannelId);
            }

            info.InstanceId = ChannelId;
            _taskStoreService.Save(info);

            try
            {
                _queueTasks.Enqueue((info, discordSubmit));

                // 通知后台服务有新的任务
                _mre.Set();

                //// 当执行中的任务没有满时，重新计算队列中的任务数量
                //if (_runningTasks.Count < _account.CoreSize)
                //{
                //    // 等待 10ms 检查
                //    Thread.Sleep(10);
                //}

                //currentWaitNumbers = _queueTasks.Count;

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
        /// 异步执行任务。
        /// </summary>
        /// <param name="info">任务信息</param>
        /// <param name="discordSubmit">Discord提交任务的委托</param>
        /// <returns>异步任务</returns>
        private async Task ExecuteTaskAsync(TaskInfo info, Func<Task<Message>> discordSubmit)
        {
            try
            {
                await _semaphoreSlimLock.LockAsync();

                _runningTasks.Add(info);

                // 判断当前实例是否可用
                if (!IsAlive)
                {
                    info.Fail("实例不可用");
                    SaveAndNotify(info);
                    _logger.Debug("[{@0}] task error, id: {@1}, status: {@2}", Account.GetDisplay(), info.Id, info.Status);
                    return;
                }

                // banned 判断
                // banned 会导致执行中的数量计算不准确问题，暂时不处理
                //if (!info.IsWhite)
                //{
                //    if (!string.IsNullOrWhiteSpace(info.UserId))
                //    {
                //        var lockKey = $"banned:lock:{info.UserId}";
                //        if (_cache.TryGetValue(lockKey, out int lockValue) && lockValue > 0)
                //        {
                //            info.Fail("账号已被临时封锁，请勿使用违规词作图");
                //            SaveAndNotify(info);
                //            _logger.Debug("[{@0}] task error, id: {@1}, status: {@2}", Account.GetDisplay(), info.Id, info.Status);
                //            return;
                //        }
                //    }

                //    if (true)
                //    {
                //        var lockKey = $"banned:lock:{info.ClientIp}";
                //        if (_cache.TryGetValue(lockKey, out int lockValue) && lockValue > 0)
                //        {
                //            info.Fail("账号已被临时封锁，请勿使用违规词作图");
                //            SaveAndNotify(info);
                //            _logger.Debug("[{@0}] task error, id: {@1}, status: {@2}", Account.GetDisplay(), info.Id, info.Status);
                //            return;
                //        }
                //    }
                //}

                info.Status = TaskStatus.SUBMITTED;
                info.Progress = "0%";
                SaveAndNotify(info);

                var result = await discordSubmit();

                // 判断当前实例是否可用
                if (!IsAlive)
                {
                    info.Fail("实例不可用");
                    SaveAndNotify(info);
                    _logger.Debug("[{@0}] task error, id: {@1}, status: {@2}", Account.GetDisplay(), info.Id, info.Status);
                    return;
                }

                info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                if (result.Code != ReturnCode.SUCCESS)
                {
                    info.Fail(result.Description);
                    SaveAndNotify(info);
                    _logger.Debug("[{@0}] task finished, id: {@1}, status: {@2}", Account.GetDisplay(), info.Id, info.Status);
                    return;
                }

                info.Status = TaskStatus.SUBMITTED;
                info.Progress = "0%";

                await Task.Delay(500);

                SaveAndNotify(info);

                // 超时处理
                var timeoutMin = Account.TimeoutMinutes;
                var sw = new Stopwatch();
                sw.Start();

                while (info.Status == TaskStatus.SUBMITTED || info.Status == TaskStatus.IN_PROGRESS)
                {
                    SaveAndNotify(info);

                    // 每 500ms
                    await Task.Delay(500);

                    if (sw.ElapsedMilliseconds > timeoutMin * 60 * 1000)
                    {
                        info.Fail($"执行超时 {timeoutMin} 分钟");
                        SaveAndNotify(info);
                        return;
                    }
                }

                // 不随机，直接读消息
                // 任务完成后，自动读消息
                // 随机 3 次，如果命中则读消息
                //if (new Random().Next(0, 3) == 0)
                //{
                try
                {
                    // 成功才都消息
                    if (info.Status == TaskStatus.SUCCESS)
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
                //}

                SaveAndNotify(info);

                _logger.Debug("[{AccountDisplay}] task finished, id: {TaskId}, status: {TaskStatus}", Account.GetDisplay(), info.Id, info.Status);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{AccountDisplay}] task execute error, id: {TaskId}", Account.GetDisplay(), info.Id);
                info.Fail("[Internal Server Error] " + ex.Message);

                SaveAndNotify(info);
            }
            finally
            {
                _runningTasks.Remove(info);
                _taskFutureMap.TryRemove(info.Id, out _);

                _semaphoreSlimLock.Unlock();

                SaveAndNotify(info);
            }
        }

        public void AddRunningTask(TaskInfo task)
        {
            _runningTasks.Add(task);
        }

        public void RemoveRunningTask(TaskInfo task)
        {
            _runningTasks.Remove(task);
        }

        /// <summary>
        /// 保存并通知任务状态变化。
        /// </summary>
        /// <param name="task">任务信息</param>
        private void SaveAndNotify(TaskInfo task)
        {
            try
            {
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
        public void Dispose()
        {
            try
            {
                // 清除缓存
                ClearAccountCache(Account?.Id);

                BotMessageListener?.Dispose();
                WebSocketManager?.Dispose();

                _mre.Set();

                // 任务取消
                _longToken.Cancel();

                // 停止后台任务
                _mre.Set(); // 解除等待，防止死锁

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
                    runningTask.Fail("强制取消"); // 取消任务（假设TaskInfo有Cancel方法）
                }

                // 清理任务队列
                while (_queueTasks.TryDequeue(out var taskInfo))
                {
                    taskInfo.Item1.Fail("强制取消"); // 取消任务（假设TaskInfo有Cancel方法）
                }

                // 释放信号量
                //_semaphoreSlimLock?.Dispose();

                // 释放信号
                _mre?.Dispose();

                // 释放任务映射
                foreach (var task in _taskFutureMap.Values)
                {
                    if (!task.IsCompleted)
                    {
                        try
                        {
                            task.Wait(); // 等待任务完成
                        }
                        catch
                        {
                            // Ignore exceptions from tasks
                        }
                    }
                }

                // 清理资源
                _taskFutureMap.Clear();
                _runningTasks.Clear();
            }
            catch
            {
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
            prompt = prompt.Replace(" -- ", " ")
                .Replace("  ", " ").Replace("  ", " ").Replace("  ", " ").Trim();

            // 任务指定速度模式
            if (info != null && info.Mode != null)
            {
                // 移除 prompt 可能的的参数
                prompt = prompt.Replace("--fast", "").Replace("--relax", "").Replace("--turbo", "");

                // 如果任务指定了速度模式
                if (info.Mode != null)
                {
                    switch (info.Mode.Value)
                    {
                        case GenerationSpeedMode.RELAX:
                            prompt += " --relax";
                            break;

                        case GenerationSpeedMode.FAST:
                            prompt += " --fast";
                            break;

                        case GenerationSpeedMode.TURBO:
                            prompt += " --turbo";
                            break;

                        default:
                            break;
                    }
                }
            }

            // 允许速度模式
            if (Account.AllowModes?.Count > 0)
            {
                // 计算不允许的速度模式，并删除相关参数
                var notAllowModes = new List<string>();
                if (!Account.AllowModes.Contains(GenerationSpeedMode.RELAX))
                {
                    notAllowModes.Add("--relax");
                }
                if (!Account.AllowModes.Contains(GenerationSpeedMode.FAST))
                {
                    notAllowModes.Add("--fast");
                }
                if (!Account.AllowModes.Contains(GenerationSpeedMode.TURBO))
                {
                    notAllowModes.Add("--turbo");
                }

                // 移除 prompt 可能的的参数
                foreach (var mode in notAllowModes)
                {
                    prompt = prompt.Replace(mode, "");
                }
            }

            // 指定生成速度模式
            if (Account.Mode != null)
            {
                // 移除 prompt 可能的的参数
                prompt = prompt.Replace("--fast", "").Replace("--relax", "").Replace("--turbo", "");

                switch (Account.Mode.Value)
                {
                    case GenerationSpeedMode.RELAX:
                        prompt += " --relax";
                        break;

                    case GenerationSpeedMode.FAST:
                        prompt += " --fast";
                        break;

                    case GenerationSpeedMode.TURBO:
                        prompt += " --turbo";
                        break;

                    default:
                        break;
                }
            }

            // 如果快速模式用完了，则指定慢速
            if (Account.FastExhausted)
            {
                // 移除 prompt 可能的的参数
                prompt = prompt.Replace("--fast", "").Replace("--relax", "").Replace("--turbo", "");

                prompt += " --relax";
            }

            //// 处理转义字符引号等
            //return prompt.Replace("\\\"", "\"").Replace("\\'", "'").Replace("\\\\", "\\");

            prompt = FormatUrls(prompt).ConfigureAwait(false).GetAwaiter().GetResult();

            return prompt;
        }

        /// <summary>
        /// 对 prompt 中含有 url 的进行转换为官方 url 处理
        /// 同一个 url 1 小时内有效缓存
        /// </summary>
        /// <param name="prompt"></param>
        /// <returns></returns>
        public async Task<string> FormatUrls(string prompt)
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
                        // url 缓存默认 24 小时有效
                        var okUrl = await _cache.GetOrCreateAsync($"tmp:{url}", async entry =>
                        {
                            entry.AbsoluteExpiration = DateTimeOffset.Now.AddHours(24);

                            var ff = new FileFetchHelper();
                            var res = await ff.FetchFileAsync(url);
                            if (res.Success && !string.IsNullOrWhiteSpace(res.Url))
                            {
                                return res.Url;
                            }
                            else if (res.Success && res.FileBytes.Length > 0)
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

                            throw new LogicException($"解析链接失败 {url}, {res?.Msg}");
                        });

                        urlDic[url] = okUrl;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "解析 url 异常 {0}", url);
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

        public async Task<Message> UploadAsync(string fileName, DataUrl dataUrl, bool useDiscordUpload = false)
        {
            // 启用转为云链接
            if (GlobalConfiguration.Setting.EnableConvertAliyunLink && !useDiscordUpload)
            {
                try
                {
                    //var oss = new AliyunOssStorageService();

                    var localPath = $"attachments/{DateTime.Now:yyyyMMdd}/{fileName}";

                    var mt = MimeKit.MimeTypes.GetMimeType(Path.GetFileName(localPath));
                    if (string.IsNullOrWhiteSpace(mt))
                    {
                        mt = "image/png";
                    }

                    var stream = new MemoryStream(dataUrl.Data);
                    var res = StorageHelper.Instance.SaveAsync(stream, localPath, dataUrl.MimeType ?? mt);
                    if (string.IsNullOrWhiteSpace(res?.Url))
                    {
                        throw new Exception("上传图片到加速站点失败");
                    }

                    //// 替换 url
                    //var customCdn = oss.Options.CustomCdn;
                    //if (string.IsNullOrWhiteSpace(customCdn))
                    //{
                    //    customCdn = oss.Options.Endpoint;
                    //}

                    //var url = $"{customCdn?.Trim()?.Trim('/')}/{res.Key}";

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
                        DbHelper.Instance.AccountStore.Update(Account);
                        ClearAccountCache(Account.Id);

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
            if (botType == EBotType.NIJI_JOURNEY && Account.EnableNiji != true)
            {
                return Message.Success("忽略提交，未开启 niji");
            }

            if (botType == EBotType.MID_JOURNEY && Account.EnableMj != true)
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
        /// 全局切换快速模式检查
        /// </summary>
        /// <returns></returns>
        public async Task RelaxToFastValidate()
        {
            try
            {
                // 快速用完时
                // 并且开启快速切换慢速模式时
                if (Account != null && Account.FastExhausted && Account.EnableRelaxToFast == true)
                {
                    // 每 6~12 小时，和启动时检查账号是否有快速时长
                    await RandomSyncInfo();

                    // 判断 info 检查时间是否在 5 分钟内
                    if (Account.InfoUpdated != null && Account.InfoUpdated.Value.AddMinutes(5) >= DateTime.Now)
                    {
                        _logger.Information("自动切换快速模式，验证 {@0}", Account.ChannelId);

                        // 提取 fastime
                        // 如果检查完之后，快速超过 1 小时，则标记为快速未用完
                        var fastTime = Account.FastTimeRemaining?.ToString()?.Split('/')?.FirstOrDefault()?.Trim();
                        if (!string.IsNullOrWhiteSpace(fastTime) && double.TryParse(fastTime, out var ftime) && ftime >= 1)
                        {
                            _logger.Information("自动切换快速模式，开始 {@0}", Account.ChannelId);

                            // 标记未用完快速
                            Account.FastExhausted = false;
                            DbHelper.Instance.AccountStore.Update("FastExhausted", Account);

                            // 如果开启了自动切换到快速，则自动切换到快速
                            try
                            {
                                if (Account.EnableRelaxToFast == true)
                                {
                                    Thread.Sleep(2500);
                                    await FastAsync(SnowFlake.NextId(), EBotType.MID_JOURNEY);

                                    Thread.Sleep(2500);
                                    await FastAsync(SnowFlake.NextId(), EBotType.NIJI_JOURNEY);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "自动切换快速模式，执行异常 {@0}", Account.ChannelId);
                            }

                            ClearAccountCache(Account.Id);

                            _logger.Information("自动切换快速模式，执行完成 {@0}", Account.ChannelId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "快速切换慢速模式，检查执行异常");
            }
        }

        /// <summary>
        /// 随机 6-12 小时 同步一次账号信息
        /// </summary>
        /// <returns></returns>
        public async Task RandomSyncInfo()
        {
            // 每 6~12 小时
            if (Account.InfoUpdated == null || Account.InfoUpdated.Value.AddMinutes(5) < DateTime.Now)
            {
                var key = $"fast_exhausted_{Account.ChannelId}";
                await _cache.GetOrCreateAsync(key, async c =>
                {
                    try
                    {
                        _logger.Information("随机同步账号信息开始 {@0}", Account.ChannelId);

                        // 随机 6~12 小时
                        var random = new Random();
                        var minutes = random.Next(360, 600);
                        c.SetAbsoluteExpiration(TimeSpan.FromMinutes(minutes));

                        await _taskService.InfoSetting(Account.Id);

                        _logger.Information("随机同步账号信息完成 {@0}", Account.ChannelId);

                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "随机同步账号信息异常 {@0}", Account.ChannelId);
                    }

                    return false;
                });
            }
        }
    }
}
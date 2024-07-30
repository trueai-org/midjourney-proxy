using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.Util;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Midjourney.Infrastructure.LoadBalancer
{
    /// <summary>
    /// Discord实例实现类。
    /// 实现了IDiscordInstance接口，负责处理Discord相关的任务管理和执行。
    /// </summary>
    public class DiscordInstanceImpl : IDiscordInstance
    {
        private readonly DiscordAccount _account;
        private readonly IDiscordService _service;
        private readonly ITaskStoreService _taskStoreService;
        private readonly INotifyService _notifyService;
        private readonly ILogger _logger;
        private readonly List<TaskInfo> _runningTasks;
        private readonly ConcurrentDictionary<string, Task> _taskFutureMap;
        private readonly SemaphoreSlimLock _semaphoreSlimLock;

        private readonly Task _longTask;
        private readonly CancellationTokenSource _longToken;
        private readonly ManualResetEvent _mre; // 信号

        private ConcurrentQueue<(TaskInfo, Func<Task<Message>>)> _queueTasks;

        public DiscordInstanceImpl(
            DiscordAccount account,
            IDiscordService service,
            ITaskStoreService taskStoreService,
            INotifyService notifyService)
        {
            _account = account;
            _service = service;
            _taskStoreService = taskStoreService;
            _notifyService = notifyService;

            _logger = Log.Logger;
            _runningTasks = new List<TaskInfo>();
            _queueTasks = new ConcurrentQueue<(TaskInfo, Func<Task<Message>>)>();
            _taskFutureMap = new ConcurrentDictionary<string, Task>();

            // 最小 1, 最大 12
            _semaphoreSlimLock = new SemaphoreSlimLock(Math.Max(1, Math.Min(account.CoreSize, 12)));

            // 初始化信号器
            _mre = new ManualResetEvent(false);

            // 后台任务
            // 后台任务取消 token
            _longToken = new CancellationTokenSource();
            _longTask = new Task(Running, _longToken.Token, TaskCreationOptions.LongRunning);
            _longTask.Start();
        }

        /// <summary>
        /// 获取实例ID。
        /// </summary>
        /// <returns>实例ID</returns>
        public string GetInstanceId => _account.ChannelId;

        /// <summary>
        /// 获取Discord账号信息。
        /// </summary>
        /// <returns>Discord账号</returns>
        public DiscordAccount Account => _account;

        /// <summary>
        /// 判断实例是否存活
        /// </summary>
        /// <returns>是否存活</returns>
        public bool IsAlive => _account?.Enable == true
            && WebSocketManager?.Running == true
            && _account?.Lock != true;

        /// <summary>
        /// 获取正在运行的任务列表。
        /// </summary>
        /// <returns>正在运行的任务列表</returns>
        public List<TaskInfo> GetRunningTasks() => _runningTasks;

        /// <summary>
        /// 获取队列中的任务列表。
        /// </summary>
        /// <returns>队列中的任务列表</returns>
        public List<TaskInfo> GetQueueTasks() => new List<TaskInfo>(_queueTasks.Select(c => c.Item1));

        public BotMessageListener BotMessageListener { get; set; }

        public WebSocketManager WebSocketManager { get; set; }

        /// <summary>
        /// 后台服务执行任务
        /// </summary>
        private void Running()
        {
            while (true)
            {
                if (_longToken.Token.IsCancellationRequested)
                {
                    // 清理资源（如果需要）
                    break;
                }

                // 等待信号通知
                _mre.WaitOne();

                // 判断是否还有资源可用
                while (!_semaphoreSlimLock.TryWait(100))
                {
                    if (_longToken.Token.IsCancellationRequested)
                    {
                        // 清理资源（如果需要）
                        break;
                    }

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
                while (true)
                {
                    if (_longToken.Token.IsCancellationRequested)
                    {
                        // 清理资源（如果需要）
                        break;
                    }

                    if (_queueTasks.TryPeek(out var info))
                    {
                        // 判断是否还有资源可用
                        if (_semaphoreSlimLock.TryWait(100))
                        {
                            // 从队列中移除任务，并开始执行
                            if (_queueTasks.TryDequeue(out info))
                            {
                                _taskFutureMap[info.Item1.Id] = ExecuteTaskAsync(info.Item1, info.Item2);

                                // 如果是图生文操作
                                if (info.Item1.GetProperty<string>(Constants.TASK_PROPERTY_CUSTOM_ID, default)?.Contains("PicReader") == true)
                                {
                                    // 批量任务操作提交间隔 8s
                                    Thread.Sleep(8000);
                                }
                                else
                                {
                                    // 任务提交间隔 1.2s
                                    Thread.Sleep(1200);
                                }
                            }
                        }
                        else
                        {
                            // 如果没有可用资源，等待
                            Thread.Sleep(100);
                        }
                    }
                    else
                    {
                        // 队列为空，退出循环
                        break;
                    }
                }

                if (_longToken.Token.IsCancellationRequested)
                {
                    // 清理资源（如果需要）
                    break;
                }

                // 重新设置信号
                _mre.Reset();
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
            if (_account.MaxQueueSize > 0 && currentWaitNumbers >= _account.MaxQueueSize)
            {
                return SubmitResultVO.Fail(ReturnCode.FAILURE, "提交失败，队列已满，请稍后重试")
                    .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, GetInstanceId);
            }

            info.InstanceId = GetInstanceId;
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
                        .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, GetInstanceId);
                }
                else
                {
                    return SubmitResultVO.Of(ReturnCode.IN_QUEUE, $"排队中，前面还有{currentWaitNumbers}个任务", info.Id)
                        .SetProperty("numberOfQueues", currentWaitNumbers)
                        .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, GetInstanceId);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "submit task error");

                _taskStoreService.Delete(info.Id);

                return SubmitResultVO.Fail(ReturnCode.FAILURE, "提交失败，系统异常")
                    .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, GetInstanceId);
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
            _semaphoreSlimLock.Wait();
            _runningTasks.Add(info);

            try
            {
                var result = await discordSubmit();

                info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                if (result.Code != ReturnCode.SUCCESS)
                {
                    info.Fail(result.Description);
                    SaveAndNotify(info);
                    _logger.Debug("[{@0}] task finished, id: {@1}, status: {@2}", _account.GetDisplay(), info.Id, info.Status);
                    return;
                }

                info.Status = TaskStatus.SUBMITTED;
                info.Progress = "0%";

                await Task.Delay(1000);

                await AsyncSaveAndNotify(info);

                // 超时处理
                var timeoutMin = _account.TimeoutMinutes;
                var sw = new Stopwatch();
                sw.Start();

                while (info.Status == TaskStatus.SUBMITTED || info.Status == TaskStatus.IN_PROGRESS)
                {
                    await Task.Delay(100);
                    await AsyncSaveAndNotify(info);

                    if (sw.ElapsedMilliseconds > timeoutMin * 60 * 1000)
                    {
                        throw new TimeoutException($"执行超时 {timeoutMin} 分钟");
                    }
                }

                // 任务完成后，自动读消息
                // 随机 3 次，如果命中则读消息
                if (new Random().Next(0, 3) == 0)
                {
                    try
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
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "自动读消息异常 {@0} - {@1}", info.InstanceId, info.Id);
                    }
                }

                _logger.Debug("[{AccountDisplay}] task finished, id: {TaskId}, status: {TaskStatus}", _account.GetDisplay(), info.Id, info.Status);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{AccountDisplay}] task execute error, id: {TaskId}", _account.GetDisplay(), info.Id);
                info.Fail("[Internal Server Error] " + ex.Message);

                SaveAndNotify(info);
            }
            finally
            {
                _runningTasks.Remove(info);
                _taskFutureMap.TryRemove(info.Id, out _);
                _semaphoreSlimLock.Release();

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
        /// 异步保存和通知任务。
        /// </summary>
        /// <param name="task">任务信息</param>
        /// <returns>异步任务</returns>
        private async Task AsyncSaveAndNotify(TaskInfo task) => await Task.Run(() => SaveAndNotify(task));

        /// <summary>
        /// 保存并通知任务状态变化。
        /// </summary>
        /// <param name="task">任务信息</param>
        private void SaveAndNotify(TaskInfo task)
        {
            _taskStoreService.Save(task);
            _notifyService.NotifyTaskChange(task);
        }

        /// <summary>
        /// 异步执行想象任务。
        /// </summary>
        /// <param name="prompt">提示信息</param>
        /// <param name="nonce">随机数</param>
        /// <returns>异步任务</returns>
        public Task<Message> ImagineAsync(string prompt, string nonce, EBotType botType)
            => _service.ImagineAsync(prompt, nonce, botType);

        /// <summary>
        /// 异步执行放大任务。
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <param name="index">索引</param>
        /// <param name="messageHash">消息哈希</param>
        /// <param name="messageFlags">消息标志</param>
        /// <param name="nonce">随机数</param>
        /// <returns>异步任务</returns>
        public Task<Message> UpscaleAsync(string messageId, int index, string messageHash, int messageFlags, string nonce, EBotType botType) => _service.UpscaleAsync(messageId, index, messageHash, messageFlags, nonce, botType);

        /// <summary>
        /// 异步执行变体任务。
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <param name="index">索引</param>
        /// <param name="messageHash">消息哈希</param>
        /// <param name="messageFlags">消息标志</param>
        /// <param name="nonce">随机数</param>
        /// <returns>异步任务</returns>
        public Task<Message> VariationAsync(string messageId, int index, string messageHash, int messageFlags, string nonce, EBotType botType) => _service.VariationAsync(messageId, index, messageHash, messageFlags, nonce, botType);

        /// <summary>
        /// 异步执行重新滚动任务。
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <param name="messageHash">消息哈希</param>
        /// <param name="messageFlags">消息标志</param>
        /// <param name="nonce">随机数</param>
        /// <returns>异步任务</returns>
        public Task<Message> RerollAsync(string messageId, string messageHash, int messageFlags, string nonce, EBotType botType) => _service.RerollAsync(messageId, messageHash, messageFlags, nonce, botType);

        /// <summary>
        /// 执行动作
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="customId"></param>
        /// <param name="messageFlags"></param>
        /// <param name="nonce"></param>
        /// <returns></returns>
        public Task<Message> ActionAsync(string messageId, string customId, int messageFlags, string nonce, EBotType botType) =>
              _service.ActionAsync(messageId, customId, messageFlags, nonce, botType);

        /// <summary>
        /// 图片 seed 值
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="nonce"></param>
        /// <returns></returns>
        public Task<Message> SeedAsync(string jobId, string nonce, EBotType botType) =>
            _service.SeedAsync(jobId, nonce, botType);

        /// <summary>
        /// 图片 seed 值消息
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="nonce"></param>
        /// <returns></returns>
        public Task<Message> SeedMessagesAsync(string url) =>
            _service.SeedMessagesAsync(url);

        /// <summary>
        /// 执行 ZOOM
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <returns></returns>
        public Task<Message> ZoomAsync(string messageId, string customId, string prompt, string nonce, EBotType botType) =>
            _service.ZoomAsync(messageId, customId, prompt, nonce, botType);


        /// <summary>
        /// 图生文 - 生图
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public Task<Message> PicReaderAsync(string messageId, string customId, string prompt, string nonce, EBotType botType)
            => _service.PicReaderAsync(messageId, customId, prompt, nonce, botType);

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
        public Task<Message> RemixAsync(TaskAction action, string messageId, string modal, string customId, string prompt, string nonce, EBotType botType)
            => _service.RemixAsync(action, messageId, modal, customId, prompt, nonce, botType);

        /// <summary>
        /// 执行 info 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <returns></returns>
        public Task<Message> InfoAsync(string nonce, EBotType botType) =>
            _service.InfoAsync(nonce, botType);

        /// <summary>
        /// 根据 job id 显示任务信息
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="nonce"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public Task<Message> ShowAsync(string jobId, string nonce, EBotType botType) =>
            _service.ShowAsync(jobId, nonce, botType);

        /// <summary>
        /// 执行 setting 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="isNiji"></param>
        /// <returns></returns>
        public Task<Message> SettingAsync(string nonce, EBotType botType) =>
            _service.SettingAsync(nonce, botType);

        /// <summary>
        /// 执行 settings button 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="custom_id"></param>
        /// <returns></returns>
        public Task<Message> SettingButtonAsync(string nonce, string custom_id, EBotType botType) =>
              _service.SettingButtonAsync(nonce, custom_id, botType);

        /// <summary>
        /// 执行 settings select 操作
        /// </summary>
        /// <param name="nonce"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        public Task<Message> SettingSelectAsync(string nonce, string values) =>
            _service.SettingSelectAsync(nonce, values);

        /// <summary>
        /// 局部重绘
        /// </summary>
        /// <param name="customId"></param>
        /// <param name="prompt"></param>
        /// <param name="maskBase64"></param>
        /// <returns></returns>
        public Task<Message> InpaintAsync(string customId, string prompt, string maskBase64, EBotType botType) =>
            _service.InpaintAsync(customId, prompt, maskBase64, botType);

        /// <summary>
        /// 异步执行描述任务。
        /// </summary>
        /// <param name="finalFileName">最终文件名</param>
        /// <param name="nonce">随机数</param>
        /// <returns>异步任务</returns>
        public Task<Message> DescribeAsync(string finalFileName, string nonce, EBotType botType) => _service.DescribeAsync(finalFileName, nonce, botType);

        /// <summary>
        /// 异步执行混合任务。
        /// </summary>
        /// <param name="finalFileNames">最终文件名列表</param>
        /// <param name="dimensions">混合维度</param>
        /// <param name="nonce">随机数</param>
        /// <returns>异步任务</returns>
        public Task<Message> BlendAsync(List<string> finalFileNames, BlendDimensions dimensions, string nonce, EBotType botType) => _service.BlendAsync(finalFileNames, dimensions, nonce, botType);

        /// <summary>
        /// 异步上传文件。
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="dataUrl">数据URL</param>
        /// <returns>异步任务</returns>
        public Task<Message> UploadAsync(string fileName, DataUrl dataUrl) => _service.UploadAsync(fileName, dataUrl);

        /// <summary>
        /// 异步发送图像消息。
        /// </summary>
        /// <param name="content">消息内容</param>
        /// <param name="finalFileName">最终文件名</param>
        /// <returns>异步任务</returns>
        public Task<Message> SendImageMessageAsync(string content, string finalFileName) => _service.SendImageMessageAsync(content, finalFileName);

        /// <summary>
        /// 自动读 discord 最后一条消息（设置为已读）
        /// </summary>
        /// <param name="lastMessageId"></param>
        /// <returns></returns>
        public Task<Message> ReadMessageAsync(string lastMessageId) =>
            _service.ReadMessageAsync(lastMessageId);

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
    }
}
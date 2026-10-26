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
using System.Text.RegularExpressions;
using CSRedis;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Midjourney.Services
{
    /// <summary>
    /// 违规词测试服务
    ///
    /// 业务说明：
    /// 1. 管理后台录入敏感词后，通过真实的绘图链路提交到上游，观察上游是否返回违规拦截；
    /// 2. 测试任务跳过本地违规词检查，避免还没到上游就被本地词库拦截，从而得到错误的结论；
    /// 3. 测试任务、确认违规记录存放在 Redis hash 中，不设置过期时间，默认永久保留；
    /// 4. 同一个违规词只保留一条测试记录，重新添加同一个违规词等同于重置状态并重新排队；
    /// 5. 后台调度器按全局并发数提交任务并轮询结果，上游返回违规时追加确认违规记录，
    ///    同时把该敏感词自动追加到“确认违规词”违规词条；
    /// 6. 等待宿主 ApplicationStarted 后开始调度；
    /// 7. 多台服务器部署时通过 Redis 领导锁保证同一时间只有一台服务器执行调度。
    /// </summary>
    public class BannedWordTestService : BackgroundService
    {
        /// <summary>
        /// 测试任务 hash key
        /// </summary>
        private const string TaskHashKey = "BannedWordTest:Tasks";

        /// <summary>
        /// 待提交任务队列 key
        /// </summary>
        private const string QueueKey = "BannedWordTest:Queue";

        /// <summary>
        /// 确认违规记录 hash key
        /// </summary>
        private const string ConfirmedHashKey = "BannedWordTest:Confirmed";

        /// <summary>
        /// 测试全局配置 hash key（当前并发数）
        /// </summary>
        private const string SettingsHashKey = "BannedWordTest:Settings";

        /// <summary>
        /// 全局运行中任务 hash key
        /// </summary>
        private const string RunningHashKey = "BannedWordTest:Running";

        /// <summary>
        /// 调度领导锁 key
        /// 多台服务器部署时，只有持有该锁的服务器执行调度，避免重复提交
        /// </summary>
        private const string LeaderLockKey = "BannedWordTest:LeaderLock";

        /// <summary>
        /// 创建/重置/清空测试任务的分布式锁 key
        /// </summary>
        private const string CreateLockKey = "BannedWordTest:Create";

        /// <summary>
        /// 确认违规词条追加锁 key，避免多任务同时追加导致词条丢失
        /// </summary>
        private const string BannedWordAppendLockKey = "BannedWordTest:ConfirmedBannedWord";

        /// <summary>
        /// 全局并发数配置字段
        /// </summary>
        private const string ConcurrencySettingField = "Concurrency";

        /// <summary>
        /// 提示词模板配置字段
        /// </summary>
        private const string TemplateSettingField = "Template";

        /// <summary>
        /// 调度领导锁 TTL（秒）
        /// CSRedis Lock 自带自动续期，这里同时作为续期检查周期
        /// </summary>
        private const int LeaderLockTtlSeconds = 15;

        /// <summary>
        /// 应用启动完成后等待时间，给账号初始化等后台任务留出稳定时间
        /// </summary>
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 单个测试任务最长跟踪时长：60 分钟（包含排队时间）
        /// </summary>
        private static readonly TimeSpan TaskTimeout = TimeSpan.FromMinutes(60);

        /// <summary>
        /// 单次最多创建的敏感词数量
        /// </summary>
        private const int MaxWordsPerRequest = 200;

        /// <summary>
        /// 单个敏感词最大长度
        /// </summary>
        private const int MaxWordLength = 100;

        /// <summary>
        /// 提示词模板最大长度
        /// </summary>
        private const int MaxTemplateLength = 2000;

        /// <summary>
        /// 最终提示词最大长度（Discord 单条消息限制 2000 字符，预留 /imagine 前缀和参数空间）
        /// </summary>
        private const int MaxPromptLength = 1900;

        /// <summary>
        /// 并发数上限
        /// </summary>
        private const int MaxConcurrency = 20;

        /// <summary>
        /// 默认并发数
        /// </summary>
        private const int DefaultConcurrency = 3;

        /// <summary>
        /// 调度间隔
        /// </summary>
        private static readonly TimeSpan LoopInterval = TimeSpan.FromSeconds(3);

        /// <summary>
        /// 上游违规拦截的失败原因特征
        /// 只匹配上游审核/内容策略相关文案，避免把“无可用账号”“任务超时”等普通失败误判为确认违规
        /// </summary>
        private static readonly string[] ViolationMarkers =
        [
            "banned prompt",
            "banned_prompt",
            "prompt was flagged",
            "image denied",
            "output filters",
            "content policy",
            "moderation",
            "violat",
            "sensitive word",
            "内容审核",
            "审核不通过",
            "违规内容",
            "敏感词",
            "社区政策",
            "提示词违规"
        ];

        /// <summary>
        /// 测试占位符，支持大小写
        /// </summary>
        private static readonly Regex WordPlaceholderRegex = new(@"\{word\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 提示词参数起始位置（--ar / --v / --relax 等）
        /// </summary>
        private static readonly Regex PromptFlagRegex = new(@"(^|\s)--", RegexOptions.Compiled);

        private readonly ITaskService _taskService;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly IFreeSql _freeSql = FreeSqlHelper.FreeSql;
        private readonly ILogger _logger = Log.Logger;

        /// <summary>
        /// 当前实例唯一标识，用于日志区分
        /// </summary>
        private readonly string _instanceId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

        /// <summary>
        /// 当前实例持有的调度领导锁
        /// </summary>
        private CSRedisClientLock _leaderLock;

        /// <summary>
        /// 当前节点正在跟踪的测试任务 ID
        /// </summary>
        private readonly ConcurrentDictionary<string, byte> _watching = new();

        /// <summary>
        /// 是否已经完成启动状态恢复
        /// </summary>
        private bool _recovered;

        /// <summary>
        /// 上次维护时间
        /// </summary>
        private DateTime _lastMaintenanceTime = DateTime.MinValue;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="taskService">绘图任务服务</param>
        /// <param name="applicationLifetime">应用生命周期，用于等待 ApplicationStarted</param>
        public BannedWordTestService(ITaskService taskService, IHostApplicationLifetime applicationLifetime)
        {
            _taskService = taskService;
            _applicationLifetime = applicationLifetime;
        }

        /// <summary>
        /// 后台调度循环
        /// 多台服务器部署时，只有抢到 Redis 领导锁的实例执行调度
        /// </summary>
        /// <param name="stoppingToken">停止令牌</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.Information("违规词测试服务已启动，等待应用启动完成: InstanceId={InstanceId}", _instanceId);

            try
            {
                // 等待宿主完全启动（ApplicationStarted）后再开始调度
                await WaitForApplicationStartedAsync(stoppingToken);

                _logger.Information("应用已启动，违规词测试调度开始: InstanceId={InstanceId}", _instanceId);

                // 给账号初始化等后台任务留出稳定时间
                await Task.Delay(StartupDelay, stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        if (!IsRedisValid())
                        {
                            MarkLeadershipLost("Redis 不可用");
                        }
                        else if (EnsureLeadership())
                        {
                            RecoverState();
                            PollRunningTasks();
                            await DispatchPendingTasksAsync();
                            MaintainState();
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "违规词测试调度异常: InstanceId={InstanceId}", _instanceId);
                    }

                    try
                    {
                        await Task.Delay(LoopInterval, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // 宿主正常停止
            }
            finally
            {
                ReleaseLeadership();
                _logger.Information("违规词测试服务已停止: InstanceId={InstanceId}", _instanceId);
            }
        }

        /// <summary>
        /// 等待宿主触发 ApplicationStarted
        /// 避免 Kestrel 或其它 hosted service 尚未就绪时提前开始调度
        /// </summary>
        /// <param name="stoppingToken">停止令牌</param>
        private async Task WaitForApplicationStartedAsync(CancellationToken stoppingToken)
        {
            if (_applicationLifetime.ApplicationStarted.IsCancellationRequested)
            {
                return;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var startedRegistration = _applicationLifetime.ApplicationStarted.Register(() => tcs.TrySetResult(true));
            using var stoppingRegistration = stoppingToken.Register(() => tcs.TrySetCanceled(stoppingToken));

            await tcs.Task;
        }

        /// <summary>
        /// 获取或续期调度领导锁
        /// 使用 CSRedis Lock 自带的自动续期能力，不再手写续期脚本
        /// </summary>
        /// <returns>当前实例是否持有调度领导锁</returns>
        private bool EnsureLeadership()
        {
            try
            {
                if (_leaderLock != null)
                {
                    // 显式续期并确认锁仍属于当前实例；
                    // CSRedis Lock 内部还有自动续期定时器，这里再校验一次所有权，避免 Redis 重启后误判
                    if (_leaderLock.Refresh(LeaderLockTtlSeconds * 1000))
                    {
                        return true;
                    }

                    MarkLeadershipLost("调度领导锁已失效或被其它实例接管");
                }

                // 这里必须用 TryLock，不能用 Lock：
                // CSRedis 的 Lock 在锁被占用时会每 3ms 重试一次，持续整个 timeoutSeconds（这里是 15s），
                // 每个 follower 每轮会产生数千条 SET NX，多个节点/多个前缀共享 Redis 时会形成命令风暴。
                // 领导选举只需要每轮尝试一次，失败后由外层 LoopInterval（3s）再试。
                var leaderLock = RedisHelper.Instance.TryLock(LeaderLockKey, LeaderLockTtlSeconds, true);
                if (leaderLock == null)
                {
                    return false;
                }

                _leaderLock = leaderLock;
                _recovered = false;
                _watching.Clear();

                _logger.Information(
                    "当前实例已获得违规词测试调度权: InstanceId={InstanceId}, TtlSeconds={TtlSeconds}",
                    _instanceId, LeaderLockTtlSeconds);

                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "获取或续期违规词测试调度领导锁失败: InstanceId={InstanceId}", _instanceId);
                MarkLeadershipLost("调度领导锁获取异常");
                return false;
            }
        }

        /// <summary>
        /// 标记当前实例失去调度领导权
        /// </summary>
        /// <param name="reason">失去原因</param>
        private void MarkLeadershipLost(string reason)
        {
            if (_leaderLock != null)
            {
                _logger.Warning(
                    "当前实例失去违规词测试调度权: {Reason}, InstanceId={InstanceId}",
                    reason, _instanceId);
            }

            var leaderLock = _leaderLock;
            _leaderLock = null;
            _recovered = false;
            _watching.Clear();

            try
            {
                leaderLock?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "释放失效的违规词测试调度领导锁异常: InstanceId={InstanceId}", _instanceId);
            }
        }

        /// <summary>
        /// 停止时释放调度领导锁
        /// CSRedis Lock.Dispose 内部使用 Lua 校验锁值，不会误删其它实例的锁
        /// </summary>
        private void ReleaseLeadership()
        {
            var leaderLock = _leaderLock;
            _leaderLock = null;
            _recovered = false;
            _watching.Clear();

            if (leaderLock == null)
            {
                return;
            }

            try
            {
                leaderLock.Dispose();
                _logger.Information("违规词测试调度领导锁已释放: InstanceId={InstanceId}", _instanceId);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "释放违规词测试调度领导锁失败，等待自动过期: InstanceId={InstanceId}", _instanceId);
            }
        }

        /// <summary>
        /// 创建违规词测试任务
        /// 同一个违规词只保留一条记录；已存在的违规词会重置状态并重新排队
        /// </summary>
        /// <param name="request">创建请求</param>
        /// <returns>本次处理的任务数</returns>
        public BannedWordTestCreateResult Create(BannedWordTestCreateRequest request)
        {
            EnsureRedis();

            var words = NormalizeWords(request?.Words);
            var template = NormalizeTemplate(request?.Template);
            var concurrency = NormalizeConcurrency(request?.Concurrency ?? DefaultConcurrency);

            using var createLock = RedisHelper.Lock(CreateLockKey, 30);
            if (createLock == null)
            {
                throw new LogicException("正在处理其它测试请求，请稍后重试");
            }

            try
            {
                // 全局并发数：没有批次概念，测试任务共用当前设置的并发数
                RedisHelper.HSet(SettingsHashKey, ConcurrencySettingField, concurrency);

                // 持久化提示词模板，下次打开测试弹窗时自动回填
                RedisHelper.HSet(SettingsHashKey, TemplateSettingField, template ?? string.Empty);

                var all = GetAllTasks();
                var knownWords = all
                    .Select(c => c.Word)
                    .Concat(words)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var byWord = all
                    .Where(c => !string.IsNullOrWhiteSpace(c.Word))
                    .GroupBy(c => c.Word, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderByDescending(c => c.CreateTime).ThenByDescending(c => c.Id).ToList(),
                        StringComparer.OrdinalIgnoreCase);

                var now = DateTime.Now;
                var taskIdsToStart = new List<string>();
                var processed = 0;

                foreach (var word in words)
                {
                    var prompt = BuildPrompt(word, template, knownWords);
                    if (prompt.Length > MaxPromptLength)
                    {
                        throw new LogicException($"违规词 [{word}] 生成的提示词过长（{prompt.Length} 字符），请缩短提示词模板");
                    }

                    if (byWord.TryGetValue(word, out var existing) && existing.Count > 0)
                    {
                        var primary = existing[0];

                        // 先清理该违规词的历史重复记录，保证同一个违规词只有一条记录
                        foreach (var duplicate in existing.Skip(1))
                        {
                            RedisHelper.HDel(TaskHashKey, duplicate.Id);
                            RedisHelper.HDel(RunningHashKey, duplicate.Id);
                            _watching.TryRemove(duplicate.Id, out _);
                        }

                        // 与任务完成使用同一把锁，避免重置过程中被后台状态流转覆盖
                        using var finalizeLock = RedisHelper.Lock($"BannedWordTest:Finalize:{primary.Id}", 30);
                        if (finalizeLock == null)
                        {
                            throw new LogicException("测试任务正在处理中，请稍后重试");
                        }

                        var current = GetTask(primary.Id);
                        if (current == null)
                        {
                            // 记录在读取后被删除，直接新建
                            var newTask = NewTask(word, prompt, now);
                            SaveTask(newTask);
                            taskIdsToStart.Add(newTask.Id);
                            processed++;
                            continue;
                        }

                        // 重置状态：停止跟踪旧的上游任务，重新入队
                        _watching.TryRemove(current.Id, out _);
                        RedisHelper.HDel(RunningHashKey, current.Id);

                        current.Word = word;
                        current.Prompt = prompt;
                        current.Status = BannedWordTestStatus.NOT_STARTED;
                        current.MjTaskId = null;
                        current.FailReason = null;
                        current.SubmitTime = null;
                        current.FinishTime = null;
                        current.CreateTime = now;
                        current.UpdateTime = now;
                        SaveTask(current);
                        taskIdsToStart.Add(current.Id);
                        processed++;
                    }
                    else
                    {
                        var task = NewTask(word, prompt, now);
                        SaveTask(task);
                        taskIdsToStart.Add(task.Id);
                        processed++;
                    }
                }

                if (taskIdsToStart.Count > 0)
                {
                    RedisHelper.RPush(QueueKey, taskIdsToStart.ToArray());
                }

                return new BannedWordTestCreateResult
                {
                    AddedCount = processed
                };
            }
            catch (Exception ex) when (ex is not LogicException)
            {
                _logger.Error(ex, "创建违规词测试任务失败");
                throw new LogicException("创建测试失败，请检查 Redis 连接");
            }
        }

        /// <summary>
        /// 查询测试任务列表
        /// </summary>
        /// <param name="request">查询条件</param>
        /// <returns>分页任务与全量统计</returns>
        public BannedWordTestListResult Query(BannedWordTestQueryRequest request)
        {
            EnsureRedis();

            try
            {
                var all = GetAllTasks();
                var result = new BannedWordTestListResult
                {
                    NotStartedCount = all.Count(c => c.Status == BannedWordTestStatus.NOT_STARTED),
                    RunningCount = all.Count(c => c.Status == BannedWordTestStatus.RUNNING),
                    PassedCount = all.Count(c => c.Status == BannedWordTestStatus.PASSED),
                    ViolationCount = all.Count(c => c.Status == BannedWordTestStatus.VIOLATION),
                    FailedCount = all.Count(c => c.Status == BannedWordTestStatus.FAILED)
                };

                IEnumerable<BannedWordTestTask> filtered = all;

                if (request?.Status != null)
                {
                    filtered = filtered.Where(c => c.Status == request.Status);
                }

                if (!string.IsNullOrWhiteSpace(request?.Keyword))
                {
                    var keyword = request.Keyword.Trim();
                    filtered = filtered.Where(c => c.Word != null
                        && c.Word.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                }

                var ordered = filtered
                    .OrderByDescending(c => c.SubmitTime ?? DateTime.MinValue)
                    .ThenByDescending(c => c.CreateTime)
                    .ThenByDescending(c => c.Id)
                    .ToList();

                var current = Math.Max(1, request?.Current ?? 1);
                var pageSize = request?.PageSize > 0 ? Math.Clamp(request.PageSize, 1, 100) : 10;

                result.Total = ordered.Count;
                result.List = ordered
                    .Skip((current - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                result.GlobalConcurrency = GetGlobalConcurrency();
                result.Template = RedisHelper.HGet(SettingsHashKey, TemplateSettingField);

                result.Confirmed = GetAllConfirmed()
                    .OrderByDescending(c => c.LastConfirmedTime)
                    .Take(200)
                    .ToList();

                return result;
            }
            catch (Exception ex) when (ex is not LogicException)
            {
                _logger.Error(ex, "查询违规词测试列表失败");
                throw new LogicException("查询测试数据失败，请检查 Redis 连接");
            }
        }

        /// <summary>
        /// 删除单条测试任务记录
        /// 说明：只删除测试跟踪记录，不会撤销已经提交到上游的绘图任务
        /// </summary>
        /// <param name="taskId">测试任务 ID</param>
        /// <returns>删除结果</returns>
        public bool DeleteTask(string taskId)
        {
            EnsureRedis();

            if (string.IsNullOrWhiteSpace(taskId))
            {
                throw new LogicException("测试任务 ID 不能为空");
            }

            try
            {
                // 与任务完成使用同一把锁，避免删除后又被后台状态流转重新写回
                using var lockObj = RedisHelper.Lock($"BannedWordTest:Finalize:{taskId}", 30);
                if (lockObj == null)
                {
                    throw new LogicException("测试任务正在处理中，请稍后重试");
                }

                var task = GetTask(taskId);
                if (task == null)
                {
                    return false;
                }

                RedisHelper.HDel(TaskHashKey, taskId);
                RedisHelper.HDel(RunningHashKey, taskId);

                _watching.TryRemove(taskId, out _);
                return true;
            }
            catch (Exception ex) when (ex is not LogicException)
            {
                _logger.Error(ex, "删除违规词测试任务失败: TaskId={TaskId}", taskId);
                throw new LogicException("删除测试任务失败，请检查 Redis 连接");
            }
        }

        /// <summary>
        /// 重试所有测试失败的任务
        /// 重置为未开始并重新排队，使用当前全局并发数
        /// </summary>
        /// <returns>重新排队的任务数量</returns>
        public int RetryFailed()
        {
            EnsureRedis();

            using var lockObj = RedisHelper.Lock(CreateLockKey, 30);
            if (lockObj == null)
            {
                throw new LogicException("正在处理其它测试请求，请稍后重试");
            }

            try
            {
                var failedTasks = GetAllTasks()
                    .Where(c => c.Status == BannedWordTestStatus.FAILED)
                    .ToList();
                var taskIds = new List<string>();

                foreach (var task in failedTasks)
                {
                    using var finalizeLock = RedisHelper.Lock($"BannedWordTest:Finalize:{task.Id}", 30);
                    if (finalizeLock == null)
                    {
                        continue;
                    }

                    var current = GetTask(task.Id);
                    if (current == null || current.Status != BannedWordTestStatus.FAILED)
                    {
                        continue;
                    }

                    current.Status = BannedWordTestStatus.NOT_STARTED;
                    current.MjTaskId = null;
                    current.FailReason = null;
                    current.SubmitTime = null;
                    current.FinishTime = null;
                    current.UpdateTime = DateTime.Now;
                    SaveTask(current);

                    RedisHelper.HDel(RunningHashKey, current.Id);
                    _watching.TryRemove(current.Id, out _);
                    taskIds.Add(current.Id);
                }

                if (taskIds.Count > 0)
                {
                    RedisHelper.RPush(QueueKey, taskIds.ToArray());
                }

                _logger.Information("违规词测试失败任务已重新排队: Count={Count}", taskIds.Count);
                return taskIds.Count;
            }
            catch (Exception ex) when (ex is not LogicException)
            {
                _logger.Error(ex, "重试违规词测试失败任务失败");
                throw new LogicException("重试失败任务失败，请检查 Redis 连接");
            }
        }

        /// <summary>
        /// 清空所有测试记录
        /// 只清空测试任务、队列、运行中标记和确认违规记录，不影响已提交到上游的绘图任务
        /// </summary>
        /// <returns>清空的任务数量</returns>
        public int ClearAll()
        {
            EnsureRedis();

            using var clearLock = RedisHelper.Lock(CreateLockKey, 30);
            if (clearLock == null)
            {
                throw new LogicException("正在处理其它测试请求，请稍后重试");
            }

            try
            {
                var count = (int)(RedisHelper.HLen(TaskHashKey) + RedisHelper.HLen(ConfirmedHashKey));

                RedisHelper.Del(TaskHashKey, QueueKey, RunningHashKey, ConfirmedHashKey);
                _watching.Clear();

                _logger.Information("违规词测试记录已清空: Count={Count}", count);
                return count;
            }
            catch (Exception ex) when (ex is not LogicException)
            {
                _logger.Error(ex, "清空违规词测试记录失败");
                throw new LogicException("清空测试记录失败，请检查 Redis 连接");
            }
        }

        /// <summary>
        /// 新建测试任务对象
        /// </summary>
        /// <param name="word">敏感词</param>
        /// <param name="prompt">提示词</param>
        /// <param name="now">创建时间</param>
        /// <returns>测试任务</returns>
        private static BannedWordTestTask NewTask(string word, string prompt, DateTime now)
        {
            return new BannedWordTestTask
            {
                Id = Guid.NewGuid().ToString("N"),
                Word = word,
                Prompt = prompt,
                Status = BannedWordTestStatus.NOT_STARTED,
                CreateTime = now,
                UpdateTime = now
            };
        }

        /// <summary>
        /// 启动时恢复运行中任务与待提交队列，并清理历史重复记录
        /// </summary>
        private void RecoverState()
        {
            if (_recovered)
            {
                return;
            }

            try
            {
                // 兼容旧版本：移除测试数据 key 上已有的过期时间，测试记录永久保留
                EnsurePermanentKeys();

                var all = GetAllTasks();

                // 先把历史上被误判为“测试失败”的上游违规原因重新归类为确认违规，
                // 再清理重复记录，避免历史违规结论随重复记录一起被删除
                ReclassifyFailedViolations(all);
                all = DeduplicateTasks(all);

                // 恢复运行中任务：进程重启后继续跟踪，避免任务永远停留在“进行中”
                foreach (var task in all.Where(c => c.Status == BannedWordTestStatus.RUNNING))
                {
                    _watching.TryAdd(task.Id, 0);
                    RedisHelper.HSet(RunningHashKey, task.Id, DateTime.Now.Ticks);
                }

                // 恢复待提交队列：Redis 重启或队列丢失后，重新入队未开始任务
                var queued = (RedisHelper.LRange(QueueKey, 0, -1) ?? [])
                    .Select(NormalizeQueueId)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToHashSet();
                var pending = all
                    .Where(c => c.Status == BannedWordTestStatus.NOT_STARTED && !queued.Contains(c.Id))
                    .Select(c => c.Id)
                    .ToList();

                if (pending.Count > 0)
                {
                    RedisHelper.RPush(QueueKey, pending.ToArray());
                }

                // 首次启用时，把已有的确认违规记录补齐到违规词条
                SyncConfirmedBannedWordGroup();

                _recovered = true;
                _logger.Information("违规词测试状态恢复完成，运行中 {Running}，待提交 {Pending}", _watching.Count, pending.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "恢复违规词测试状态失败");
            }
        }

        /// <summary>
        /// 移除测试数据 key 上的过期时间，保证测试记录永久保留
        /// 兼容旧版本曾经设置过 TTL 的 Redis 数据
        /// </summary>
        private static void EnsurePermanentKeys()
        {
            RedisHelper.Persist(TaskHashKey);
            RedisHelper.Persist(QueueKey);
            RedisHelper.Persist(ConfirmedHashKey);
            RedisHelper.Persist(SettingsHashKey);
            RedisHelper.Persist(RunningHashKey);
        }

        /// <summary>
        /// 清理同一个违规词的历史重复记录，只保留最新的一条
        /// 说明：删除运行中的重复记录只会停止跟踪，不会撤销已提交的上游任务
        /// </summary>
        /// <param name="all">全部测试任务</param>
        /// <returns>去重后的测试任务</returns>
        private List<BannedWordTestTask> DeduplicateTasks(List<BannedWordTestTask> all)
        {
            var result = new List<BannedWordTestTask>();
            var removed = 0;

            foreach (var group in all
                .Where(c => !string.IsNullOrWhiteSpace(c.Word))
                .GroupBy(c => c.Word, StringComparer.OrdinalIgnoreCase))
            {
                var ordered = group
                    .OrderByDescending(c => c.CreateTime)
                    .ThenByDescending(c => c.Id)
                    .ToList();

                result.Add(ordered[0]);

                foreach (var duplicate in ordered.Skip(1))
                {
                    RedisHelper.HDel(TaskHashKey, duplicate.Id);
                    RedisHelper.HDel(RunningHashKey, duplicate.Id);
                    _watching.TryRemove(duplicate.Id, out _);
                    removed++;
                }
            }

            // 理论上不存在空 Word，保留以免误删异常数据
            result.AddRange(all.Where(c => string.IsNullOrWhiteSpace(c.Word)));

            if (removed > 0)
            {
                _logger.Information("已清理违规词测试重复记录: Removed={Removed}", removed);
            }

            return result;
        }

        /// <summary>
        /// 把历史上被误判为失败的上游违规原因重新归类为确认违规
        /// 只在领导实例启动时执行一次，状态变更后不会重复处理
        /// </summary>
        /// <param name="tasks">测试任务</param>
        private void ReclassifyFailedViolations(List<BannedWordTestTask> tasks)
        {
            foreach (var task in tasks.Where(c => c.Status == BannedWordTestStatus.FAILED && IsViolation(c.FailReason)))
            {
                try
                {
                    using var lockObj = RedisHelper.Lock($"BannedWordTest:Finalize:{task.Id}", 30);
                    if (lockObj == null)
                    {
                        continue;
                    }

                    var current = GetTask(task.Id);
                    if (current == null
                        || current.Status != BannedWordTestStatus.FAILED
                        || !IsViolation(current.FailReason))
                    {
                        continue;
                    }

                    current.Status = BannedWordTestStatus.VIOLATION;
                    current.UpdateTime = DateTime.Now;
                    SaveTask(current);

                    RecordConfirmed(current, current.FailReason);
                    AppendToConfirmedBannedWord(current.Word);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "重新归类确认违规任务失败: TaskId={TaskId}", task.Id);
                }
            }
        }

        /// <summary>
        /// 轮询运行中测试任务的上游结果
        /// </summary>
        private void PollRunningTasks()
        {
            var watchedIds = _watching.Keys.ToList();
            if (watchedIds.Count == 0)
            {
                return;
            }

            // 批量读取测试任务，避免逐个访问 Redis
            var tasks = new Dictionary<string, BannedWordTestTask>();
            var taskJson = RedisHelper.HMGet(TaskHashKey, watchedIds.ToArray()) ?? [];
            for (var i = 0; i < watchedIds.Count && i < taskJson.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(taskJson[i]))
                {
                    continue;
                }

                try
                {
                    var task = taskJson[i].ToObject<BannedWordTestTask>();
                    if (task != null)
                    {
                        tasks[watchedIds[i]] = task;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "解析违规词测试任务失败: TaskId={TaskId}", watchedIds[i]);
                }
            }

            // 批量读取关联的代理任务，避免在高并发测试时产生大量数据库查询
            var mjTaskIds = tasks.Values
                .Where(c => c.Status == BannedWordTestStatus.RUNNING && !string.IsNullOrWhiteSpace(c.MjTaskId))
                .Select(c => c.MjTaskId)
                .Distinct()
                .ToList();
            var mjTaskMap = GetTaskInfosByIds(mjTaskIds)
                .GroupBy(c => c.Id)
                .ToDictionary(c => c.Key, c => c.First());

            foreach (var taskId in watchedIds)
            {
                if (!tasks.TryGetValue(taskId, out var task))
                {
                    _watching.TryRemove(taskId, out _);
                    continue;
                }

                if (task.Status != BannedWordTestStatus.RUNNING)
                {
                    FinishWatch(task);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(task.MjTaskId))
                {
                    FinalizeFailed(task, "测试任务缺少代理任务 ID");
                    continue;
                }

                var submitTime = task.SubmitTime ?? task.CreateTime;

                if (!mjTaskMap.TryGetValue(task.MjTaskId, out var mjTask))
                {
                    if (DateTime.Now - submitTime > TimeSpan.FromMinutes(2))
                    {
                        FinalizeFailed(task, "上游任务未创建，可能已提交失败");
                    }
                    continue;
                }

                if (mjTask.IsCompleted)
                {
                    if (mjTask.Status == TaskStatus.SUCCESS)
                    {
                        FinalizePassed(task);
                    }
                    else if (mjTask.Status == TaskStatus.CANCEL)
                    {
                        FinalizeFailed(task, "上游任务已取消");
                    }
                    else
                    {
                        var violationText = string.Join(" | ", new[] { mjTask.FailReason, mjTask.Description }
                            .Where(c => !string.IsNullOrWhiteSpace(c)));

                        if (IsViolation(violationText))
                        {
                            var reason = IsViolation(mjTask.FailReason)
                                ? mjTask.FailReason
                                : (IsViolation(mjTask.Description) ? mjTask.Description : violationText);
                            FinalizeViolation(task, reason);
                        }
                        else
                        {
                            FinalizeFailed(task, FirstNotEmpty(mjTask.FailReason, mjTask.Description, "上游任务失败"));
                        }
                    }
                }
                else if (DateTime.Now - submitTime > TaskTimeout)
                {
                    FinalizeFailed(task, "测试超时，未能获取上游结果");
                }
            }
        }

        /// <summary>
        /// 按 ID 批量查询代理任务
        /// 分批查询，避免 IN 条件过长
        /// </summary>
        /// <param name="ids">代理任务 ID 列表</param>
        /// <returns>代理任务集合</returns>
        private List<TaskInfo> GetTaskInfosByIds(List<string> ids)
        {
            var result = new List<TaskInfo>();
            if (ids == null || ids.Count == 0)
            {
                return result;
            }

            foreach (var chunk in ids.Distinct().Chunk(200))
            {
                var chunkIds = chunk.ToList();
                var items = _freeSql.Select<TaskInfo>()
                    .Where(c => chunkIds.Contains(c.Id))
                    .ToList();
                result.AddRange(items);
            }

            return result;
        }

        /// <summary>
        /// 按全局并发数提交待处理任务
        /// </summary>
        private async Task DispatchPendingTasksAsync()
        {
            var queueLength = (int)RedisHelper.LLen(QueueKey);
            if (queueLength <= 0)
            {
                return;
            }

            var concurrency = GetGlobalConcurrency();

            // 单次最多处理 25 条，避免长时间占用后台线程；未获得并发槽位的任务会重新入队
            var processCount = Math.Min(25, queueLength);
            var deferred = new List<string>();

            for (var i = 0; i < processCount; i++)
            {
                var taskId = NormalizeQueueId(RedisHelper.LPop(QueueKey));
                if (string.IsNullOrWhiteSpace(taskId))
                {
                    break;
                }

                var task = GetTask(taskId);
                if (task == null || task.Status != BannedWordTestStatus.NOT_STARTED)
                {
                    continue;
                }

                if (GetRunningCount() >= concurrency)
                {
                    deferred.Add(task.Id);
                    continue;
                }

                var submitted = await SubmitTaskAsync(task, concurrency);
                if (!submitted)
                {
                    deferred.Add(task.Id);
                }
            }

            if (deferred.Count > 0)
            {
                RedisHelper.RPush(QueueKey, deferred.ToArray());
            }
        }

        /// <summary>
        /// 提交单个测试任务到绘图链路
        /// </summary>
        /// <param name="task">测试任务</param>
        /// <param name="concurrency">本次调度使用的全局并发数</param>
        /// <returns>是否已经成功开始提交（false 表示需要重新排队）</returns>
        private async Task<bool> SubmitTaskAsync(BannedWordTestTask task, int concurrency)
        {
            try
            {
                using var lockObj = RedisHelper.Lock($"BannedWordTest:Submit:{task.Id}", 60);
                if (lockObj == null)
                {
                    return false;
                }

                // 拿到锁后重新读取，避免多节点重复提交
                task = GetTask(task.Id);
                if (task == null || task.Status != BannedWordTestStatus.NOT_STARTED)
                {
                    return true;
                }

                if (GetRunningCount() >= concurrency)
                {
                    return false;
                }

                var prompt = task.Prompt;
                var info = new TaskInfo
                {
                    Id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{RandomHelper.RandomNumbers(3)}",
                    SubmitTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Status = TaskStatus.NOT_START,
                    Action = TaskAction.IMAGINE,
                    Prompt = prompt,
                    PromptEn = prompt,
                    Description = $"/imagine {prompt}",
                    BotType = prompt.Contains("--niji", StringComparison.OrdinalIgnoreCase)
                        ? EBotType.NIJI_JOURNEY
                        : EBotType.MID_JOURNEY,
                    ClientIp = "127.0.0.1",
                    AccountFilter = new AccountFilter()
                };

                // 解析速度模式，保证测试任务与正常提交使用相同的账号过滤规则
                var mode = ParseSpeedMode(prompt);
                if (mode != null)
                {
                    info.Mode = mode;
                    info.RequestMode = mode;
                    info.AccountFilter.Modes = [mode.Value];
                }

                // 标记为违规词测试任务：工作线程会跳过本地违规词检查
                info.SetProperty(Constants.TASK_PROPERTY_BANNED_WORD_TEST_ID, task.Id);
                info.SetProperty(Constants.TASK_PROPERTY_BOT_TYPE, info.BotType.GetDescription());

                SubmitResultVO result;
                try
                {
                    result = await _taskService.SubmitImagine(info, []);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "提交违规词测试任务异常: TaskId={TaskId}, Word={Word}", task.Id, task.Word);
                    FinalizeFailed(task, "提交测试任务异常: " + ex.Message);
                    return true;
                }

                var code = result?.Code ?? ReturnCode.FAILURE;
                if (code == ReturnCode.SUCCESS || code == ReturnCode.IN_QUEUE)
                {
                    // 提交期间可能已被人工删除或重置，此时不再恢复测试记录
                    var current = GetTask(task.Id);
                    if (current == null || current.Status != BannedWordTestStatus.NOT_STARTED)
                    {
                        _logger.Information("违规词测试任务在提交期间被删除或重置，停止跟踪: TaskId={TaskId}, MjTaskId={MjTaskId}",
                            task.Id, info.Id);
                        return true;
                    }

                    current.Status = BannedWordTestStatus.RUNNING;
                    current.MjTaskId = info.Id;
                    current.SubmitTime = DateTime.Now;
                    current.FailReason = null;
                    current.UpdateTime = DateTime.Now;
                    SaveTask(current);

                    RedisHelper.HSet(RunningHashKey, current.Id, DateTime.Now.Ticks);
                    _watching.TryAdd(current.Id, 0);

                    _logger.Information("违规词测试任务已提交: TaskId={TaskId}, MjTaskId={MjTaskId}, Word={Word}, Code={Code}",
                        current.Id, info.Id, current.Word, code);
                    return true;
                }

                // 提交阶段就被上游拦截，同样视为确认违规
                var reason = FirstNotEmpty(result?.Description, "提交测试任务失败");
                if (IsViolation(reason))
                {
                    FinalizeViolation(task, reason);
                }
                else
                {
                    FinalizeFailed(task, reason);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "提交违规词测试任务失败: TaskId={TaskId}", task.Id);
                FinalizeFailed(task, "提交测试任务失败，请稍后重试");
                return true;
            }
        }

        /// <summary>
        /// 任务未触发违规
        /// </summary>
        /// <param name="task">测试任务</param>
        private void FinalizePassed(BannedWordTestTask task)
        {
            task.Status = BannedWordTestStatus.PASSED;
            task.FailReason = null;
            Finalize(task);
        }

        /// <summary>
        /// 任务确认触发违规
        /// 同时追加确认违规记录，并自动把敏感词追加到“确认违规词”违规词条
        /// </summary>
        /// <param name="task">测试任务</param>
        /// <param name="reason">上游失败原因</param>
        private void FinalizeViolation(BannedWordTestTask task, string reason)
        {
            task.Status = BannedWordTestStatus.VIOLATION;
            task.FailReason = Truncate(reason, 1000);

            // 只有成功完成状态流转的节点才记录，避免多节点重复累加
            if (!Finalize(task))
            {
                return;
            }

            RecordConfirmed(task, reason);
            AppendToConfirmedBannedWord(task.Word);
        }

        /// <summary>
        /// 任务测试失败，无法判定是否违规
        /// </summary>
        /// <param name="task">测试任务</param>
        /// <param name="reason">失败原因</param>
        private void FinalizeFailed(BannedWordTestTask task, string reason)
        {
            task.Status = BannedWordTestStatus.FAILED;
            task.FailReason = Truncate(reason, 1000);
            Finalize(task);
        }

        /// <summary>
        /// 完成测试任务并移除运行中标记
        /// 使用 Redis 分布式锁保证多节点同时判定时只生效一次
        /// </summary>
        /// <param name="task">测试任务</param>
        /// <returns>是否由当前节点完成状态流转</returns>
        private bool Finalize(BannedWordTestTask task)
        {
            using var lockObj = RedisHelper.Lock($"BannedWordTest:Finalize:{task.Id}", 30);
            if (lockObj == null)
            {
                return false;
            }

            var current = GetTask(task.Id);
            if (current == null)
            {
                _watching.TryRemove(task.Id, out _);
                return false;
            }

            // 其他节点已经处理完成
            if (current.Status is BannedWordTestStatus.PASSED
                or BannedWordTestStatus.VIOLATION
                or BannedWordTestStatus.FAILED)
            {
                FinishWatch(current);
                return false;
            }

            task.UpdateTime = DateTime.Now;
            task.FinishTime = DateTime.Now;
            SaveTask(task);

            RedisHelper.HDel(RunningHashKey, task.Id);
            _watching.TryRemove(task.Id, out _);
            return true;
        }

        /// <summary>
        /// 移除运行中任务监听
        /// </summary>
        /// <param name="task">测试任务</param>
        private void FinishWatch(BannedWordTestTask task)
        {
            _watching.TryRemove(task?.Id ?? string.Empty, out _);

            if (task != null)
            {
                RedisHelper.HDel(RunningHashKey, task.Id);
            }
        }

        /// <summary>
        /// 写入确认违规记录（按敏感词累加）
        /// </summary>
        /// <param name="task">测试任务</param>
        /// <param name="reason">上游失败原因</param>
        private void RecordConfirmed(BannedWordTestTask task, string reason)
        {
            if (string.IsNullOrWhiteSpace(task?.Word))
            {
                return;
            }

            try
            {
                var field = task.Word.Trim().ToLowerInvariant();
                var existing = GetConfirmed(field);

                if (existing == null)
                {
                    existing = new BannedWordTestConfirmed
                    {
                        Word = task.Word,
                        Count = 1,
                        FirstConfirmedTime = DateTime.Now
                    };
                }
                else
                {
                    existing.Count++;
                }

                existing.TestTaskId = task.Id;
                existing.MjTaskId = task.MjTaskId;
                existing.Prompt = task.Prompt;
                existing.Reason = Truncate(reason, 1000);
                existing.LastConfirmedTime = DateTime.Now;

                RedisHelper.HSet(ConfirmedHashKey, field, existing);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "记录确认违规词失败: Word={Word}", task.Word);
            }
        }

        /// <summary>
        /// 把确认违规词自动追加到“确认违规词”违规词条
        /// 违规词条不存在时自动创建，已存在时按词去重追加
        /// </summary>
        /// <param name="word">确认违规的敏感词</param>
        private void AppendToConfirmedBannedWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return;
            }

            try
            {
                using var lockObj = RedisHelper.Lock(BannedWordAppendLockKey, 30);
                if (lockObj == null)
                {
                    _logger.Warning("追加确认违规词到违规词条失败，未获取到锁: Word={Word}", word);
                    return;
                }

                var keyword = word.Trim();
                var group = _freeSql.Select<BannedWord>()
                    .Where(c => c.Id == Constants.CONFIRMED_VIOLATION_BANNED_WORD_ID)
                    .First();

                group ??= _freeSql.Select<BannedWord>()
                    .Where(c => c.Name == "确认违规词")
                    .First();

                if (group == null)
                {
                    group = new BannedWord
                    {
                        Id = Constants.CONFIRMED_VIOLATION_BANNED_WORD_ID,
                        Name = "确认违规词",
                        Keywords = [keyword],
                        Enable = true,
                        Description = "违规词测试自动追加的确认违规词",
                        CreateTime = DateTime.Now,
                        UpdateTime = DateTime.Now
                    };

                    _freeSql.Insert(group).ExecuteAffrows();
                }
                else
                {
                    var keywords = group.Keywords ?? [];
                    if (keywords.Any(c => string.Equals(c, keyword, StringComparison.OrdinalIgnoreCase)))
                    {
                        return;
                    }

                    keywords.Add(keyword);

                    group.Keywords = keywords;
                    group.UpdateTime = DateTime.Now;
                    _freeSql.Save(group);
                }

                // 清理违规词缓存，使新词立即生效
                _taskService.ClearBannedWordsCache();

                _logger.Information("确认违规词已追加到违规词条: Word={Word}, BannedWordId={BannedWordId}", keyword, group.Id);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "追加确认违规词到违规词条失败: Word={Word}", word);
            }
        }

        /// <summary>
        /// 如果“确认违规词”违规词条不存在，则用当前确认违规记录初始化
        /// 只在词条缺失时执行一次，避免覆盖管理员后续的人工调整
        /// </summary>
        private void SyncConfirmedBannedWordGroup()
        {
            try
            {
                using var lockObj = RedisHelper.Lock(BannedWordAppendLockKey, 30);
                if (lockObj == null)
                {
                    return;
                }

                var group = _freeSql.Select<BannedWord>()
                    .Where(c => c.Id == Constants.CONFIRMED_VIOLATION_BANNED_WORD_ID)
                    .First();
                group ??= _freeSql.Select<BannedWord>()
                    .Where(c => c.Name == "确认违规词")
                    .First();

                if (group != null)
                {
                    return;
                }

                var words = GetAllConfirmed()
                    .Where(c => !string.IsNullOrWhiteSpace(c.Word))
                    .Select(c => c.Word.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (words.Count == 0)
                {
                    return;
                }

                group = new BannedWord
                {
                    Id = Constants.CONFIRMED_VIOLATION_BANNED_WORD_ID,
                    Name = "确认违规词",
                    Keywords = words,
                    Enable = true,
                    Description = "违规词测试自动追加的确认违规词",
                    CreateTime = DateTime.Now,
                    UpdateTime = DateTime.Now
                };

                _freeSql.Save(group);
                _taskService.ClearBannedWordsCache();

                _logger.Information("已用确认违规记录初始化违规词条: Count={Count}", words.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "初始化确认违规词条失败");
            }
        }

        /// <summary>
        /// 获取当前全局运行中任务数
        /// </summary>
        /// <returns>运行中任务数</returns>
        private int GetRunningCount()
        {
            var fields = RedisHelper.HKeys(RunningHashKey);
            if (fields == null || fields.Length == 0)
            {
                return 0;
            }

            // 清理已结束/已删除任务留下的脏字段，避免并发槽位被永久占用
            var stale = new List<string>();
            foreach (var field in fields)
            {
                var task = GetTask(field);
                if (task == null || task.Status != BannedWordTestStatus.RUNNING)
                {
                    stale.Add(field);
                }
            }

            if (stale.Count > 0)
            {
                RedisHelper.HDel(RunningHashKey, stale.ToArray());
            }

            return (int)RedisHelper.HLen(RunningHashKey);
        }

        /// <summary>
        /// 获取当前全局并发数
        /// </summary>
        /// <returns>1-20 之间的并发数，缺省为 3</returns>
        private int GetGlobalConcurrency()
        {
            try
            {
                var concurrency = RedisHelper.HGet<int>(SettingsHashKey, ConcurrencySettingField);
                if (concurrency <= 0)
                {
                    return DefaultConcurrency;
                }

                return Math.Clamp(concurrency, 1, MaxConcurrency);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "读取违规词测试并发数失败，使用默认值: {DefaultConcurrency}", DefaultConcurrency);
                return DefaultConcurrency;
            }
        }

        /// <summary>
        /// 保存测试任务
        /// </summary>
        /// <param name="task">测试任务</param>
        private void SaveTask(BannedWordTestTask task)
        {
            RedisHelper.HSet(TaskHashKey, task.Id, task);
        }

        /// <summary>
        /// 读取单个测试任务
        /// </summary>
        /// <param name="taskId">测试任务 ID</param>
        /// <returns>测试任务；不存在时返回 null</returns>
        private static BannedWordTestTask GetTask(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId))
            {
                return null;
            }

            var json = RedisHelper.HGet(TaskHashKey, taskId);
            return string.IsNullOrWhiteSpace(json) ? null : json.ToObject<BannedWordTestTask>();
        }

        /// <summary>
        /// 读取单条确认违规记录
        /// </summary>
        /// <param name="field">归一化后的敏感词</param>
        /// <returns>确认违规记录；不存在时返回 null</returns>
        private static BannedWordTestConfirmed GetConfirmed(string field)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                return null;
            }

            var json = RedisHelper.HGet(ConfirmedHashKey, field);
            return string.IsNullOrWhiteSpace(json) ? null : json.ToObject<BannedWordTestConfirmed>();
        }

        /// <summary>
        /// 队列中的 ID 可能是 JSON 字符串（带引号），统一去掉首尾引号后再使用
        /// </summary>
        /// <param name="value">队列原始值</param>
        /// <returns>测试任务 ID</returns>
        private static string NormalizeQueueId(string value)
        {
            return value?.Trim().Trim('"');
        }

        /// <summary>
        /// 获取全部测试任务
        /// </summary>
        /// <returns>测试任务集合</returns>
        private List<BannedWordTestTask> GetAllTasks()
        {
            var result = new List<BannedWordTestTask>();
            var dict = RedisHelper.HGetAll(TaskHashKey);
            if (dict == null || dict.Count == 0)
            {
                return result;
            }

            foreach (var item in dict)
            {
                try
                {
                    var task = item.Value.ToObject<BannedWordTestTask>();
                    if (task != null && !string.IsNullOrWhiteSpace(task.Id))
                    {
                        result.Add(task);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "解析违规词测试任务失败，已跳过: Field={Field}", item.Key);
                }
            }

            return result;
        }

        /// <summary>
        /// 获取全部确认违规记录
        /// </summary>
        /// <returns>确认违规记录</returns>
        private List<BannedWordTestConfirmed> GetAllConfirmed()
        {
            var result = new List<BannedWordTestConfirmed>();
            var dict = RedisHelper.HGetAll(ConfirmedHashKey);
            if (dict == null || dict.Count == 0)
            {
                return result;
            }

            foreach (var item in dict)
            {
                try
                {
                    var confirmed = item.Value.ToObject<BannedWordTestConfirmed>();
                    if (confirmed != null && !string.IsNullOrWhiteSpace(confirmed.Word))
                    {
                        result.Add(confirmed);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "解析确认违规记录失败，已跳过: Field={Field}", item.Key);
                }
            }

            return result;
        }

        /// <summary>
        /// 定期维护：补投未开始任务、接管孤儿运行中任务
        /// 不清理任何测试记录，测试记录默认永久保留
        /// </summary>
        private void MaintainState()
        {
            if (DateTime.Now - _lastMaintenanceTime < TimeSpan.FromMinutes(10))
            {
                return;
            }

            _lastMaintenanceTime = DateTime.Now;

            try
            {
                var tasks = GetAllTasks();
                EnsurePendingQueue(tasks);
                AdoptOrphanRunningTasks(tasks);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "违规词测试状态维护失败");
            }
        }

        /// <summary>
        /// 认领孤儿的运行中任务
        /// 提交节点宕机后，其它节点在下一个维护周期接管跟踪，避免任务永久停留在“进行中”
        /// </summary>
        /// <param name="tasks">全部测试任务</param>
        private void AdoptOrphanRunningTasks(List<BannedWordTestTask> tasks)
        {
            foreach (var task in tasks.Where(c => c.Status == BannedWordTestStatus.RUNNING))
            {
                if (_watching.TryAdd(task.Id, 0))
                {
                    RedisHelper.HSet(RunningHashKey, task.Id, DateTime.Now.Ticks);
                    _logger.Information("接管违规词测试运行中任务: TaskId={TaskId}, Word={Word}", task.Id, task.Word);
                }
            }
        }

        /// <summary>
        /// 确保所有未开始任务都在队列中
        /// 进程异常退出或队列被清理后，下一次维护周期会自动补投
        /// </summary>
        /// <param name="tasks">全部测试任务</param>
        private void EnsurePendingQueue(List<BannedWordTestTask> tasks)
        {
            var pending = tasks.Where(c => c.Status == BannedWordTestStatus.NOT_STARTED).ToList();
            if (pending.Count == 0)
            {
                return;
            }

            var queued = (RedisHelper.LRange(QueueKey, 0, -1) ?? [])
                .Select(NormalizeQueueId)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToHashSet();

            var missing = pending
                .Where(c => !queued.Contains(c.Id))
                .Select(c => c.Id)
                .Distinct()
                .ToList();

            if (missing.Count > 0)
            {
                RedisHelper.RPush(QueueKey, missing.ToArray());
                _logger.Warning("违规词测试队列补投 {Count} 个未开始任务", missing.Count);
            }
        }

        /// <summary>
        /// 根据模板生成实际提交的提示词
        /// 规则：
        /// 1. 模板为空时直接使用敏感词；
        /// 2. 模板包含 {word} 占位符时替换占位符；
        /// 3. 模板本身包含该敏感词时按原样使用（兼容用户直接粘贴带示例词的模板）；
        /// 4. 模板包含其它敏感词时，把示例词替换为当前敏感词；
        /// 5. 其他情况把敏感词拼接到提示词描述部分前部，避免落到 --ar/--v 等参数之后。
        /// </summary>
        /// <param name="word">敏感词</param>
        /// <param name="template">提示词模板</param>
        /// <param name="knownWords">当前全部敏感词</param>
        /// <returns>最终提示词</returns>
        private static string BuildPrompt(string word, string template, List<string> knownWords)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return word;
            }

            var trimmed = template.Trim();
            if (WordPlaceholderRegex.IsMatch(trimmed))
            {
                return WordPlaceholderRegex.Replace(trimmed, _ => word).Trim();
            }

            var wordRegex = new Regex($@"(?<![\w-]){Regex.Escape(word)}(?![\w-])", RegexOptions.IgnoreCase);
            if (wordRegex.IsMatch(trimmed))
            {
                return trimmed;
            }

            // 模板里已经写了其它示例词时，替换为当前敏感词，避免示例词造成干扰
            foreach (var knownWord in (knownWords ?? [])
                .Where(c => !string.IsNullOrWhiteSpace(c) && c.Length >= 2 && !string.Equals(c, word, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.Length))
            {
                if (!trimmed.Contains(knownWord, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var knownRegex = new Regex($@"(?<![\w-]){Regex.Escape(knownWord)}(?![\w-])", RegexOptions.IgnoreCase);
                if (knownRegex.IsMatch(trimmed))
                {
                    return knownRegex.Replace(trimmed, _ => word);
                }
            }

            var flagMatch = PromptFlagRegex.Match(trimmed);
            if (!flagMatch.Success)
            {
                return $"{word}, {trimmed}";
            }

            var flagIndex = flagMatch.Index == 0 ? 0 : flagMatch.Index;
            var textPart = trimmed[..flagIndex].TrimEnd(' ', ',');
            var flagPart = trimmed[flagIndex..].TrimStart();

            return string.IsNullOrWhiteSpace(textPart)
                ? $"{word} {flagPart}"
                : $"{word}, {textPart} {flagPart}";
        }

        /// <summary>
        /// 解析提示词中的速度模式
        /// </summary>
        /// <param name="prompt">提示词</param>
        /// <returns>速度模式</returns>
        private static GenerationSpeedMode? ParseSpeedMode(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return null;
            }

            if (prompt.Contains("--relax", StringComparison.OrdinalIgnoreCase))
            {
                return GenerationSpeedMode.RELAX;
            }

            if (prompt.Contains("--turbo", StringComparison.OrdinalIgnoreCase))
            {
                return GenerationSpeedMode.TURBO;
            }

            if (prompt.Contains("--fast", StringComparison.OrdinalIgnoreCase))
            {
                return GenerationSpeedMode.FAST;
            }

            return null;
        }

        /// <summary>
        /// 判断上游失败原因是否为违规拦截
        /// </summary>
        /// <param name="reason">失败原因</param>
        /// <returns>是否确认违规</returns>
        private static bool IsViolation(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return false;
            }

            // 本地违规词检查的提示不是上游结论，不能计入确认违规
            if (reason.Contains("可能包含敏感词"))
            {
                return false;
            }

            var lower = reason.ToLowerInvariant();
            return ViolationMarkers.Any(c => lower.Contains(c));
        }

        /// <summary>
        /// 标准化敏感词列表
        /// 支持逗号、中文逗号、分号、换行分隔；去空、去重、转小写
        /// </summary>
        /// <param name="words">原始敏感词</param>
        /// <returns>标准化后的敏感词</returns>
        private static List<string> NormalizeWords(IEnumerable<string> words)
        {
            var result = (words ?? [])
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .SelectMany(c => c.Split([',', '，', ';', '；', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                .Select(c => c.Trim().ToLowerInvariant())
                .Where(c => c.Length > 0)
                .Distinct()
                .ToList();

            if (result.Count == 0)
            {
                throw new LogicException("违规词不能为空");
            }

            if (result.Count > MaxWordsPerRequest)
            {
                throw new LogicException($"单次最多测试 {MaxWordsPerRequest} 个违规词");
            }

            if (result.Any(c => c.Length > MaxWordLength))
            {
                throw new LogicException($"单个违规词长度不能超过 {MaxWordLength} 个字符");
            }

            return result;
        }

        /// <summary>
        /// 标准化提示词模板
        /// </summary>
        /// <param name="template">原始模板</param>
        /// <returns>标准化后的模板</returns>
        private static string NormalizeTemplate(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return null;
            }

            var result = template.Trim();
            if (result.Length > MaxTemplateLength)
            {
                throw new LogicException($"提示词模板长度不能超过 {MaxTemplateLength} 个字符");
            }

            return result;
        }

        /// <summary>
        /// 标准化并发数
        /// </summary>
        /// <param name="concurrency">原始并发数</param>
        /// <returns>1-20 之间的并发数</returns>
        private static int NormalizeConcurrency(int concurrency)
        {
            if (concurrency <= 0)
            {
                return DefaultConcurrency;
            }

            return Math.Clamp(concurrency, 1, MaxConcurrency);
        }

        /// <summary>
        /// 检查 Redis 是否可用
        /// </summary>
        private static void EnsureRedis()
        {
            if (!IsRedisValid())
            {
                throw new LogicException("违规词测试依赖 Redis，请先在系统设置中启用 Redis");
            }
        }

        /// <summary>
        /// Redis 是否可用
        /// </summary>
        /// <returns>是否可用</returns>
        private static bool IsRedisValid()
        {
            return GlobalConfiguration.Setting?.IsValidRedis == true;
        }

        /// <summary>
        /// 取第一个非空字符串
        /// </summary>
        /// <param name="values">候选值</param>
        /// <returns>第一个非空值</returns>
        private static string FirstNotEmpty(params string[] values)
        {
            return values.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? string.Empty;
        }

        /// <summary>
        /// 截断字符串
        /// </summary>
        /// <param name="value">原值</param>
        /// <param name="maxLength">最大长度</param>
        /// <returns>截断后的值</returns>
        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return value.Length <= maxLength ? value : value[..maxLength];
        }
    }
}

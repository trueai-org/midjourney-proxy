using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using CSRedis;
using Midjourney.Base;
using Midjourney.Base.Dto;
using Midjourney.Base.Models;
using Midjourney.Base.Services;
using Midjourney.Base.Util;
using Midjourney.Services;
using Xunit;

namespace Midjourney.Tests
{
    /// <summary>
    /// 选号集成测试：真实 Redis + 200 个账号 + 完整 ChooseInstance / GetAliveInstances / GetDescribeInstance 链路。
    /// 需要环境变量 MJ_TEST_REDIS，且连接串必须使用独立 prefix=accseltest:；未配置时直接跳过。
    /// </summary>
    [Collection("RedisIntegration")]
    public class AccountSelectionChooseInstanceIntegrationTests
    {
        private const int AccountCount = 200;
        private const int PerformanceThresholdMilliseconds = 500;

        [Fact]
        public void GetRuntimeSnapshots_IncludesInactiveAccountDisplayValues()
        {
            if (!TryPrepareEnvironment(out var previousSetting, out var keysToDelete))
            {
                return;
            }

            var today = DateTime.Now.Date;
            keysToDelete.Add(CounterHelper.GetAccountTodayTotalCountKey(today));

            try
            {
                var spec = new EdgeAccountSpec
                {
                    Id = "display00",
                    YouChuan = true,
                    Enable = false,
                    FastAvailable = 10,
                    RelaxCount = 1,
                    FastQueue = 3,
                    RelaxQueue = 2,
                    DescribeQueue = 1,
                    FastCore = 1,
                    RelaxCore = 2,
                    DescribeCore = 1,
                    TodayFast = 4,
                    TodayTurbo = 5,
                    TodayRelax = 6
                };

                var (service, _) = CreateEdgeService(spec, keysToDelete, today);
                using var executor = new BackgroundTaskExecutor("AccountSelectionIntegrationTest");
                var accountService = CreateAccountService(executor);
                var snapshots = accountService.GetRuntimeSnapshots([service]);

                Assert.True(snapshots.TryGetValue(service, out var snapshot));
                Assert.False(snapshot.IsAlive);
                Assert.Equal(6, snapshot.GetQueueTaskCount(service.Account));
                Assert.Equal(4, snapshot.GetRunningTaskCount(service.Account));
                Assert.Equal(9, snapshot.GetTodayCount(GenerationSpeedMode.FAST));
                Assert.Equal(6, snapshot.GetTodayCount(GenerationSpeedMode.RELAX));
            }
            finally
            {
                Cleanup(previousSetting, keysToDelete);
            }
        }

        [Fact]
        public void ChooseInstance_200YouChuan_MultiModeRefreshesSnapshot()
        {
            if (!TryPrepareEnvironment(out var previousSetting, out var keysToDelete))
            {
                return;
            }

            var today = DateTime.Now.Date;
            keysToDelete.Add(CounterHelper.GetAccountTodayTotalCountKey(today));

            var instances = new List<DiscordService>(AccountCount);

            try
            {
                using var executor = new BackgroundTaskExecutor("AccountSelectionIntegrationTest");
                var accountService = CreateAccountService(executor);
                var relaxReset = DateTime.Now.Date.AddDays(1);

                for (var i = 0; i < AccountCount; i++)
                {
                    var spec = new EdgeAccountSpec
                    {
                        Id = $"multi{i:D4}",
                        YouChuan = true,
                        FastAvailable = 10,
                        FastQueue = i % 10,
                        RelaxReset = relaxReset
                    };

                    var (service, _) = CreateEdgeService(spec, keysToDelete, today);
                    instances.Add(service);
                    accountService.AddInstance(service);
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var (chosen, mode) = accountService.ChooseInstance(
                    accountFilter: new AccountFilter { Modes = [GenerationSpeedMode.RELAX, GenerationSpeedMode.FAST] },
                    isNewTask: true);
                sw.Stop();

                Assert.NotNull(chosen);
                Assert.Equal(GenerationSpeedMode.FAST, mode);

                var chosenIndex = instances.FindIndex(x => ReferenceEquals(x, chosen));
                Assert.True(chosenIndex >= 0, "选中的实例不在候选列表中");
                Assert.Equal(0, chosenIndex % 10);

                Assert.True(sw.ElapsedMilliseconds < PerformanceThresholdMilliseconds,
                    $"200 账号多模式 ChooseInstance 耗时过长：{sw.ElapsedMilliseconds}ms（阈值 {PerformanceThresholdMilliseconds}ms）");
            }
            finally
            {
                Cleanup(previousSetting, keysToDelete);
            }
        }

        [Fact]
        public void ChooseInstance_200DiscordAccounts_UsesOneBatchSnapshot()
        {
            RunChooseInstanceTest(youChuan: false, GenerationSpeedMode.FAST);
        }

        [Fact]
        public void ChooseInstance_200YouChuanAccounts_UsesOneBatchSnapshot()
        {
            RunChooseInstanceTest(youChuan: true, GenerationSpeedMode.RELAX);
        }

        [Fact]
        public void GetDescribeInstance_200YouChuanAccounts_UsesOneBatchSnapshot()
        {
            RunDescribeTest();
        }

        private static void RunChooseInstanceTest(bool youChuan, GenerationSpeedMode mode)
        {
            if (!TryPrepareEnvironment(out var previousSetting, out var keysToDelete))
            {
                return;
            }

            var instances = new List<DiscordService>(AccountCount);

            try
            {
                SeedAccounts(keysToDelete, youChuan, isDescribe: false);

                using var executor = new BackgroundTaskExecutor("AccountSelectionIntegrationTest");
                var accountService = CreateAccountService(executor);

                foreach (var instance in CreateServices(keysToDelete, youChuan, isDescribe: false))
                {
                    instances.Add(instance);
                    accountService.AddInstance(instance);
                }

                AssertBatchAlive(accountService);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var (chosen, confirmMode) = accountService.ChooseInstance(
                    accountFilter: new AccountFilter { Modes = [mode] },
                    isNewTask: true);
                sw.Stop();

                Assert.NotNull(chosen);
                Assert.Equal(mode, confirmMode);

                var chosenIndex = instances.FindIndex(x => ReferenceEquals(x, chosen));
                Assert.True(chosenIndex >= 0, "选中的实例不在候选列表中");
                Assert.Equal(0, chosenIndex % 10);

                Assert.True(sw.ElapsedMilliseconds < PerformanceThresholdMilliseconds,
                    $"200 账号 ChooseInstance 耗时过长：{sw.ElapsedMilliseconds}ms（阈值 {PerformanceThresholdMilliseconds}ms），可能退回了逐账号查询");
            }
            finally
            {
                Cleanup(previousSetting, keysToDelete);
            }
        }

        private static void RunDescribeTest()
        {
            if (!TryPrepareEnvironment(out var previousSetting, out var keysToDelete))
            {
                return;
            }

            var instances = new List<DiscordService>(AccountCount);

            try
            {
                SeedAccounts(keysToDelete, youChuan: true, isDescribe: true);

                using var executor = new BackgroundTaskExecutor("AccountSelectionIntegrationTest");
                var accountService = CreateAccountService(executor);

                foreach (var instance in CreateServices(keysToDelete, youChuan: true, isDescribe: true))
                {
                    instances.Add(instance);
                    accountService.AddInstance(instance);
                }

                AssertBatchAlive(accountService);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var chosen = accountService.GetDescribeInstance();
                sw.Stop();

                Assert.NotNull(chosen);
                Assert.True(sw.ElapsedMilliseconds < PerformanceThresholdMilliseconds,
                    $"200 账号 GetDescribeInstance 耗时过长：{sw.ElapsedMilliseconds}ms（阈值 {PerformanceThresholdMilliseconds}ms），可能退回了逐账号查询");
            }
            finally
            {
                Cleanup(previousSetting, keysToDelete);
            }
        }

        private static bool TryPrepareEnvironment(out Setting? previousSetting, out HashSet<string> keysToDelete)
        {
            previousSetting = null;
            keysToDelete = null!;

            var connectionString = Environment.GetEnvironmentVariable("MJ_TEST_REDIS");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return false;
            }

            if (!connectionString.Contains("prefix=accseltest:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("MJ_TEST_REDIS 必须使用独立 prefix=accseltest:<唯一值>:，避免污染业务数据");
            }

            RedisHelper.Initialization(new CSRedisClient(connectionString));

            previousSetting = GlobalConfiguration.Setting;
            GlobalConfiguration.Setting = new Setting
            {
                YouChuanForcePreferRelax = false
            };

            keysToDelete = new HashSet<string>
            {
                CounterHelper.FastTaskAvailableCountKey,
                CounterHelper.GetYouchuanRelaxCountKey(DateTime.Now.Date),
                CounterHelper.GetDescribeCountKey(DateTime.Now.Date)
            };

            return true;
        }

        private static DiscordAccountService CreateAccountService(BackgroundTaskExecutor executor)
        {
            return new DiscordAccountService(
                new FakeNotifyService(),
                new FakeHttpClientFactory(),
                new BestWaitIdleRule(),
                executor);
        }

        private static void AssertBatchAlive(DiscordAccountService accountService)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var alive = accountService.GetAliveInstances();
            sw.Stop();

            Assert.Equal(AccountCount, alive.Count);
            Assert.True(sw.ElapsedMilliseconds < PerformanceThresholdMilliseconds,
                $"200 账号 GetAliveInstances 耗时过长：{sw.ElapsedMilliseconds}ms（阈值 {PerformanceThresholdMilliseconds}ms），可能退回了逐账号查询");
        }

        private static IEnumerable<DiscordService> CreateServices(HashSet<string> keysToDelete, bool youChuan, bool isDescribe)
        {
            for (var i = 0; i < AccountCount; i++)
            {
                var id = $"{(youChuan ? "ycc" : "accsel")}{i:D4}";
                var account = new DiscordAccount
                {
                    Id = id,
                    ChannelId = id,
                    Enable = true,
                    Lock = false,
                    IsYouChuan = youChuan,
                    IsOfficial = false,
                    IsDescribe = isDescribe,
                    IsRelaxVideo = false,
                    AllowModes = [],
                    CoreSize = 3,
                    RelaxCoreSize = 3,
                    QueueSize = 10,
                    RelaxQueueSize = 10,
                    QueueCount = 10,
                    YouChuanRelaxDailyLimit = 17,
                    DayDrawLimit = -1,
                    DayRelaxDrawLimit = -1
                };

                var service = (DiscordService)RuntimeHelpers.GetUninitializedObject(typeof(DiscordService));
                SetField(service, "_account", account);
                service.IsInit = true;

                if (youChuan)
                {
                    SetField(service, "_ymTaskService", DispatchProxy.Create<IYmTaskService, FakeYmTaskProxy>());
                }

                var fastQueue = new RedisQueue<TaskInfoQueue>(RedisHelper.Instance, $"fast:{id}", account.QueueSize);
                var relaxQueue = new RedisQueue<TaskInfoQueue>(RedisHelper.Instance, $"relax:{id}", account.RelaxQueueSize);
                var describeQueue = new RedisQueue<TaskInfoQueue>(RedisHelper.Instance, $"describe:{id}", account.QueueSize);
                var fastConcurrent = new RedisConcurrent(RedisHelper.Instance, $"fast:{id}");
                var relaxConcurrent = new RedisConcurrent(RedisHelper.Instance, $"relax:{id}");
                var describeConcurrent = new RedisConcurrent(RedisHelper.Instance, $"describe:{id}");

                SetField(service, "_defaultOrFastQueue", fastQueue);
                SetField(service, "_relaxQueue", relaxQueue);
                SetField(service, "_describeQueue", describeQueue);
                SetField(service, "_defaultOrFastConcurrent", fastConcurrent);
                SetField(service, "_relaxConcurrent", relaxConcurrent);
                SetField(service, "_describeConcurrent", describeConcurrent);

                keysToDelete.Add(fastQueue.Key);
                keysToDelete.Add(relaxQueue.Key);
                keysToDelete.Add(describeQueue.Key);

                yield return service;
            }
        }

        private static void SeedAccounts(HashSet<string> keysToDelete, bool youChuan, bool isDescribe)
        {
            var today = DateTime.Now.Date;

            using var pipe = RedisHelper.StartPipe();

            for (var i = 0; i < AccountCount; i++)
            {
                var id = $"{(youChuan ? "ycc" : "accsel")}{i:D4}";

                // 快速任务剩余次数
                pipe.HSet(CounterHelper.FastTaskAvailableCountKey, id, 10);

                if (youChuan)
                {
                    // 悠船慢速今日已用次数：0，确保不会提前触发慢速上限
                    pipe.HSet(CounterHelper.GetYouchuanRelaxCountKey(today), id, 0);
                }
                else
                {
                    // Discord WS 已连接（无连接中 key）
                    var initConnectKey = $"CSRedisClientLock:DiscordAccountInitConnectLock:{id}";
                    pipe.Set(initConnectKey, "1");
                    keysToDelete.Add(initConnectKey);
                }

                if (isDescribe)
                {
                    // 图生文今日已用次数：0
                    pipe.HSet(CounterHelper.GetDescribeCountKey(today), id, 0);

                    // 图生文队列长度 0~9，确保 GetDescribeInstance 能取到可用账号
                    var describeQueueCount = i % 10;
                    if (describeQueueCount > 0)
                    {
                        pipe.RPush($"queue:describe:{id}", CreateQueueValues(describeQueueCount));
                    }
                }
                else if (youChuan)
                {
                    // 慢速队列长度 0~9，BestWaitIdle 应选择队列最短的账号
                    var relaxQueueCount = i % 10;
                    if (relaxQueueCount > 0)
                    {
                        pipe.RPush($"queue:relax:{id}", CreateQueueValues(relaxQueueCount));
                    }
                }
                else
                {
                    // 快速队列长度 0~9，BestWaitIdle 应选择队列最短的账号
                    var fastQueueCount = i % 10;
                    if (fastQueueCount > 0)
                    {
                        pipe.RPush($"queue:fast:{id}", CreateQueueValues(fastQueueCount));
                    }
                }
            }

            pipe.EndPipe();
        }

        private static object[] CreateQueueValues(int count)
        {
            var values = new object[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = "x";
            }
            return values;
        }

        private static void Cleanup(Setting? previousSetting, HashSet<string> keysToDelete)
        {
            GlobalConfiguration.Setting = previousSetting!;

            try
            {
                RedisHelper.Del(keysToDelete.ToArray());
            }
            catch
            {
                // 测试清理失败不影响断言结果
            }
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException($"字段不存在：{fieldName}");
            }

            field.SetValue(target, value);
        }

        /// <summary>
        /// 差分测试：同一批账号/同一 Redis 状态下，批量快照判定必须与实时逐项判定完全一致。
        /// 覆盖悠船/官方/Discord、固定模式、AllowModes、日限额、慢速/图生文/视频边界、并发和 WS 状态。
        /// </summary>
        [Fact]
        public void BatchSnapshot_MatchesLiveBusinessRules()
        {
            if (!TryPrepareEnvironment(out var previousSetting, out var keysToDelete))
            {
                return;
            }

            var today = DateTime.Now.Date;
            keysToDelete.Add(CounterHelper.GetAccountTodayTotalCountKey(today));

            try
            {
                var specs = new[]
                {
                    new EdgeAccountSpec { Id = "edge00", YouChuan = true },
                    new EdgeAccountSpec
                    {
                        Id = "edge01", YouChuan = true, FastAvailable = 13, RelaxCount = 4,
                        TodayFast = 4, TodayTurbo = 5, DayDrawLimit = 100, IsDescribe = true, DescribeCount = 187
                    },
                    new EdgeAccountSpec
                    {
                        Id = "edge02", YouChuan = true, FastAvailable = 7, RelaxCount = 17, RelaxQueue = 10,
                        RelaxCore = 3, DayRelaxDrawLimit = 100, TodayRelax = 3, IsDescribe = true, DescribeCount = 200,
                        FastQueue = 10
                    },
                    new EdgeAccountSpec
                    {
                        Id = "edge03", YouChuan = true, FastAvailable = 7, QueueSize = 0, RelaxQueueSize = 0,
                        FastQueue = 10, RelaxQueue = 10
                    },
                    new EdgeAccountSpec { Id = "edge04", YouChuan = true, FastAvailable = 20, FastCore = 3 },
                    new EdgeAccountSpec
                    {
                        Id = "edge05", YouChuan = true, FastAvailable = 20, IsDescribe = true,
                        RelaxReset = DateTime.Now.Date.AddDays(1), PicReset = DateTime.Now.Date.AddDays(1)
                    },
                    new EdgeAccountSpec
                    {
                        Id = "edge06", YouChuan = true, FastAvailable = 7, Mode = GenerationSpeedMode.FAST,
                        AllowModes = [GenerationSpeedMode.RELAX]
                    },
                    new EdgeAccountSpec
                    {
                        Id = "edge07", YouChuan = true, FastAvailable = 7, Mode = GenerationSpeedMode.RELAX,
                        AllowModes = [GenerationSpeedMode.FAST]
                    },
                    new EdgeAccountSpec
                    {
                        Id = "edge08", Official = true, FastAvailable = 9, IsDescribe = true, IsRelaxVideo = false
                    },
                    new EdgeAccountSpec
                    {
                        Id = "edge09", Official = true, FastAvailable = 2, IsDescribe = true, IsRelaxVideo = true
                    },
                    new EdgeAccountSpec { Id = "edge10", WsInit = true },
                    new EdgeAccountSpec { Id = "edge11", WsInit = true, WsConnecting = true },
                    new EdgeAccountSpec { Id = "edge12", WsInit = false },
                    new EdgeAccountSpec { Id = "edge13", WsInit = true, Lock = true },
                    new EdgeAccountSpec { Id = "edge14", YouChuan = true, Enable = false }
                };

                var entries = new List<(DiscordService Service, AccountRuntimeSource Source)>(specs.Length);
                foreach (var spec in specs)
                {
                    entries.Add(CreateEdgeService(spec, keysToDelete, today));
                }

                var sources = entries.Select(x => x.Source).ToList();
                var snapshots = AccountRuntimeSnapshotLoader.Load(sources, includeDescribeQuota: true);
                var modes = new[] { GenerationSpeedMode.RELAX, GenerationSpeedMode.FAST, GenerationSpeedMode.TURBO };

                for (var i = 0; i < entries.Count; i++)
                {
                    var service = entries[i].Service;
                    var source = entries[i].Source;
                    var snapshot = snapshots[i];
                    var account = service.Account;

                    Assert.True(service.IsAlive == snapshot.IsAlive,
                        $"{account.ChannelId} IsAlive live={service.IsAlive} batch={snapshot.IsAlive}");

                    // 选号只会对存活候选执行 AllowContinue/Describe/Video/队列统计；
                    // 前置存活判断失败的账号，快照按设计不读取这些值，不能参与差分。
                    if (!source.PrecheckAlive)
                    {
                        continue;
                    }

                    foreach (var mode in modes)
                    {
                        var liveContinue = service.IsAllowContinue(mode);
                        var batchContinue = service.IsAllowContinue(mode, snapshot);
                        Assert.True(liveContinue == batchContinue,
                            $"{account.ChannelId} IsAllowContinue({mode}) live={liveContinue} batch={batchContinue}");

                        var liveVideo = service.IsAllowGenerateVideo(mode);
                        var batchVideo = service.IsAllowGenerateVideo(mode, snapshot);
                        Assert.True(liveVideo == batchVideo,
                            $"{account.ChannelId} IsAllowGenerateVideo({mode}) live={liveVideo} batch={batchVideo}");
                    }

                    var liveDescribe = service.IsAllowDescribe();
                    var batchDescribe = service.IsAllowDescribe(snapshot);
                    Assert.True(liveDescribe == batchDescribe,
                        $"{account.ChannelId} IsAllowDescribe live={liveDescribe} batch={batchDescribe}");

                    Assert.True(service.GetQueueTaskCount == snapshot.GetQueueTaskCount(account),
                        $"{account.ChannelId} GetQueueTaskCount live={service.GetQueueTaskCount} batch={snapshot.GetQueueTaskCount(account)}");

                    Assert.True(service.GetRunningTaskCount == snapshot.GetRunningTaskCount(account),
                        $"{account.ChannelId} GetRunningTaskCount live={service.GetRunningTaskCount} batch={snapshot.GetRunningTaskCount(account)}");
                }
            }
            finally
            {
                Cleanup(previousSetting, keysToDelete);
            }
        }

        private static (DiscordService Service, AccountRuntimeSource Source) CreateEdgeService(
            EdgeAccountSpec spec,
            HashSet<string> keysToDelete,
            DateTime today)
        {
            var account = new DiscordAccount
            {
                Id = spec.Id,
                ChannelId = spec.Id,
                Enable = spec.Enable,
                Lock = spec.Lock,
                IsYouChuan = spec.YouChuan,
                IsOfficial = spec.Official,
                IsDescribe = spec.IsDescribe,
                IsRelaxVideo = spec.IsRelaxVideo,
                AllowModes = spec.AllowModes,
                Mode = spec.Mode,
                CoreSize = spec.CoreSize,
                RelaxCoreSize = spec.RelaxCoreSize,
                QueueSize = spec.QueueSize,
                RelaxQueueSize = spec.RelaxQueueSize,
                QueueCount = spec.QueueCount,
                YouChuanRelaxDailyLimit = spec.YouChuanRelaxDailyLimit,
                DayDrawLimit = spec.DayDrawLimit,
                DayRelaxDrawLimit = spec.DayRelaxDrawLimit,
                YouChuanRelaxedReset = spec.RelaxReset,
                YouChuanPicreadReset = spec.PicReset
            };

            var service = (DiscordService)RuntimeHelpers.GetUninitializedObject(typeof(DiscordService));
            SetField(service, "_account", account);
            service.IsInit = true;

            if (spec.YouChuan || spec.Official)
            {
                SetField(service, "_ymTaskService", DispatchProxy.Create<IYmTaskService, FakeYmTaskProxy>());
            }

            var fastQueue = new RedisQueue<TaskInfoQueue>(RedisHelper.Instance, $"fast:{spec.Id}", account.QueueSize);
            var relaxQueue = new RedisQueue<TaskInfoQueue>(RedisHelper.Instance, $"relax:{spec.Id}", account.RelaxQueueSize);
            var describeQueue = new RedisQueue<TaskInfoQueue>(RedisHelper.Instance, $"describe:{spec.Id}", account.QueueSize);
            var fastConcurrent = new RedisConcurrent(RedisHelper.Instance, $"fast:{spec.Id}");
            var relaxConcurrent = new RedisConcurrent(RedisHelper.Instance, $"relax:{spec.Id}");
            var describeConcurrent = new RedisConcurrent(RedisHelper.Instance, $"describe:{spec.Id}");

            SetField(service, "_defaultOrFastQueue", fastQueue);
            SetField(service, "_relaxQueue", relaxQueue);
            SetField(service, "_describeQueue", describeQueue);
            SetField(service, "_defaultOrFastConcurrent", fastConcurrent);
            SetField(service, "_relaxConcurrent", relaxConcurrent);
            SetField(service, "_describeConcurrent", describeConcurrent);

            keysToDelete.Add(fastQueue.Key);
            keysToDelete.Add(relaxQueue.Key);
            keysToDelete.Add(describeQueue.Key);

            RedisHelper.HSet(CounterHelper.FastTaskAvailableCountKey, spec.Id, spec.FastAvailable);

            if (spec.YouChuan)
            {
                RedisHelper.HSet(CounterHelper.GetYouchuanRelaxCountKey(today), spec.Id, spec.RelaxCount);
            }

            var todayTotalHash = CounterHelper.GetAccountTodayTotalCountKey(today);
            keysToDelete.Add(todayTotalHash);
            RedisHelper.HSet(todayTotalHash, CounterHelper.GetAccountTodayTotalCountField(spec.Id, GenerationSpeedMode.RELAX), spec.TodayRelax);
            RedisHelper.HSet(todayTotalHash, CounterHelper.GetAccountTodayTotalCountField(spec.Id, GenerationSpeedMode.FAST), spec.TodayFast);
            RedisHelper.HSet(todayTotalHash, CounterHelper.GetAccountTodayTotalCountField(spec.Id, GenerationSpeedMode.TURBO), spec.TodayTurbo);

            var describeHash = CounterHelper.GetDescribeCountKey(today);
            keysToDelete.Add(describeHash);
            RedisHelper.HSet(describeHash, spec.Id, spec.DescribeCount);

            SeedEdgeQueue(fastQueue.Key, spec.FastQueue);
            SeedEdgeQueue(relaxQueue.Key, spec.RelaxQueue);
            SeedEdgeQueue(describeQueue.Key, spec.DescribeQueue);

            var keyProvider = (IAccountRuntimeKeyProvider)service;
            for (var slot = 0; slot < spec.FastCore; slot++)
            {
                var key = keyProvider.GetFastConcurrencyKey(slot);
                RedisHelper.Set(key, "1");
                keysToDelete.Add(key);
            }

            for (var slot = 0; slot < spec.RelaxCore; slot++)
            {
                var key = keyProvider.GetRelaxConcurrencyKey(slot);
                RedisHelper.Set(key, "1");
                keysToDelete.Add(key);
            }

            for (var slot = 0; slot < spec.DescribeCore; slot++)
            {
                var key = keyProvider.GetDescribeConcurrencyKey(slot);
                RedisHelper.Set(key, "1");
                keysToDelete.Add(key);
            }

            if (account.IsDiscord)
            {
                if (spec.WsInit)
                {
                    var key = DiscordWebSocketService.InitConnectLockRedisKey(account);
                    RedisHelper.Set(key, "1");
                    keysToDelete.Add(key);
                }

                if (spec.WsConnecting)
                {
                    var key = DiscordWebSocketService.InitConnectingRedisKey(account);
                    RedisHelper.Set(key, "1");
                    keysToDelete.Add(key);
                }
            }

            var source = new AccountRuntimeSource
            {
                Account = account,
                Keys = service,
                PrecheckAlive = service.IsAlivePrecheck(),
                IsSessionValid = service.IsSessionValidInMemory
            };

            return (service, source);
        }

        private static void SeedEdgeQueue(string key, int count)
        {
            if (count <= 0)
            {
                return;
            }

            var values = new object[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = "x";
            }

            RedisHelper.RPush(key, values);
        }

        private sealed class EdgeAccountSpec
        {
            public string Id { get; init; } = "";
            public bool YouChuan { get; init; }
            public bool Official { get; init; }
            public bool Enable { get; init; } = true;
            public bool Lock { get; init; }
            public bool WsInit { get; init; }
            public bool WsConnecting { get; init; }
            public bool IsDescribe { get; init; }
            public bool IsRelaxVideo { get; init; }
            public int FastAvailable { get; init; } = 7;
            public int RelaxCount { get; init; }
            public int TodayRelax { get; init; }
            public int TodayFast { get; init; }
            public int TodayTurbo { get; init; }
            public int DescribeCount { get; init; }
            public int FastQueue { get; init; }
            public int RelaxQueue { get; init; }
            public int DescribeQueue { get; init; }
            public int FastCore { get; init; }
            public int RelaxCore { get; init; }
            public int DescribeCore { get; init; }
            public int CoreSize { get; init; } = 3;
            public int RelaxCoreSize { get; init; } = 3;
            public int QueueSize { get; init; } = 10;
            public int RelaxQueueSize { get; init; } = 10;
            public int QueueCount { get; init; } = 10;
            public int YouChuanRelaxDailyLimit { get; init; } = 17;
            public int DayDrawLimit { get; init; } = -1;
            public int DayRelaxDrawLimit { get; init; } = -1;
            public GenerationSpeedMode? Mode { get; init; }
            public List<GenerationSpeedMode> AllowModes { get; init; } = [];
            public DateTime? RelaxReset { get; init; }
            public DateTime? PicReset { get; init; }
        }

        private sealed class FakeNotifyService : INotifyService
        {
            public Task NotifyTaskChange(TaskInfo task) => Task.CompletedTask;

            public (int fastTotal, int notYcRelaxTotal, int ycRelaxTotal) GetTodayAccountCount() => (0, 0, 0);
        }

        private sealed class FakeHttpClientFactory : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new HttpClient();
        }
    }

    /// <summary>
    /// 测试用 IYmTaskService 代理：只返回非空 Token，其他成员不会被选号路径调用。
    /// </summary>
    public class FakeYmTaskProxy : DispatchProxy
    {
        /// <summary>
        /// 拦截接口调用，返回选号路径需要的 YouChuanToken/OfficialToken。
        /// </summary>
        /// <param name="targetMethod">接口方法。</param>
        /// <param name="args">参数。</param>
        /// <returns>Token 或默认值。</returns>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_YouChuanToken" || targetMethod?.Name == "get_OfficialToken")
            {
                return "probe-token";
            }

            var returnType = targetMethod?.ReturnType;
            if (returnType == null || returnType == typeof(void))
            {
                return null;
            }

            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType.IsValueType)
            {
                return Activator.CreateInstance(returnType);
            }

            return null;
        }
    }

    /// <summary>
    /// 真实 Redis 集成测试集合，禁止与其他测试并行，避免静态 RedisHelper 互相覆盖。
    /// </summary>
    [CollectionDefinition("RedisIntegration", DisableParallelization = true)]
    public class RedisIntegrationCollection
    {
    }
}

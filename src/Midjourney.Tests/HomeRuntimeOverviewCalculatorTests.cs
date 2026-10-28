using CSRedis;
using Midjourney.Base;
using Midjourney.Base.Dto;
using Midjourney.Base.Models;
using Midjourney.Services;
using Xunit;

namespace Midjourney.Tests
{
    /// <summary>
    /// 首页实时并发与队列概览计算器测试。
    /// 需要环境变量 MJ_TEST_REDIS，且连接串必须使用独立 prefix=accseltest:；未配置时直接跳过。
    /// 用例直接向 Redis 预置队列/并发 key，不构造 DiscordService 实例，验证「启用账号 + 悠船口径」的汇总规则。
    /// </summary>
    [Collection("RedisIntegration")]
    public class HomeRuntimeOverviewCalculatorTests
    {
        /// <summary>
        /// 覆盖口径：所有启用账号的快速队列/快速并发计入汇总；慢速队列/并发只统计悠船账号。
        /// Total 为账号配置上限之和，与 Redis 中是否真的占满无关。
        /// </summary>
        [Fact]
        public void Calculate_SumsQueueAndConcurrencyByAccountType()
        {
            if (!TryPrepareEnvironment(out var previousSetting, out var keysToDelete))
            {
                return;
            }

            try
            {
                // 非悠船账号：快速并发上限 4、快速队列上限 7；慢速配置存在但慢速运行态必须被忽略
                // 关闭图生文（IsDescribe=false），其 describe 队列/并发必须被忽略
                var fastAccount = new DiscordAccount
                {
                    Id = "ovfast",
                    ChannelId = "ovfast",
                    Enable = true,
                    IsYouChuan = false,
                    IsDescribe = false,
                    CoreSize = 4,
                    QueueSize = 7,
                    RelaxCoreSize = 1,
                    RelaxQueueSize = 1
                };

                // 悠船账号：快速并发上限 3、快速队列上限 5、慢速并发上限 2、慢速队列上限 4
                // 开启图生文，describe 复用 CoreSize/QueueSize，每日 200 次额度
                var youChuanAccount = new DiscordAccount
                {
                    Id = "ovycc",
                    ChannelId = "ovycc",
                    Enable = true,
                    IsYouChuan = true,
                    IsDescribe = true,
                    CoreSize = 3,
                    QueueSize = 5,
                    RelaxCoreSize = 2,
                    RelaxQueueSize = 4
                };

                // 快速剩余次数达到阈值（>= 8），可用容量才会被计入
                keysToDelete.Add(CounterHelper.FastTaskAvailableCountKey);
                CounterHelper.SetFastTaskAvailableCount("ovfast", 10);
                CounterHelper.SetFastTaskAvailableCount("ovycc", 10);

                // 快速队列：非悠船 1 条、悠船 2 条，合计 3
                SeedQueue(keysToDelete, RedisQueue.BuildQueueKey("fast:ovfast"), 1);
                SeedQueue(keysToDelete, RedisQueue.BuildQueueKey("fast:ovycc"), 2);

                // 慢速队列：悠船 2 条计入；非悠船 3 条必须被忽略
                SeedQueue(keysToDelete, RedisQueue.BuildQueueKey("relax:ovycc"), 2);
                SeedQueue(keysToDelete, RedisQueue.BuildQueueKey("relax:ovfast"), 3);

                // 快速并发：两个账号各占用 1 个槽位，合计 2
                SeedConcurrency(keysToDelete, "fast:ovfast", 0);
                SeedConcurrency(keysToDelete, "fast:ovycc", 0);

                // 慢速并发：悠船占用 2 个槽位；非悠船占用 1 个槽位必须被忽略
                SeedConcurrency(keysToDelete, "relax:ovycc", 0);
                SeedConcurrency(keysToDelete, "relax:ovycc", 1);
                SeedConcurrency(keysToDelete, "relax:ovfast", 0);

                // 图生文队列：悠船 2 条计入；非悠船（IsDescribe=false）3 条必须被忽略
                SeedQueue(keysToDelete, RedisQueue.BuildQueueKey("describe:ovycc"), 2);
                SeedQueue(keysToDelete, RedisQueue.BuildQueueKey("describe:ovfast"), 3);

                // 图生文并发：悠船占用 1 个槽位计入；非悠船（IsDescribe=false）占用 1 个槽位必须被忽略
                SeedConcurrency(keysToDelete, "describe:ovycc", 0);
                SeedConcurrency(keysToDelete, "describe:ovfast", 0);

                // 悠船图生文今日已用 150 次，剩余额度 50 次
                SeedDescribeCount(keysToDelete, "ovycc", 150);

                var overview = AccountRuntimeOverviewCalculator.Calculate(new[] { fastAccount, youChuanAccount });

                Assert.NotNull(overview);
                Assert.Equal(2, overview.FastConcurrency);
                Assert.Equal(7, overview.FastConcurrencyTotal);
                Assert.Equal(3, overview.FastQueueCount);
                Assert.Equal(12, overview.FastQueueCountTotal);
                Assert.Equal(2, overview.YouChuanRelaxConcurrency);
                Assert.Equal(2, overview.YouChuanRelaxConcurrencyTotal);
                Assert.Equal(2, overview.YouChuanRelaxQueueCount);
                Assert.Equal(4, overview.YouChuanRelaxQueueCountTotal);

                // 可用容量：快速剩余 >= 8 时按 (上限 - 已用) 累加；悠船慢速剩余 > 0 时同理
                // 非悠船 (4-1)+(3-1)=5；队列 (7-1)+(5-2)=9
                Assert.Equal(5, overview.FastConcurrencyAvailable);
                Assert.Equal(9, overview.FastQueueAvailable);
                // 悠船慢速并发 2-2=0；慢速队列 4-2=2
                Assert.Equal(0, overview.YouChuanRelaxConcurrencyAvailable);
                Assert.Equal(2, overview.YouChuanRelaxQueueAvailable);

                // 图生文：只统计 IsDescribe=true 的悠船账号，非悠船 describe 运行态必须被忽略
                Assert.Equal(1, overview.DescribeConcurrency);
                Assert.Equal(3, overview.DescribeConcurrencyTotal);
                Assert.Equal(2, overview.DescribeConcurrencyAvailable);
                Assert.Equal(2, overview.DescribeQueueCount);
                Assert.Equal(5, overview.DescribeQueueCountTotal);
                Assert.Equal(3, overview.DescribeQueueAvailable);

                // 悠船图生文额度：每日 200 次，已用 150，剩余 50
                Assert.Equal(150, overview.YouChuanDescribeQuotaUsed);
                Assert.Equal(200, overview.YouChuanDescribeQuotaTotal);
                Assert.Equal(50, overview.YouChuanDescribeQuotaRemaining);
            }
            finally
            {
                Cleanup(previousSetting, keysToDelete);
            }
        }

        /// <summary>
        /// 快速剩余次数低于阈值 8 时，可用快速并发/队列均为 0；已用与总数仍如实返回。
        /// </summary>
        [Fact]
        public void Calculate_FastRemainingBelowThreshold_FastAvailableIsZero()
        {
            if (!TryPrepareEnvironment(out var previousSetting, out var keysToDelete))
            {
                return;
            }

            try
            {
                var account = new DiscordAccount
                {
                    Id = "ovlow",
                    ChannelId = "ovlow",
                    Enable = true,
                    IsYouChuan = false,
                    CoreSize = 12,
                    QueueSize = 10
                };

                // 剩余 7 次，未达到 8 次阈值
                keysToDelete.Add(CounterHelper.FastTaskAvailableCountKey);
                CounterHelper.SetFastTaskAvailableCount("ovlow", 7);
                SeedQueue(keysToDelete, RedisQueue.BuildQueueKey("fast:ovlow"), 2);
                SeedConcurrency(keysToDelete, "fast:ovlow", 0);

                var overview = AccountRuntimeOverviewCalculator.Calculate(new[] { account });

                Assert.NotNull(overview);
                Assert.Equal(12, overview.FastConcurrencyTotal);
                Assert.Equal(1, overview.FastConcurrency);
                Assert.Equal(2, overview.FastQueueCount);
                Assert.Equal(0, overview.FastConcurrencyAvailable);
                Assert.Equal(0, overview.FastQueueAvailable);
            }
            finally
            {
                Cleanup(previousSetting, keysToDelete);
            }
        }

        /// <summary>
        /// 覆盖图生文额度口径：悠船额度用尽或当日已重置时可用并发/队列为 0；
        /// 官方账号不限额度、空闲槽位始终计入可用；关闭图生文的账号不计入任何 describe 指标与额度。
        /// </summary>
        [Fact]
        public void Calculate_DescribeQuotaExhaustedOrReset_AvailableIsZeroAndOfficialHasNoQuota()
        {
            if (!TryPrepareEnvironment(out var previousSetting, out var keysToDelete))
            {
                return;
            }

            try
            {
                // 悠船：今日已用 200 次，额度用尽
                var exhausted = new DiscordAccount
                {
                    Id = "ovdsc1",
                    ChannelId = "ovdsc1",
                    Enable = true,
                    IsYouChuan = true,
                    IsDescribe = true,
                    CoreSize = 2,
                    QueueSize = 3
                };

                // 悠船：当日已触发图生文上限重置，即使已用次数很少也视为无剩余额度
                var reset = new DiscordAccount
                {
                    Id = "ovdsc2",
                    ChannelId = "ovdsc2",
                    Enable = true,
                    IsYouChuan = true,
                    IsDescribe = true,
                    CoreSize = 2,
                    QueueSize = 3,
                    YouChuanPicreadReset = DateTime.Now.Date.AddDays(1)
                };

                // 悠船：关闭图生文，不参与 describe 指标与额度统计
                var disabled = new DiscordAccount
                {
                    Id = "ovdsc3",
                    ChannelId = "ovdsc3",
                    Enable = true,
                    IsYouChuan = true,
                    IsDescribe = false,
                    CoreSize = 5,
                    QueueSize = 5
                };

                // 官方账号：不限制图生文次数
                var official = new DiscordAccount
                {
                    Id = "ovdsc4",
                    ChannelId = "ovdsc4",
                    Enable = true,
                    IsOfficial = true,
                    IsDescribe = true,
                    CoreSize = 4,
                    QueueSize = 6
                };

                SeedDescribeCount(keysToDelete, "ovdsc1", 200);
                SeedDescribeCount(keysToDelete, "ovdsc2", 10);
                SeedDescribeCount(keysToDelete, "ovdsc3", 0);

                SeedQueue(keysToDelete, RedisQueue.BuildQueueKey("describe:ovdsc1"), 1);
                SeedQueue(keysToDelete, RedisQueue.BuildQueueKey("describe:ovdsc2"), 1);
                SeedQueue(keysToDelete, RedisQueue.BuildQueueKey("describe:ovdsc4"), 2);

                SeedConcurrency(keysToDelete, "describe:ovdsc1", 0);
                SeedConcurrency(keysToDelete, "describe:ovdsc4", 0);

                var overview = AccountRuntimeOverviewCalculator.Calculate(new[] { exhausted, reset, disabled, official });

                Assert.NotNull(overview);

                // 总额度只累加开启图生文的悠船账号：200 * 2
                Assert.Equal(400, overview.YouChuanDescribeQuotaTotal);
                // 已用 200 + 10；关闭图生文的 ovdsc3 不计入
                Assert.Equal(210, overview.YouChuanDescribeQuotaUsed);
                // 两个悠船账号剩余均为 0
                Assert.Equal(0, overview.YouChuanDescribeQuotaRemaining);

                // describe 汇总只含三个开启图生文的账号（exhausted 2/3、reset 2/3、official 4/6）
                Assert.Equal(2, overview.DescribeConcurrency);
                Assert.Equal(8, overview.DescribeConcurrencyTotal);
                // 队列：1 + 1 + 2
                Assert.Equal(4, overview.DescribeQueueCount);
                Assert.Equal(12, overview.DescribeQueueCountTotal);

                // 悠船无剩余额度 → 不计入可用；官方 4-1=3 并发、6-2=4 队列
                Assert.Equal(3, overview.DescribeConcurrencyAvailable);
                Assert.Equal(4, overview.DescribeQueueAvailable);
            }
            finally
            {
                Cleanup(previousSetting, keysToDelete);
            }
        }

        /// <summary>
        /// 覆盖临近每日 200 次上限的口径：悠船可用并发/队列按「今日剩余可提交次数」截断，
        /// 不能把空闲槽位全部显示为可用；额度字段本身仍按「200 - 今日已用」计算。
        /// </summary>
        [Fact]
        public void Calculate_DescribeNearDailyLimit_AvailableCappedBySubmittable()
        {
            if (!TryPrepareEnvironment(out var previousSetting, out var keysToDelete))
            {
                return;
            }

            try
            {
                // 已用 195、运行中并发 1、排队 2 → 剩余可提交 200-195-1-2=2；
                // 空闲并发 3-1=2、空闲队列 5-2=3，队列可用必须被截断为 0
                var nearLimit = new DiscordAccount
                {
                    Id = "ovdsc5",
                    ChannelId = "ovdsc5",
                    Enable = true,
                    IsYouChuan = true,
                    IsDescribe = true,
                    CoreSize = 3,
                    QueueSize = 5
                };

                // 已用 190、并发占满 2/2、排队 1 → 剩余可提交 200-190-2-1=7；
                // 空闲并发 0、空闲队列 4-1=3，队列可用为 3
                var coreBusy = new DiscordAccount
                {
                    Id = "ovdsc6",
                    ChannelId = "ovdsc6",
                    Enable = true,
                    IsYouChuan = true,
                    IsDescribe = true,
                    CoreSize = 2,
                    QueueSize = 4
                };

                SeedDescribeCount(keysToDelete, "ovdsc5", 195);
                SeedConcurrency(keysToDelete, "describe:ovdsc5", 0);
                SeedQueue(keysToDelete, RedisQueue.BuildQueueKey("describe:ovdsc5"), 2);

                SeedDescribeCount(keysToDelete, "ovdsc6", 190);
                SeedConcurrency(keysToDelete, "describe:ovdsc6", 0);
                SeedConcurrency(keysToDelete, "describe:ovdsc6", 1);
                SeedQueue(keysToDelete, RedisQueue.BuildQueueKey("describe:ovdsc6"), 1);

                var overview = AccountRuntimeOverviewCalculator.Calculate(new[] { nearLimit, coreBusy });

                Assert.NotNull(overview);

                // 已用/总数仍如实统计：并发 1+2、队列 2+1；上限 3+2、5+4
                Assert.Equal(3, overview.DescribeConcurrency);
                Assert.Equal(5, overview.DescribeConcurrencyTotal);
                Assert.Equal(3, overview.DescribeQueueCount);
                Assert.Equal(9, overview.DescribeQueueCountTotal);

                // nearLimit：并发可用 min(2, 2)=2，队列可用 min(3, 2-2)=0
                // coreBusy：并发可用 min(0, 7)=0，队列可用 min(3, 7-0)=3
                // 旧口径会直接累加空闲槽位得到并发 2 + 队列 6，明显高估
                Assert.Equal(2, overview.DescribeConcurrencyAvailable);
                Assert.Equal(3, overview.DescribeQueueAvailable);
                // 两个账号剩余可提交次数合计 2+7=9，可用并发 + 可用队列不得超过它
                Assert.True(overview.DescribeConcurrencyAvailable + overview.DescribeQueueAvailable <= 2 + 7);

                // 额度字段口径不变：400 次总额度、已用 385、剩余 15（逐账号 max(0, 200 - 已用) 累加，即 5+10）
                Assert.Equal(385, overview.YouChuanDescribeQuotaUsed);
                Assert.Equal(400, overview.YouChuanDescribeQuotaTotal);
                Assert.Equal(15, overview.YouChuanDescribeQuotaRemaining);
            }
            finally
            {
                Cleanup(previousSetting, keysToDelete);
            }
        }

        /// <summary>
        /// 覆盖口径：入参为 null 或过滤后为空时不访问 Redis，返回非 null 且全部字段为 0。
        /// </summary>
        [Fact]
        public void Calculate_NullOrEmptyAccounts_ReturnsZeroOverview()
        {
            if (!TryPrepareEnvironment(out var previousSetting, out var keysToDelete))
            {
                return;
            }

            try
            {
                AssertAllZero(AccountRuntimeOverviewCalculator.Calculate(null!));
                AssertAllZero(AccountRuntimeOverviewCalculator.Calculate(new List<DiscordAccount>()));
            }
            finally
            {
                Cleanup(previousSetting, keysToDelete);
            }
        }

        /// <summary>
        /// 初始化测试 Redis 环境；未配置 MJ_TEST_REDIS 时返回 false 表示跳过。
        /// </summary>
        /// <param name="previousSetting">测试前的全局配置，用于 finally 恢复。</param>
        /// <param name="keysToDelete">测试预置的 key，用于 finally 清理。</param>
        /// <returns>是否已完成初始化。</returns>
        /// <exception cref="InvalidOperationException">连接串未使用独立 prefix。</exception>
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

            keysToDelete = new HashSet<string>();
            return true;
        }

        /// <summary>
        /// 预置队列任务，并登记待清理 key。
        /// </summary>
        /// <param name="keysToDelete">待清理 key 集合。</param>
        /// <param name="key">队列 key。</param>
        /// <param name="count">任务条数，小于等于 0 时不写入。</param>
        private static void SeedQueue(HashSet<string> keysToDelete, string key, int count)
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
            keysToDelete.Add(key);
        }

        /// <summary>
        /// 预置一个已占用的并发槽位（值与业务一致，任意非空值即可让 EXISTS 返回 true），并登记待清理 key。
        /// </summary>
        /// <param name="keysToDelete">待清理 key 集合。</param>
        /// <param name="concurrentName">并发名称，例如 <c>fast:{channelId}</c>。</param>
        /// <param name="index">槽位序号。</param>
        private static void SeedConcurrency(HashSet<string> keysToDelete, string concurrentName, int index)
        {
            var key = RedisConcurrent.BuildTokenKey(concurrentName, index);
            RedisHelper.Set(key, "1");
            keysToDelete.Add(key);
        }

        /// <summary>
        /// 断言概览非 null 且全部统计字段为 0。
        /// </summary>
        /// <param name="overview">待断言的概览。</param>
        private static void AssertAllZero(HomeRuntimeOverviewDto? overview)
        {
            Assert.NotNull(overview);
            Assert.Equal(0, overview.FastConcurrency);
            Assert.Equal(0, overview.FastConcurrencyTotal);
            Assert.Equal(0, overview.FastConcurrencyAvailable);
            Assert.Equal(0, overview.FastQueueCount);
            Assert.Equal(0, overview.FastQueueCountTotal);
            Assert.Equal(0, overview.FastQueueAvailable);
            Assert.Equal(0, overview.YouChuanRelaxConcurrency);
            Assert.Equal(0, overview.YouChuanRelaxConcurrencyTotal);
            Assert.Equal(0, overview.YouChuanRelaxConcurrencyAvailable);
            Assert.Equal(0, overview.YouChuanRelaxQueueCount);
            Assert.Equal(0, overview.YouChuanRelaxQueueCountTotal);
            Assert.Equal(0, overview.YouChuanRelaxQueueAvailable);
            Assert.Equal(0, overview.DescribeConcurrency);
            Assert.Equal(0, overview.DescribeConcurrencyTotal);
            Assert.Equal(0, overview.DescribeConcurrencyAvailable);
            Assert.Equal(0, overview.DescribeQueueCount);
            Assert.Equal(0, overview.DescribeQueueCountTotal);
            Assert.Equal(0, overview.DescribeQueueAvailable);
            Assert.Equal(0, overview.YouChuanDescribeQuotaUsed);
            Assert.Equal(0, overview.YouChuanDescribeQuotaTotal);
            Assert.Equal(0, overview.YouChuanDescribeQuotaRemaining);
        }

        /// <summary>
        /// 预置悠船图生文今日已用次数，并登记待清理 key。
        /// </summary>
        /// <param name="keysToDelete">待清理 key 集合。</param>
        /// <param name="channelId">账号频道 ID（Hash field）。</param>
        /// <param name="count">今日已用图生文次数。</param>
        private static void SeedDescribeCount(HashSet<string> keysToDelete, string channelId, int count)
        {
            var key = CounterHelper.GetDescribeCountKey(DateTime.Now.Date);
            RedisHelper.HSet(key, channelId, count);
            keysToDelete.Add(key);
        }

        /// <summary>
        /// 恢复全局配置并删除测试预置的 key。
        /// </summary>
        /// <param name="previousSetting">测试前的全局配置。</param>
        /// <param name="keysToDelete">待清理 key 集合。</param>
        private static void Cleanup(Setting? previousSetting, HashSet<string> keysToDelete)
        {
            GlobalConfiguration.Setting = previousSetting!;

            try
            {
                if (keysToDelete.Count > 0)
                {
                    RedisHelper.Del(keysToDelete.ToArray());
                }
            }
            catch
            {
                // 测试清理失败不影响断言结果
            }
        }
    }
}

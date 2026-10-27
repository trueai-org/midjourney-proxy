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
                var fastAccount = new DiscordAccount
                {
                    Id = "ovfast",
                    ChannelId = "ovfast",
                    Enable = true,
                    IsYouChuan = false,
                    CoreSize = 4,
                    QueueSize = 7,
                    RelaxCoreSize = 1,
                    RelaxQueueSize = 1
                };

                // 悠船账号：快速并发上限 3、快速队列上限 5、慢速并发上限 2、慢速队列上限 4
                var youChuanAccount = new DiscordAccount
                {
                    Id = "ovycc",
                    ChannelId = "ovycc",
                    Enable = true,
                    IsYouChuan = true,
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
        /// 覆盖口径：入参为 null 或过滤后为空时不访问 Redis，返回非 null 且 8 个字段全 0。
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
        /// 断言概览非 null 且 8 个统计字段全为 0。
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

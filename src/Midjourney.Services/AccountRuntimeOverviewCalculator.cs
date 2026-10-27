using Serilog;

namespace Midjourney.Services
{
    /// <summary>
    /// 首页实时并发与队列概览计算器。
    /// 一次性读取所有传入账号的队列长度与并发占用（单次 Redis pipeline），汇总为
    /// <see cref="HomeRuntimeOverviewDto"/>；不读取图生文（describe）队列与并发。
    /// 只负责读取，不修改 Redis / 账号状态。
    /// </summary>
    public static class AccountRuntimeOverviewCalculator
    {
        /// <summary>
        /// 可用快速并发/队列的判定阈值：账号快速剩余次数达到该值时才把其空闲容量计入可用数。
        /// 单位：次。
        /// </summary>
        private const int FastAvailableThreshold = 8;

        /// <summary>
        /// 计算启用账号的实时并发与队列概览。
        /// 「Total」为账号配置上限之和（负值按 0 计）；「已用」为当前 Redis 实际占用；
        /// 「可用」按剩余次数阈值逐账号累加（快速剩余 &gt;= 8 次、悠船慢速剩余 &gt; 0 才计入）。
        /// 传入 null 或过滤后为空时不访问 Redis，直接返回全 0 结果，便于首页在无账号时正常展示。
        /// Redis 读取失败（未启用 Redis 或 pipeline 异常）时记录警告并返回 null，
        /// 由调用方决定不展示该区块，避免把异常抛给 HTTP 请求或返回半成品数据。
        /// </summary>
        /// <param name="accounts">待统计的账号集合，调用方负责只传入启用（<c>Enable == true</c>）的账号；元素可为 null（会被忽略）。</param>
        /// <returns>并发与队列概览；Redis 读取失败时返回 null。</returns>
        public static HomeRuntimeOverviewDto Calculate(IEnumerable<DiscordAccount> accounts)
        {
            if (accounts == null)
            {
                return new HomeRuntimeOverviewDto();
            }

            var enabledAccounts = new List<DiscordAccount>();
            foreach (var account in accounts)
            {
                if (account != null)
                {
                    enabledAccounts.Add(account);
                }
            }

            var overview = new HomeRuntimeOverviewDto();
            if (enabledAccounts.Count == 0)
            {
                return overview;
            }

            var sources = new List<AccountRuntimeSource>(enabledAccounts.Count);
            foreach (var account in enabledAccounts)
            {
                // 配置上限：使用账号自身的并发/队列配置，负值视为 0，避免脏数据把总容量算成负数
                overview.FastConcurrencyTotal += Math.Max(0, account.CoreSize);
                overview.FastQueueCountTotal += Math.Max(0, account.QueueSize);

                if (account.IsYouChuan)
                {
                    overview.YouChuanRelaxConcurrencyTotal += Math.Max(0, account.RelaxCoreSize);
                    overview.YouChuanRelaxQueueCountTotal += Math.Max(0, account.RelaxQueueSize);
                }

                sources.Add(new AccountRuntimeSource
                {
                    Account = account,
                    Keys = new ChannelKeyProvider(account.ChannelId),

                    // 首页统计不依赖本节点是否已创建 DiscordService 实例：
                    // PrecheckAlive=false 配合 includeValuesForInactive=true，仍然读取全部运行态值。
                    PrecheckAlive = false,

                    // IsSessionValid 使用 AccountRuntimeSource 的默认实现（恒为 true），首页只关心队列与并发占用
                });
            }

            try
            {
                // 只调用一次：所有账号的 LLen / EXISTS 合并到同一批 pipeline 命令中，结果顺序与入参一致
                var snapshots = AccountRuntimeSnapshotLoader.Load(
                    sources,
                    includeDescribeQuota: false,
                    includeValuesForInactive: true);

                for (var i = 0; i < snapshots.Count; i++)
                {
                    var snapshot = snapshots[i];
                    var account = enabledAccounts[i];

                    overview.FastConcurrency += snapshot.FastConcurrency;
                    overview.FastQueueCount += snapshot.FastQueueCount;

                    // 可用快速容量：仅当该账号快速剩余次数达到阈值时才计入，避免把「剩余次数不足」的账号算成可用
                    var fastRemaining = ResolveFastRemaining(
                        account,
                        snapshot.FastAvailableCount,
                        snapshot.TodayFastCount + snapshot.TodayTurboCount);
                    if (fastRemaining >= FastAvailableThreshold)
                    {
                        overview.FastConcurrencyAvailable += Math.Max(0, Math.Max(0, account.CoreSize) - snapshot.FastConcurrency);
                        overview.FastQueueAvailable += Math.Max(0, Math.Max(0, account.QueueSize) - snapshot.FastQueueCount);
                    }

                    // 慢速队列/并发只统计悠船账号；非悠船账号的慢速运行态不参与首页概览
                    if (account.IsYouChuan)
                    {
                        overview.YouChuanRelaxConcurrency += snapshot.RelaxConcurrency;
                        overview.YouChuanRelaxQueueCount += snapshot.RelaxQueueCount;

                        // 可用慢速容量：悠船慢速剩余次数 > 0 时才计入
                        var relaxRemaining = ResolveYouChuanRelaxRemaining(account, snapshot.RelaxCount);
                        if (relaxRemaining > 0)
                        {
                            overview.YouChuanRelaxConcurrencyAvailable += Math.Max(0, Math.Max(0, account.RelaxCoreSize) - snapshot.RelaxConcurrency);
                            overview.YouChuanRelaxQueueAvailable += Math.Max(0, Math.Max(0, account.RelaxQueueSize) - snapshot.RelaxQueueCount);
                        }
                    }

                    // 图生文（DescribeQueueCount / DescribeConcurrency）按口径一律不计入
                }
            }
            catch (Exception ex)
            {
                // Redis 未启用或 pipeline 异常：整体降级为 null，调用方据此不展示该区块；
                // 已累加的 Total 也不能作为半成品返回
                Log.Warning(ex, "计算首页实时并发与队列概览失败");
                return null;
            }

            return overview;
        }

        /// <summary>
        /// 计算账号当前可用的快速剩余次数。
        /// Redis 计数是上限来源；若配置了快速日限额（DayDrawLimit &gt; 0），还要扣掉今日已用的快速 + 极速次数。
        /// </summary>
        /// <param name="account">账号。</param>
        /// <param name="fastAvailableCount">Redis 中的快速剩余次数。</param>
        /// <param name="todayFastCount">今日已用的快速 + 极速次数。</param>
        /// <returns>非负的剩余次数。</returns>
        private static int ResolveFastRemaining(DiscordAccount account, int fastAvailableCount, int todayFastCount)
        {
            var remaining = Math.Max(0, fastAvailableCount);
            if (account.DayDrawLimit > 0)
            {
                remaining = Math.Min(remaining, Math.Max(0, account.DayDrawLimit - Math.Max(0, todayFastCount)));
            }

            return remaining;
        }

        /// <summary>
        /// 计算悠船账号当前可用的慢速剩余次数。
        /// 与首页「悠船慢速可用次数」口径保持一致：当日已触发慢速重置返回 0；
        /// 未配置日上限（YouChuanRelaxDailyLimit &lt;= 0）视为不限次数；否则取配置上限与今日已用次数的差值。
        /// </summary>
        /// <param name="account">悠船账号。</param>
        /// <param name="relaxUsedToday">今日已用的慢速绘图次数。</param>
        /// <returns>慢速剩余次数。</returns>
        private static int ResolveYouChuanRelaxRemaining(DiscordAccount account, int relaxUsedToday)
        {
            if (account.YouChuanRelaxedReset > DateTime.Now.Date)
            {
                return 0;
            }

            if (account.YouChuanRelaxDailyLimit <= 0)
            {
                return int.MaxValue;
            }

            var limit = account.DayRelaxDrawLimit > 0
                ? Math.Min(account.YouChuanRelaxDailyLimit, account.DayRelaxDrawLimit)
                : account.YouChuanRelaxDailyLimit;

            return Math.Max(0, limit - Math.Max(0, relaxUsedToday));
        }

        /// <summary>
        /// 按频道 ID 构造账号运行态 Redis key。
        /// key 规则统一复用 <see cref="RedisQueue.BuildQueueKey"/> 与
        /// <see cref="RedisConcurrent.BuildTokenKey"/>，避免手写字符串前缀与业务代码不一致。
        /// </summary>
        private sealed class ChannelKeyProvider : IAccountRuntimeKeyProvider
        {
            /// <summary>频道 ID；为空时无法拼出有效 key，读取结果为 0。</summary>
            private readonly string _channelId;

            /// <summary>
            /// 创建 key 提供器。
            /// </summary>
            /// <param name="channelId">账号频道 ID；为 null 时按空字符串处理，代表该账号无法产生有效 key，计数自然为 0。</param>
            internal ChannelKeyProvider(string channelId)
            {
                _channelId = channelId ?? string.Empty;
            }

            /// <summary>快速/默认队列 key。</summary>
            string IAccountRuntimeKeyProvider.FastQueueKey => RedisQueue.BuildQueueKey($"fast:{_channelId}");

            /// <summary>悠船慢速队列 key。</summary>
            string IAccountRuntimeKeyProvider.RelaxQueueKey => RedisQueue.BuildQueueKey($"relax:{_channelId}");

            /// <summary>图生文队列 key。</summary>
            string IAccountRuntimeKeyProvider.DescribeQueueKey => RedisQueue.BuildQueueKey($"describe:{_channelId}");

            /// <summary>
            /// 获取快速并发槽位 key。
            /// </summary>
            /// <param name="index">槽位序号，从 0 开始。</param>
            /// <returns>带 CSRedis 锁前缀的槽位 key。</returns>
            string IAccountRuntimeKeyProvider.GetFastConcurrencyKey(int index) => RedisConcurrent.BuildTokenKey($"fast:{_channelId}", index);

            /// <summary>
            /// 获取悠船慢速并发槽位 key。
            /// </summary>
            /// <param name="index">槽位序号，从 0 开始。</param>
            /// <returns>带 CSRedis 锁前缀的槽位 key。</returns>
            string IAccountRuntimeKeyProvider.GetRelaxConcurrencyKey(int index) => RedisConcurrent.BuildTokenKey($"relax:{_channelId}", index);

            /// <summary>
            /// 获取图生文并发槽位 key。
            /// </summary>
            /// <param name="index">槽位序号，从 0 开始。</param>
            /// <returns>带 CSRedis 锁前缀的槽位 key。</returns>
            string IAccountRuntimeKeyProvider.GetDescribeConcurrencyKey(int index) => RedisConcurrent.BuildTokenKey($"describe:{_channelId}", index);
        }
    }
}

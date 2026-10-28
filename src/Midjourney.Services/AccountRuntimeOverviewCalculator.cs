using Serilog;

namespace Midjourney.Services
{
    /// <summary>
    /// 首页实时并发与队列概览计算器。
    /// 一次性读取所有传入账号的队列长度与并发占用（单次 Redis pipeline），汇总为
    /// <see cref="HomeRuntimeOverviewDto"/>；覆盖快速、悠船慢速与图生文（describe）三组指标。
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
        /// 悠船图生文每日固定额度（次/账号/天）。
        /// 与 <see cref="AccountSelectionEvaluator.IsAllowDescribe"/> 中的上限保持一致，修改时两处必须同步。
        /// </summary>
        private const int YouChuanDescribeDailyLimit = 200;

        /// <summary>
        /// 计算启用账号的实时并发与队列概览。
        /// 「Total」为账号配置上限之和（负值按 0 计）；「已用」为当前 Redis 实际占用；
        /// 「可用」按剩余次数阈值逐账号累加（快速剩余 &gt;= 8 次、悠船慢速剩余 &gt; 0 才计入）。
        /// 图生文指标只统计开启 <c>IsDescribe</c> 的账号：悠船可用槽位还要受「今日剩余可提交次数」截断，
        /// 避免临近每日 200 次上限时把空闲槽位全部显示为可用；官方/Discord 不限额度，空闲槽位即可用。
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

                // 图生文沿用账号自身的 CoreSize / QueueSize（见 DiscordService 的 describe 队列构造）；
                // 仅统计开启 IsDescribe 的账号，关闭图生文的账号不会产生 describe 任务
                if (account.IsDescribe)
                {
                    overview.DescribeConcurrencyTotal += Math.Max(0, account.CoreSize);
                    overview.DescribeQueueCountTotal += Math.Max(0, account.QueueSize);

                    // 悠船图生文每日 200 次固定额度，总额度按账号数累加；官方/Discord 不限额，不计入额度
                    if (account.IsYouChuan)
                    {
                        overview.YouChuanDescribeQuotaTotal += YouChuanDescribeDailyLimit;
                    }
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
                // 只调用一次：所有账号的 LLen / EXISTS / HGet 合并到同一批 pipeline 命令中，结果顺序与入参一致
                var snapshots = AccountRuntimeSnapshotLoader.Load(
                    sources,
                    includeDescribeQuota: true,
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

                    // 图生文：只统计开启 IsDescribe 的账号；额度口径与选号 IsAllowDescribe 保持一致
                    if (account.IsDescribe)
                    {
                        overview.DescribeConcurrency += snapshot.DescribeConcurrency;
                        overview.DescribeQueueCount += snapshot.DescribeQueueCount;

                        if (account.IsYouChuan)
                        {
                            var describeUsed = Math.Max(0, snapshot.DescribeCount);
                            var describeRemaining = ResolveYouChuanDescribeRemaining(account, describeUsed);

                            overview.YouChuanDescribeQuotaUsed += describeUsed;
                            overview.YouChuanDescribeQuotaRemaining += describeRemaining;

                            // 空闲槽位不等于可用槽位：选号侧 IsAllowDescribe 要求
                            // 今日已用 + 运行中并发 + 排队数 < 每日 200 次，
                            // 临近上限时只有「剩余可提交次数」个槽位能真正承接新任务。
                            var submittable = ResolveYouChuanDescribeSubmittable(account, snapshot);
                            var idleConcurrency = Math.Max(0, Math.Max(0, account.CoreSize) - Math.Max(0, snapshot.DescribeConcurrency));
                            var idleQueue = Math.Max(0, Math.Max(0, account.QueueSize) - Math.Max(0, snapshot.DescribeQueueCount));

                            // 任务先占用空闲并发槽位，超出的才排队，因此并发可用先截断，余量才计入队列可用，
                            // 保证「可用并发 + 可用队列」不超过剩余可提交次数
                            var availableConcurrency = Math.Min(idleConcurrency, submittable);
                            overview.DescribeConcurrencyAvailable += availableConcurrency;
                            overview.DescribeQueueAvailable += Math.Min(idleQueue, Math.Max(0, submittable - availableConcurrency));
                        }
                        else
                        {
                            // 官方 / Discord 账号不限制图生文次数（见 AccountSelectionEvaluator.IsAllowDescribe），空闲槽位始终可用
                            overview.DescribeConcurrencyAvailable += Math.Max(0, Math.Max(0, account.CoreSize) - snapshot.DescribeConcurrency);
                            overview.DescribeQueueAvailable += Math.Max(0, Math.Max(0, account.QueueSize) - snapshot.DescribeQueueCount);
                        }
                    }
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
        /// 计算悠船账号当日剩余的图生文次数。
        /// 悠船图生文是每账号每天固定 200 次的额度；当日已触发上限重置（<c>YouChuanPicreadReset</c> 大于当天）时剩余为 0。
        /// </summary>
        /// <param name="account">悠船账号。</param>
        /// <param name="describeUsedToday">今日已用的图生文次数。</param>
        /// <returns>非负的剩余次数。</returns>
        private static int ResolveYouChuanDescribeRemaining(DiscordAccount account, int describeUsedToday)
        {
            if (account.YouChuanPicreadReset > DateTime.Now.Date)
            {
                return 0;
            }

            return Math.Max(0, YouChuanDescribeDailyLimit - describeUsedToday);
        }

        /// <summary>
        /// 计算悠船账号当前还能提交多少个图生文任务。
        /// 与 <see cref="AccountSelectionEvaluator.IsAllowDescribe"/> 的判定保持一致：
        /// 当日已触发上限重置（<c>YouChuanPicreadReset</c> 大于当天）时返回 0；
        /// 否则返回 <c>max(0, 200 - 今日已用 - 运行中并发 - 排队数)</c>，即还能被选号接收的任务数。
        /// </summary>
        /// <param name="account">悠船账号。</param>
        /// <param name="snapshot">账号运行态快照。</param>
        /// <returns>非负的剩余可提交次数。</returns>
        private static int ResolveYouChuanDescribeSubmittable(DiscordAccount account, AccountRuntimeSnapshot snapshot)
        {
            if (account.YouChuanPicreadReset > DateTime.Now.Date)
            {
                return 0;
            }

            var used = Math.Max(0, snapshot.DescribeCount);
            var inFlight = Math.Max(0, snapshot.DescribeConcurrency) + Math.Max(0, snapshot.DescribeQueueCount);

            return Math.Max(0, YouChuanDescribeDailyLimit - used - inFlight);
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

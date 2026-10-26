using System.Globalization;
using CSRedis;
using Midjourney.Base;
using Midjourney.Base.Models;

namespace Midjourney.Services
{
    /// <summary>
    /// 账号选择运行态快照加载器。
    /// 所有候选账号的 HExists / HGet / LLen / Exists 一次性打包到一条 Redis pipeline，
    /// 正常选号只产生 1 次 RTT；结果按命令顺序回填，避免逐账号访问 Redis。
    /// </summary>
    internal static class AccountRuntimeSnapshotLoader
    {
        /// <summary>
        /// 批量加载账号运行态快照。
        /// 返回列表与 <paramref name="sources"/> 一一对应；未通过前置存活判断的账号默认不会访问 Redis，
        /// 其快照字段保持默认值且 <see cref="AccountRuntimeSnapshot.IsAlive"/> 为 false。
        /// </summary>
        /// <param name="sources">候选账号及 key 提供器。</param>
        /// <param name="includeDescribeQuota">是否读取图生文计数（仅 describe 过滤时使用）。</param>
        /// <param name="includeValuesForInactive">是否连未通过前置存活判断的账号也读取运行态值（账号列表展示使用）。</param>
        /// <returns>与输入顺序一致的快照列表。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="sources"/> 为 null。</exception>
        /// <exception cref="ArgumentException">需要读取运行态但账号或 key 提供器为空。</exception>
        /// <exception cref="InvalidOperationException">pipeline 返回结果数量与命令数量不一致。</exception>
        internal static List<AccountRuntimeSnapshot> Load(
            IReadOnlyList<AccountRuntimeSource> sources,
            bool includeDescribeQuota,
            bool includeValuesForInactive = false)
        {
            ArgumentNullException.ThrowIfNull(sources);

            var snapshots = new List<AccountRuntimeSnapshot>(sources.Count);
            foreach (var _ in sources)
            {
                snapshots.Add(new AccountRuntimeSnapshot());
            }

            if (sources.Count == 0)
            {
                return snapshots;
            }

            var commands = new List<SnapshotCommand>(sources.Count * 16);
            var today = DateTime.Now.Date;

            object[] results;
            using (var pipe = RedisHelper.StartPipe())
            {
                for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
                {
                    var source = sources[sourceIndex];
                    if (!source.PrecheckAlive && !includeValuesForInactive)
                    {
                        continue;
                    }

                    if (source.Account == null || source.Keys == null)
                    {
                        throw new ArgumentException("读取账号运行态时 Account 和 Keys 不能为空", nameof(sources));
                    }

                    var acc = source.Account;
                    var keys = source.Keys;

                    // 存活判断：快速任务可用计数 key 是否存在
                    pipe.HExists(CounterHelper.FastTaskAvailableCountKey, acc.ChannelId);
                    commands.Add(new SnapshotCommand(sourceIndex, SnapshotField.HasFastAvailableCount));

                    // Discord WS 连接状态（锁存在 = 已连接；连接中 key 存在 = 正在连接）
                    if (acc.IsDiscord)
                    {
                        pipe.Exists(DiscordWebSocketService.InitConnectLockRedisKey(acc));
                        commands.Add(new SnapshotCommand(sourceIndex, SnapshotField.IsInitConnect));

                        pipe.Exists(DiscordWebSocketService.InitConnectingRedisKey(acc));
                        commands.Add(new SnapshotCommand(sourceIndex, SnapshotField.IsInitConnecting));
                    }

                    // 快速任务剩余次数
                    pipe.HGet(CounterHelper.FastTaskAvailableCountKey, acc.ChannelId);
                    commands.Add(new SnapshotCommand(sourceIndex, SnapshotField.FastAvailableCount));

                    // 悠船慢速今日已用次数
                    if (acc.IsYouChuan)
                    {
                        pipe.HGet(CounterHelper.GetYouchuanRelaxCountKey(today), acc.ChannelId);
                        commands.Add(new SnapshotCommand(sourceIndex, SnapshotField.RelaxCount));
                    }

                    // 日绘图计数完整读取：账号限制可能在选号期间被修改，快照不能因为
                    // 读取时的限制为 -1 就缺失计数字段，否则会与逐账号实时判定产生差异。
                    pipe.HGet(
                        CounterHelper.GetAccountTodayTotalCountKey(today),
                        CounterHelper.GetAccountTodayTotalCountField(acc.ChannelId, GenerationSpeedMode.RELAX));
                    commands.Add(new SnapshotCommand(sourceIndex, SnapshotField.TodayRelaxCount));

                    pipe.HGet(
                        CounterHelper.GetAccountTodayTotalCountKey(today),
                        CounterHelper.GetAccountTodayTotalCountField(acc.ChannelId, GenerationSpeedMode.FAST));
                    commands.Add(new SnapshotCommand(sourceIndex, SnapshotField.TodayFastCount));

                    pipe.HGet(
                        CounterHelper.GetAccountTodayTotalCountKey(today),
                        CounterHelper.GetAccountTodayTotalCountField(acc.ChannelId, GenerationSpeedMode.TURBO));
                    commands.Add(new SnapshotCommand(sourceIndex, SnapshotField.TodayTurboCount));

                    // 图生文计数：describe 过滤下为所有悠船账号读取，避免 IsDescribe 在选号期间
                    // 由 false 变为 true 时快照缺少计数字段。
                    if (includeDescribeQuota && acc.IsYouChuan)
                    {
                        pipe.HGet(CounterHelper.GetDescribeCountKey(today), acc.ChannelId);
                        commands.Add(new SnapshotCommand(sourceIndex, SnapshotField.DescribeCount));
                    }

                    // 队列长度：快速/默认、悠船慢速、图生文
                    pipe.LLen(keys.FastQueueKey);
                    commands.Add(new SnapshotCommand(sourceIndex, SnapshotField.FastQueueCount));

                    if (acc.IsYouChuan)
                    {
                        pipe.LLen(keys.RelaxQueueKey);
                        commands.Add(new SnapshotCommand(sourceIndex, SnapshotField.RelaxQueueCount));
                    }

                    pipe.LLen(keys.DescribeQueueKey);
                    commands.Add(new SnapshotCommand(sourceIndex, SnapshotField.DescribeQueueCount));

                    // 并发占用：快速、悠船慢速、图生文
                    for (var i = 0; i < acc.CoreSize; i++)
                    {
                        pipe.Exists(keys.GetFastConcurrencyKey(i));
                        commands.Add(new SnapshotCommand(sourceIndex, SnapshotField.FastConcurrency));
                    }

                    if (acc.IsYouChuan)
                    {
                        for (var i = 0; i < acc.RelaxCoreSize; i++)
                        {
                            pipe.Exists(keys.GetRelaxConcurrencyKey(i));
                            commands.Add(new SnapshotCommand(sourceIndex, SnapshotField.RelaxConcurrency));
                        }
                    }

                    for (var i = 0; i < acc.CoreSize; i++)
                    {
                        pipe.Exists(keys.GetDescribeConcurrencyKey(i));
                        commands.Add(new SnapshotCommand(sourceIndex, SnapshotField.DescribeConcurrency));
                    }
                }

                results = commands.Count > 0 ? pipe.EndPipe() : [];
            }

            if (results.Length != commands.Count)
            {
                throw new InvalidOperationException($"Redis pipeline 返回数量不一致：commands={commands.Count}, results={results.Length}");
            }

            for (var i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                var snapshot = snapshots[command.SourceIndex];

                switch (command.Field)
                {
                    case SnapshotField.HasFastAvailableCount:
                        snapshot.HasFastAvailableCount = ToBoolStrict(results[i]);
                        break;

                    case SnapshotField.IsInitConnect:
                        snapshot.IsInitConnect = ToBoolStrict(results[i]);
                        break;

                    case SnapshotField.IsInitConnecting:
                        snapshot.IsInitConnecting = ToBoolStrict(results[i]);
                        break;

                    case SnapshotField.FastAvailableCount:
                        snapshot.FastAvailableCount = ToInt(results[i]);
                        break;

                    case SnapshotField.RelaxCount:
                        snapshot.RelaxCount = ToInt(results[i]);
                        break;

                    case SnapshotField.TodayRelaxCount:
                        snapshot.TodayRelaxCount = ToInt(results[i]);
                        break;

                    case SnapshotField.TodayFastCount:
                        snapshot.TodayFastCount = ToInt(results[i]);
                        break;

                    case SnapshotField.TodayTurboCount:
                        snapshot.TodayTurboCount = ToInt(results[i]);
                        break;

                    case SnapshotField.DescribeCount:
                        snapshot.DescribeCount = ToInt(results[i]);
                        break;

                    case SnapshotField.FastQueueCount:
                        snapshot.FastQueueCount = ToIntFromLongStrict(results[i]);
                        break;

                    case SnapshotField.RelaxQueueCount:
                        snapshot.RelaxQueueCount = ToIntFromLongStrict(results[i]);
                        break;

                    case SnapshotField.DescribeQueueCount:
                        snapshot.DescribeQueueCount = ToIntFromLongStrict(results[i]);
                        break;

                    case SnapshotField.FastConcurrency:
                        if (ToBoolStrict(results[i]))
                        {
                            snapshot.FastConcurrency++;
                        }
                        break;

                    case SnapshotField.RelaxConcurrency:
                        if (ToBoolStrict(results[i]))
                        {
                            snapshot.RelaxConcurrency++;
                        }
                        break;

                    case SnapshotField.DescribeConcurrency:
                        if (ToBoolStrict(results[i]))
                        {
                            snapshot.DescribeConcurrency++;
                        }
                        break;
                }
            }

            for (var i = 0; i < sources.Count; i++)
            {
                var snapshot = snapshots[i];
                var alive = sources[i].PrecheckAlive && snapshot.HasFastAvailableCount;
                var acc = sources[i].Account;

                // 先判断 Discord WS，再判断 Token/Lock，保持原 IsAlive 的检查顺序
                if (alive && acc != null && acc.IsDiscord)
                {
                    alive = snapshot.IsInitConnect && !snapshot.IsInitConnecting;
                }

                if (alive)
                {
                    alive = sources[i].IsSessionValid();
                }

                snapshot.IsAlive = alive;
            }

            return snapshots;
        }

        /// <summary>
        /// 批量获取存活实例。
        /// 只查询刷新存活所需的 HExists 和 Discord WS 状态，一条 pipeline 完成，不读取队列/计数/并发。
        /// </summary>
        /// <param name="instances">候选实例。</param>
        /// <returns>存活实例列表，顺序与输入一致。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="instances"/> 为 null。</exception>
        /// <exception cref="InvalidOperationException">pipeline 返回结果数量或类型异常。</exception>
        internal static List<DiscordService> LoadAlive(IReadOnlyList<DiscordService> instances)
        {
            ArgumentNullException.ThrowIfNull(instances);

            var candidates = new List<DiscordService>(instances.Count);
            foreach (var instance in instances)
            {
                if (instance != null)
                {
                    candidates.Add(instance);
                }
            }

            if (candidates.Count == 0)
            {
                return [];
            }

            var states = new AliveState[candidates.Count];
            var commands = new List<AliveCommand>(candidates.Count * 2);

            object[] results;
            using (var pipe = RedisHelper.StartPipe())
            {
                for (var i = 0; i < candidates.Count; i++)
                {
                    var service = candidates[i];
                    var state = new AliveState
                    {
                        PrecheckAlive = service.IsAlivePrecheck(),
                        IsDiscord = service.Account?.IsDiscord == true
                    };
                    states[i] = state;

                    if (!state.PrecheckAlive || service.Account == null)
                    {
                        continue;
                    }

                    pipe.HExists(CounterHelper.FastTaskAvailableCountKey, service.Account.ChannelId);
                    commands.Add(new AliveCommand(i, AliveField.HasFastAvailableCount));

                    if (state.IsDiscord)
                    {
                        pipe.Exists(DiscordWebSocketService.InitConnectLockRedisKey(service.Account));
                        commands.Add(new AliveCommand(i, AliveField.IsInitConnect));

                        pipe.Exists(DiscordWebSocketService.InitConnectingRedisKey(service.Account));
                        commands.Add(new AliveCommand(i, AliveField.IsInitConnecting));
                    }
                }

                results = commands.Count > 0 ? pipe.EndPipe() : [];
            }

            if (results.Length != commands.Count)
            {
                throw new InvalidOperationException($"Redis pipeline 返回数量不一致：commands={commands.Count}, results={results.Length}");
            }

            for (var i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                var state = states[command.Index];

                switch (command.Field)
                {
                    case AliveField.HasFastAvailableCount:
                        state.HasFastAvailableCount = ToBoolStrict(results[i]);
                        break;

                    case AliveField.IsInitConnect:
                        state.IsInitConnect = ToBoolStrict(results[i]);
                        break;

                    case AliveField.IsInitConnecting:
                        state.IsInitConnecting = ToBoolStrict(results[i]);
                        break;
                }

                states[command.Index] = state;
            }

            var alive = new List<DiscordService>(candidates.Count);
            for (var i = 0; i < candidates.Count; i++)
            {
                var state = states[i];
                var isAlive = state.PrecheckAlive && state.HasFastAvailableCount;

                // 先判断 Discord WS，再判断 Token/Lock，保持原 IsAlive 的检查顺序
                if (isAlive && state.IsDiscord)
                {
                    isAlive = state.IsInitConnect && !state.IsInitConnecting;
                }

                if (isAlive)
                {
                    isAlive = candidates[i].IsSessionValidInMemory();
                }

                if (isAlive)
                {
                    alive.Add(candidates[i]);
                }
            }

            return alive;
        }

        /// <summary>
        /// 批量存活判断的中间状态。
        /// </summary>
        private struct AliveState
        {
            /// <summary>不访问 Redis 的前置存活判断。</summary>
            public bool PrecheckAlive;

            /// <summary>是否 Discord 账号。</summary>
            public bool IsDiscord;

            /// <summary>快速计数 key 是否存在。</summary>
            public bool HasFastAvailableCount;

            /// <summary>Discord WS 初始化连接锁是否存在。</summary>
            public bool IsInitConnect;

            /// <summary>Discord WS 是否正在初始化连接。</summary>
            public bool IsInitConnecting;
        }

        /// <summary>
        /// 存活 pipeline 命令与回填字段。
        /// </summary>
        private readonly struct AliveCommand
        {
            /// <summary>候选实例下标。</summary>
            public int Index { get; }

            /// <summary>回填字段。</summary>
            public AliveField Field { get; }

            /// <summary>
            /// 创建存活命令映射。
            /// </summary>
            /// <param name="index">候选实例下标。</param>
            /// <param name="field">回填字段。</param>
            public AliveCommand(int index, AliveField field)
            {
                Index = index;
                Field = field;
            }
        }

        /// <summary>
        /// 存活字段枚举。
        /// </summary>
        private enum AliveField
        {
            /// <summary>快速计数 key 是否存在。</summary>
            HasFastAvailableCount,

            /// <summary>Discord WS 初始化连接锁是否存在。</summary>
            IsInitConnect,

            /// <summary>Discord WS 是否正在初始化连接。</summary>
            IsInitConnecting
        }

        /// <summary>
        /// 把 Redis 返回的整数值转为 int；字段缺失返回 0，非法值抛错回退，兼容带引号的字符串。
        /// </summary>
        /// <param name="value">Redis pipeline 返回值。</param>
        /// <returns>整数值。</returns>
        private static int ToInt(object value)
        {
            switch (value)
            {
                case null:
                    return 0;

                case int intValue:
                    return intValue;

                case long longValue:
                    return longValue > int.MaxValue ? int.MaxValue : longValue < int.MinValue ? int.MinValue : (int)longValue;

                case string text:
                    text = text.Trim();
                    if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
                    {
                        text = text.Substring(1, text.Length - 2);
                    }
                    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : throw new InvalidOperationException("Redis pipeline 返回的计数值不是整数");

                default:
                    throw new InvalidOperationException($"Redis pipeline 返回的计数值类型异常：{value.GetType().Name}");
            }
        }

        /// <summary>
        /// 把 Redis 返回的布尔值转为 bool。
        /// 命令成功时只会返回 bool；null/其他类型说明 pipeline 失败，必须抛错回退，不能静默按 false 处理。
        /// </summary>
        /// <param name="value">Redis pipeline 返回值。</param>
        /// <returns>布尔值。</returns>
        private static bool ToBoolStrict(object value)
        {
            return value is bool boolValue
                ? boolValue
                : throw new InvalidOperationException($"Redis pipeline 期望布尔值，实际为 {value?.GetType().Name ?? "null"}");
        }

        /// <summary>
        /// 把 Redis 返回的整数转为 long。
        /// 命令成功时只会返回 long/int；null/其他类型说明 pipeline 失败，必须抛错回退。
        /// </summary>
        /// <param name="value">Redis pipeline 返回值。</param>
        /// <returns>整数值。</returns>
        private static long ToLongStrict(object value)
        {
            return value switch
            {
                long longValue => longValue,
                int intValue => intValue,
                _ => throw new InvalidOperationException($"Redis pipeline 期望整数，实际为 {value?.GetType().Name ?? "null"}")
            };
        }

        /// <summary>
        /// 把 LLen 的 long 结果安全转换为 int（超出范围时截断）。
        /// </summary>
        /// <param name="value">Redis pipeline 返回值。</param>
        /// <returns>队列长度。</returns>
        private static int ToIntFromLongStrict(object value) => ToInt(ToLongStrict(value));

        /// <summary>
        /// pipeline 命令与回填字段的对应关系。
        /// </summary>
        private readonly struct SnapshotCommand
        {
            /// <summary>候选账号在输入列表中的下标。</summary>
            public int SourceIndex { get; }

            /// <summary>需要回填的字段。</summary>
            public SnapshotField Field { get; }

            /// <summary>
            /// 创建命令映射。
            /// </summary>
            /// <param name="sourceIndex">候选账号下标。</param>
            /// <param name="field">回填字段。</param>
            public SnapshotCommand(int sourceIndex, SnapshotField field)
            {
                SourceIndex = sourceIndex;
                Field = field;
            }
        }

        /// <summary>
        /// 快照字段枚举。
        /// </summary>
        private enum SnapshotField
        {
            /// <summary>快速计数 key 是否存在。</summary>
            HasFastAvailableCount,

            /// <summary>Discord WS 初始化连接锁是否存在。</summary>
            IsInitConnect,

            /// <summary>Discord WS 是否正在初始化连接。</summary>
            IsInitConnecting,

            /// <summary>快速任务剩余次数。</summary>
            FastAvailableCount,

            /// <summary>悠船慢速今日已用次数。</summary>
            RelaxCount,

            /// <summary>慢速今日绘图总数。</summary>
            TodayRelaxCount,

            /// <summary>快速今日绘图总数。</summary>
            TodayFastCount,

            /// <summary>极速今日绘图总数。</summary>
            TodayTurboCount,

            /// <summary>图生文今日已用次数。</summary>
            DescribeCount,

            /// <summary>快速队列长度。</summary>
            FastQueueCount,

            /// <summary>慢速队列长度。</summary>
            RelaxQueueCount,

            /// <summary>图生文队列长度。</summary>
            DescribeQueueCount,

            /// <summary>快速并发占用。</summary>
            FastConcurrency,

            /// <summary>慢速并发占用。</summary>
            RelaxConcurrency,

            /// <summary>图生文并发占用。</summary>
            DescribeConcurrency
        }
    }
}

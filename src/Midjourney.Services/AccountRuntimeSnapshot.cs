using Midjourney.Base;
using Midjourney.Base.Models;

namespace Midjourney.Services
{
    /// <summary>
    /// 账号选择所需的运行态数据读取接口。
    /// 实时实现逐项访问 Redis；快照实现直接返回一次 pipeline 读取的结果。
    /// </summary>
    internal interface IAccountRuntimeValues
    {
        /// <summary>快速任务可用计数 key 是否存在。</summary>
        bool HasFastAvailableCount { get; }

        /// <summary>快速任务剩余次数。</summary>
        int FastAvailableCount { get; }

        /// <summary>悠船慢速今日已用次数。</summary>
        int RelaxCount { get; }

        /// <summary>获取指定速度模式的今日绘图总数。</summary>
        /// <param name="mode">速度模式。</param>
        /// <returns>今日绘图总数。</returns>
        int TodayCount(GenerationSpeedMode mode);

        /// <summary>图生文今日已用次数。</summary>
        int DescribeCount { get; }

        /// <summary>快速/默认队列长度。</summary>
        int FastQueueCount { get; }

        /// <summary>悠船慢速队列长度。</summary>
        int RelaxQueueCount { get; }

        /// <summary>图生文队列长度。</summary>
        int DescribeQueueCount { get; }

        /// <summary>快速/默认并发占用数。</summary>
        int FastConcurrency { get; }

        /// <summary>悠船慢速并发占用数。</summary>
        int RelaxConcurrency { get; }

        /// <summary>图生文并发占用数。</summary>
        int DescribeConcurrency { get; }
    }

    /// <summary>
    /// 账号选择运行态快照。
    /// 一次 Redis pipeline 读取所有候选账号的计数、队列和并发占用，
    /// 选号过程中只读内存，避免按账号、按模式反复往返 Redis。
    /// </summary>
    public sealed class AccountRuntimeSnapshot : IAccountRuntimeValues
    {
        /// <summary>
        /// 账号当前是否存活（内存会话有效且快速计数 key 存在）。
        /// </summary>
        public bool IsAlive { get; internal set; }

        /// <summary>
        /// Discord WS 初始化连接锁是否存在。
        /// </summary>
        public bool IsInitConnect { get; internal set; }

        /// <summary>
        /// Discord WS 是否正在初始化连接。
        /// </summary>
        public bool IsInitConnecting { get; internal set; }

        /// <summary>
        /// 快速任务可用计数 key 是否存在。
        /// </summary>
        public bool HasFastAvailableCount { get; internal set; }

        /// <summary>
        /// 快速任务剩余次数。
        /// </summary>
        public int FastAvailableCount { get; internal set; }

        /// <summary>
        /// 悠船慢速今日已用次数。
        /// </summary>
        public int RelaxCount { get; internal set; }

        /// <summary>
        /// 慢速模式今日绘图总数（TaskAccountAll 中 RELAX 字段）。
        /// </summary>
        public int TodayRelaxCount { get; internal set; }

        /// <summary>
        /// 快速模式今日绘图总数（TaskAccountAll 中 FAST 字段）。
        /// </summary>
        public int TodayFastCount { get; internal set; }

        /// <summary>
        /// 极速模式今日绘图总数（TaskAccountAll 中 TURBO 字段）。
        /// </summary>
        public int TodayTurboCount { get; internal set; }

        /// <summary>
        /// 图生文今日已用次数。
        /// </summary>
        public int DescribeCount { get; internal set; }

        /// <summary>
        /// 快速/默认队列长度。
        /// </summary>
        public int FastQueueCount { get; internal set; }

        /// <summary>
        /// 悠船慢速队列长度。
        /// </summary>
        public int RelaxQueueCount { get; internal set; }

        /// <summary>
        /// 图生文队列长度。
        /// </summary>
        public int DescribeQueueCount { get; internal set; }

        /// <summary>
        /// 快速/默认并发占用数。
        /// </summary>
        public int FastConcurrency { get; internal set; }

        /// <summary>
        /// 悠船慢速并发占用数。
        /// </summary>
        public int RelaxConcurrency { get; internal set; }

        /// <summary>
        /// 图生文并发占用数。
        /// </summary>
        public int DescribeConcurrency { get; internal set; }

        /// <summary>
        /// 获取指定速度模式的今日绘图总数。
        /// 慢速返回 RELAX 计数；快速/极速返回 FAST + TURBO 计数。
        /// </summary>
        /// <param name="mode">速度模式。</param>
        /// <returns>今日绘图总数。</returns>
        public int GetTodayCount(GenerationSpeedMode mode)
        {
            return mode == GenerationSpeedMode.RELAX
                ? TodayRelaxCount
                : TodayFastCount + TodayTurboCount;
        }

        int IAccountRuntimeValues.TodayCount(GenerationSpeedMode mode) => GetTodayCount(mode);

        /// <summary>
        /// 获取队列任务数：快速队列 + 悠船慢速队列 + 图生文队列。
        /// </summary>
        /// <param name="account">账号。</param>
        /// <returns>队列任务数。</returns>
        public int GetQueueTaskCount(DiscordAccount account) => AccountSelectionEvaluator.GetQueueTaskCount(account, this);

        /// <summary>
        /// 获取正在运行的并发任务数：快速并发 + 悠船慢速并发 + 图生文并发。
        /// </summary>
        /// <param name="account">账号。</param>
        /// <returns>并发任务数。</returns>
        public int GetRunningTaskCount(DiscordAccount account) => AccountSelectionEvaluator.GetRunningTaskCount(account, this);
    }

    /// <summary>
    /// 账号运行态 Redis key 提供器。
    /// 由 <see cref="DiscordService"/> 实现，测试可注入独立 key。
    /// </summary>
    internal interface IAccountRuntimeKeyProvider
    {
        /// <summary>快速/默认队列 key。</summary>
        string FastQueueKey { get; }

        /// <summary>悠船慢速队列 key。</summary>
        string RelaxQueueKey { get; }

        /// <summary>图生文队列 key。</summary>
        string DescribeQueueKey { get; }

        /// <summary>获取快速并发槽位 key。</summary>
        /// <param name="index">槽位序号。</param>
        /// <returns>槽位 key。</returns>
        string GetFastConcurrencyKey(int index);

        /// <summary>获取悠船慢速并发槽位 key。</summary>
        /// <param name="index">槽位序号。</param>
        /// <returns>槽位 key。</returns>
        string GetRelaxConcurrencyKey(int index);

        /// <summary>获取图生文并发槽位 key。</summary>
        /// <param name="index">槽位序号。</param>
        /// <returns>槽位 key。</returns>
        string GetDescribeConcurrencyKey(int index);
    }

    /// <summary>
    /// 批量快照的输入项。
    /// </summary>
    internal sealed class AccountRuntimeSource
    {
        /// <summary>账号。</summary>
        public DiscordAccount Account { get; init; }

        /// <summary>队列/并发 key 提供器。</summary>
        public IAccountRuntimeKeyProvider Keys { get; init; }

        /// <summary>不访问 Redis 的存活前置判断结果（账号启用、已初始化）。</summary>
        public bool PrecheckAlive { get; init; }

        /// <summary>
        /// 只读内存的会话有效性判断（悠船/官方 Token、Discord Lock）。
        /// pipeline 完成后调用，确保会话/Lock 状态取最终值。
        /// </summary>
        public Func<bool> IsSessionValid { get; init; } = () => true;
    }
}

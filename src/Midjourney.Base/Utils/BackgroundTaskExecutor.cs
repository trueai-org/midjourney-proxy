using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Midjourney.Base.Util
{
    /// <summary>
    /// 后台任务执行器 — 继承 BackgroundService，由 DI 容器管理生命周期
    ///
    /// 优势：
    /// 1. Host 启动时自动 Start，关闭时自动 StopAsync（优雅关闭）
    /// 2. 不需要手动调用 Start/Stop
    /// 3. 与 IHostApplicationLifetime 天然集成
    /// 4. 健康检查可直接注入检查 InflightCount
    ///
    /// 策略：
    /// - 只有一个调度循环（Dispatcher）
    /// - 每来一个 WorkItem，从信号量获取一个许可，启动一个消费协程
    /// - 消费完毕，归还许可
    /// - maxConcurrency 控制同时执行的上限
    /// - 空闲时零消费者在运行
    /// </summary>
    public class BackgroundTaskExecutor : BackgroundService
    {
        private readonly Channel<BackgroundWorkItem> _channel;
        private readonly SemaphoreSlim _semaphore; // 控制最大并发
        private readonly int _maxConcurrency;
        private readonly string _name;

        public int PendingCount => _channel.Reader.Count;

        /// <summary>
        /// 当前活跃的消费者数量
        /// </summary>
        private int _activeConsumers;

        public int ActiveConsumers => Volatile.Read(ref _activeConsumers);

        public BackgroundTaskExecutor(string name, int maxConcurrency = 3, int capacity = 128)
        {
            _name = name;
            _maxConcurrency = Math.Max(1, maxConcurrency);
            _semaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
            _channel = Channel.CreateBounded<BackgroundWorkItem>(new BoundedChannelOptions(capacity)
            {
                // ═══ capacity（构造函数参数）═══
                // 缓冲区大小，即最多缓存多少个未消费的 item
                // 你的场景：Account.QueueSize * 2 ≈ 64~256

                // ═══ FullMode ═══
                // 缓冲区满时的行为策略
                FullMode = BoundedChannelFullMode.Wait,

                //
                // Wait          → 写入者异步等待，直到有空位（推荐，天然背压）
                // DropNewest    → 丢弃缓冲区中最新的 item，腾出空位写入
                // DropOldest    → 丢弃缓冲区中最旧的 item，腾出空位写入
                // DropWrite     → 直接丢弃当前要写入的 item（静默丢弃）

                // ═══ SingleWriter ═══
                // 是否只有一个线程写入
                SingleWriter = false,
                // true  → 内部用更轻量的同步原语（性能更好）
                // false → 多线程安全写入（你的场景：Running 主循环是单线程写入，可以设 true）

                // ═══ SingleReader ═══
                // 是否只有一个线程读取
                SingleReader = false,
                // true  → 内部优化，只允许一个消费者
                // false → 多个消费者并行读取（你的场景：N 个 Consumer 并行读，必须 false）

                // ═══ AllowSynchronousContinuations ═══
                // 是否允许同步延续
                AllowSynchronousContinuations = false,
                // true  → 写入时如果有读者在等待，直接在写入线程上执行读者的回调（低延迟）
                // false → 读者的回调在线程池上执行（更安全，避免写入线程被消费者逻辑阻塞）
                // 你的场景：消费者执行 RedisQueueUpdateProgress 可能很慢，必须 false
            });
        }

        public bool TryPost(BackgroundWorkItem item) => _channel.Writer.TryWrite(item);

        public ValueTask PostAsync(BackgroundWorkItem item, CancellationToken token = default)
            => _channel.Writer.WriteAsync(item, token);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Log.Information("[{Name}] 执行器已启动，最大并发: {Max}", _name, _maxConcurrency);

            // 用于跟踪所有飞行中的消费任务，关闭时等待它们完成
            var inflightTasks = new List<Task>();

            try
            {
                await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
                {
                    // 等待一个并发许可（满了就在这里等）
                    await _semaphore.WaitAsync(stoppingToken);

                    // 启动一个消费协程处理理这个 item
                    var task = ConsumeOneAsync(item);
                    inflightTasks.Add(task);

                    // 定期清理已完成的 Task，防止 List 无限增长
                    if (inflightTasks.Count > _maxConcurrency * 1.2)
                    {
                        inflightTasks.RemoveAll(t => t.IsCompleted);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Host 关闭，正常退出调度循环
            }

            // 等待所有飞行中的任务完成
            if (inflightTasks.Count > 0)
            {
                Log.Information("[{Name}] 等待 {Count} 个飞行中的任务完成...",
                    _name, inflightTasks.Count(t => !t.IsCompleted));
                await Task.WhenAll(inflightTasks);
            }

            Log.Information("[{Name}] 执行器已停止", _name);
        }

        private async Task ConsumeOneAsync(BackgroundWorkItem item)
        {
            try
            {
                Interlocked.Increment(ref _activeConsumers);

                Log.Information("[{Name}] 收到新任务: {Desc}, 活跃: {Active}, 排队: {Pending}",
                    _name, item.Description, ActiveConsumers, PendingCount);

                await item.WorkAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[{Name}] 任务执行异常: {Desc}", _name, item.Description);
            }
            finally
            {
                Interlocked.Decrement(ref _activeConsumers);

                // 归还并发许可
                _semaphore.Release();

                SafeInvokeCompleted(item);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            Log.Information("[{Name}] 正在关闭，活跃: {Active}, 排队: {Pending}",
                _name, ActiveConsumers, PendingCount);

            _channel.Writer.TryComplete();
            await base.StopAsync(cancellationToken);
        }

        private void SafeInvokeCompleted(BackgroundWorkItem item)
        {
            try
            {
                item.OnCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[{Name}] OnCompleted 回调异常: {Desc}", _name, item.Description);
            }
        }
    }

    /// <summary>
    /// 后台工作项
    /// </summary>
    public sealed class BackgroundWorkItem
    {
        /// <summary>
        /// 核心业务逻辑
        /// </summary>
        public Func<Task> WorkAsync { get; init; }

        /// <summary>
        /// 完成回调（释放锁、发布通知等），无论成功/异常都执行
        /// </summary>
        public Action OnCompleted { get; init; }

        /// <summary>
        /// 描述（用于日志）
        /// </summary>
        public string Description { get; init; }
    }
}
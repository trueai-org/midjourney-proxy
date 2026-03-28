using System.Collections.Concurrent;
using System.Diagnostics;
using Midjourney.Base.Util;
using Xunit.Abstractions;

namespace Midjourney.Tests
{
    /// <summary>
    /// AsyncParallelLock 单元测试
    /// </summary>
    public class AsyncParallelLockTests : BaseTests
    {
        private readonly TestOutputWrapper _output;

        public AsyncParallelLockTests(ITestOutputHelper output)
        {
            _output = new TestOutputWrapper(output);
        }

        #region using 模式基础功能

        [Fact]
        public async Task Acquire_Using_自动释放()
        {
            using var locker = new AsyncParallelLock(1);

            using (var h = await locker.AcquireAsync())
            {
                Assert.True(h.IsAcquired);
                Assert.Equal(1, locker.CurrentlyHeldCount);
                Assert.Equal(0, locker.AvailableCount);
                _output.WriteLine($"持有中: held={locker.CurrentlyHeldCount}, available={locker.AvailableCount}");
            }

            Assert.Equal(0, locker.CurrentlyHeldCount);
            Assert.Equal(1, locker.AvailableCount);
            _output.WriteLine("using 结束自动释放");
        }

        [Fact]
        public async Task Acquire_释放后可重新获取()
        {
            using var locker = new AsyncParallelLock(1);

            using (await locker.AcquireAsync()) { }
            using (var h = await locker.AcquireAsync())
                Assert.True(h.IsAcquired);

            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine("释放后重新获取正常");
        }

        [Fact]
        public async Task TryAcquire_成功获取()
        {
            using var locker = new AsyncParallelLock(1);

            using var h = await locker.TryAcquireAsync(TimeSpan.FromSeconds(5));
            Assert.True(h.IsAcquired);
            Assert.Equal(1, locker.CurrentlyHeldCount);
            _output.WriteLine("TryAcquire 成功");
        }

        [Fact]
        public async Task TryAcquire_超时返回false()
        {
            using var locker = new AsyncParallelLock(1);

            using (await locker.AcquireAsync())
            {
                using var h = await locker.TryAcquireAsync(TimeSpan.FromMilliseconds(100));
                Assert.False(h.IsAcquired);
                _output.WriteLine("超时正确返回 IsAcquired=false");
            }
        }

        [Fact]
        public async Task TryAcquire_Zero超时_立即返回()
        {
            using var locker = new AsyncParallelLock(1);

            using (await locker.AcquireAsync())
            {
                var sw = Stopwatch.StartNew();
                using var h = await locker.TryAcquireAsync(TimeSpan.Zero);
                sw.Stop();

                Assert.False(h.IsAcquired);
                Assert.True(sw.ElapsedMilliseconds < 1000,
                    $"应立即返回，实际耗时 {sw.ElapsedMilliseconds}ms");
                _output.WriteLine($"Zero 超时: {sw.ElapsedMilliseconds}ms");
            }
        }

        [Fact]
        public async Task TryAcquire_超时后不泄漏()
        {
            using var locker = new AsyncParallelLock(1);

            using (await locker.AcquireAsync())
            {
                using var h = await locker.TryAcquireAsync(TimeSpan.FromMilliseconds(50));
                Assert.False(h.IsAcquired);
            }

            Assert.Equal(0, locker.CurrentlyHeldCount);
            Assert.Equal(0, locker.WaitingCount);
            Assert.True(locker.AreAllLocksAvailable());
            _output.WriteLine("超时后无泄漏");
        }

        [Fact]
        public async Task TryAcquire_未获取的Handle_Dispose安全()
        {
            using var locker = new AsyncParallelLock(1);

            using (await locker.AcquireAsync())
            {
                using var h = await locker.TryAcquireAsync(TimeSpan.FromMilliseconds(10));
                Assert.False(h.IsAcquired);
                h.Dispose();
                h.Dispose();
            }

            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine("未获取的 Handle 多次 Dispose 安全");
        }

        #endregion using 模式基础功能

        #region Dispose 幂等

        [Fact]
        public async Task Handle_多次Dispose_只释放一次()
        {
            using var locker = new AsyncParallelLock(1);

            var h = await locker.AcquireAsync();
            Assert.Equal(1, locker.CurrentlyHeldCount);

            h.Dispose();
            Assert.Equal(0, locker.CurrentlyHeldCount);

            h.Dispose();
            Assert.Equal(0, locker.CurrentlyHeldCount);

            h.Dispose();
            Assert.Equal(0, locker.CurrentlyHeldCount);

            _output.WriteLine("多次 Dispose 安全");
        }

        [Fact]
        public async Task Handle_Empty_Dispose安全()
        {
            var empty = AsyncParallelLock.LockHandle.Empty;
            Assert.False(empty.IsAcquired);
            empty.Dispose();
            empty.Dispose();
            _output.WriteLine("Empty Handle 多次 Dispose 无异常");
        }

        [Fact]
        public async Task Handle_幂等释放不破坏互斥性()
        {
            using var locker = new AsyncParallelLock(1);

            var h = await locker.AcquireAsync();
            h.Dispose();
            h.Dispose();

            using (var r1 = await locker.TryAcquireAsync(TimeSpan.FromSeconds(1)))
            {
                Assert.True(r1.IsAcquired);
                using var r2 = await locker.TryAcquireAsync(TimeSpan.FromMilliseconds(50));
                Assert.False(r2.IsAcquired, "幂等 Dispose 不应破坏互斥性");
            }

            _output.WriteLine("幂等释放后互斥性正常");
        }

        [Fact]
        public async Task Handle_手动Unlock后_Handle_Dispose安全()
        {
            using var locker = new AsyncParallelLock(1);

            var h = await locker.AcquireAsync();
            locker.Unlock(); // 手动先释放

            // Handle.Dispose 会再调 Unlock → 抛 InvalidOperationException → 被 catch
            h.Dispose();

            Assert.Equal(0, locker.CurrentlyHeldCount);

            // 锁仍可用
            using var h2 = await locker.TryAcquireAsync(TimeSpan.FromSeconds(1));
            Assert.True(h2.IsAcquired);
            _output.WriteLine("手动 Unlock + Handle Dispose 不冲突");
        }

        #endregion Dispose 幂等

        #region 并发度控制

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(10)]
        public async Task 并发数永远不超过MaxParallelism(int maxParallelism)
        {
            using var locker = new AsyncParallelLock(maxParallelism);
            int current = 0;
            bool exceeded = false;

            var tasks = Enumerable.Range(0, maxParallelism * 5).Select(_ => Task.Run(async () =>
            {
                using (await locker.AcquireAsync())
                {
                    if (Interlocked.Increment(ref current) > maxParallelism)
                        exceeded = true;
                    await Task.Delay(10);
                    Interlocked.Decrement(ref current);
                }
            }));

            await Task.WhenAll(tasks);

            Assert.False(exceeded, $"并发超过了 maxParallelism={maxParallelism}");
            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine($"maxParallelism={maxParallelism} 测试通过");
        }

        [Fact]
        public async Task 并发度3_实际能并行()
        {
            using var locker = new AsyncParallelLock(3);
            int peakConcurrent = 0;
            int current = 0;

            var tasks = Enumerable.Range(0, 30).Select(_ => Task.Run(async () =>
            {
                using (await locker.AcquireAsync())
                {
                    var val = Interlocked.Increment(ref current);
                    int snapshot;
                    do { snapshot = peakConcurrent; }
                    while (val > snapshot && Interlocked.CompareExchange(ref peakConcurrent, val, snapshot) != snapshot);

                    await Task.Delay(20);
                    Interlocked.Decrement(ref current);
                }
            }));

            await Task.WhenAll(tasks);

            Assert.True(peakConcurrent >= 2, $"并发度应>=2，实际={peakConcurrent}");
            Assert.True(peakConcurrent <= 3, $"并发度应<=3，实际={peakConcurrent}");
            _output.WriteLine($"峰值并发: {peakConcurrent}");
        }

        [Fact]
        public async Task 所有等待者最终都能执行()
        {
            using var locker = new AsyncParallelLock(2);
            var executed = new ConcurrentBag<int>();
            var barrier = new SemaphoreSlim(0);

            var h1 = await locker.AcquireAsync();
            var h2 = await locker.AcquireAsync();

            var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
            {
                barrier.Release();
                using (await locker.AcquireAsync())
                    executed.Add(i);
            })).ToArray();

            for (int i = 0; i < 10; i++) await barrier.WaitAsync();
            await Task.Delay(100);

            h1.Dispose();
            h2.Dispose();
            await Task.WhenAll(tasks);

            Assert.Equal(10, executed.Count);
            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine("全部 10 个等待者执行完成");
        }

        #endregion 并发度控制

        #region WaitingCount 跟踪

        [Fact]
        public async Task WaitingCount_正常跟踪()
        {
            using var locker = new AsyncParallelLock(1);
            var barrier = new SemaphoreSlim(0);
            var waitersReady = new SemaphoreSlim(0);

            Assert.Equal(0, locker.WaitingCount);

            // 占住锁
            var holder = await locker.AcquireAsync();

            // 启动 5 个等待者
            var tasks = Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
            {
                waitersReady.Release();
                using (await locker.AcquireAsync())
                    barrier.Release();
            })).ToArray();

            // 等所有等待者排上队
            for (int i = 0; i < 5; i++) await waitersReady.WaitAsync();
            await Task.Delay(100);

            Assert.Equal(5, locker.WaitingCount);
            _output.WriteLine($"排队中 WaitingCount={locker.WaitingCount}");

            holder.Dispose();

            // 等所有完成
            for (int i = 0; i < 5; i++) await barrier.WaitAsync();
            await Task.WhenAll(tasks);

            Assert.Equal(0, locker.WaitingCount);
            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine("全部完成后 WaitingCount=0");
        }

        [Fact]
        public async Task WaitingCount_取消时正确递减()
        {
            using var locker = new AsyncParallelLock(1);
            using var holder = await locker.AcquireAsync();

            var cts = new CancellationTokenSource(50);
            try { await locker.AcquireAsync(cts.Token); }
            catch (OperationCanceledException) { }

            Assert.Equal(0, locker.WaitingCount);
            _output.WriteLine("取消后 WaitingCount 正确递减");
        }

        [Fact]
        public async Task WaitingCount_超时时正确递减()
        {
            using var locker = new AsyncParallelLock(1);
            using var holder = await locker.AcquireAsync();

            using var h = await locker.TryAcquireAsync(TimeSpan.FromMilliseconds(50));
            Assert.False(h.IsAcquired);
            Assert.Equal(0, locker.WaitingCount);
            _output.WriteLine("超时后 WaitingCount 正确递减");
        }

        #endregion WaitingCount 跟踪

        #region 取消

        [Fact]
        public async Task Acquire_取消时抛异常()
        {
            using var locker = new AsyncParallelLock(1);

            using (await locker.AcquireAsync())
            {
                var cts = new CancellationTokenSource(50);
                await Assert.ThrowsAsync<OperationCanceledException>(
                    () => locker.AcquireAsync(cts.Token));
            }

            Assert.Equal(0, locker.CurrentlyHeldCount);
            Assert.Equal(0, locker.WaitingCount);
            _output.WriteLine("取消后状态正确");
        }

        [Fact]
        public async Task TryAcquire_取消时抛异常()
        {
            using var locker = new AsyncParallelLock(1);

            using (await locker.AcquireAsync())
            {
                var cts = new CancellationTokenSource(50);
                await Assert.ThrowsAsync<OperationCanceledException>(
                    () => locker.TryAcquireAsync(TimeSpan.FromSeconds(30), cts.Token));
            }

            Assert.Equal(0, locker.CurrentlyHeldCount);
            Assert.Equal(0, locker.WaitingCount);
            _output.WriteLine("TryAcquire 取消后状态正确");
        }

        [Fact]
        public async Task 多个等待者取消后_锁仍可用()
        {
            using var locker = new AsyncParallelLock(1);
            using var holder = await locker.AcquireAsync();

            var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
            {
                var cts = new CancellationTokenSource(30);
                try { using var h = await locker.AcquireAsync(cts.Token); }
                catch (OperationCanceledException) { }
            }));

            await Task.WhenAll(tasks);

            Assert.Equal(0, locker.WaitingCount);

            holder.Dispose();

            using var h2 = await locker.TryAcquireAsync(TimeSpan.FromSeconds(1));
            Assert.True(h2.IsAcquired);
            _output.WriteLine("20 个取消后锁状态正常");
        }

        [Fact]
        public async Task 已取消的Token_直接抛异常_不进入等待()
        {
            using var locker = new AsyncParallelLock(1);
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Assert.ThrowsAsync<T>	精确匹配，typeof(ex) == typeof(T)
            // Assert.ThrowsAnyAsync<T>	继承匹配，ex is T
            // SemaphoreSlim.WaitAsync  在已取消的 token 下抛的是 TaskCanceledException。
            await Assert.ThrowsAsync<TaskCanceledException>(
                () => locker.AcquireAsync(cts.Token));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
              () => locker.AcquireAsync(cts.Token));

            Assert.Equal(0, locker.CurrentlyHeldCount);
            Assert.Equal(0, locker.WaitingCount);
            _output.WriteLine("已取消的 Token 直接抛异常");
        }

        #endregion 取消

        #region 异常安全

        [Fact]
        public async Task 业务代码异常_锁自动释放()
        {
            using var locker = new AsyncParallelLock(1);

            try
            {
                using (await locker.AcquireAsync())
                    throw new InvalidOperationException("业务异常");
            }
            catch (InvalidOperationException) { }

            Assert.Equal(0, locker.CurrentlyHeldCount);

            using var h = await locker.TryAcquireAsync(TimeSpan.FromSeconds(1));
            Assert.True(h.IsAcquired);
            _output.WriteLine("业务异常后 using 自动释放锁");
        }

        [Fact]
        public async Task 嵌套异常_外层锁也正确释放()
        {
            using var locker = new AsyncParallelLock(2);

            try
            {
                using (await locker.AcquireAsync())
                {
                    using (await locker.AcquireAsync())
                        throw new Exception("内层异常");
                }
            }
            catch (Exception) { }

            Assert.Equal(0, locker.CurrentlyHeldCount);
            Assert.True(locker.AreAllLocksAvailable());
            _output.WriteLine("嵌套异常后全部释放");
        }

        #endregion 异常安全

        #region 原有手动模式兼容

        [Fact]
        public async Task Legacy_LockAsync_Unlock()
        {
            using var locker = new AsyncParallelLock(1);

            await locker.LockAsync();
            Assert.Equal(1, locker.CurrentlyHeldCount);

            locker.Unlock();
            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine("LockAsync + Unlock 正常");
        }

        [Fact]
        public void Legacy_同步Lock_Unlock()
        {
            using var locker = new AsyncParallelLock(1);

            locker.Lock();
            Assert.Equal(1, locker.CurrentlyHeldCount);

            locker.Unlock();
            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine("同步 Lock + Unlock 正常");
        }

        [Fact]
        public void Legacy_TryLock()
        {
            using var locker = new AsyncParallelLock(1);

            Assert.True(locker.TryLock());
            Assert.Equal(1, locker.CurrentlyHeldCount);

            Assert.False(locker.TryLock());

            locker.Unlock();
            Assert.True(locker.TryLock());
            locker.Unlock();

            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine("TryLock 正常");
        }

        [Fact]
        public void Legacy_未获取时Unlock_抛异常()
        {
            using var locker = new AsyncParallelLock(1);
            Assert.Throws<InvalidOperationException>(() => locker.Unlock());
            _output.WriteLine("未获取时 Unlock 正确抛出异常");
        }

        [Fact]
        public async Task Legacy_LockAsync_取消时抛异常()
        {
            using var locker = new AsyncParallelLock(1);

            using (await locker.AcquireAsync())
            {
                var cts = new CancellationTokenSource(50);
                await Assert.ThrowsAsync<OperationCanceledException>(
                    () => locker.LockAsync(cts.Token));
            }

            Assert.Equal(0, locker.WaitingCount);
            _output.WriteLine("LockAsync 取消后 WaitingCount 正确");
        }

        [Fact]
        public void Legacy_同步Lock_取消时抛异常()
        {
            using var locker = new AsyncParallelLock(1);
            locker.Lock();

            var cts = new CancellationTokenSource(50);
            Assert.Throws<OperationCanceledException>(() => locker.Lock(cts.Token));

            Assert.Equal(0, locker.WaitingCount);
            locker.Unlock();
            _output.WriteLine("同步 Lock 取消后 WaitingCount 正确");
        }

        [Fact]
        public async Task Legacy_和using模式混合使用()
        {
            using var locker = new AsyncParallelLock(2);
            int done = 0;

            var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
            {
                if (i % 2 == 0)
                {
                    using (await locker.AcquireAsync())
                        Interlocked.Increment(ref done);
                }
                else
                {
                    await locker.LockAsync();
                    try { Interlocked.Increment(ref done); }
                    finally { locker.Unlock(); }
                }
            }));

            await Task.WhenAll(tasks);

            Assert.Equal(50, done);
            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine("using + 手动混合模式通过");
        }

        [Fact]
        public void Legacy_TryLock_WaitingCount追踪()
        {
            using var locker = new AsyncParallelLock(1);

            // TryLock 是同步立即返回的，WaitingCount 应该始终回到 0
            Assert.True(locker.TryLock());
            Assert.Equal(0, locker.WaitingCount);

            Assert.False(locker.TryLock());
            Assert.Equal(0, locker.WaitingCount);

            locker.Unlock();
            _output.WriteLine("TryLock 的 WaitingCount 始终回到 0");
        }

        #endregion 原有手动模式兼容

        #region 动态调整并行度

        [Fact]
        public void SetMaxParallelism_空闲时成功()
        {
            using var locker = new AsyncParallelLock(2);

            Assert.True(locker.SetMaxParallelism(5));
            Assert.Equal(5, locker.MaxParallelism);
            Assert.Equal(5, locker.AvailableCount);
            _output.WriteLine("空闲时调整: 2 → 5");
        }

        [Fact]
        public async Task SetMaxParallelism_有持有者时失败()
        {
            using var locker = new AsyncParallelLock(2);

            using (await locker.AcquireAsync())
            {
                Assert.False(locker.SetMaxParallelism(5));
                Assert.Equal(2, locker.MaxParallelism);
            }

            _output.WriteLine("有持有者时调整正确拒绝");
        }

        [Fact]
        public async Task SetMaxParallelism_有等待者时失败()
        {
            using var locker = new AsyncParallelLock(1);
            var holder = await locker.AcquireAsync();
            var barrier = new SemaphoreSlim(0);

            // 启动一个等待者
            var waiterTask = Task.Run(async () =>
            {
                barrier.Release();
                using (await locker.AcquireAsync()) { }
            });

            await barrier.WaitAsync();
            await Task.Delay(50);

            Assert.True(locker.WaitingCount > 0);
            Assert.False(locker.SetMaxParallelism(5), "有等待者时应拒绝调整");
            Assert.Equal(1, locker.MaxParallelism);

            holder.Dispose();
            await waiterTask;
            _output.WriteLine("有等待者时调整正确拒绝");
        }

        [Fact]
        public void SetMaxParallelism_相同值_无操作()
        {
            using var locker = new AsyncParallelLock(3);
            Assert.True(locker.SetMaxParallelism(3));
            Assert.Equal(3, locker.MaxParallelism);
            _output.WriteLine("相同值无操作");
        }

        [Fact]
        public async Task SetMaxParallelism_调整后新并发度生效()
        {
            using var locker = new AsyncParallelLock(1);

            Assert.True(locker.SetMaxParallelism(3));

            var h1 = await locker.AcquireAsync();
            var h2 = await locker.AcquireAsync();
            var h3 = await locker.AcquireAsync();
            Assert.Equal(3, locker.CurrentlyHeldCount);

            using var h4 = await locker.TryAcquireAsync(TimeSpan.FromMilliseconds(50));
            Assert.False(h4.IsAcquired);

            h1.Dispose();
            h2.Dispose();
            h3.Dispose();
            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine("调整为 3 后新并发度生效");
        }

        [Fact]
        public void SetMaxParallelism_调小()
        {
            using var locker = new AsyncParallelLock(10);

            Assert.True(locker.SetMaxParallelism(2));
            Assert.Equal(2, locker.MaxParallelism);
            Assert.Equal(2, locker.AvailableCount);
            _output.WriteLine("调小: 10 → 2");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-10)]
        public void SetMaxParallelism_无效值_抛异常(int value)
        {
            using var locker = new AsyncParallelLock(1);
            Assert.Throws<ArgumentException>(() => locker.SetMaxParallelism(value));
            _output.WriteLine($"无效值 {value} 正确抛异常");
        }

        #endregion 动态调整并行度

        #region 监控 API

        [Fact]
        public async Task Monitor_各属性全流程状态正确()
        {
            using var locker = new AsyncParallelLock(3);

            Assert.Equal(3, locker.MaxParallelism);
            Assert.Equal(0, locker.CurrentlyHeldCount);
            Assert.Equal(0, locker.WaitingCount);
            Assert.Equal(3, locker.AvailableCount);
            Assert.True(locker.IsLockAvailable());
            Assert.True(locker.AreAllLocksAvailable());

            var h1 = await locker.AcquireAsync();
            Assert.Equal(1, locker.CurrentlyHeldCount);
            Assert.Equal(2, locker.AvailableCount);
            Assert.True(locker.IsLockAvailable());
            Assert.False(locker.AreAllLocksAvailable());

            var h2 = await locker.AcquireAsync();
            var h3 = await locker.AcquireAsync();
            Assert.Equal(3, locker.CurrentlyHeldCount);
            Assert.Equal(0, locker.AvailableCount);
            Assert.False(locker.IsLockAvailable());
            Assert.False(locker.AreAllLocksAvailable());

            h1.Dispose();
            Assert.Equal(2, locker.CurrentlyHeldCount);
            Assert.Equal(1, locker.AvailableCount);
            Assert.True(locker.IsLockAvailable());

            h2.Dispose();
            h3.Dispose();
            Assert.Equal(0, locker.CurrentlyHeldCount);
            Assert.Equal(3, locker.AvailableCount);
            Assert.True(locker.AreAllLocksAvailable());

            _output.WriteLine("监控属性全流程正确");
        }

        [Fact]
        public async Task AreAllLocksAvailable_有等待者时返回false()
        {
            using var locker = new AsyncParallelLock(1);
            var holder = await locker.AcquireAsync();
            var barrier = new SemaphoreSlim(0);

            var waiterTask = Task.Run(async () =>
            {
                barrier.Release();
                using (await locker.AcquireAsync()) { }
            });

            await barrier.WaitAsync();
            await Task.Delay(50);

            // 虽然没有额外持有者，但有等待者
            Assert.False(locker.AreAllLocksAvailable());

            holder.Dispose();
            await waiterTask;

            Assert.True(locker.AreAllLocksAvailable());
            _output.WriteLine("有等待者时 AreAllLocksAvailable=false");
        }

        #endregion 监控 API

        #region 参数验证

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public void 构造函数_无效并行数_抛异常(int value)
        {
            Assert.Throws<ArgumentException>(() => new AsyncParallelLock(value));
            _output.WriteLine($"无效并行数 {value} 正确抛异常");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        public void 构造函数_正常值(int value)
        {
            using var locker = new AsyncParallelLock(value);
            Assert.Equal(value, locker.MaxParallelism);
            Assert.Equal(value, locker.AvailableCount);
            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine($"构造 maxParallelism={value} 正常");
        }

        #endregion 参数验证

        #region Dispose

        [Fact]
        public void Dispose_多次调用安全()
        {
            var locker = new AsyncParallelLock(1);
            locker.Dispose();
            locker.Dispose();
            _output.WriteLine("双重 Dispose 安全");
        }

        [Fact]
        public void Dispose_后AvailableCount为0()
        {
            var locker = new AsyncParallelLock(3);
            Assert.Equal(3, locker.AvailableCount);

            locker.Dispose();
            Assert.Equal(0, locker.AvailableCount);
            _output.WriteLine("Dispose 后 AvailableCount=0");
        }

        [Fact]
        public async Task Dispose_后Acquire_抛ObjectDisposedException()
        {
            var locker = new AsyncParallelLock(1);
            locker.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => locker.AcquireAsync());
            _output.WriteLine("Dispose 后 Acquire 正确抛异常");
        }

        [Fact]
        public async Task Dispose_后TryAcquire_抛ObjectDisposedException()
        {
            var locker = new AsyncParallelLock(1);
            locker.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => locker.TryAcquireAsync(TimeSpan.FromSeconds(1)));
            _output.WriteLine("Dispose 后 TryAcquire 正确抛异常");
        }

        [Fact]
        public void Dispose_后LockAsync_抛ObjectDisposedException()
        {
            var locker = new AsyncParallelLock(1);
            locker.Dispose();

            Assert.ThrowsAsync<ObjectDisposedException>(
                () => locker.LockAsync());
            _output.WriteLine("Dispose 后 LockAsync 正确抛异常");
        }

        [Fact]
        public void Dispose_后Unlock_抛ObjectDisposedException()
        {
            var locker = new AsyncParallelLock(1);
            locker.Dispose();

            Assert.Throws<ObjectDisposedException>(() => locker.Unlock());
            _output.WriteLine("Dispose 后 Unlock 正确抛异常");
        }

        [Fact]
        public void Dispose_后SetMaxParallelism_抛ObjectDisposedException()
        {
            var locker = new AsyncParallelLock(1);
            locker.Dispose();

            Assert.Throws<ObjectDisposedException>(() => locker.SetMaxParallelism(5));
            _output.WriteLine("Dispose 后 SetMaxParallelism 正确抛异常");
        }

        [Fact]
        public void Dispose_后IsLockAvailable_返回false()
        {
            var locker = new AsyncParallelLock(1);
            locker.Dispose();

            Assert.False(locker.IsLockAvailable());
            Assert.False(locker.AreAllLocksAvailable());
            _output.WriteLine("Dispose 后监控 API 返回 false");
        }

        [Fact]
        public async Task Dispose_后Handle_Dispose安全()
        {
            var locker = new AsyncParallelLock(1);
            var h = await locker.AcquireAsync();

            locker.Dispose();

            // Handle.Dispose → Unlock → ThrowIfDisposed → ODE → 被 catch (IOE)
            h.Dispose();
            h.Dispose(); // 幂等

            _output.WriteLine("Dispose 后 Handle Dispose 安全（ODE 被 catch）");
        }

        #endregion Dispose

        #region 压力测试

        [Fact]
        public async Task Stress_单并发_10000次()
        {
            using var locker = new AsyncParallelLock(1);
            int done = 0;
            var sw = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(async () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    using (await locker.AcquireAsync())
                        Interlocked.Increment(ref done);
                }
            }));

            await Task.WhenAll(tasks);
            sw.Stop();

            Assert.Equal(10000, done);
            Assert.Equal(0, locker.CurrentlyHeldCount);
            Assert.Equal(0, locker.WaitingCount);
            _output.WriteLine($"单并发 10000 次，耗时: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task Stress_多并发_10000次()
        {
            using var locker = new AsyncParallelLock(5);
            int done = 0;
            var sw = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(async () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    using (await locker.AcquireAsync())
                    {
                        await Task.Yield();
                        Interlocked.Increment(ref done);
                    }
                }
            }));

            await Task.WhenAll(tasks);
            sw.Stop();

            Assert.Equal(10000, done);
            Assert.Equal(0, locker.CurrentlyHeldCount);
            Assert.Equal(0, locker.WaitingCount);
            _output.WriteLine($"5 并发 10000 次，耗时: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task Stress_混合超时和取消()
        {
            using var locker = new AsyncParallelLock(2);
            int acquired = 0, timedOut = 0, cancelled = 0;

            var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
            {
                for (int j = 0; j < 10; j++)
                {
                    try
                    {
                        switch (j % 3)
                        {
                            case 0:
                                using (await locker.AcquireAsync())
                                {
                                    await Task.Delay(5);
                                    Interlocked.Increment(ref acquired);
                                }
                                break;

                            case 1:
                                using (var h = await locker.TryAcquireAsync(TimeSpan.FromMilliseconds(10)))
                                {
                                    if (h.IsAcquired) Interlocked.Increment(ref acquired);
                                    else Interlocked.Increment(ref timedOut);
                                }
                                break;

                            case 2:
                                var cts = new CancellationTokenSource(5);
                                try
                                {
                                    using (await locker.AcquireAsync(cts.Token))
                                        Interlocked.Increment(ref acquired);
                                }
                                catch (OperationCanceledException)
                                {
                                    Interlocked.Increment(ref cancelled);
                                }
                                break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Interlocked.Increment(ref cancelled);
                    }
                }
            }));

            await Task.WhenAll(tasks);

            Assert.True(acquired > 0);
            Assert.Equal(0, locker.CurrentlyHeldCount);
            Assert.Equal(0, locker.WaitingCount);

            _output.WriteLine($"混合压测: acquired={acquired}, timedOut={timedOut}, cancelled={cancelled}");
        }

        [Fact]
        public async Task Stress_并发度验证_从不超限()
        {
            const int maxP = 5;
            using var locker = new AsyncParallelLock(maxP);
            int current = 0;
            bool violated = false;
            var sw = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, 200).Select(_ => Task.Run(async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    using (await locker.AcquireAsync())
                    {
                        if (Interlocked.Increment(ref current) > maxP)
                            violated = true;
                        await Task.Yield();
                        Interlocked.Decrement(ref current);
                    }
                }
            }));

            await Task.WhenAll(tasks);
            sw.Stop();

            Assert.False(violated, $"并发度超过了 {maxP}");
            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine($"10000 次，从未超过并发度 {maxP}，耗时: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task Stress_混合using和Legacy_并发度不超限()
        {
            const int maxP = 3;
            using var locker = new AsyncParallelLock(maxP);
            int current = 0;
            bool violated = false;
            int done = 0;

            var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
            {
                for (int j = 0; j < 30; j++)
                {
                    if (j % 3 == 0)
                    {
                        // using 模式
                        using (await locker.AcquireAsync())
                        {
                            if (Interlocked.Increment(ref current) > maxP) violated = true;
                            await Task.Yield();
                            Interlocked.Decrement(ref current);
                            Interlocked.Increment(ref done);
                        }
                    }
                    else if (j % 3 == 1)
                    {
                        // 手动模式
                        await locker.LockAsync();
                        try
                        {
                            if (Interlocked.Increment(ref current) > maxP) violated = true;
                            await Task.Yield();
                            Interlocked.Decrement(ref current);
                            Interlocked.Increment(ref done);
                        }
                        finally { locker.Unlock(); }
                    }
                    else
                    {
                        // TryAcquire 模式
                        using (var h = await locker.TryAcquireAsync(TimeSpan.FromSeconds(5)))
                        {
                            if (h.IsAcquired)
                            {
                                if (Interlocked.Increment(ref current) > maxP) violated = true;
                                await Task.Yield();
                                Interlocked.Decrement(ref current);
                                Interlocked.Increment(ref done);
                            }
                        }
                    }
                }
            }));

            await Task.WhenAll(tasks);

            Assert.False(violated, $"并发度超过了 {maxP}");
            Assert.Equal(0, locker.CurrentlyHeldCount);
            Assert.Equal(0, locker.WaitingCount);
            _output.WriteLine($"三种模式混合 {done} 次操作，并发度从未超过 {maxP}");
        }

        #endregion 压力测试
    }
}
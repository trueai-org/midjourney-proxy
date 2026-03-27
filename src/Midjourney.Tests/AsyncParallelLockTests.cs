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
            _output.WriteLine("using 结束自动释放，锁已归还");
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

            // 先占住
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
        public async Task TryAcquire_未获取的Handle_Dispose安全()
        {
            using var locker = new AsyncParallelLock(1);

            using (await locker.AcquireAsync())
            {
                using var h = await locker.TryAcquireAsync(TimeSpan.FromMilliseconds(10));
                Assert.False(h.IsAcquired);
                h.Dispose(); // 不应抛异常
                h.Dispose(); // 多次也安全
            }

            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine("未获取的 Handle Dispose 安全");
        }

        #endregion

        #region Dispose 幂等

        [Fact]
        public async Task Handle_多次Dispose_只释放一次()
        {
            using var locker = new AsyncParallelLock(1);

            var h = await locker.AcquireAsync();
            Assert.Equal(1, locker.CurrentlyHeldCount);

            h.Dispose();
            Assert.Equal(0, locker.CurrentlyHeldCount);

            h.Dispose(); // 第二次
            Assert.Equal(0, locker.CurrentlyHeldCount);

            h.Dispose(); // 第三次
            Assert.Equal(0, locker.CurrentlyHeldCount);

            _output.WriteLine("多次 Dispose 安全，CurrentlyHeldCount 始终为 0");
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
            h.Dispose(); // 幂等

            // 如果 Semaphore.Release 被调了两次，下面两个请求会同时拿到锁
            using (var r1 = await locker.TryAcquireAsync(TimeSpan.FromSeconds(1)))
            {
                Assert.True(r1.IsAcquired);
                using var r2 = await locker.TryAcquireAsync(TimeSpan.FromMilliseconds(50));
                Assert.False(r2.IsAcquired, "幂等 Dispose 不应破坏互斥性");
            }

            _output.WriteLine("幂等释放后互斥性正常");
        }

        #endregion

        #region 并发度控制（核心功能）

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
            _output.WriteLine($"maxParallelism={maxParallelism}，测试通过");
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

            Assert.True(peakConcurrent >= 2, $"并发度应 >=2，实际峰值={peakConcurrent}");
            Assert.True(peakConcurrent <= 3, $"并发度应 <=3，实际峰值={peakConcurrent}");
            _output.WriteLine($"峰值并发: {peakConcurrent}");
        }

        [Fact]
        public async Task 所有等待者最终都能执行()
        {
            using var locker = new AsyncParallelLock(2);
            var executed = new ConcurrentBag<int>();
            var barrier = new SemaphoreSlim(0);

            // 先占满 2 个槽位
            var h1 = await locker.AcquireAsync();
            var h2 = await locker.AcquireAsync();
            Assert.Equal(2, locker.CurrentlyHeldCount);

            // 启动 10 个等待者
            var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
            {
                barrier.Release();
                using (await locker.AcquireAsync())
                    executed.Add(i);
            })).ToArray();

            // 等所有线程排队
            for (int i = 0; i < 10; i++) await barrier.WaitAsync();
            await Task.Delay(100);

            // 释放
            h1.Dispose();
            h2.Dispose();
            await Task.WhenAll(tasks);

            Assert.Equal(10, executed.Count);
            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine("全部 10 个等待者执行完成");
        }

        #endregion

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
            holder.Dispose();

            using var h2 = await locker.TryAcquireAsync(TimeSpan.FromSeconds(1));
            Assert.True(h2.IsAcquired, "取消后锁仍可用");
            _output.WriteLine("20 个取消后锁状态正常");
        }

        #endregion

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
            Assert.True(h.IsAcquired, "异常后锁应被释放");
            _output.WriteLine("业务异常后 using 自动释放锁");
        }

        #endregion

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
        public async Task Legacy_和using模式混合使用()
        {
            using var locker = new AsyncParallelLock(2);
            int done = 0;

            var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
            {
                if (i % 2 == 0)
                {
                    // using 模式
                    using (await locker.AcquireAsync())
                        Interlocked.Increment(ref done);
                }
                else
                {
                    // 手动模式
                    await locker.LockAsync();
                    try { Interlocked.Increment(ref done); }
                    finally { locker.Unlock(); }
                }
            }));

            await Task.WhenAll(tasks);

            Assert.Equal(50, done);
            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine("using + 手动混合模式测试通过");
        }

        #endregion

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
        public async Task SetMaxParallelism_忙碌时失败()
        {
            using var locker = new AsyncParallelLock(2);

            using (await locker.AcquireAsync())
            {
                Assert.False(locker.SetMaxParallelism(5));
                Assert.Equal(2, locker.MaxParallelism);
            }

            _output.WriteLine("忙碌时调整正确拒绝");
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

            // 应该能同时获取 3 个
            var h1 = await locker.AcquireAsync();
            var h2 = await locker.AcquireAsync();
            var h3 = await locker.AcquireAsync();

            Assert.Equal(3, locker.CurrentlyHeldCount);

            // 第 4 个应该拿不到
            using var h4 = await locker.TryAcquireAsync(TimeSpan.FromMilliseconds(50));
            Assert.False(h4.IsAcquired);

            h1.Dispose();
            h2.Dispose();
            h3.Dispose();

            Assert.Equal(0, locker.CurrentlyHeldCount);
            _output.WriteLine("调整为 3 后，新并发度生效");
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

        #endregion

        #region 监控 API

        [Fact]
        public async Task Monitor_各属性状态正确()
        {
            using var locker = new AsyncParallelLock(3);

            Assert.Equal(3, locker.MaxParallelism);
            Assert.Equal(0, locker.CurrentlyHeldCount);
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

            _output.WriteLine("监控属性全流程状态正确");
        }

        #endregion

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

        [Fact]
        public void 构造函数_正常值()
        {
            using var locker = new AsyncParallelLock(1);
            Assert.Equal(1, locker.MaxParallelism);

            using var locker2 = new AsyncParallelLock(100);
            Assert.Equal(100, locker2.MaxParallelism);

            _output.WriteLine("构造函数正常值通过");
        }

        #endregion

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
        public async Task Dispose_释放后AvailableCount为0()
        {
            var locker = new AsyncParallelLock(3);
            Assert.Equal(3, locker.AvailableCount);

            locker.Dispose();
            Assert.Equal(0, locker.AvailableCount);
            _output.WriteLine("Dispose 后 AvailableCount=0");
        }

        #endregion

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
            _output.WriteLine($"单并发 10000 次完成，耗时: {sw.ElapsedMilliseconds}ms");
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
            _output.WriteLine($"5 并发 10000 次完成，耗时: {sw.ElapsedMilliseconds}ms");
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
                            case 0: // 正常获取
                                using (await locker.AcquireAsync())
                                {
                                    await Task.Delay(5);
                                    Interlocked.Increment(ref acquired);
                                }
                                break;

                            case 1: // 超时
                                using (var h = await locker.TryAcquireAsync(TimeSpan.FromMilliseconds(10)))
                                {
                                    if (h.IsAcquired) Interlocked.Increment(ref acquired);
                                    else Interlocked.Increment(ref timedOut);
                                }
                                break;

                            case 2: // 取消
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

            Assert.True(acquired > 0, "应有成功获取");
            Assert.Equal(0, locker.CurrentlyHeldCount);

            _output.WriteLine($"混合压测: acquired={acquired}, timedOut={timedOut}, cancelled={cancelled}");
            _output.WriteLine($"总计: {acquired + timedOut + cancelled}");
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
            _output.WriteLine($"10000 次操作，从未超过并发度 {maxP}，耗时: {sw.ElapsedMilliseconds}ms");
        }

        #endregion
    }
}
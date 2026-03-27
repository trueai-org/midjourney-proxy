using System.Collections.Concurrent;
using System.Diagnostics;
using Midjourney.Base.Util;
using Xunit.Abstractions;

namespace Midjourney.Tests
{
    /// <summary>
    /// AsyncKeyedLock 单元测试
    /// </summary>
    public class AsyncKeyedLockTests : BaseTests
    {
        private readonly TestOutputWrapper _output;

        public AsyncKeyedLockTests(ITestOutputHelper output)
        {
            _output = new TestOutputWrapper(output);
        }

        // ─── 基础功能 ───

        [Fact]
        public async Task Basic_LockAndAutoRelease()
        {
            var key = $"basic-{Guid.NewGuid()}";

            using (var h = await AsyncKeyedLock.LockAsync(key))
            {
                Assert.True(h.IsAcquired);
                Assert.True(AsyncKeyedLock.HasActiveReference(key));
                _output.WriteLine($"锁已获取: {key}");
            }

            Assert.False(AsyncKeyedLock.HasActiveReference(key));
            _output.WriteLine($"锁已自动释放: {key}");
        }

        [Fact]
        public async Task Basic_ReacquireAfterRelease()
        {
            var key = $"reacq-{Guid.NewGuid()}";

            using (await AsyncKeyedLock.LockAsync(key)) { }

            using (var h = await AsyncKeyedLock.LockAsync(key))
            {
                Assert.True(h.IsAcquired);
            }

            Assert.False(AsyncKeyedLock.HasActiveReference(key));
            _output.WriteLine("释放后可重新获取");
        }

        [Fact]
        public async Task Basic_TryLock_Success_WhenAvailable()
        {
            var key = $"try-ok-{Guid.NewGuid()}";

            using var h = await AsyncKeyedLock.TryLockAsync(key, TimeSpan.FromSeconds(5));
            Assert.True(h.IsAcquired);
            _output.WriteLine("TryLock 成功获取");
        }

        [Fact]
        public async Task Basic_TryLock_WithReturnValue()
        {
            var key = $"try-val-{Guid.NewGuid()}";
            bool executed = false;

            using (var h = await AsyncKeyedLock.TryLockAsync(key, TimeSpan.FromSeconds(5)))
            {
                Assert.True(h.IsAcquired);
                executed = true;
            }

            Assert.True(executed);
            Assert.False(AsyncKeyedLock.HasActiveReference(key));
            _output.WriteLine("TryLock 带返回值测试通过");
        }

        // ─── 不同 key 并行 ───

        [Fact]
        public async Task Parallel_DifferentKeys_AreIndependent()
        {
            var kA = $"pA-{Guid.NewGuid()}";
            var kB = $"pB-{Guid.NewGuid()}";
            var order = new ConcurrentQueue<string>();

            var taskA = Task.Run(async () =>
            {
                using (await AsyncKeyedLock.LockAsync(kA))
                {
                    order.Enqueue("A-start");
                    await Task.Delay(200);
                    order.Enqueue("A-end");
                }
            });

            var taskB = Task.Run(async () =>
            {
                await Task.Delay(50);
                using (await AsyncKeyedLock.LockAsync(kB))
                    order.Enqueue("B-start");
            });

            await Task.WhenAll(taskA, taskB);

            var list = order.ToArray();
            var bIdx = Array.IndexOf(list, "B-start");
            var aEndIdx = Array.IndexOf(list, "A-end");
            Assert.True(bIdx < aEndIdx, "不同 key 应并行执行");

            _output.WriteLine($"执行顺序: {string.Join(" → ", list)}");
        }

        // ─── 同 key 串行 ───

        [Fact]
        public async Task Serial_SameKey_MutualExclusion()
        {
            var key = $"serial-{Guid.NewGuid()}";
            int counter = 0;
            bool overlap = false;

            var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
            {
                using (await AsyncKeyedLock.LockAsync(key))
                {
                    if (Interlocked.Increment(ref counter) != 1) overlap = true;
                    await Task.Delay(5);
                    Interlocked.Decrement(ref counter);
                }
            }));

            await Task.WhenAll(tasks);
            Assert.False(overlap, "同 key 必须串行，不应出现重叠");
            _output.WriteLine("同 key 串行测试通过，20 个并发无重叠");
        }

        [Fact]
        public async Task Serial_AllWaiters_EventuallyExecute()
        {
            var key = $"waiters-{Guid.NewGuid()}";
            var executed = new ConcurrentBag<int>();
            var barrier = new SemaphoreSlim(0);

            var holder = await AsyncKeyedLock.LockAsync(key);

            var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
            {
                barrier.Release();
                using (await AsyncKeyedLock.LockAsync(key))
                    executed.Add(i);
            })).ToArray();

            for (int i = 0; i < 10; i++) await barrier.WaitAsync();
            await Task.Delay(100);

            holder.Dispose();
            await Task.WhenAll(tasks);

            Assert.Equal(10, executed.Count);
            _output.WriteLine("全部 10 个等待者执行完成");
        }

        // ─── 超时 ───

        [Fact]
        public async Task Timeout_ReturnsFalse()
        {
            var key = $"timeout-{Guid.NewGuid()}";

            using (await AsyncKeyedLock.LockAsync(key))
            {
                using var h = await AsyncKeyedLock.TryLockAsync(key, TimeSpan.FromMilliseconds(100));
                Assert.False(h.IsAcquired);
                _output.WriteLine("超时正确返回 IsAcquired=false");
            }
        }

        [Fact]
        public async Task Timeout_Zero_ImmediateFail()
        {
            var key = $"zero-{Guid.NewGuid()}";

            using (await AsyncKeyedLock.LockAsync(key))
            {
                var sw = Stopwatch.StartNew();
                using var h = await AsyncKeyedLock.TryLockAsync(key, TimeSpan.Zero);
                sw.Stop();

                Assert.False(h.IsAcquired);
                Assert.True(sw.ElapsedMilliseconds < 1000,
                    $"TimeSpan.Zero 应立即返回，耗时 {sw.ElapsedMilliseconds}ms");
                _output.WriteLine($"Zero 超时耗时: {sw.ElapsedMilliseconds}ms");
            }
        }

        [Fact]
        public async Task Timeout_NoEntryLeak()
        {
            var key = $"tleak-{Guid.NewGuid()}";

            using (await AsyncKeyedLock.LockAsync(key))
            {
                using var h = await AsyncKeyedLock.TryLockAsync(key, TimeSpan.FromMilliseconds(50));
                Assert.False(h.IsAcquired);
            }

            Assert.False(AsyncKeyedLock.HasActiveReference(key));
            _output.WriteLine("超时后 entry 正确清理，无泄漏");
        }

        // ─── 取消 ───

        [Fact]
        public async Task Cancel_ThrowsOperationCanceled()
        {
            var key = $"cancel-{Guid.NewGuid()}";

            using (await AsyncKeyedLock.LockAsync(key))
            {
                var cts = new CancellationTokenSource(50);
                await Assert.ThrowsAsync<OperationCanceledException>(
                    () => AsyncKeyedLock.LockAsync(key, cts.Token));
            }

            Assert.False(AsyncKeyedLock.HasActiveReference(key));
            _output.WriteLine("取消后正确清理");
        }

        [Fact]
        public async Task Cancel_TryLock_ThrowsOperationCanceled()
        {
            var key = $"cancel-try-{Guid.NewGuid()}";

            using (await AsyncKeyedLock.LockAsync(key))
            {
                var cts = new CancellationTokenSource(50);
                await Assert.ThrowsAsync<OperationCanceledException>(
                    () => AsyncKeyedLock.TryLockAsync(key, TimeSpan.FromSeconds(30), cts.Token));
            }

            Assert.False(AsyncKeyedLock.HasActiveReference(key));
            _output.WriteLine("TryLock 取消后正确清理");
        }

        [Fact]
        public async Task Cancel_ManyWaiters_StateOk()
        {
            var key = $"cmany-{Guid.NewGuid()}";
            using var holder = await AsyncKeyedLock.LockAsync(key);

            var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
            {
                var cts = new CancellationTokenSource(30);
                try { using var h = await AsyncKeyedLock.LockAsync(key, cts.Token); }
                catch (OperationCanceledException) { }
            }));

            await Task.WhenAll(tasks);
            holder.Dispose();

            using var h2 = await AsyncKeyedLock.TryLockAsync(key, TimeSpan.FromSeconds(1));
            Assert.True(h2.IsAcquired, "取消后锁仍可用");
            _output.WriteLine("20 个取消后锁状态正常");
        }

        // ─── Dispose 幂等 ───

        [Fact]
        public async Task Dispose_Idempotent()
        {
            var key = $"idem-{Guid.NewGuid()}";

            var h = await AsyncKeyedLock.LockAsync(key);
            h.Dispose();
            h.Dispose();
            h.Dispose();

            Assert.False(AsyncKeyedLock.HasActiveReference(key));
            _output.WriteLine("多次 Dispose 安全，无异常");
        }

        [Fact]
        public async Task Dispose_Idempotent_MutualExclusion_NotBroken()
        {
            var key = $"idmx-{Guid.NewGuid()}";

            var h = await AsyncKeyedLock.LockAsync(key);
            h.Dispose();
            h.Dispose();

            using (var r1 = await AsyncKeyedLock.TryLockAsync(key, TimeSpan.FromSeconds(1)))
            {
                Assert.True(r1.IsAcquired);
                using var r2 = await AsyncKeyedLock.TryLockAsync(key, TimeSpan.FromMilliseconds(100));
                Assert.False(r2.IsAcquired, "幂等 Dispose 不应破坏互斥性");
            }

            _output.WriteLine("幂等释放未破坏互斥性");
        }

        [Fact]
        public async Task Dispose_NotAcquired_Handle_Safe()
        {
            var key = $"notacq-{Guid.NewGuid()}";

            using (await AsyncKeyedLock.LockAsync(key))
            {
                using var h = await AsyncKeyedLock.TryLockAsync(key, TimeSpan.FromMilliseconds(10));
                Assert.False(h.IsAcquired);
                h.Dispose();
                h.Dispose();
            }

            _output.WriteLine("未获取的 Handle Dispose 安全");
        }

        // ─── 自动清理 ───

        [Fact]
        public async Task Cleanup_AfterNormalUse()
        {
            var key = $"cleanup-{Guid.NewGuid()}";

            using (await AsyncKeyedLock.LockAsync(key))
                Assert.True(AsyncKeyedLock.HasActiveReference(key));

            Assert.False(AsyncKeyedLock.HasActiveReference(key));
            _output.WriteLine("正常使用后自动清理");
        }

        [Fact]
        public async Task Cleanup_ManyKeys()
        {
            var keys = Enumerable.Range(0, 100).Select(i => $"bulk-{Guid.NewGuid()}-{i}").ToArray();

            var handles = new List<AsyncKeyedLock.LockHandle>();
            foreach (var key in keys)
                handles.Add(await AsyncKeyedLock.LockAsync(key));

            _output.WriteLine($"已获取 {handles.Count} 个锁");

            foreach (var h in handles) h.Dispose();

            foreach (var key in keys)
                Assert.False(AsyncKeyedLock.HasActiveReference(key));

            _output.WriteLine("100 个 key 全部清理完成");
        }

        [Fact]
        public async Task Cleanup_Concurrent_NoLeak()
        {
            var key = $"cleak-{Guid.NewGuid()}";

            var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    using (await AsyncKeyedLock.LockAsync(key))
                        await Task.Yield();
                }
            }));

            await Task.WhenAll(tasks);
            Assert.False(AsyncKeyedLock.HasActiveReference(key));
            _output.WriteLine("50×20 并发后无泄漏");
        }

        // ─── 参数验证 ───

        [Fact]
        public async Task Validation_NullKey_Lock()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => AsyncKeyedLock.LockAsync(null!));
            _output.WriteLine("null key 正确抛出 ArgumentException");
        }

        [Fact]
        public async Task Validation_EmptyKey_Lock()
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => AsyncKeyedLock.LockAsync(""));
            _output.WriteLine("空 key 正确抛出 ArgumentException");
        }

        [Fact]
        public async Task Validation_WhitespaceKey_Lock()
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => AsyncKeyedLock.LockAsync("   "));
            _output.WriteLine("空白 key 正确抛出 ArgumentException");
        }

        [Fact]
        public async Task Validation_NullKey_TryLock()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => AsyncKeyedLock.TryLockAsync(null!, TimeSpan.FromSeconds(1)));
            _output.WriteLine("TryLock null key 正确抛出异常");
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Validation_InvalidKeys_TryLock(string invalidKey)
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => AsyncKeyedLock.TryLockAsync(invalidKey, TimeSpan.FromSeconds(1)));
            _output.WriteLine($"TryLock 无效 key '{invalidKey}' 正确抛出异常");
        }

        // ─── 异常安全 ───

        [Fact]
        public async Task Exception_InCriticalSection_ReleasesLock()
        {
            var key = $"exception-{Guid.NewGuid()}";

            try
            {
                using (await AsyncKeyedLock.LockAsync(key))
                    throw new InvalidOperationException("boom");
            }
            catch (InvalidOperationException) { }

            using var h = await AsyncKeyedLock.TryLockAsync(key, TimeSpan.FromMilliseconds(500));
            Assert.True(h.IsAcquired, "异常后锁应被释放");
            _output.WriteLine("临界区异常后锁正确释放");
        }

        // ─── 监控 API ───

        [Fact]
        public async Task Monitor_ActiveKeyCount()
        {
            var k1 = $"mon1-{Guid.NewGuid()}";
            var k2 = $"mon2-{Guid.NewGuid()}";
            int before = AsyncKeyedLock.ActiveKeyCount;

            var h1 = await AsyncKeyedLock.LockAsync(k1);
            var h2 = await AsyncKeyedLock.LockAsync(k2);

            Assert.True(AsyncKeyedLock.ActiveKeyCount >= before + 2);
            _output.WriteLine($"获取 2 个锁后 ActiveKeyCount: {AsyncKeyedLock.ActiveKeyCount}");

            h1.Dispose();
            h2.Dispose();

            _output.WriteLine($"释放后 ActiveKeyCount: {AsyncKeyedLock.ActiveKeyCount}");
        }

        [Fact]
        public async Task Monitor_HasActiveReference()
        {
            var key = $"has-ref-{Guid.NewGuid()}";

            Assert.False(AsyncKeyedLock.HasActiveReference(key));

            var h = await AsyncKeyedLock.LockAsync(key);
            Assert.True(AsyncKeyedLock.HasActiveReference(key));

            h.Dispose();
            Assert.False(AsyncKeyedLock.HasActiveReference(key));

            _output.WriteLine("HasActiveReference 状态变化正确");
        }

        // ─── 压力测试 ───

        [Fact]
        public async Task Stress_SameKey_10K_NoDeadlock()
        {
            var key = $"stress-{Guid.NewGuid()}";
            int done = 0;
            var sw = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(async () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    using (await AsyncKeyedLock.LockAsync(key))
                        Interlocked.Increment(ref done);
                }
            }));

            await Task.WhenAll(tasks);
            sw.Stop();

            Assert.Equal(10000, done);
            Assert.False(AsyncKeyedLock.HasActiveReference(key));
            _output.WriteLine($"10000 次同 key 锁操作完成，耗时: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task Stress_ManyKeys_Parallel()
        {
            int done = 0;
            var sw = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, 100).Select(t => Task.Run(async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    using (await AsyncKeyedLock.LockAsync($"mk-{Guid.NewGuid()}-{i % 10}"))
                    {
                        await Task.Yield();
                        Interlocked.Increment(ref done);
                    }
                }
            }));

            await Task.WhenAll(tasks);
            sw.Stop();

            Assert.Equal(5000, done);
            _output.WriteLine($"5000 次多 key 锁操作完成，耗时: {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task Stress_Mixed_TimeoutAndCancel()
        {
            var key = $"mixed-{Guid.NewGuid()}";
            int acquired = 0, timedOut = 0, cancelled = 0;

            var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
            {
                for (int j = 0; j < 10; j++)
                {
                    try
                    {
                        switch (j % 3)
                        {
                            case 0:
                                using (await AsyncKeyedLock.LockAsync(key))
                                {
                                    await Task.Delay(2);
                                    Interlocked.Increment(ref acquired);
                                }
                                break;
                            case 1:
                                using (var h = await AsyncKeyedLock.TryLockAsync(key, TimeSpan.FromMilliseconds(10)))
                                {
                                    if (h.IsAcquired) Interlocked.Increment(ref acquired);
                                    else Interlocked.Increment(ref timedOut);
                                }
                                break;
                            case 2:
                                var cts = new CancellationTokenSource(5);
                                try
                                {
                                    using (await AsyncKeyedLock.LockAsync(key, cts.Token))
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
            Assert.False(AsyncKeyedLock.HasActiveReference(key));

            _output.WriteLine($"混合压力测试: acquired={acquired}, timedOut={timedOut}, cancelled={cancelled}");
        }

        [Fact]
        public async Task Stress_RefCount_ManyWaiters()
        {
            var key = $"rc-{Guid.NewGuid()}";
            var holder = await AsyncKeyedLock.LockAsync(key);
            var barrier = new SemaphoreSlim(0);

            var tasks = Enumerable.Range(0, 30).Select(_ => Task.Run(async () =>
            {
                barrier.Release();
                using (await AsyncKeyedLock.LockAsync(key))
                    await Task.Delay(3);
            })).ToArray();

            for (int i = 0; i < 30; i++) await barrier.WaitAsync();
            await Task.Delay(100);

            Assert.True(AsyncKeyedLock.HasActiveReference(key));
            _output.WriteLine("30 个等待者排队中");

            holder.Dispose();
            await Task.WhenAll(tasks);

            Assert.False(AsyncKeyedLock.HasActiveReference(key));
            _output.WriteLine("全部完成，无残留");
        }

        // ─── 边界条件 ───

        [Fact]
        public async Task Edge_VeryLongKey()
        {
            var key = new string('x', 10000);
            using (var h = await AsyncKeyedLock.LockAsync(key))
                Assert.True(h.IsAcquired);
            _output.WriteLine("10000 字符 key 正常工作");
        }

        [Theory]
        [InlineData("key with spaces")]
        [InlineData("🔒")]
        [InlineData("中文key")]
        [InlineData("key/with/slashes")]
        [InlineData("key\twith\ttabs")]
        public async Task Edge_SpecialCharacterKeys(string key)
        {
            var uniqueKey = $"{key}-{Guid.NewGuid()}";
            using (var h = await AsyncKeyedLock.LockAsync(uniqueKey))
                Assert.True(h.IsAcquired);
            _output.WriteLine($"特殊字符 key '{key}' 正常工作");
        }
    }
}
using System.Collections.Concurrent;

namespace Midjourney.Infrastructure.Util
{
    /// <summary>
    /// 异步锁工具类，用于处理异步锁定和等待操作。
    /// </summary>
    public static class AsyncLockUtils
    {
        private static readonly ConcurrentDictionary<string, LockObject> LockMap = new ConcurrentDictionary<string, LockObject>();

        private static readonly TaskFactory TaskFactory = new TaskFactory(
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            TaskContinuationOptions.None,
            TaskScheduler.Default
        );

        /// <summary>
        /// 获取指定键的锁对象。
        /// </summary>
        /// <param name="key">锁的键。</param>
        /// <returns>锁对象。</returns>
        public static LockObject GetLock(string key)
        {
            LockMap.TryGetValue(key, out var lockObject);
            return lockObject;
        }

        /// <summary>
        /// 等待指定键的锁对象，直到超时。
        /// </summary>
        /// <param name="key">锁的键。</param>
        /// <param name="duration">等待时长。</param>
        /// <returns>锁对象。</returns>
        /// <exception cref="TimeoutException">等待超时异常。</exception>
        public static async Task<LockObject> WaitForLockAsync(string key, TimeSpan duration)
        {
            LockObject lockObject = LockMap.GetOrAdd(key, k => new LockObject(k));

            try
            {
                var cts = new CancellationTokenSource();
                cts.CancelAfter(duration);
                await TaskFactory.StartNew(async () => await lockObject.WaitAsync(cts.Token), cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("等待超时");
            }
            finally
            {
                LockMap.TryRemove(key, out _);
            }

            return lockObject;
        }

        /// <summary>
        /// 锁对象类。
        /// </summary>
        public class LockObject
        {
            private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(0, 1);

            /// <summary>
            /// 锁对象的键。
            /// </summary>
            public string Id { get; }

            /// <summary>
            /// 初始化锁对象。
            /// </summary>
            /// <param name="id">锁对象的键。</param>
            public LockObject(string id)
            {
                Id = id;
            }

            /// <summary>
            /// 使当前线程等待，直到收到信号。
            /// </summary>
            public async Task WaitAsync(CancellationToken cancellationToken = default)
            {
                await _semaphore.WaitAsync(cancellationToken);
            }

            /// <summary>
            /// 释放当前线程的等待状态。
            /// </summary>
            public void Release()
            {
                _semaphore.Release();
            }
        }
    }
}
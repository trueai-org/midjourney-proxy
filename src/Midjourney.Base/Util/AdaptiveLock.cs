using System.Collections.Concurrent;
using CSRedis;

namespace Midjourney.Base.Util
{
    /// <summary>
    /// 自适应锁 - v20251129
    /// 如果有开启 Redis 则使用 CSRedis 锁，否则使用本地锁（支持异步）
    /// 默认使用 await using 自动释放锁，也可以手动释放
    /// 获取锁时，一定要设置超时时间，默认 5 秒，超过未获取到锁则返回未获取状态
    /// </summary>
    public static class AdaptiveLock
    {
        #region 静态配置与状态

        /// <summary>
        /// 是否启用分布式锁
        /// </summary>
        public static bool IsDistributed { get; private set; }

        /// <summary>
        /// 本地锁的信号量池，为每个键管理一个独立的 SemaphoreSlim
        /// </summary>
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _localSemaphores = new();

        /// <summary>
        /// 项目启动时初始化
        /// </summary>
        /// <param name="redisClient">如果提供，则启用分布式锁；否则使用本地锁</param>
        public static void Initialization(CSRedisClient redisClient = null)
        {
            IsDistributed = redisClient != null;

            if (redisClient != null)
            {
                RedisHelper.Initialization(redisClient);
            }
        }

        #endregion 静态配置与状态

        #region 公共 API

        /// <summary>
        /// 获取一个锁
        /// </summary>
        /// <param name="key">锁的唯一标识</param>
        /// <param name="timeoutSeconds">获取锁的超时时间</param>
        /// <returns>一个 LockHandle 实例，请配合 await using 使用</returns>
        public static async Task<LockHandle> LockAsync(string key, int timeoutSeconds = 5)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));
            if (timeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "超时时间必须大于零。");

            if (IsDistributed)
            {
                // --- 分布式锁逻辑 ---
                try
                {
                    // CSRedis 的 LockAsync 返回一个 IDisposable，调用 Dispose 即可解锁
                    // 锁在 Redis 中的过期时间（秒）
                    var redisLock = RedisHelper.Lock(key, timeoutSeconds);

                    // 成功，返回一个持有 redisLock 对象的句柄
                    return new LockHandle(key, redisLock, isAcquired: redisLock != null);
                }
                catch
                {
                    // 获取失败（如超时），返回一个未持有的句柄
                    return new LockHandle(key, null, isAcquired: false);
                }
            }
            else
            {
                // --- 本地锁逻辑 ---
                var semaphore = _localSemaphores.GetOrAdd(key, new SemaphoreSlim(1, 1));
                try
                {
                    // 等待获取信号量
                    bool acquired = await semaphore.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));

                    // 返回一个持有 semaphore 对象的句柄
                    return new LockHandle(key, semaphore, acquired);
                }
                catch
                {
                    // WaitAsync 在取消时会抛出，但我们的场景下主要是超时返回false
                    // 为了安全，返回一个未持有的句柄
                    return new LockHandle(key, null, isAcquired: false);
                }
            }
        }

        /// <summary>
        /// 使用锁执行函数
        /// </summary>
        public static async Task<T> ExecuteWithLock<T>(string key, TimeSpan timeout, Func<T> action)
        {
            await using var lockHandle = await LockAsync(key, (int)timeout.TotalSeconds);
            return action();
        }

        /// <summary>
        /// 异步使用锁执行函数
        /// </summary>
        public static async Task<T> ExecuteWithLockAsync<T>(string key, TimeSpan timeout, Func<Task<T>> action)
        {
            await using var lockHandle = await LockAsync(key, (int)timeout.TotalSeconds);
            return await action();
        }

        /// <summary>
        /// 执行无返回值的函数
        /// </summary>
        /// <param name="key"></param>
        /// <param name="timeout"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        public static async Task ExecuteWithLock(string key, TimeSpan timeout, Action action)
        {
            await using var lockHandle = await LockAsync(key, (int)timeout.TotalSeconds);
            action();
        }

        /// <summary>
        /// 异步执行无返回值的函数
        /// </summary>
        /// <param name="key"></param>
        /// <param name="timeout"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        public static async Task ExecuteWithLockAsync(string key, TimeSpan timeout, Func<Task> action)
        {
            await using var lockHandle = await LockAsync(key, (int)timeout.TotalSeconds);
            await action();
        }

        #endregion 公共 API

        #region 锁句柄

        /// <summary>
        /// 锁句柄，代表一个已获取或未获取的锁。
        /// 它实现了 IAsyncDisposable，以支持 await using 语法。
        /// </summary>
        public sealed class LockHandle : IAsyncDisposable
        {
            private readonly string _key;

            /// <summary>
            /// 内部锁实例，可能是 SemaphoreSlim (本地锁) 或 CSRedisClientLock (分布式锁)
            /// </summary>
            private readonly object _lockInstance;

            /// <summary>
            /// 锁是否已成功获取
            /// </summary>
            public bool IsAcquired { get; }

            /// <summary>
            /// 是否已被释放
            /// </summary>
            private bool _disposed;

            /// <summary>
            /// 内部构造函数，由 AdaptiveLock.LockAsync 调用
            /// </summary>
            /// <param name="lockInstance">实际的锁对象</param>
            /// <param name="isAcquired">是否成功获取</param>
            internal LockHandle(string key, object lockInstance, bool isAcquired)
            {
                _key = key;
                _lockInstance = lockInstance;
                IsAcquired = isAcquired;
            }

            /// <summary>
            /// 释放锁
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                // 如果已经释放，或者从未获取，则直接返回
                if (_disposed || !IsAcquired || _lockInstance == null)
                {
                    return;
                }

                // 根据锁的实际类型执行释放操作
                if (_lockInstance is CSRedisClientLock redisLock)
                {
                    // 释放分布式锁
                    redisLock.Dispose();
                }
                else if (_lockInstance is SemaphoreSlim semaphore)
                {
                    // 释放本地锁
                    semaphore.Release();

                    // 判断是否还有信号量被占用
                    if (semaphore.CurrentCount == 1)
                    {
                        // 没有被占用，可以安全移除
                        _localSemaphores.TryRemove(_key, out _);
                    }
                }

                _disposed = true;

                await ValueTask.CompletedTask; // 返回一个已完成的 ValueTask
            }
        }

        #endregion 锁句柄
    }
}
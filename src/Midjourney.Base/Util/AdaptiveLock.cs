using CSRedis;
using Serilog;

namespace Midjourney.Base.Util
{
    /// <summary>
    /// 自适应锁（不允许嵌套） - v20251203
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
        /// 项目启动时初始化（可以多次调用）
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
                catch (Exception ex)
                {
                    Log.Warning(ex, "获取分布式锁时发生异常 {@0}", key);

                    // 获取失败（如超时），返回一个未持有的句柄
                    return new LockHandle(key, null, isAcquired: false);
                }
            }
            else
            {
                // --- 本地锁逻辑 ---
                bool acquired;

                try
                {
                    // 等待获取信号量
                    acquired = await AsyncLocalLock.LockEnterAsync(key, TimeSpan.FromSeconds(timeoutSeconds));

                    // 返回一个持有 semaphore 对象的句柄
                    return new LockHandle(key, null, acquired);
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
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "超时时间必须大于零。");
            var seconds = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));

            await using var lockHandle = await LockAsync(key, seconds);
            if (!lockHandle.IsAcquired)
            {
                throw new TimeoutException($"无法在 {timeout.TotalSeconds} 秒内获取锁: {key}");
            }

            return action();
        }

        /// <summary>
        /// 异步使用锁执行函数
        /// </summary>
        public static async Task<T> ExecuteWithLockAsync<T>(string key, TimeSpan timeout, Func<Task<T>> action)
        {
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "超时时间必须大于零。");
            var seconds = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));

            await using var lockHandle = await LockAsync(key, seconds);
            if (!lockHandle.IsAcquired)
            {
                throw new TimeoutException($"无法在 {timeout.TotalSeconds} 秒内获取锁: {key}");
            }
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
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "超时时间必须大于零。");
            var seconds = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));

            await using var lockHandle = await LockAsync(key, seconds);
            if (!lockHandle.IsAcquired)
            {
                throw new TimeoutException($"无法在 {timeout.TotalSeconds} 秒内获取锁: {key}");
            }
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
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "超时时间必须大于零。");
            var seconds = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));

            await using var lockHandle = await LockAsync(key, seconds);
            if (!lockHandle.IsAcquired)
            {
                throw new TimeoutException($"无法在 {timeout.TotalSeconds} 秒内获取锁: {key}");
            }
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
            /// Redis 锁实例
            /// </summary>
            private readonly CSRedisClientLock _lockInstance;

            /// <summary>
            /// 锁是否已成功获取
            /// </summary>
            public bool IsAcquired { get; }

            /// <summary>
            /// 使用 int 配合 Interlocked, 0: 未释放, 1: 已释放
            /// </summary>
            private int _disposed;

            /// <summary>
            /// 内部构造函数，由 AdaptiveLock.LockAsync 调用
            /// </summary>
            /// <param name="lockInstance">实际的锁对象</param>
            /// <param name="isAcquired">是否成功获取</param>
            internal LockHandle(string key, CSRedisClientLock lockInstance, bool isAcquired)
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
                // 原子操作：如果已经释放过，直接返回
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                // 如果从未获取锁，无需释放
                if (!IsAcquired)
                {
                    return;
                }

                // 根据锁的实际类型执行释放操作
                if (_lockInstance != null)
                {
                    // 释放分布式锁
                    _lockInstance.Dispose();
                }
                else
                {
                    // 释放本地锁
                    AsyncLocalLock.LockExit(_key);
                }

                await ValueTask.CompletedTask; // 返回一个已完成的 ValueTask
            }
        }

        #endregion 锁句柄
    }
}
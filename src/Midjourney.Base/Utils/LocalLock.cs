// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
// Copyright (C) 2024 trueai.org

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// Additional Terms:
// This software shall not be used for any illegal activities.
// Users must comply with all applicable laws and regulations,
// particularly those related to image and video processing.
// The use of this software for any form of illegal face swapping,
// invasion of privacy, or any other unlawful purposes is strictly prohibited.
// Violation of these terms may result in termination of the license and may subject the violator to legal action.

using System.Collections.Concurrent;

namespace Midjourney.Base.Util
{
    /// <summary>
    /// 本地锁 - v20251203
    /// 提示：不支持异步，绝对不能在 async 方法中使用，async 方法请使用：AsyncLocalLock
    /// 说明：Monitor.TryEnter(obj, span) 是一个同步阻塞调用。如果锁被占用，调用它的线程会停下来，原地等待，直到获取到锁或超时。
    /// Monitor 锁与线程是绑定的：哪个线程 Enter，就必须由哪个线程 Exit。
    /// 在 async/await 中，当一个 await 操作完成时，代码可能会在任意一个线程池线程上恢复执行，而不一定是原来的线程。
    /// </summary>
    public static class LocalLock
    {
        /// <summary>
        /// 锁对象的集合
        /// </summary>
        private static readonly ConcurrentDictionary<string, LockWrapper> _lockWrappers = new();

        /// <summary>
        /// 全局清理锁对象
        /// </summary>
        private static readonly object _cleanupLock = new();

        /// <summary>
        /// 锁对象的包装器，包含锁本身和一个引用计数
        /// </summary>
        internal sealed class LockWrapper
        {
            public object LockObject { get; } = new object();

            /// <summary>
            /// 引用计数：包括正在等待获取锁和已获取锁的线程总数
            /// </summary>
            public int RefCount;
        }

        /// <summary>
        /// 锁句柄，用于 using 语法自动释放锁
        /// </summary>
        public sealed class LockHandle : IDisposable
        {
            private readonly string _key;
            private readonly bool _acquired;
            private int _disposed; // 0 = 未释放, 1 = 已释放

            internal LockHandle(string key, bool acquired)
            {
                _key = key;
                _acquired = acquired;
            }

            /// <summary>
            /// 是否成功获取到锁
            /// </summary>
            public bool Acquired => _acquired;

            /// <summary>
            /// 是否已释放
            /// </summary>
            public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

            public void Dispose()
            {
                // 原子操作确保只释放一次
                if (_acquired && Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
                {
                    LockExit(_key);
                }
            }
        }

        /// <summary>
        /// 尝试获取锁
        /// </summary>
        private static bool LockEnter(string key, TimeSpan span)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            LockWrapper wrapper;

            // 在持有 _cleanupLock 的情况下完成 Increment
            // 在锁内直接递增，不需要 Interlocked
            lock (_cleanupLock)
            {
                wrapper = _lockWrappers.GetOrAdd(key, _ => new LockWrapper());
                wrapper.RefCount++;
            }

            if (Monitor.TryEnter(wrapper.LockObject, span))
            {
                return true;
            }

            // 获取失败，需要减少引用计数并可能清理
            // 获取失败，回滚引用计数
            lock (_cleanupLock)
            {
                wrapper.RefCount--;
                if (wrapper.RefCount <= 0)
                {
                    // 确保移除的是同一个 wrapper
                    // 仅当对象仍然是字典中的那个对象时才移除
                    _lockWrappers.TryRemove(KeyValuePair.Create(key, wrapper));
                }
            }

            return false;
        }

        /// <summary>
        /// 退出锁
        /// </summary>
        private static void LockExit(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            LockWrapper wrapper;

            // 先获取 wrapper 引用
            lock (_cleanupLock)
            {
                if (!_lockWrappers.TryGetValue(key, out wrapper) || wrapper == null)
                {
                    return;
                }
            }

            // 在 _cleanupLock 外释放业务锁
            try
            {
                Monitor.Exit(wrapper.LockObject);
            }
            catch (SynchronizationLockException)
            {
                // 调用者错误地多次调用 LockExit
                // 或在未获取锁的情况下调用

                // 当前线程未持有锁
                // 未持有锁，直接返回，不减少计数
                return;
            }

            // 再次获取 _cleanupLock 来处理清理
            lock (_cleanupLock)
            {
                wrapper.RefCount--;
                if (wrapper.RefCount <= 0)
                {
                    _lockWrappers.TryRemove(KeyValuePair.Create(key, wrapper));
                }
            }
        }

        /// <summary>
        /// 等待并获取锁（不支持异步 async）
        /// </summary>
        /// <param name="key"></param>
        /// <param name="timeout"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static bool TryLock(string key, TimeSpan timeout, Action action)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(action);

            if (LockEnter(key, timeout))
            {
                try
                {
                    action();
                    return true;
                }
                finally
                {
                    LockExit(key);
                }
            }
            return false;
        }

        /// <summary>
        /// 等待并获取锁（不支持异步 async）
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="timeout"></param>
        /// <param name="func"></param>
        /// <returns></returns>
        public static (bool Success, T Result) TryLock<T>(string key, TimeSpan timeout, Func<T> func)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(func);

            if (LockEnter(key, timeout))
            {
                try
                {
                    return (true, func());
                }
                finally
                {
                    LockExit(key);
                }
            }
            return (false, default);
        }

        /// <summary>
        /// 尝试获取锁，返回可用于 using 的句柄 - IDisposable 模式
        /// </summary>
        /// <param name="resource">资源标识</param>
        /// <param name="timeout">超时时间</param>
        /// <returns>锁句柄，通过 Acquired 属性判断是否成功</returns>
        /// <example>
        /// <code>
        /// using (var handle = LocalLock.TryAcquire("myKey", TimeSpan.FromSeconds(5)))
        /// {
        ///     if (handle.Acquired)
        ///     {
        ///         // 获取锁成功，执行业务逻辑
        ///     }
        ///     else
        ///     {
        ///         // 获取锁失败
        ///     }
        /// }
        /// </code>
        /// </example>
        public static LockHandle TryAcquire(string resource, TimeSpan timeout)
        {
            var acquired = LockEnter(resource, timeout);
            return new LockHandle(resource, acquired);
        }

        /// <summary>
        /// 获取锁，如果超时则抛出异常 - IDisposable 模式
        /// </summary>
        /// <param name="resource">资源标识</param>
        /// <param name="timeout">超时时间</param>
        /// <returns>锁句柄</returns>
        /// <exception cref="TimeoutException">获取锁超时</exception>
        /// <example>
        /// <code>
        /// using (LocalLock. Acquire("myKey", TimeSpan.FromSeconds(5)))
        /// {
        ///     // 获取锁成功，执行业务逻辑
        /// }
        /// </code>
        /// </example>
        public static LockHandle Acquire(string resource, TimeSpan timeout)
        {
            if (LockEnter(resource, timeout))
            {
                return new LockHandle(resource, true);
            }

            throw new TimeoutException($"Failed to acquire lock for resource '{resource}' within {timeout.TotalMilliseconds}ms.");
        }

        /// <summary>
        /// 立即尝试获取锁（不等待） - IDisposable 模式
        /// </summary>
        /// <param name="resource">资源标识</param>
        /// <returns>锁句柄</returns>
        public static LockHandle TryAcquire(string resource)
        {
            return TryAcquire(resource, TimeSpan.Zero);
        }

        /// <summary>
        /// 获取当前活跃的锁数量（用于调试/监控）
        /// </summary>
        public static int ActiveLockCount => _lockWrappers.Count;

        /// <summary>
        /// 检查指定资源是否有锁存在
        /// 注意：返回 true 表示当前有线程正在尝试或持有该资源的锁（包括等待中的线程）
        /// </summary>
        public static bool HasActiveReference(string resource)
        {
            lock (_cleanupLock)
            {
                return _lockWrappers.TryGetValue(resource, out var wrapper)
                       && wrapper.RefCount > 0;
            }
        }
    }
}
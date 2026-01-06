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
    /// 异步本地锁 - v20251203
    /// </summary>
    public static class AsyncLocalLock
    {
        private static readonly ConcurrentDictionary<string, LockWrapper> _lockWrappers = new();
        private static readonly object _cleanupLock = new();

        /// <summary>
        /// 锁包装器，包含信号量和引用计数
        /// </summary>
        private sealed class LockWrapper
        {
            public SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(1, 1);

            /// <summary>
            /// 引用计数：包括正在等待和已获取锁的总数
            /// </summary>
            public int RefCount;
        }

        /// <summary>
        /// 获取锁
        /// </summary>
        public static async Task<bool> LockEnterAsync(string key, TimeSpan span)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            LockWrapper wrapper;

            // 增加引用计数
            lock (_cleanupLock)
            {
                wrapper = _lockWrappers.GetOrAdd(key, _ => new LockWrapper());
                wrapper.RefCount++;
            }

            bool acquired = false;
            try
            {
                acquired = await wrapper.Semaphore.WaitAsync(span);
                return acquired;
            }
            finally
            {
                if (!acquired)
                {
                    // 获取失败，回滚引用计数
                    lock (_cleanupLock)
                    {
                        wrapper.RefCount--;
                        if (wrapper.RefCount <= 0)
                        {
                            if (_lockWrappers.TryRemove(KeyValuePair.Create(key, wrapper)))
                            {
                                wrapper.Semaphore.Dispose();
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 退出锁
        /// </summary>
        public static void LockExit(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            lock (_cleanupLock)
            {
                if (!_lockWrappers.TryGetValue(key, out var wrapper) || wrapper == null)
                {
                    return;
                }

                try
                {
                    wrapper.Semaphore.Release();
                }
                catch (SemaphoreFullException)
                {
                    // 重复释放，忽略
                    return;
                }

                wrapper.RefCount--;
                if (wrapper.RefCount <= 0)
                {
                    if (_lockWrappers.TryRemove(KeyValuePair.Create(key, wrapper)))
                    {
                        wrapper.Semaphore.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// 等待并获取锁
        /// </summary>
        public static async Task<bool> TryLockAsync(string resource, TimeSpan expirationTime, Func<Task> action)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            ArgumentNullException.ThrowIfNull(action);

            if (await LockEnterAsync(resource, expirationTime))
            {
                try
                {
                    await action();
                    return true;
                }
                finally
                {
                    LockExit(resource);
                }
            }
            return false;
        }

        /// <summary>
        /// 等待并获取锁（带返回值）
        /// </summary>
        public static async Task<(bool Success, T Result)> TryLockAsync<T>(string resource, TimeSpan expirationTime, Func<Task<T>> func)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            ArgumentNullException.ThrowIfNull(func);

            if (await LockEnterAsync(resource, expirationTime))
            {
                try
                {
                    return (true, await func());
                }
                finally
                {
                    LockExit(resource);
                }
            }
            return (false, default);
        }

        /// <summary>
        /// 获取当前活跃的锁数量（用于调试/监控）
        /// </summary>
        public static int ActiveLockCount => _lockWrappers.Count;

        /// <summary>
        /// 检查指定资源是否有活跃引用
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
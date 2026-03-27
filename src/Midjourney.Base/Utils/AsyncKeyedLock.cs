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

namespace Midjourney.Base.Util
{
    /// <summary>
    /// 全局异步 Keyed Lock - v20260327
    ///
    /// 特性：
    /// 1. 同 key 串行，不同 key 完全并行
    /// 2. 支持 using 自动释放
    /// 3. Dispose 幂等
    /// 4. 自动清理空闲 key
    ///
    /// 用法：
    /// using (await AsyncKeyedLock.LockAsync("myKey"))
    /// {
    ///     // 临界区
    /// }
    ///
    /// // 带超时
    /// using (var handle = await AsyncKeyedLock.TryLockAsync("myKey", TimeSpan.FromSeconds(5)))
    /// {
    ///     if (handle.IsAcquired)
    ///     {
    ///         // 临界区
    ///     }
    /// }
    /// </summary>
    public static class AsyncKeyedLock
    {
        private static readonly Dictionary<string, LockEntry> _entries = new();
        private static readonly object _lock = new();

        internal sealed class LockEntry
        {
            public readonly SemaphoreSlim Semaphore = new(1, 1);
            public int RefCount;
        }

        /// <summary>
        /// 获取锁，无限等待。支持 using 自动释放。
        /// </summary>
        public static async Task<LockHandle> LockAsync(string key, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            var entry = AddRef(key);
            try
            {
                await entry.Semaphore.WaitAsync(ct).ConfigureAwait(false);
                return new LockHandle(key, entry, acquired: true);
            }
            catch
            {
                RemoveRef(key, entry, releaseSemaphore: false);
                throw;
            }
        }

        /// <summary>
        /// 尝试获取锁，支持超时。支持 using 自动释放。
        /// 通过 handle.IsAcquired 判断是否成功。
        /// </summary>
        public static async Task<LockHandle> TryLockAsync(string key, TimeSpan timeout, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            var entry = AddRef(key);
            bool acquired = false;
            try
            {
                acquired = await entry.Semaphore.WaitAsync(timeout, ct).ConfigureAwait(false);
                if (acquired)
                {
                    return new LockHandle(key, entry, acquired: true);
                }
                else
                {
                    RemoveRef(key, entry, releaseSemaphore: false);
                    return new LockHandle(key, entry: null, acquired: false);
                }
            }
            catch
            {
                RemoveRef(key, entry, releaseSemaphore: false);
                throw;
            }
        }

        /// <summary>
        /// 当前活跃 key 数量（仅监控用）
        /// </summary>
        public static int ActiveKeyCount
        {
            get { lock (_lock) return _entries.Count; }
        }

        /// <summary>
        /// 指定 key 是否有活跃引用（仅监控用）
        /// </summary>
        public static bool HasActiveReference(string key)
        {
            lock (_lock)
                return _entries.TryGetValue(key, out var e) && e.RefCount > 0;
        }

        // ─── 内部方法 ───

        private static LockEntry AddRef(string key)
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(key, out var entry))
                {
                    entry = new LockEntry();
                    _entries[key] = entry;
                }
                entry.RefCount++;
                return entry;
            }
        }

        internal static void RemoveRef(string key, LockEntry entry, bool releaseSemaphore)
        {
            lock (_lock)
            {
                if (releaseSemaphore)
                {
                    try { entry.Semaphore.Release(); }
                    catch (SemaphoreFullException) { /* 防御性保护 */ }
                }

                entry.RefCount--;
                if (entry.RefCount <= 0)
                {
                    _entries.Remove(key);
                    entry.Semaphore.Dispose();
                }
            }
        }

        /// <summary>
        /// 锁句柄，支持 using 自动释放，Dispose 幂等。
        /// </summary>
        public sealed class LockHandle : IDisposable
        {
            private readonly string _key;
            private LockEntry _entry;
            private int _disposed;

            internal LockHandle(string key, LockEntry entry, bool acquired)
            {
                _key = key;
                _entry = entry;
                IsAcquired = acquired;
            }

            /// <summary>
            /// 是否成功获取了锁
            /// </summary>
            public bool IsAcquired { get; }

            public void Dispose()
            {
                if (!IsAcquired)
                    return;

                if (Interlocked.Exchange(ref _disposed, 1) == 1)
                    return;

                var entry = _entry;
                _entry = null;

                if (entry != null)
                    RemoveRef(_key, entry, releaseSemaphore: true);
            }
        }
    }
}
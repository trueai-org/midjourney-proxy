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
    /// 基于信号量的并发锁，支持动态调整最大并行度 - v20260328
    ///
    /// 特性：
    /// 1. 支持 using 自动释放（LockHandle）
    /// 2. Dispose 幂等
    /// 3. 支持异步等待 / 超时 / 取消
    /// 4. 兼容原有 LockAsync + Unlock 手动模式
    /// 5. 支持动态调整并行度
    /// 6. 动态调整与并发获取之间无竞态窗口
    ///
    /// 用法：
    /// // 方式 1：using 自动释放（推荐）
    /// using (var handle = await lock.AcquireAsync())
    /// {
    ///     // 临界区
    /// }
    ///
    /// // 方式 2：带超时
    /// using (var handle = await lock.TryAcquireAsync(TimeSpan.FromSeconds(5)))
    /// {
    ///     if (handle.IsAcquired) { /* 临界区 */ }
    /// }
    ///
    /// // 方式 3：兼容原有手动模式
    /// await lock.LockAsync(token);
    /// try { /* 临界区 */ }
    /// finally { lock.Unlock(); }
    ///
    /// 注意：
    /// 1. 不需要在 Unlock 外通过临时变量捕获 semaphore，因为 LockHandle 内部已经处理了防止重复释放的逻辑。
    /// 2. 不需要提前判断 cancelledToken 是否已取消，因为 SemaphoreSlim.WaitAsync 本身会正确处理取消逻辑。
    /// </summary>
    public class AsyncParallelLock : IDisposable
    {
        private readonly object _syncLock = new();
        private SemaphoreSlim _semaphore;

        private int _maxCount;       // 存储最大数量
        private int _currentlyHeld;  // 跟踪当前已获取的资源数量
        private int _waitingCount;   // 跟踪正在等待获取信号量的线程数（防止动态调整时的竞态窗口）
        private bool _disposed;      // Dispose 标志

        /// <summary>
        /// 构造并发锁，允许设置最大并发数量。
        /// </summary>
        /// <param name="maxParallelism">最大并行数量。</param>
        public AsyncParallelLock(int maxParallelism)
        {
            if (maxParallelism <= 0)
                throw new ArgumentException("并行数必须大于0", nameof(maxParallelism));

            _maxCount = maxParallelism;
            _currentlyHeld = 0;
            _waitingCount = 0;
            _semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);
        }

        /// <summary>
        /// 最大并发数
        /// </summary>
        public int MaxParallelism
        {
            get { lock (_syncLock) return _maxCount; }
        }

        /// <summary>
        /// 当前已获取的资源数量
        /// </summary>
        public int CurrentlyHeldCount
        {
            get { lock (_syncLock) return _currentlyHeld; }
        }

        /// <summary>
        /// 当前正在等待获取的线程数量
        /// </summary>
        public int WaitingCount
        {
            get { lock (_syncLock) return _waitingCount; }
        }

        /// <summary>
        /// 当前可用的资源数量
        /// </summary>
        public int AvailableCount
        {
            get { lock (_syncLock) return _semaphore?.CurrentCount ?? 0; }
        }

        #region using 模式 API（推荐）

        /// <summary>
        /// 异步获取锁，无限等待。配合 using 自动释放。
        ///
        /// using (var handle = await globalLock.AcquireAsync(token))
        /// {
        ///     // 临界区
        /// }
        /// </summary>
        public async Task<LockHandle> AcquireAsync(CancellationToken cancellationToken = default)
        {
            SemaphoreSlim semaphore;
            lock (_syncLock)
            {
                ThrowIfDisposed();
                _waitingCount++;
                semaphore = _semaphore;
            }

            try
            {
                await semaphore.WaitAsync(cancellationToken);
            }
            catch
            {
                lock (_syncLock) { _waitingCount--; }
                throw;
            }

            lock (_syncLock)
            {
                _waitingCount--;
                _currentlyHeld++;
            }

            return new LockHandle(this);
        }

        /// <summary>
        /// 尝试获取锁，支持超时。配合 using 自动释放。
        /// 通过 handle.IsAcquired 判断是否成功。
        ///
        /// using (var handle = await globalLock.TryAcquireAsync(TimeSpan.FromSeconds(5)))
        /// {
        ///     if (handle.IsAcquired) { /* 临界区 */ }
        /// }
        /// </summary>
        public async Task<LockHandle> TryAcquireAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            SemaphoreSlim semaphore;
            lock (_syncLock)
            {
                ThrowIfDisposed();
                _waitingCount++;
                semaphore = _semaphore;
            }

            bool acquired;
            try
            {
                acquired = await semaphore.WaitAsync(timeout, cancellationToken);
            }
            catch
            {
                lock (_syncLock) { _waitingCount--; }
                throw;
            }

            if (acquired)
            {
                lock (_syncLock)
                {
                    _waitingCount--;
                    _currentlyHeld++;
                }
                return new LockHandle(this);
            }

            lock (_syncLock) { _waitingCount--; }
            return LockHandle.Empty;
        }

        #endregion using 模式 API（推荐）

        #region 原有手动模式 API（兼容）

        /// <summary>
        /// 异步等待获取锁。需手动调用 Unlock() 释放。
        /// </summary>
        public async Task LockAsync(CancellationToken cancellationToken = default)
        {
            SemaphoreSlim semaphore;
            lock (_syncLock)
            {
                ThrowIfDisposed();
                _waitingCount++;
                semaphore = _semaphore;
            }

            try
            {
                await semaphore.WaitAsync(cancellationToken);
            }
            catch
            {
                lock (_syncLock) { _waitingCount--; }
                throw;
            }

            lock (_syncLock)
            {
                _waitingCount--;
                _currentlyHeld++;
            }
        }

        /// <summary>
        /// 同步等待获取锁。需手动调用 Unlock() 释放。
        /// </summary>
        public void Lock(CancellationToken cancellationToken = default)
        {
            SemaphoreSlim semaphore;
            lock (_syncLock)
            {
                ThrowIfDisposed();
                _waitingCount++;
                semaphore = _semaphore;
            }

            try
            {
                semaphore.Wait(cancellationToken);
            }
            catch
            {
                lock (_syncLock) { _waitingCount--; }
                throw;
            }

            lock (_syncLock)
            {
                _waitingCount--;
                _currentlyHeld++;
            }
        }

        /// <summary>
        /// 尝试获取锁，如果无法立即获取则返回失败。
        /// </summary>
        public bool TryLock()
        {
            SemaphoreSlim semaphore;
            lock (_syncLock)
            {
                ThrowIfDisposed();
                _waitingCount++;
                semaphore = _semaphore;
            }

            bool acquired;
            try
            {
                acquired = semaphore.Wait(0);
            }
            catch
            {
                lock (_syncLock) { _waitingCount--; }
                throw;
            }

            lock (_syncLock)
            {
                _waitingCount--;
                if (acquired)
                    _currentlyHeld++;
            }

            return acquired;
        }

        /// <summary>
        /// 释放锁。
        /// </summary>
        public void Unlock()
        {
            lock (_syncLock)
            {
                ThrowIfDisposed();

                if (_currentlyHeld <= 0)
                    throw new InvalidOperationException("尝试释放未获取的锁");

                _currentlyHeld--;
                _semaphore.Release();
            }
        }

        #endregion 原有手动模式 API（兼容）

        #region 监控 API

        /// <summary>
        /// 判断当前是否有可用锁。
        /// </summary>
        public bool IsLockAvailable()
        {
            lock (_syncLock)
            {
                return !_disposed && _semaphore?.CurrentCount > 0;
            }
        }

        /// <summary>
        /// 判断是否所有锁都可用（没有锁被持有）
        /// </summary>
        public bool AreAllLocksAvailable()
        {
            lock (_syncLock)
            {
                return !_disposed
                    && _currentlyHeld == 0
                    && _waitingCount == 0
                    && _semaphore?.CurrentCount == _maxCount;
            }
        }

        #endregion 监控 API

        #region 动态调整

        /// <summary>
        /// 设置新的最大并行度（必须所有锁可用且无等待线程时才允许修改）
        /// </summary>
        public bool SetMaxParallelism(int newMaxParallelism)
        {
            if (newMaxParallelism <= 0)
                throw new ArgumentException("并行数必须大于0", nameof(newMaxParallelism));

            lock (_syncLock)
            {
                ThrowIfDisposed();

                if (newMaxParallelism == _maxCount)
                    return true;

                if (_currentlyHeld > 0 || _waitingCount > 0 || _semaphore.CurrentCount < _maxCount)
                    return false;

                var oldSemaphore = _semaphore;
                _semaphore = new SemaphoreSlim(newMaxParallelism, newMaxParallelism);
                _maxCount = newMaxParallelism;
                oldSemaphore.Dispose();

                return true;
            }
        }

        #endregion 动态调整

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_disposed) return;
                _disposed = true;
                _semaphore?.Dispose();
                _semaphore = null;
            }
        }

        /// <summary>
        /// 检查是否已释放，若已释放则抛出异常。
        /// 调用方必须已持有 _syncLock。
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AsyncParallelLock));
        }

        /// <summary>
        /// 锁句柄，支持 using 自动释放，Dispose 幂等。
        /// </summary>
        public sealed class LockHandle : IDisposable
        {
            public static readonly LockHandle Empty = new(null);

            private AsyncParallelLock _owner;
            private int _released;

            internal LockHandle(AsyncParallelLock owner)
            {
                _owner = owner;
                IsAcquired = owner != null;
                _released = owner != null ? 0 : 1;
            }

            /// <summary>
            /// 是否成功获取了锁
            /// </summary>
            public bool IsAcquired { get; }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 1)
                    return;

                try
                {
                    _owner?.Unlock();
                }
                catch (InvalidOperationException)
                {
                    // 防御：已经被手动 Unlock 过了
                    // 防御：锁已经被 Dispose 了
                }

                _owner = null;
            }
        }
    }
}
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
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
    /// 异步本地锁
    /// </summary>
    public static class AsyncLocalLock
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _lockObjs = new();

        /// <summary>
        /// 获取锁
        /// </summary>
        /// <param name="key"></param>
        /// <param name="span"></param>
        /// <returns></returns>
        private static async Task<bool> LockEnterAsync(string key, TimeSpan span)
        {
            var semaphore = _lockObjs.GetOrAdd(key, new SemaphoreSlim(1, 1));
            return await semaphore.WaitAsync(span);
        }

        ///// <summary>
        ///// 获取锁
        ///// </summary>
        ///// <param name="key"></param>
        ///// <param name="span"></param>
        ///// <returns></returns>
        //private static bool LockEnter(string key, TimeSpan span)
        //{
        //    var semaphore = _lockObjs.GetOrAdd(key, new SemaphoreSlim(1, 1));
        //    return semaphore.Wait(span);
        //}

        /// <summary>
        /// 退出锁
        /// </summary>
        /// <param name="key"></param>
        private static void LockExit(string key)
        {
            //if (_lockObjs.TryGetValue(key, out SemaphoreSlim semaphore) && semaphore != null)
            //{
            //    semaphore.Release();
            //}

            if (_lockObjs.TryGetValue(key, out SemaphoreSlim semaphore))
            {
                _lockObjs.TryRemove(key, out _);

                semaphore?.Release();
                semaphore?.Dispose();

                //if (semaphore.CurrentCount == 1) // 表示没有其他线程在等待锁
                //{
                //    _lockObjs.TryRemove(key, out _);

                //    semaphore.Dispose(); // 释放 SemaphoreSlim 的资源
                //}
            }
        }

        /// <summary>
        /// 等待并获取锁
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="expirationTime">等待锁超时时间，如果超时没有获取到锁，返回 false</param>
        /// <param name="action"></param>
        /// <returns></returns>
        public static async Task<bool> TryLockAsync(string resource, TimeSpan expirationTime, Func<Task> action)
        {
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

        ///// <summary>
        ///// 等待并获取锁 - 不要同步和异步混用
        ///// </summary>
        ///// <param name="resource"></param>
        ///// <param name="expirationTime">等待锁超时时间，如果超时没有获取到锁，返回 false</param>
        ///// <param name="action"></param>
        ///// <returns></returns>
        //public static bool TryLock(string resource, TimeSpan expirationTime, Action action)
        //{
        //    if (LockEnter(resource, expirationTime))
        //    {
        //        try
        //        {
        //            action?.Invoke();
        //            return true;
        //        }
        //        finally
        //        {
        //            LockExit(resource);
        //        }
        //    }
        //    return false;
        //}

        /// <summary>
        /// 判断指定的锁是否可用
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static bool IsLockAvailable(string key)
        {
            if (_lockObjs.TryGetValue(key, out SemaphoreSlim semaphore) && semaphore != null)
            {
                return semaphore.CurrentCount > 0;
            }
            return true;
        }
    }
}
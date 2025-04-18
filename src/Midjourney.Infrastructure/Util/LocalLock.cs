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
    /// 本地锁（不支持异步 async）
    /// </summary>
    public static class LocalLock
    {
        private static readonly ConcurrentDictionary<string, object> _lockObjs = new();

        /// <summary>
        /// 获取锁（不支持异步 async）
        /// </summary>
        /// <param name="key"></param>
        /// <param name="span"></param>
        /// <returns></returns>
        private static bool LockEnter(string key, TimeSpan span)
        {
            var obj = _lockObjs.GetOrAdd(key, new object());
            if (Monitor.TryEnter(obj, span))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 退出锁（不支持异步 async）
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private static bool LockExit(string key)
        {
            if (_lockObjs.TryGetValue(key, out object obj) && obj != null)
            {
                Monitor.Exit(obj);
            }
            return true;
        }

        /// <summary>
        /// 等待并获取锁（不支持异步 async）
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="expirationTime">等待锁超时时间，如果超时没有获取到锁，返回 false</param>
        /// <param name="action"></param>
        /// <returns></returns>
        public static bool TryLock(string resource, TimeSpan expirationTime, Action action)
        {
            if (LockEnter(resource, expirationTime))
            {
                try
                {
                    action();
                    return true;
                }
                finally
                {
                    LockExit(resource);
                }
            }
            return false;
        }
    }
}

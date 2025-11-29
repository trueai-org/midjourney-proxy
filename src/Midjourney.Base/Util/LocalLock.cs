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
    /// 本地锁 - v20251129
    /// 提示：不支持异步，绝对不能在 async 方法中使用，async 方法请使用：AsyncLocalLock
    /// 说明：Monitor.TryEnter(obj, span) 是一个同步阻塞调用。如果锁被占用，调用它的线程会停下来，原地等待，直到获取到锁或超时。
    /// Monitor 锁与线程是绑定的：哪个线程 Enter，就必须由哪个线程 Exit。
    /// 在 async/await 中，当一个 await 操作完成时，代码可能会在任意一个线程池线程上恢复执行，而不一定是原来的线程。
    /// </summary>
    public static class LocalLock
    {
        private static readonly ConcurrentDictionary<string, LockWrapper> _lockWrappers = new();

        /// <summary>
        /// 锁对象的包装器，包含锁本身和一个引用计数
        /// </summary>
        internal sealed class LockWrapper
        {
            public object LockObject { get; } = new object();

            // 将属性改为公共字段，以支持 ref 参数
            public int RefCount;
        }

        /// <summary>
        /// 尝试获取锁
        /// </summary>
        private static bool LockEnter(string key, TimeSpan span)
        {
            var wrapper = _lockWrappers.GetOrAdd(key, new LockWrapper());

            // 现在这行代码可以正常工作了
            Interlocked.Increment(ref wrapper.RefCount);

            if (Monitor.TryEnter(wrapper.LockObject, span))
            {
                return true;
            }

            // 如果获取失败，减少引用计数
            Interlocked.Decrement(ref wrapper.RefCount);

            return false;
        }

        /// <summary>
        /// 退出锁
        /// </summary>
        private static void LockExit(string key)
        {
            if (_lockWrappers.TryGetValue(key, out var wrapper))
            {
                Monitor.Exit(wrapper.LockObject);

                Interlocked.Decrement(ref wrapper.RefCount);

                // 如果引用计数为0，说明没有其他线程正在使用这个锁，可以安全移除
                if (wrapper.RefCount <= 0)
                {
                    _lockWrappers.TryRemove(key, out _);
                }
            }
        }

        /// <summary>
        /// 等待并获取锁（不支持异步 async）
        /// </summary>
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

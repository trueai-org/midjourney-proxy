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

namespace Midjourney.Infrastructure.Util
{
    /// <summary>
    /// 定义一个基于信号量的锁管理类
    /// </summary>
    public class SemaphoreSlimLock
    {
        private SemaphoreSlim _semaphore; // 使用 SemaphoreSlim 替代 Semaphore
        private int _maxCount; // 最大允许的并发任务数
        private object _lock = new object(); // 用于保护信号量值更新的锁

        public SemaphoreSlimLock(int maxCount)
        {
            _maxCount = maxCount;
            _semaphore = new SemaphoreSlim(maxCount, maxCount);
        }

        /// <summary>
        /// 获取信号量，阻塞直到有可用资源
        /// </summary>
        public void Wait()
        {
            _semaphore.Wait();
        }

        /// <summary>
        /// 释放信号量，释放一个资源
        /// </summary>
        public void Release()
        {
            _semaphore.Release();
        }

        /// <summary>
        /// 判断是否有可用资源，即当前是否有空闲的信号量资源
        /// </summary>
        /// <param name="millisecondsTimeout"></param>
        /// <returns></returns>
        public bool TryWait(int millisecondsTimeout = 0)
        {
            var available = _semaphore.Wait(millisecondsTimeout);
            if (available)
            {
                _semaphore.Release(); // 如果成功获取，则立即释放
            }
            return available;
        }

        /// <summary>
        /// 更新最大并发任务数
        /// </summary>
        /// <param name="newMaxCount"></param>
        public void UpdateMaxCount(int newMaxCount)
        {
            lock (_lock)
            {
                int diff = newMaxCount - _maxCount; // 计算新值与当前值的差值

                if (diff > 0)
                {
                    // 如果新值大于当前值，尝试增加信号量资源
                    for (int i = 0; i < diff; i++)
                    {
                        _semaphore.Release();
                    }
                }
                else if (diff < 0)
                {
                    // 如果新值小于当前值，通过减少信号量的可用资源来调整
                    for (int i = 0; i < -diff; i++)
                    {
                        _semaphore.Wait();
                    }
                }

                _maxCount = newMaxCount; // 更新当前值
            }
        }
    }
}
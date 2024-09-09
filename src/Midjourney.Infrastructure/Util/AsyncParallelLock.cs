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
    public class AsyncParallelLock
    {
        private readonly SemaphoreSlim _semaphore;

        /// <summary>
        /// 构造并发锁，允许设置最大并发数量。
        /// </summary>
        /// <param name="maxParallelism">最大并行数量。</param>
        public AsyncParallelLock(int maxParallelism)
        {
            if (maxParallelism <= 0)
                throw new ArgumentException("并行数必须大于0", nameof(maxParallelism));

            _semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);
        }

        /// <summary>
        /// 异步等待获取锁。
        /// </summary>
        public async Task LockAsync()
        {
            await _semaphore.WaitAsync();
        }

        /// <summary>
        /// 释放锁。
        /// </summary>
        public void Unlock()
        {
            _semaphore.Release();
        }

        /// <summary>
        /// 判断当前是否有可用锁。
        /// </summary>
        /// <returns>如果有可用锁则返回 true，否则返回 false。</returns>
        public bool IsLockAvailable()
        {
            return _semaphore.CurrentCount > 0;

            //// 尝试立即获取信号量
            //if (_semaphore.Wait(millisecondsTimeout))
            //{
            //    // 如果成功获取，则立刻释放，并返回 true
            //    _semaphore.Release();
            //    return true;
            //}
            //else
            //{
            //    // 如果无法获取，返回 false
            //    return false;
            //}
        }
    }
}
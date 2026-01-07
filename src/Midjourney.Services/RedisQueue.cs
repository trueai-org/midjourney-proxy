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

using CSRedis;

namespace Midjourney.Services
{
    /// <summary>
    /// 安全的 Redis 队列实现（支持分布式锁和并发控制）
    ///
    /// var queue1 = new RedisQueue(redis, "queue1", maxSize: 10, maxConcurrency: 12)
    ///
    /// // DequeueAsync 内部已经控制了并发和阻塞，这里只需循环调用
    /// var item = await _queue.DequeueAsync(token);
    ///
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class RedisQueue<T> where T : class
    {
        private readonly CSRedisClient _redis;
        private readonly string _queueKey;
        private readonly string _lockKey;

        // 本地并发控制信号量
        private readonly SemaphoreSlim _consumerSemaphore;

        private readonly int _blockTimeoutSeconds;

        /// <summary>
        /// 初始化队列
        /// </summary>
        /// <param name="redis">CSRedis 客户端实例</param>
        /// <param name="queueName">队列名称</param>
        /// <param name="maxConcurrency">最大并发消费数</param>
        public RedisQueue(CSRedisClient redis, string queueName, int maxConcurrency = 12)
        {
            _redis = redis;
            _queueKey = $"queue:{queueName}";
            _lockKey = $"lock:queue:{queueName}"; // 锁的Key
            _blockTimeoutSeconds = 5; // BLPOP 超时时间
            _consumerSemaphore = new SemaphoreSlim(maxConcurrency);
        }

        /// <summary>
        /// 安全入队（带分布式锁）
        /// </summary>
        /// <param name="item">数据对象</param>
        /// <param name="maxSize">队列最大容量</param>
        /// <param name="timeoutSeconds">等待锁的超时时间(秒)，自动延长锁超时时间</param>
        /// <param name="ignoreFull">是否忽略队列已满，强制加入</param>
        /// <returns>是否成功入队</returns>
        public async Task<bool> EnqueueAsync(T item, int maxSize, int timeoutSeconds = 1, bool ignoreFull = false)
        {
            var json = item.ToJson();

            // 1. 尝试获取分布式锁
            // 这里的 lockSeconds=2 表示锁的有效期，防止死锁
            // 使用 using 语法糖，确保 lock 离开作用域时自动释放
            using (var lockObj = _redis.Lock(_lockKey, timeoutSeconds))
            {
                if (lockObj == null)
                {
                    // 获取锁失败，直接返回
                    return false;
                }

                // 拿到锁了，执行核心逻辑
                return await InternalEnqueue(json, maxSize, ignoreFull);
            }
        }

        // 内部私有方法：执行具体的检查和写入
        private async Task<bool> InternalEnqueue(string json, int maxSize, bool ignoreFull)
        {
            if (!ignoreFull && maxSize > 0)
            {
                // 2. 检查长度
                long count = await _redis.LLenAsync(_queueKey);

                // 3. 判断容量
                if (count >= maxSize)
                {
                    return false; // 队列已满
                }
            }

            // 4. 写入
            await _redis.RPushAsync(_queueKey, json);

            return true;
        }

        /// <summary>
        /// 阻塞式出队（高效并发控制）
        /// </summary>
        public async Task<T> DequeueAsync(CancellationToken cancellationToken = default)
        {
            // 1. 申请一个并发槽位 (异步等待，不占用线程)
            await _consumerSemaphore.WaitAsync(cancellationToken);

            try
            {
                // 2. 阻塞式拉取数据 (BLPOP)
                // 如果队列为空，Redis 会挂起连接，直到有数据或超时
                var result = _redis.BLPop(_blockTimeoutSeconds, _queueKey);

                if (!string.IsNullOrWhiteSpace(result))
                {
                    return result.ToObject<T>();
                }

                return null; // 超时未获取到数据
            }
            catch
            {
                throw;
            }
            finally
            {
                // 3. 释放并发槽位
                _consumerSemaphore.Release();
            }
        }

        /// <summary>
        /// 获取当前队列长度
        /// </summary>
        public async Task<int> CountAsync()
        {
            return (int)await _redis.LLenAsync(_queueKey);
        }

        /// <summary>
        /// 获取当前队列长度
        /// </summary>
        public int Count()
        {
            return (int)_redis.LLen(_queueKey);
        }

        /// <summary>
        /// 获取队列中所有元素
        /// </summary>
        /// <returns></returns>
        public HashSet<T> Items()
        {
            var items = _redis.LRange(_queueKey, 0, -1);
            var result = new HashSet<T>();
            foreach (var item in items)
            {
                var obj = item.ToObject<T>();
                if (obj != null && !result.Contains(obj))
                {
                    result.Add(obj);
                }
            }
            return result;
        }
    }
}
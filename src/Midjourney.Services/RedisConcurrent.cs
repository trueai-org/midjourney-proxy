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
    /// 分布式并发控制（基于 Redis 分布式锁）
    /// </summary>
    public class RedisConcurrent
    {
        private readonly CSRedisClient _redis;

        // 并发名称（例如 fast:{channelId}），槽位 key 由 BuildLockName/BuildTokenKey 统一拼装
        private readonly string _concurrentName;

        // csredis 分布式锁前缀
        // CSRedis 的 TryLock 内部会自动加上该前缀，因此直接传给 TryLock 的锁名称不能带前缀，
        // 而 EXISTS 探测槽位时必须使用带前缀的完整 key，两者因此存在差异。
        internal const string LOCK_PREFIX = "CSRedisClientLock:";

        /// <summary>
        /// 构造分布式锁名称（不含 CSRedisClientLock 前缀，供 CSRedis TryLock 使用）。
        /// </summary>
        /// <param name="concurrentName">并发名称，例如 <c>fast:{channelId}</c>。</param>
        /// <param name="index">槽位序号，从 0 开始。</param>
        /// <returns>锁名称，格式为 <c>concurrent:{concurrentName}:{index}</c>。</returns>
        internal static string BuildLockName(string concurrentName, int index) => $"concurrent:{concurrentName}:{index}";

        /// <summary>
        /// 构造并发槽位在 Redis 中的 key（含 CSRedisClientLock 前缀，不含全局 key 前缀），供 EXISTS 探测使用。
        /// </summary>
        /// <param name="concurrentName">并发名称，例如 <c>fast:{channelId}</c>。</param>
        /// <param name="index">槽位序号，从 0 开始。</param>
        /// <returns>槽位 key，格式为 <c>CSRedisClientLock:concurrent:{concurrentName}:{index}</c>。</returns>
        internal static string BuildTokenKey(string concurrentName, int index) => $"{LOCK_PREFIX}{BuildLockName(concurrentName, index)}";

        public RedisConcurrent(CSRedisClient redis, string concurrentName)
        {
            _redis = redis;
            _concurrentName = concurrentName;
        }

        /// <summary>
        /// 获取指定并发槽位的 Redis key（包含 CSRedis 锁前缀，不含全局 key 前缀）。
        /// </summary>
        /// <param name="index">槽位序号。</param>
        /// <returns>槽位 key。</returns>
        internal string GetTokenKey(int index) => BuildTokenKey(_concurrentName, index);

        /// <summary>
        /// 尝试获取执行锁
        /// </summary>
        /// <param name="maxConcurrency">最大并发数 (如 N=12)</param>
        /// <param name="jobTimeoutSeconds">锁的 TTL (Time To Live)</param>
        /// <returns>获取到的锁对象，如果没有可用令牌则返回 null</returns>
        public CSRedisClientLock TryLockWithLock(int maxConcurrency, int jobTimeoutSeconds = 10)
        {
            for (int i = 0; i < maxConcurrency; i++)
            {
                // 注意：传给 CSRedis TryLock 的锁名称不能带 CSRedisClientLock 前缀，由 TryLock 内部补全
                var tokenKey = BuildLockName(_concurrentName, i);

                // 尝试获取令牌 i 的锁
                var lockObj = _redis.TryLock(tokenKey, jobTimeoutSeconds, true);
                if (lockObj != null)
                {
                    return lockObj;
                }
            }

            // 尝试了所有槽位，都失败了
            return null; ;
        }

        /// <summary>
        /// 【获取名额】使用 1 RTT 侦查空闲槽位，然后使用 LockAsync 抢占。
        /// </summary>
        /// <param name="maxConcurrency">最大并发数 (N)</param>
        /// <param name="jobTimeoutSeconds">锁的生存时间（Watchdog 会自动续期）</param>
        /// <returns>返回 CSRedisClientLock 实例（IDisposable），失败返回 null。</returns>
        public CSRedisClientLock TryLock(int maxConcurrency, int jobTimeoutSeconds = 10)
        {
            string availableKeySuffix = null;

            // ----------------------------------------------------
            // 阶段一：快速侦查空闲槽位 (1 RTT)
            // ----------------------------------------------------
            using (var pipe = _redis.StartPipe())
            {
                // 批量生成 N 个 EXISTS 命令
                for (int i = 0; i < maxConcurrency; i++)
                {
                    // Key 格式: JobTokenPool:DataSyncJob:1
                    var tokenKey = GetTokenKey(i);
                    pipe.Exists(tokenKey);
                }

                // 执行 Pipeline (1 RTT)
                var results = pipe.EndPipe();

                // 检查结果，寻找第一个空闲槽位
                for (int i = 0; i < maxConcurrency; i++)
                {
                    if (results[i] is bool any && !any)
                    {
                        // 发现空闲槽位，记录锁名称（不含 LOCK_PREFIX，后续交给 TryLock 自动补全）
                        availableKeySuffix = BuildLockName(_concurrentName, i);

                        // 只要找到一个空闲槽位，就立即跳出，进入抢占阶段
                        break;
                    }
                }
            }

            // ----------------------------------------------------
            // 阶段二：抢占并启动 Watchdog (1 RTT)
            // ----------------------------------------------------

            if (availableKeySuffix != null)
            {
                // LockAsync 内部执行 SETNX，并启动 Watchdog 线程，是原子性抢占
                // LockAsync 内部会使用其完整前缀
                var acquiredLock = _redis.TryLock(availableKeySuffix, jobTimeoutSeconds, true);
                if (acquiredLock != null)
                {
                    // 成功抢占，Watchdog 自动启动
                    return acquiredLock;
                }
                else
                {
                    // 抢占失败：发生了竞态条件，其他客户端在我们侦查到空闲后抢走了锁。
                    // 允许失败：返回 null，由外部重试机制处理。
                    return null;
                }
            }

            // 所有槽位都已被占用
            return null;
        }

        /// <summary>
        /// 获取并发数
        /// </summary>
        /// <param name="maxConcurrency"></param>
        /// <returns></returns>
        public int GetConcurrency(int maxConcurrency)
        {
            //int count = 0;
            //for (int i = 0; i < maxConcurrency; i++)
            //{
            //    var tokenKey = $"{_concurrentKey}:{i}";
            //    using var lockObj = _redis.TryLock(tokenKey, 1, false);
            //    if (lockObj == null)
            //    {
            //        count++;
            //    }
            //}
            //return count;

            var total = 0;

            // ----------------------------------------------------
            // 阶段一：快速侦查空闲槽位 (1 RTT)
            // ----------------------------------------------------
            using (var pipe = _redis.StartPipe())
            {
                // 批量生成 N 个 EXISTS 命令
                for (int i = 0; i < maxConcurrency; i++)
                {
                    // Key 格式: JobTokenPool:DataSyncJob:1
                    var tokenKey = GetTokenKey(i);
                    pipe.Exists(tokenKey);
                }

                // 执行 Pipeline (1 RTT)
                var results = pipe.EndPipe();

                for (int i = 0; i < maxConcurrency; i++)
                {
                    if (results[i] is bool any && any)
                    {
                        total++;
                    }
                }
            }

            return total;
        }
    }
}
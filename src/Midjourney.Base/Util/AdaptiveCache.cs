using System.Collections.Concurrent;
using CSRedis;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace Midjourney.Base
{
    /// <summary>
    /// 自适应缓存 - v20251129
    /// 全局缓存助手类，用于管理全局缓存，支持Redis或内存缓存，默认为内存缓存模式
    /// </summary>
    public class AdaptiveCache
    {
        /// <summary>
        /// 本地内存缓存实例
        /// </summary>
        private static readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());

        /// <summary>
        /// 于内存模式下线程安全计数器的字典
        /// </summary>
        private static readonly ConcurrentDictionary<string, long> _counters = new();

        /// <summary>
        /// 于内存模式下计数器过期定时器的字典
        /// </summary>
        private static readonly ConcurrentDictionary<string, Timer> _counterTimers = new();

        /// <summary>
        /// 计数器锁对象
        /// </summary>
        private static readonly object _counterLock = new();

        /// <summary>
        /// 是否启用分布式锁
        /// </summary>
        public static bool IsDistributed { get; private set; }

        /// <summary>
        /// 项目启动时初始化（可以多次调用）
        /// </summary>
        /// <param name="redisClient">如果提供，则启用分布式缓存；否则使用本地缓存</param>
        public static void Initialization(CSRedisClient redisClient = null)
        {
            IsDistributed = redisClient != null;

            if (redisClient != null)
            {
                // redis
                // 127.0.0.1:6999,password=123,defaultDatabase=9,keepAlive=180
                // 127.0.0.1:6379,password=123,defaultDatabase=1,prefix=my_

                RedisHelper.Initialization(redisClient);
                Log.Information("Redis缓存模式");
            }
            else
            {
                Log.Information("内存缓存模式");
            }
        }

        /// <summary>
        /// 从缓存获取值
        /// </summary>
        public static T Get<T>(string key)
        {
            if (IsDistributed)
                return RedisHelper.Get<T>(key);

            return _memoryCache.Get<T>(key);
        }

        /// <summary>
        /// 尝试从缓存获取值
        /// </summary>
        public static bool TryGetValue<T>(string key, out T value)
        {
            if (IsDistributed)
            {
                var redisValue = RedisHelper.Get<T>(key);
                if (redisValue == null || redisValue.Equals(default(T)))
                {
                    value = default;
                    return false;
                }
                value = redisValue;
                return true;
            }

            return _memoryCache.TryGetValue(key, out value);
        }

        /// <summary>
        /// 在缓存中设置值
        /// </summary>
        public static void Set<T>(string key, T value, TimeSpan? expiry = null)
        {
            if (IsDistributed)
            {
                if (expiry.HasValue)
                {
                    RedisHelper.Set(key, value, (int)expiry.Value.TotalSeconds);
                }
                else
                {
                    RedisHelper.Set(key, value);
                }
            }
            else
            {
                var options = new MemoryCacheEntryOptions();
                if (expiry.HasValue)
                {
                    options.AbsoluteExpirationRelativeToNow = expiry.Value;
                }
                _memoryCache.Set(key, value, options);
            }
        }

        /// <summary>
        /// 获取缓存值，如果不存在则创建
        /// </summary>
        public static T GetOrCreate<T>(string key, Func<T> factory, TimeSpan? expiry = null)
        {
            if (IsDistributed)
            {
                var redisValue = RedisHelper.Get<T>(key);
                if (redisValue != null && !redisValue.Equals(default(T)))
                {
                    return redisValue;
                }
                var value = factory();
                if (expiry.HasValue)
                {
                    RedisHelper.Set(key, value, (int)expiry.Value.TotalSeconds);
                }
                else
                {
                    RedisHelper.Set(key, value);
                }
                return value;
            }
            else
            {
                if (TryGetValue<T>(key, out var value))
                {
                    return value;
                }

                value = factory();
                Set(key, value, expiry);
                return value;
            }
        }

        /// <summary>
        /// 添加或更新缓存值
        /// </summary>
        public static T AddOrUpdate<T>(string key, T addValue, Func<string, T, T> updateValueFactory, TimeSpan? expiry = null)
        {
            if (IsDistributed)
            {
                T result;
                var redisValue = RedisHelper.Get<T>(key);
                if (redisValue != null && !redisValue.Equals(default(T)))
                {
                    result = updateValueFactory(key, redisValue);
                    if (expiry.HasValue)
                    {
                        RedisHelper.Set(key, result, (int)expiry.Value.TotalSeconds);
                    }
                    else
                    {
                        RedisHelper.Set(key, result);
                    }
                }
                else
                {
                    result = addValue;
                    if (expiry.HasValue)
                    {
                        RedisHelper.Set(key, result, (int)expiry.Value.TotalSeconds);
                    }
                    else
                    {
                        RedisHelper.Set(key, result);
                    }
                }
                return result;
            }
            else
            {
                T result;
                if (TryGetValue<T>(key, out var existingValue))
                {
                    result = updateValueFactory(key, existingValue);
                    Set(key, result, expiry);
                }
                else
                {
                    result = addValue;
                    Set(key, result, expiry);
                }
                return result;
            }
        }

        /// <summary>
        /// 从缓存中移除值
        /// </summary>
        public static bool Remove(string key)
        {
            if (IsDistributed)
            {
                return RedisHelper.Del(key) > 0;
            }
            else
            {
                if (Exists(key))
                {
                    _memoryCache.Remove(key);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 检查键是否存在于缓存中
        /// </summary>
        public static bool Exists(string key)
        {
            if (IsDistributed)
            {
                return RedisHelper.Exists(key);
            }
            else
            {
                return _memoryCache.TryGetValue(key, out _);
            }
        }

        public static async Task<T> GetAsync<T>(string key)
        {
            if (IsDistributed)
            {
                return await RedisHelper.GetAsync<T>(key);
            }

            return Get<T>(key);
        }

        public static async Task<(bool, T)> TryGetValueAsync<T>(string key)
        {
            if (IsDistributed)
            {
                var value = await RedisHelper.GetAsync<T>(key);
                return (value != null && !value.Equals(default(T)), value);
            }
            else
            {
                if (TryGetValue<T>(key, out var value))
                {
                    return (true, value);
                }

                return (false, default);
            }
        }

        /// <summary>
        /// 异步获取缓存值，如果不存在则创建
        /// </summary>
        public static async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
        {
            if (IsDistributed)
            {
                var redisValue = await RedisHelper.GetAsync<T>(key);
                if (redisValue != null && !redisValue.Equals(default(T)))
                {
                    return redisValue;
                }
                var value = await factory();
                if (expiry.HasValue)
                {
                    await RedisHelper.SetAsync(key, value, (int)expiry.Value.TotalSeconds);
                }
                else
                {
                    await RedisHelper.SetAsync(key, value);
                }
                return value;
            }
            else
            {
                if (TryGetValue<T>(key, out var value))
                {
                    return value;
                }
                value = await factory();
                Set(key, value, expiry);
                return value;
            }
        }

        /// <summary>
        /// 异步添加或更新缓存值
        /// </summary>
        public static async Task<T> AddOrUpdateAsync<T>(string key, T addValue, Func<string, T, Task<T>> updateValueFactory, TimeSpan? expiry = null)
        {
            if (IsDistributed)
            {
                T result;
                var redisValue = await RedisHelper.GetAsync<T>(key);
                if (redisValue != null && !redisValue.Equals(default(T)))
                {
                    result = await updateValueFactory(key, redisValue);
                    if (expiry.HasValue)
                    {
                        await RedisHelper.SetAsync(key, result, (int)expiry.Value.TotalSeconds);
                    }
                    else
                    {
                        await RedisHelper.SetAsync(key, result);
                    }
                }
                else
                {
                    result = addValue;
                    if (expiry.HasValue)
                    {
                        await RedisHelper.SetAsync(key, result, (int)expiry.Value.TotalSeconds);
                    }
                    else
                    {
                        await RedisHelper.SetAsync(key, result);
                    }
                }
                return result;
            }
            else
            {
                T result;
                if (TryGetValue<T>(key, out var existingValue))
                {
                    result = await updateValueFactory(key, existingValue);
                    Set(key, result, expiry);
                }
                else
                {
                    result = addValue;
                    Set(key, result, expiry);
                }
                return result;
            }
        }

        public static async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            if (IsDistributed)
            {
                if (expiry.HasValue)
                {
                    await RedisHelper.SetAsync(key, value, (int)expiry.Value.TotalSeconds);
                }
                else
                {
                    await RedisHelper.SetAsync(key, value);
                }
            }
            else
            {
                Set(key, value, expiry);
            }
        }

        public static async Task<bool> RemoveAsync(string key)
        {
            if (IsDistributed)
            {
                return await RedisHelper.DelAsync(key) > 0;
            }
            else
            {
                return Remove(key);
            }
        }

        public static async Task<bool> ExistsAsync(string key)
        {
            if (IsDistributed)
            {
                return await RedisHelper.ExistsAsync(key);
            }
            else
            {
                return Exists(key);
            }
        }

        /// <summary>
        /// 线程安全的递增操作
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="value">递增值，默认为1</param>
        /// <param name="expiry">过期时间（仅在键首次创建时生效，后续递增不会刷新过期时间）</param>
        /// <returns>递增后的值</returns>
        public static long Increment(string key, long value = 1, TimeSpan? expiry = null)
        {
            if (IsDistributed)
            {
                var result = RedisHelper.IncrBy(key, value);
                if (expiry.HasValue)
                {
                    RedisHelper.Expire(key, expiry.Value);
                }
                return result;
            }
            else
            {
                var isNew = !_counters.ContainsKey(key);
                var result = _counters.AddOrUpdate(key, value, (k, oldValue) => oldValue + value);

                // 仅在首次创建时设置过期时间
                if (isNew && expiry.HasValue)
                {
                    SetCounterExpiry(key, expiry.Value);
                }
                return result;
            }
        }

        /// <summary>
        /// 线程安全的递减操作
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="value">递减值，默认为1</param>
        /// <param name="expiry">过期时间（仅在键首次创建时生效，后续递减不会刷新过期时间）</param>
        /// <returns>递减后的值</returns>
        public static long Decrement(string key, long value = 1, TimeSpan? expiry = null)
        {
            if (IsDistributed)
            {
                var result = RedisHelper.IncrBy(key, -value);
                if (expiry.HasValue)
                {
                    RedisHelper.Expire(key, expiry.Value);
                }
                return result;
            }
            else
            {
                var isNew = !_counters.ContainsKey(key);
                var result = _counters.AddOrUpdate(key, -value, (k, oldValue) => oldValue - value);

                // 仅在首次创建时设置过期时间
                if (isNew && expiry.HasValue)
                {
                    SetCounterExpiry(key, expiry.Value);
                }
                return result;
            }
        }

        /// <summary>
        /// 线程安全的异步递增操作
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="value">递增值，默认为1</param>
        /// <param name="expiry">过期时间（仅在键首次创建时生效，后续递增不会刷新过期时间）</param>
        /// <returns>递增后的值</returns>
        public static async Task<long> IncrementAsync(string key, long value = 1, TimeSpan? expiry = null)
        {
            if (IsDistributed)
            {
                var result = await RedisHelper.IncrByAsync(key, value);
                if (expiry.HasValue)
                {
                    await RedisHelper.ExpireAsync(key, expiry.Value);
                }
                return result;
            }
            else
            {
                return Increment(key, value, expiry);
            }
        }

        /// <summary>
        /// 线程安全的异步递减操作
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="value">递减值，默认为1</param>
        /// <param name="expiry">过期时间（仅在键首次创建时生效，后续递减不会刷新过期时间）</param>
        /// <returns>递减后的值</returns>
        public static async Task<long> DecrementAsync(string key, long value = 1, TimeSpan? expiry = null)
        {
            if (IsDistributed)
            {
                var result = await RedisHelper.IncrByAsync(key, -value);
                if (expiry.HasValue)
                {
                    await RedisHelper.ExpireAsync(key, expiry.Value);
                }
                return result;
            }
            else
            {
                return Decrement(key, value, expiry);
            }
        }

        /// <summary>
        /// 获取计数器当前值
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <returns>当前值，如果不存在则返回0</returns>
        public static long GetCounter(string key)
        {
            if (IsDistributed)
            {
                return RedisHelper.Get<long>(key);
            }
            else
            {
                return _counters.TryGetValue(key, out var value) ? value : 0;
            }
        }

        /// <summary>
        /// 异步获取计数器当前值
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <returns>当前值，如果不存在则返回0</returns>
        public static async Task<long> GetCounterAsync(string key)
        {
            if (IsDistributed)
            {
                return await RedisHelper.GetAsync<long>(key);
            }
            else
            {
                return GetCounter(key);
            }
        }

        /// <summary>
        /// 移除计数器
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <returns>是否成功移除</returns>
        public static bool RemoveCounter(string key)
        {
            if (IsDistributed)
            {
                return RedisHelper.Del(key) > 0;
            }
            else
            {
                ClearCounterTimer(key);
                return _counters.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// 设置计数器过期时间（内存模式专用）
        /// </summary>
        private static void SetCounterExpiry(string key, TimeSpan expiry)
        {
            lock (_counterLock)
            {
                // 清除已有的定时器
                ClearCounterTimer(key);

                // 创建新的定时器
                var timer = new Timer(_ =>
                {
                    _counters.TryRemove(key, out var t);

                    ClearCounterTimer(key);
                }, null, expiry, Timeout.InfiniteTimeSpan);

                _counterTimers[key] = timer;
            }
        }

        /// <summary>
        /// 清除计数器定时器（内存模式专用）
        /// </summary>
        private static void ClearCounterTimer(string key)
        {
            if (_counterTimers.TryRemove(key, out var timer))
            {
                timer?.Dispose();
            }
        }

        /// <summary>
        /// 设置计数器过期时间
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="expiry">过期时间</param>
        /// <returns>是否设置成功</returns>
        public static bool SetCounterExpire(string key, TimeSpan expiry)
        {
            if (IsDistributed)
            {
                return RedisHelper.Expire(key, expiry);
            }
            else
            {
                if (_counters.ContainsKey(key))
                {
                    SetCounterExpiry(key, expiry);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 异步设置计数器过期时间
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="expiry">过期时间</param>
        /// <returns>是否设置成功</returns>
        public static async Task<bool> SetCounterExpireAsync(string key, TimeSpan expiry)
        {
            if (IsDistributed)
            {
                return await RedisHelper.ExpireAsync(key, expiry);
            }
            else
            {
                return SetCounterExpire(key, expiry);
            }
        }
    }
}
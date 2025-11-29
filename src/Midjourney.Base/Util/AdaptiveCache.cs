using System.Collections.Concurrent;
using CSRedis;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace Midjourney.Base
{
    /// <summary>
    /// 自适应缓存 - v20251129
    /// 全局缓存助手类，用于管理全局缓存，支持Redis或其他缓存实现
    /// </summary>
    public class AdaptiveCache
    {
        private static ICacheProvider _cacheProvider = new MemoryCacheProvider();

        /// <summary>
        /// 项目启动时初始化
        /// </summary>
        /// <param name="redisClient">如果提供，则启用分布式缓存；否则使用本地缓存</param>
        public static void Initialization(CSRedisClient redisClient = null)
        {
            // redis
            // 127.0.0.1:6999,password=123,defaultDatabase=9,keepAlive=180
            // 127.0.0.1:6379,password=123,defaultDatabase=1,prefix=my_

            if (redisClient != null)
            {
                RedisHelper.Initialization(redisClient);

                _cacheProvider = new RedisCacheProvider();

                Log.Information("Redis缓存提供者初始化成功。");
            }
            else
            {
                _cacheProvider = new MemoryCacheProvider();

                Log.Information("内存缓存提供者初始化成功。");
            }
        }

        /// <summary>
        /// 从缓存获取值
        /// </summary>
        public static T Get<T>(string key)
        {
            return _cacheProvider.Get<T>(key);
        }

        /// <summary>
        /// 尝试从缓存获取值
        /// </summary>
        public static bool TryGetValue<T>(string key, out T value)
        {
            return _cacheProvider.TryGetValue(key, out value);
        }

        /// <summary>
        /// 在缓存中设置值
        /// </summary>
        public static void Set<T>(string key, T value, TimeSpan? expiry = null)
        {
            _cacheProvider.Set(key, value, expiry);
        }

        /// <summary>
        /// 获取缓存值，如果不存在则创建
        /// </summary>
        public static T GetOrCreate<T>(string key, Func<T> factory, TimeSpan? expiry = null)
        {
            return _cacheProvider.GetOrCreate(key, factory, expiry);
        }

        /// <summary>
        /// 添加或更新缓存值
        /// </summary>
        public static T AddOrUpdate<T>(string key, T addValue, Func<string, T, T> updateValueFactory, TimeSpan? expiry = null)
        {
            return _cacheProvider.AddOrUpdate(key, addValue, updateValueFactory, expiry);
        }

        /// <summary>
        /// 从缓存中移除值
        /// </summary>
        public static bool Remove(string key)
        {
            return _cacheProvider.Remove(key);
        }

        /// <summary>
        /// 清除所有缓存
        /// </summary>
        public static void Clear()
        {
            _cacheProvider.Clear();
        }

        /// <summary>
        /// 检查键是否存在于缓存中
        /// </summary>
        public static bool Exists(string key)
        {
            return _cacheProvider.Exists(key);
        }

        /// <summary>
        /// 异步从缓存获取值
        /// </summary>
        public static async Task<T> GetAsync<T>(string key)
        {
            return await _cacheProvider.GetAsync<T>(key);
        }

        /// <summary>
        /// 异步尝试从缓存获取值
        /// </summary>
        public static async Task<(bool success, T value)> TryGetValueAsync<T>(string key)
        {
            return await _cacheProvider.TryGetValueAsync<T>(key);
        }

        /// <summary>
        /// 异步在缓存中设置值
        /// </summary>
        public static async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            await _cacheProvider.SetAsync(key, value, expiry);
        }

        /// <summary>
        /// 异步获取缓存值，如果不存在则创建
        /// </summary>
        public static async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
        {
            return await _cacheProvider.GetOrCreateAsync(key, factory, expiry);
        }

        /// <summary>
        /// 异步添加或更新缓存值
        /// </summary>
        public static async Task<T> AddOrUpdateAsync<T>(string key, T addValue, Func<string, T, Task<T>> updateValueFactory, TimeSpan? expiry = null)
        {
            return await _cacheProvider.AddOrUpdateAsync(key, addValue, updateValueFactory, expiry);
        }

        /// <summary>
        /// 异步从缓存中移除值
        /// </summary>
        public static async Task<bool> RemoveAsync(string key)
        {
            return await _cacheProvider.RemoveAsync(key);
        }

        /// <summary>
        /// 异步检查键是否存在于缓存中
        /// </summary>
        public static async Task<bool> ExistsAsync(string key)
        {
            return await _cacheProvider.ExistsAsync(key);
        }
    }

    /// <summary>
    /// 缓存提供者接口
    /// </summary>
    public interface ICacheProvider
    {
        // 同步方法
        T Get<T>(string key);

        bool TryGetValue<T>(string key, out T value);

        void Set<T>(string key, T value, TimeSpan? expiry = null);

        T GetOrCreate<T>(string key, Func<T> factory, TimeSpan? expiry = null);

        T AddOrUpdate<T>(string key, T addValue, Func<string, T, T> updateValueFactory, TimeSpan? expiry = null);

        bool Remove(string key);

        void Clear();

        bool Exists(string key);

        // 异步方法
        Task<T> GetAsync<T>(string key);

        Task<(bool, T)> TryGetValueAsync<T>(string key);

        Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);

        Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null);

        Task<T> AddOrUpdateAsync<T>(string key, T addValue, Func<string, T, Task<T>> updateValueFactory, TimeSpan? expiry = null);

        Task<bool> RemoveAsync(string key);

        Task<bool> ExistsAsync(string key);
    }

    /// <summary>
    /// 缓存提供者基类
    /// </summary>
    public abstract class CacheProviderBase : ICacheProvider
    {
        protected readonly SemaphoreSlim LockSemaphore = new SemaphoreSlim(1, 1);
        protected readonly ConcurrentDictionary<string, SemaphoreSlim> KeyLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

        public abstract T Get<T>(string key);

        public abstract bool TryGetValue<T>(string key, out T value);

        public abstract void Set<T>(string key, T value, TimeSpan? expiry = null);

        public abstract bool Remove(string key);

        public abstract void Clear();

        public abstract bool Exists(string key);

        public virtual T GetOrCreate<T>(string key, Func<T> factory, TimeSpan? expiry = null)
        {
            if (TryGetValue<T>(key, out var value))
            {
                return value;
            }

            value = factory();
            Set(key, value, expiry);
            return value;
        }

        public virtual T AddOrUpdate<T>(string key, T addValue, Func<string, T, T> updateValueFactory, TimeSpan? expiry = null)
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

        public virtual async Task<T> GetAsync<T>(string key)
        {
            return await Task.FromResult(Get<T>(key));
        }

        public virtual async Task<(bool, T)> TryGetValueAsync<T>(string key)
        {
            var success = TryGetValue<T>(key, out var value);
            return await Task.FromResult((success, value));
        }

        public virtual async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            Set(key, value, expiry);
            await Task.CompletedTask;
        }

        public virtual async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
        {
            var (success, value) = await TryGetValueAsync<T>(key);
            if (success)
            {
                return value;
            }

            value = await factory();
            await SetAsync(key, value, expiry);
            return value;
        }

        public virtual async Task<T> AddOrUpdateAsync<T>(string key, T addValue, Func<string, T, Task<T>> updateValueFactory, TimeSpan? expiry = null)
        {
            T result;
            var (success, existingValue) = await TryGetValueAsync<T>(key);
            if (success)
            {
                result = await updateValueFactory(key, existingValue);
                await SetAsync(key, result, expiry);
            }
            else
            {
                result = addValue;
                await SetAsync(key, result, expiry);
            }
            return result;
        }

        public virtual async Task<bool> RemoveAsync(string key)
        {
            return await Task.FromResult(Remove(key));
        }

        public virtual async Task<bool> ExistsAsync(string key)
        {
            return await Task.FromResult(Exists(key));
        }
    }

    /// <summary>
    /// 内存缓存实现
    /// </summary>
    public class MemoryCacheProvider : CacheProviderBase
    {
        private readonly MemoryCache _cache;

        public MemoryCacheProvider()
        {
            _cache = new MemoryCache(new MemoryCacheOptions());
        }

        public override T Get<T>(string key)
        {
            return _cache.Get<T>(key);
        }

        public override bool TryGetValue<T>(string key, out T value)
        {
            return _cache.TryGetValue(key, out value);
        }

        public override void Set<T>(string key, T value, TimeSpan? expiry = null)
        {
            var options = new MemoryCacheEntryOptions();
            if (expiry.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = expiry.Value;
            }
            _cache.Set(key, value, options);
        }

        public override bool Remove(string key)
        {
            if (Exists(key))
            {
                _cache.Remove(key);
                return true;
            }
            return false;
        }

        public override void Clear()
        {
            _cache.Clear();
        }

        public override bool Exists(string key)
        {
            return _cache.TryGetValue(key, out _);
        }
    }

    /// <summary>
    /// Redis缓存实现
    /// </summary>
    public class RedisCacheProvider : CacheProviderBase
    {
        public override T Get<T>(string key)
        {
            var value = RedisHelper.Get<T>(key);
            return value;
        }

        public override bool TryGetValue<T>(string key, out T value)
        {
            value = default;
            var redisValue = RedisHelper.Get<T>(key);
            if (redisValue == null || redisValue.Equals(default(T)))
            {
                return false;
            }

            value = redisValue;
            return true;
        }

        public override void Set<T>(string key, T value, TimeSpan? expiry = null)
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

        public override bool Remove(string key)
        {
            return RedisHelper.Del(key) > 0;
        }

        public override void Clear()
        {
            // 清除所有键在生产环境中可能很危险
            // 通常建议使用前缀来标识应用的键，只清除带有特定前缀的键
            var keys = RedisHelper.Keys("*");
            if (keys.Length > 0)
            {
                RedisHelper.Del(keys);
            }
        }

        public override bool Exists(string key)
        {
            return RedisHelper.Exists(key);
        }

        public override async Task<T> GetAsync<T>(string key)
        {
            return await RedisHelper.GetAsync<T>(key);
        }

        public override async Task<(bool, T)> TryGetValueAsync<T>(string key)
        {
            var value = await RedisHelper.GetAsync<T>(key);
            return (value != null && !value.Equals(default(T)), value);
        }

        public override async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
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

        public override async Task<bool> RemoveAsync(string key)
        {
            return await RedisHelper.DelAsync(key) > 0;
        }

        public override async Task<bool> ExistsAsync(string key)
        {
            return await RedisHelper.ExistsAsync(key);
        }
    }
}
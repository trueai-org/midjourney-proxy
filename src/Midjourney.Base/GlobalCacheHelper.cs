using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace Midjourney.Base
{
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

        Task ClearAsync();

        Task<bool> ExistsAsync(string key);

        // 锁相关方法
        bool AcquireLock(string key, TimeSpan timeout);

        void ReleaseLock(string key);

        Task<bool> AcquireLockAsync(string key, TimeSpan timeout);

        Task ReleaseLockAsync(string key);
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

        public virtual async Task ClearAsync()
        {
            Clear();
            await Task.CompletedTask;
        }

        public virtual async Task<bool> ExistsAsync(string key)
        {
            return await Task.FromResult(Exists(key));
        }

        public virtual bool AcquireLock(string key, TimeSpan timeout)
        {
            var lockKey = $"lock:{key}";
            var semaphore = KeyLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            return semaphore.Wait(timeout);
        }

        public virtual void ReleaseLock(string key)
        {
            var lockKey = $"lock:{key}";
            if (KeyLocks.TryGetValue(lockKey, out var semaphore))
            {
                semaphore.Release();
            }
        }

        public virtual async Task<bool> AcquireLockAsync(string key, TimeSpan timeout)
        {
            var lockKey = $"lock:{key}";
            var semaphore = KeyLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            return await semaphore.WaitAsync(timeout);
        }

        public virtual async Task ReleaseLockAsync(string key)
        {
            var lockKey = $"lock:{key}";
            if (KeyLocks.TryGetValue(lockKey, out var semaphore))
            {
                semaphore.Release();
                await Task.CompletedTask;
            }
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

        public override bool AcquireLock(string key, TimeSpan timeout)
        {
            var lockKey = $"lock:{key}";
            var lockValue = Guid.NewGuid().ToString();
            return RedisHelper.Set(lockKey, lockValue, (int)timeout.TotalSeconds, CSRedis.RedisExistence.Nx);
        }

        public override void ReleaseLock(string key)
        {
            var lockKey = $"lock:{key}";
            RedisHelper.Del(lockKey);
        }

        public override async Task<bool> AcquireLockAsync(string key, TimeSpan timeout)
        {
            var lockKey = $"lock:{key}";
            var lockValue = Guid.NewGuid().ToString();
            return await RedisHelper.SetAsync(lockKey, lockValue, (int)timeout.TotalSeconds, CSRedis.RedisExistence.Nx);
        }

        public override async Task ReleaseLockAsync(string key)
        {
            var lockKey = $"lock:{key}";
            await RedisHelper.DelAsync(lockKey);
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

        public override async Task ClearAsync()
        {
            // 同Clear()方法的注意事项
            var keys = await RedisHelper.KeysAsync("*");
            if (keys.Length > 0)
            {
                await RedisHelper.DelAsync(keys);
            }
        }

        public override async Task<bool> ExistsAsync(string key)
        {
            return await RedisHelper.ExistsAsync(key);
        }
    }

    /// <summary>
    /// 全局缓存助手类，用于管理全局缓存，支持Redis或其他缓存实现
    /// </summary>
    public class GlobalCacheHelper
    {
        private static ICacheProvider _cacheProvider;
        private static readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);
        private static bool _isConfigured = false;

        /// <summary>
        /// 配置缓存提供者
        /// </summary>
        public static void Configure()
        {
            if (_isConfigured)
            {
                return;
            }

            Semaphore.Wait();
            try
            {
                if (_isConfigured)
                {
                    return;
                }

                var config = GlobalConfiguration.Setting;
                if (!string.IsNullOrWhiteSpace(config.RedisConnectionString))
                {
                    try
                    {
                        // redis
                        // 127.0.0.1:6999,password=123,defaultDatabase=9,keepAlive=180
                        // 127.0.0.1:6379,password=123,defaultDatabase=1,prefix=my_

                        // 初始化Redis
                        var csredis = new CSRedis.CSRedisClient(config.RedisConnectionString);

                        // 验证连接
                        if (!csredis.Ping())
                        {
                            throw new Exception("Redis连接失败，请检查连接字符串。");
                        }

                        RedisHelper.Initialization(csredis);
                        _cacheProvider = new RedisCacheProvider();

                        Log.Information("Redis缓存提供者初始化成功。");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "初始化Redis缓存提供者失败");
                        Log.Error("回退到内存缓存提供者。");

                        _cacheProvider = new MemoryCacheProvider();
                    }
                }
                else
                {
                    // 默认使用内存缓存
                    _cacheProvider = new MemoryCacheProvider();

                    Log.Information("内存缓存提供者初始化成功。");
                }

                _isConfigured = true;
            }
            finally
            {
                Semaphore.Release();
            }
        }

        /// <summary>
        /// 确保缓存提供者已配置
        /// </summary>
        private static void EnsureConfigured()
        {
            if (!_isConfigured)
            {
                Configure();
            }
        }

        /// <summary>
        /// 从缓存获取值
        /// </summary>
        public static T Get<T>(string key)
        {
            EnsureConfigured();
            return _cacheProvider.Get<T>(key);
        }

        /// <summary>
        /// 尝试从缓存获取值
        /// </summary>
        public static bool TryGetValue<T>(string key, out T value)
        {
            EnsureConfigured();
            return _cacheProvider.TryGetValue(key, out value);
        }

        /// <summary>
        /// 在缓存中设置值
        /// </summary>
        public static void Set<T>(string key, T value, TimeSpan? expiry = null)
        {
            EnsureConfigured();
            _cacheProvider.Set(key, value, expiry);
        }

        /// <summary>
        /// 获取缓存值，如果不存在则创建
        /// </summary>
        public static T GetOrCreate<T>(string key, Func<T> factory, TimeSpan? expiry = null)
        {
            EnsureConfigured();
            return _cacheProvider.GetOrCreate(key, factory, expiry);
        }

        /// <summary>
        /// 添加或更新缓存值
        /// </summary>
        public static T AddOrUpdate<T>(string key, T addValue, Func<string, T, T> updateValueFactory, TimeSpan? expiry = null)
        {
            EnsureConfigured();
            return _cacheProvider.AddOrUpdate(key, addValue, updateValueFactory, expiry);
        }

        /// <summary>
        /// 从缓存中移除值
        /// </summary>
        public static bool Remove(string key)
        {
            EnsureConfigured();
            return _cacheProvider.Remove(key);
        }

        /// <summary>
        /// 清除所有缓存
        /// </summary>
        public static void Clear()
        {
            EnsureConfigured();
            _cacheProvider.Clear();
        }

        /// <summary>
        /// 检查键是否存在于缓存中
        /// </summary>
        public static bool Exists(string key)
        {
            EnsureConfigured();
            return _cacheProvider.Exists(key);
        }

        /// <summary>
        /// 异步从缓存获取值
        /// </summary>
        public static async Task<T> GetAsync<T>(string key)
        {
            EnsureConfigured();
            return await _cacheProvider.GetAsync<T>(key);
        }

        /// <summary>
        /// 异步尝试从缓存获取值
        /// </summary>
        public static async Task<(bool success, T value)> TryGetValueAsync<T>(string key)
        {
            EnsureConfigured();
            return await _cacheProvider.TryGetValueAsync<T>(key);
        }

        /// <summary>
        /// 异步在缓存中设置值
        /// </summary>
        public static async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            EnsureConfigured();
            await _cacheProvider.SetAsync(key, value, expiry);
        }

        /// <summary>
        /// 异步获取缓存值，如果不存在则创建
        /// </summary>
        public static async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null)
        {
            EnsureConfigured();
            return await _cacheProvider.GetOrCreateAsync(key, factory, expiry);
        }

        /// <summary>
        /// 异步添加或更新缓存值
        /// </summary>
        public static async Task<T> AddOrUpdateAsync<T>(string key, T addValue, Func<string, T, Task<T>> updateValueFactory, TimeSpan? expiry = null)
        {
            EnsureConfigured();
            return await _cacheProvider.AddOrUpdateAsync(key, addValue, updateValueFactory, expiry);
        }

        /// <summary>
        /// 异步从缓存中移除值
        /// </summary>
        public static async Task<bool> RemoveAsync(string key)
        {
            EnsureConfigured();
            return await _cacheProvider.RemoveAsync(key);
        }

        /// <summary>
        /// 异步清除所有缓存
        /// </summary>
        public static async Task ClearAsync()
        {
            EnsureConfigured();
            await _cacheProvider.ClearAsync();
        }

        /// <summary>
        /// 异步检查键是否存在于缓存中
        /// </summary>
        public static async Task<bool> ExistsAsync(string key)
        {
            EnsureConfigured();
            return await _cacheProvider.ExistsAsync(key);
        }

        /// <summary>
        /// 获取缓存键的锁
        /// </summary>
        public static bool AcquireLock(string key, TimeSpan timeout)
        {
            EnsureConfigured();
            return _cacheProvider.AcquireLock(key, timeout);
        }

        /// <summary>
        /// 释放缓存键的锁
        /// </summary>
        public static void ReleaseLock(string key)
        {
            EnsureConfigured();
            _cacheProvider.ReleaseLock(key);
        }

        /// <summary>
        /// 异步获取缓存键的锁
        /// </summary>
        public static async Task<bool> AcquireLockAsync(string key, TimeSpan timeout)
        {
            EnsureConfigured();
            return await _cacheProvider.AcquireLockAsync(key, timeout);
        }

        /// <summary>
        /// 异步释放缓存键的锁
        /// </summary>
        public static async Task ReleaseLockAsync(string key)
        {
            EnsureConfigured();
            await _cacheProvider.ReleaseLockAsync(key);
        }

        /// <summary>
        /// 使用锁执行函数
        /// </summary>
        public static T ExecuteWithLock<T>(string key, TimeSpan timeout, Func<T> action)
        {
            EnsureConfigured();
            if (AcquireLock(key, timeout))
            {
                try
                {
                    return action();
                }
                finally
                {
                    ReleaseLock(key);
                }
            }
            return default;
        }

        /// <summary>
        /// 异步使用锁执行函数
        /// </summary>
        public static async Task<T> ExecuteWithLockAsync<T>(string key, TimeSpan timeout, Func<Task<T>> action)
        {
            EnsureConfigured();
            if (await AcquireLockAsync(key, timeout))
            {
                try
                {
                    return await action();
                }
                finally
                {
                    await ReleaseLockAsync(key);
                }
            }
            return default;
        }
    }
}
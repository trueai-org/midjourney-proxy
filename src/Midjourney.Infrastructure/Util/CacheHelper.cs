using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace Midjourney.Infrastructure.Util
{
    /// <summary>
    /// 泛型缓存助手类，用于存储和管理缓存数据。
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    public static class CacheHelper<TKey, TValue>
    {
        // 使用 MemoryCache 实例来存储缓存数据
        private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

        // 使用 ConcurrentDictionary 来管理缓存策略，确保线程安全
        private static readonly ConcurrentDictionary<TKey, CacheEntryOptions> _policies = new ConcurrentDictionary<TKey, CacheEntryOptions>();

        // 默认缓存时间为1小时
        private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromHours(1);

        /// <summary>
        /// 添加或更新缓存条目。如果键已存在，则更新其值和缓存策略。
        /// </summary>
        /// <param name="key">缓存项的键</param>
        /// <param name="value">缓存项的值</param>
        /// <param name="cacheDuration">缓存持续时间（可选）。默认为1小时。</param>
        public static void AddOrUpdate(TKey key, TValue value, TimeSpan? cacheDuration = null)
        {
            var duration = cacheDuration ?? DefaultCacheDuration;
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = duration
            };
            _cache.Set(key, value, options);
            _policies[key] = new CacheEntryOptions { Expiration = DateTimeOffset.Now.Add(duration) };
        }

        /// <summary>
        /// 尝试添加缓存项。如果键已存在，则返回 false。
        /// </summary>
        /// <param name="key">缓存项的键</param>
        /// <param name="value">缓存项的值</param>
        /// <param name="cacheDuration">缓存持续时间（可选）。默认为1小时。</param>
        /// <returns>如果添加成功返回 true，否则返回 false。</returns>
        public static bool TryAdd(TKey key, TValue value, TimeSpan? cacheDuration = null)
        {
            var duration = cacheDuration ?? DefaultCacheDuration;
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = duration
            };

            if (_policies.TryAdd(key, new CacheEntryOptions { Expiration = DateTimeOffset.Now.Add(duration) }))
            {
                _cache.Set(key, value, options);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 获取缓存中的项。如果键不存在，则返回默认值。
        /// </summary>
        /// <param name="key">缓存项的键</param>
        /// <returns>缓存项的值，如果不存在则为默认值</returns>
        public static TValue Get(TKey key)
        {
            if (_cache.TryGetValue(key, out TValue value))
            {
                return value;
            }
            return default(TValue);
        }

        /// <summary>
        /// 从缓存中移除指定键的项。
        /// </summary>
        /// <param name="key">要移除的缓存项的键</param>
        public static void Remove(TKey key)
        {
            _cache.Remove(key);
            _policies.TryRemove(key, out _);
        }

        private class CacheEntryOptions
        {
            public DateTimeOffset Expiration { get; set; }
        }
    }
}
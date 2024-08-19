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
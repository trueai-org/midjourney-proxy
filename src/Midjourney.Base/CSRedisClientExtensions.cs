using CSRedis;

namespace Midjourney.Base
{
    public static class CSRedisClientExtensions
    {
        /// <summary>
        /// 获取或添加，如果不存在则添加
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="client"></param>
        /// <param name="key"></param>
        /// <param name="acquire"></param>
        /// <param name="expiry"></param>
        /// <returns></returns>
        public static T GetOrCreate<T>(this CSRedisClient client, string key, Func<T> acquire, TimeSpan? expiry = null)
        {
            if (client.Exists(key))
                return client.Get<T>(key);

            // or create it using passed function
            var result = acquire();

            client.Set(key, result, (int)(expiry?.TotalSeconds ?? -1));

            return result;
        }

        /// <summary>
        /// 添加或更新，每次更新都会重新覆盖时间
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="addValue"></param>
        /// <param name="updateValueFactory"></param>
        /// <param name="expiry"></param>
        /// <returns></returns>
        public static T AddOrUpdate<T>(this CSRedisClient client, string key, T addValue, Func<string, T, T> updateValueFactory, TimeSpan? expiry = null)
        {
            if (client.Exists(key))
            {
                var result = updateValueFactory(key, client.Get<T>(key));

                client.Set(key, result, (int)(expiry?.TotalSeconds ?? -1));

                return result;
            }
            else
            {
                client.Set(key, addValue, (int)(expiry?.TotalSeconds ?? -1));

                return addValue;
            }
        }
    }
}

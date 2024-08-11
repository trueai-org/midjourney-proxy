using System.Collections.Concurrent;

namespace Midjourney.Infrastructure.Util
{
    /// <summary>
    /// 异步本地锁
    /// </summary>
    public static class AsyncLocalLock
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _lockObjs = new();

        /// <summary>
        /// 获取锁
        /// </summary>
        /// <param name="key"></param>
        /// <param name="span"></param>
        /// <returns></returns>
        private static async Task<bool> LockEnterAsync(string key, TimeSpan span)
        {
            var semaphore = _lockObjs.GetOrAdd(key, new SemaphoreSlim(1, 1));
            return await semaphore.WaitAsync(span);
        }

        /// <summary>
        /// 退出锁
        /// </summary>
        /// <param name="key"></param>
        private static void LockExit(string key)
        {
            if (_lockObjs.TryGetValue(key, out SemaphoreSlim semaphore) && semaphore != null)
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// 等待并获取锁
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="expirationTime">等待锁超时时间，如果超时没有获取到锁，返回 false</param>
        /// <param name="action"></param>
        /// <returns></returns>
        public static async Task<bool> TryLockAsync(string resource, TimeSpan expirationTime, Func<Task> action)
        {
            if (await LockEnterAsync(resource, expirationTime))
            {
                try
                {
                    await action();
                    return true;
                }
                finally
                {
                    LockExit(resource);
                }
            }
            return false;
        }

        /// <summary>
        /// 判断指定的锁是否可用
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static bool IsLockAvailable(string key)
        {
            if (_lockObjs.TryGetValue(key, out SemaphoreSlim semaphore) && semaphore != null)
            {
                return semaphore.CurrentCount > 0;
            }
            return true;
        }
    }
}

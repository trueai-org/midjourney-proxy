using System.Collections.Concurrent;

namespace Midjourney.Infrastructure.Util
{
    /// <summary>
    /// 本地锁
    /// </summary>
    public static class LocalLock
    {
        private static readonly ConcurrentDictionary<string, object> _lockObjs = new();

        /// <summary>
        /// 获取锁
        /// </summary>
        /// <param name="key"></param>
        /// <param name="span"></param>
        /// <returns></returns>
        private static bool LockEnter(string key, TimeSpan span)
        {
            var obj = _lockObjs.GetOrAdd(key, new object());
            if (Monitor.TryEnter(obj, span))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 退出锁
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private static bool LockExit(string key)
        {
            if (_lockObjs.TryGetValue(key, out object? obj) && obj != null)
            {
                Monitor.Exit(obj);
            }
            return true;
        }

        /// <summary>
        /// 等待并获取锁
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="expirationTime">等待锁超时时间，如果超时没有获取到锁，返回 false</param>
        /// <param name="action"></param>
        /// <returns></returns>
        public static bool TryLock(string resource, TimeSpan expirationTime, Action action)
        {
            if (LockEnter(resource, expirationTime))
            {
                try
                {
                    action();
                    return true;
                }
                finally
                {
                    LockExit(resource);
                }
            }
            return false;
        }
    }
}

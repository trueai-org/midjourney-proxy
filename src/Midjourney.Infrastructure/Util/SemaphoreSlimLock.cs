namespace Midjourney.Infrastructure.Util
{
    /// <summary>
    /// 定义一个基于信号量的锁管理类
    /// </summary>
    public class SemaphoreSlimLock
    {
        private SemaphoreSlim _semaphore; // 使用 SemaphoreSlim 替代 Semaphore
        private int _maxCount; // 最大允许的并发任务数
        private object _lock = new object(); // 用于保护信号量值更新的锁

        public SemaphoreSlimLock(int maxCount)
        {
            _maxCount = maxCount;
            _semaphore = new SemaphoreSlim(maxCount, maxCount);
        }

        /// <summary>
        /// 获取信号量，阻塞直到有可用资源
        /// </summary>
        public void Wait()
        {
            _semaphore.Wait();
        }

        /// <summary>
        /// 释放信号量，释放一个资源
        /// </summary>
        public void Release()
        {
            _semaphore.Release();
        }

        /// <summary>
        /// 判断是否有可用资源，即当前是否有空闲的信号量资源
        /// </summary>
        /// <param name="millisecondsTimeout"></param>
        /// <returns></returns>
        public bool TryWait(int millisecondsTimeout = 0)
        {
            var available = _semaphore.Wait(millisecondsTimeout);
            if (available)
            {
                _semaphore.Release(); // 如果成功获取，则立即释放
            }
            return available;
        }

        /// <summary>
        /// 更新最大并发任务数
        /// </summary>
        /// <param name="newMaxCount"></param>
        public void UpdateMaxCount(int newMaxCount)
        {
            lock (_lock)
            {
                int diff = newMaxCount - _maxCount; // 计算新值与当前值的差值

                if (diff > 0)
                {
                    // 如果新值大于当前值，尝试增加信号量资源
                    for (int i = 0; i < diff; i++)
                    {
                        _semaphore.Release();
                    }
                }
                else if (diff < 0)
                {
                    // 如果新值小于当前值，通过减少信号量的可用资源来调整
                    for (int i = 0; i < -diff; i++)
                    {
                        _semaphore.Wait();
                    }
                }

                _maxCount = newMaxCount; // 更新当前值
            }
        }
    }
}
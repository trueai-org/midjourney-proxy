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
using System.Collections.Concurrent;

namespace Midjourney.Infrastructure.Util
{
    /// <summary>
    /// 队列对象接口，用于标识队列中的元素。
    /// </summary>
    public interface IQueueItem
    {
        int Index { get; set; }
    }

    /// <summary>
    /// 高性能队列的优化实现，使用了LinkedList作为底层数据结构。
    /// 性能相同：1s 200w 次入队出队操作，两者性能相差不大。
    /// </summary>
    /// <typeparam name="T">队列中元素的类型，必须实现IQueueItem接口。</typeparam>
    public class OptimizedHighPerformanceQueue<T> where T : IQueueItem
    {
        private readonly LinkedList<T> _list;
        private readonly ReaderWriterLockSlim _lock;

        /// <summary>
        /// 初始化 OptimizedHighPerformanceQueue 类的新实例。
        /// </summary>
        public OptimizedHighPerformanceQueue()
        {
            _list = new LinkedList<T>();
            _lock = new ReaderWriterLockSlim();
        }

        /// <summary>
        /// 将元素添加到队列的末尾。
        /// </summary>
        /// <param name="item">要添加的元素。</param>
        public void Enqueue(T item)
        {
            _lock.EnterWriteLock();
            try
            {
                item.Index = _list.Count;
                _list.AddLast(item);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 尝试从队列中移除并返回位于队列开头的元素。
        /// </summary>
        /// <param name="item">移除的元素，如果队列为空则为默认值。</param>
        /// <returns>如果成功移除元素，则为 true；否则为 false。</returns>
        public bool TryDequeue(out T item)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_list.Count == 0)
                {
                    item = default(T);
                    return false;
                }

                item = _list.First.Value;
                _list.RemoveFirst();
                UpdateIndices();
                return true;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 尝试返回位于队列开头的元素但不将其移除。
        /// </summary>
        /// <param name="item">位于队列开头的元素，如果队列为空则为默认值。</param>
        /// <returns>如果成功返回元素，则为 true；否则为 false。</returns>
        public bool TryPeek(out T item)
        {
            _lock.EnterReadLock();
            try
            {
                if (_list.Count == 0)
                {
                    item = default(T);
                    return false;
                }

                item = _list.First.Value;
                return true;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// 尝试从队列中移除指定的元素。
        /// </summary>
        /// <param name="item">要移除的元素。</param>
        /// <returns>如果成功移除元素，则为 true；否则为 false。</returns>
        public bool TryRemove(T item)
        {
            _lock.EnterWriteLock();
            try
            {
                var node = _list.Find(item);
                if (node == null)
                {
                    return false;
                }

                _list.Remove(node);
                UpdateIndices();
                return true;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 尝试从队列中移除指定索引位置的元素。
        /// </summary>
        /// <param name="index">要移除的元素的索引。</param>
        /// <returns>如果成功移除元素，则为 true；否则为 false。</returns>
        public bool TryRemoveAt(int index)
        {
            _lock.EnterWriteLock();
            try
            {
                if (index < 0 || index >= _list.Count)
                {
                    return false;
                }

                var node = _list.First;
                for (int i = 0; i < index; i++)
                {
                    node = node.Next;
                }

                _list.Remove(node);
                UpdateIndices();
                return true;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 获取队列中的元素数量。
        /// </summary>
        public int Count
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _list.Count;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// 更新队列中所有元素的索引。
        /// </summary>
        private void UpdateIndices()
        {
            int index = 0;
            foreach (var item in _list)
            {
                item.Index = index++;
            }
        }
    }

    /// <summary>
    /// 高性能队列的优化实现，使用了ConcurrentQueue作为底层数据结构。
    /// 性能相同：1s 200w 次入队出队操作，两者性能相差不大。
    /// </summary>
    /// <typeparam name="T">队列中元素的类型，必须实现IQueueItem接口。</typeparam>
    public class OptimizedConcurrentQueueWrapper<T> where T : IQueueItem
    {
        private readonly ConcurrentQueue<T> _queue;
        private readonly ReaderWriterLockSlim _lock;
        private readonly List<T> _items;

        /// <summary>
        /// 初始化 OptimizedConcurrentQueueWrapper 类的新实例。
        /// </summary>
        public OptimizedConcurrentQueueWrapper()
        {
            _queue = new ConcurrentQueue<T>();
            _lock = new ReaderWriterLockSlim();
            _items = new List<T>();
        }

        /// <summary>
        /// 将元素添加到队列的末尾。
        /// </summary>
        /// <param name="item">要添加的元素。</param>
        public void Enqueue(T item)
        {
            _lock.EnterWriteLock();
            try
            {
                item.Index = _items.Count;
                _items.Add(item);
                _queue.Enqueue(item);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 尝试从队列中移除并返回位于队列开头的元素。
        /// </summary>
        /// <param name="item">移除的元素，如果队列为空则为默认值。</param>
        /// <returns>如果成功移除元素，则为 true；否则为 false。</returns>
        public bool TryDequeue(out T item)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_queue.TryDequeue(out item))
                {
                    _items.RemoveAt(0);
                    UpdateIndices();
                    return true;
                }
                item = default(T);
                return false;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 尝试返回位于队列开头的元素但不将其移除。
        /// </summary>
        /// <param name="item">位于队列开头的元素，如果队列为空则为默认值。</param>
        /// <returns>如果成功返回元素，则为 true；否则为 false。</returns>
        public bool TryPeek(out T item)
        {
            _lock.EnterReadLock();
            try
            {
                if (_queue.TryPeek(out item))
                {
                    return true;
                }
                item = default(T);
                return false;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// 尝试从队列中移除指定的元素。
        /// </summary>
        /// <param name="item">要移除的元素。</param>
        /// <returns>如果成功移除元素，则为 true；否则为 false。</returns>
        public bool TryRemove(T item)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_items.Remove(item))
                {
                    var newQueue = new ConcurrentQueue<T>(_items);
                    while (_queue.TryDequeue(out _)) { }
                    foreach (var i in newQueue)
                    {
                        _queue.Enqueue(i);
                    }
                    UpdateIndices();
                    return true;
                }
                return false;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 尝试从队列中移除指定索引位置的元素。
        /// </summary>
        /// <param name="index">要移除的元素的索引。</param>
        /// <returns>如果成功移除元素，则为 true；否则为 false。</returns>
        public bool TryRemoveAt(int index)
        {
            _lock.EnterWriteLock();
            try
            {
                if (index < 0 || index >= _items.Count)
                {
                    return false;
                }

                var item = _items[index];
                if (_items.Remove(item))
                {
                    var newQueue = new ConcurrentQueue<T>(_items);
                    while (_queue.TryDequeue(out _)) { }
                    foreach (var i in newQueue)
                    {
                        _queue.Enqueue(i);
                    }
                    UpdateIndices();
                    return true;
                }
                return false;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 获取队列中的元素数量。
        /// </summary>
        public int Count
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _items.Count;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// 更新队列中所有元素的索引。
        /// </summary>
        private void UpdateIndices()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].Index = i;
            }
        }
    }
}
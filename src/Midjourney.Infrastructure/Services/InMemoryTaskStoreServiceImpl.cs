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

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// 内存任务存储服务实现类。
    /// </summary>
    public class InMemoryTaskStoreServiceImpl : ITaskStoreService
    {
        private readonly ConcurrentDictionary<string, TaskInfo> _taskMap;

        /// <summary>
        /// 初始化内存任务存储服务。
        /// </summary>
        /// <param name="timeout">任务超时时间。</param>
        public InMemoryTaskStoreServiceImpl()
        {
            _taskMap = new ConcurrentDictionary<string, TaskInfo>();
        }

        /// <summary>
        /// 保存任务。
        /// </summary>
        /// <param name="task">任务对象。</param>
        public void Save(TaskInfo task)
        {
            _taskMap[task.Id] = task;
        }

        /// <summary>
        /// 删除任务。
        /// </summary>
        /// <param name="key">任务ID。</param>
        public void Delete(string key)
        {
            _taskMap.TryRemove(key, out _);
        }

        /// <summary>
        /// 获取任务。
        /// </summary>
        /// <param name="key">任务ID。</param>
        /// <returns>任务对象。</returns>
        public TaskInfo Get(string key)
        {
            _taskMap.TryGetValue(key, out var task);
            return task;
        }

        /// <summary>
        /// 批量获取任务
        /// </summary>
        /// <param name="ids"></param>
        /// <returns></returns>
        public List<TaskInfo> GetList(List<string> ids)
        {
            return ids.Select(Get).ToList();
        }
    }
}
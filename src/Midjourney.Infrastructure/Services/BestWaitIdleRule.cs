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

namespace Midjourney.Infrastructure.LoadBalancer
{
    /// <summary>
    /// 最少等待空闲选择规则
    /// 单例场景，不需要包装 Random 实例
    /// </summary>
    public class BestWaitIdleRule : IRule
    {
        private static readonly Random random = new Random();

        ///// <summary>
        ///// 根据最少等待空闲规则选择一个 Discord 实例
        ///// </summary>
        ///// <param name="instances">可用的 Discord 实例列表。</param>
        ///// <returns>选择的 Discord 实例。</returns>
        //public DiscordInstance Choose(List<DiscordInstance> instances)
        //{
        //    if (instances.Count == 0)
        //    {
        //        return null;
        //    }

        //    // FIX：此算法存在问题：因为可能没有执行中的任务，由于间隔较大时，那么会一直选择到同一个实例
        //    //// 优先选择空闲的实例
        //    //var idleCandidates = instances
        //    //    .Where(c => c.Account.CoreSize - c.GetRunningFutures().Count > 0)
        //    //    .GroupBy(c => c.Account.CoreSize - c.GetRunningFutures().Count)
        //    //    .OrderByDescending(g => g.Key)
        //    //    .FirstOrDefault();

        //    //if (idleCandidates != null)
        //    //{
        //    //    // 随机选择一个空闲实例
        //    //    return idleCandidates.ElementAt(random.Next(idleCandidates.Count()));
        //    //}

        //    // 如果没有空闲的实例，则选择 -> (当前队列数 + 执行中的数量) / 核心数, 最小的实例
        //    var busyCandidates = instances
        //        .GroupBy(c => (double)(c.GetRunningFutures().Count + c.GetQueueTasks().Count) / c.Account.CoreSize)
        //        .OrderBy(g => g.Key)
        //        .FirstOrDefault();

        //    if (busyCandidates != null)
        //    {
        //        // 随机选择一个实例
        //        return busyCandidates.ElementAt(random.Next(busyCandidates.Count()));
        //    }

        //    return null;
        //}

        /// <summary>
        /// 根据队列利用率选择一个Discord实例
        /// </summary>
        /// <param name="instances">可用的Discord实例列表</param>
        /// <returns>选择的Discord实例</returns>
        public DiscordInstance Choose(List<DiscordInstance> instances)
        {
            if (instances == null || instances.Count == 0)
            {
                return null;
            }

            // 计算每个实例的队列利用情况
            var instanceMetrics = instances.Select(instance =>
            {
                int queuedTasks = instance.GetQueueTasks().Count;
                int queueSize = instance.Account.QueueSize;

                // 计算队列利用率
                double queueUtilization = queueSize > 0 ? (double)queuedTasks / queueSize : 1.0;

                // 计算队列剩余空间
                int remainingQueueSpace = Math.Max(0, queueSize - queuedTasks);

                return new
                {
                    Instance = instance,
                    QueueUtilization = queueUtilization,
                    RemainingQueueSpace = remainingQueueSpace
                };
            }).ToList();

            // 按队列利用率分组，选择利用率最低的组
            var bestGroup = instanceMetrics
                .GroupBy(m => m.QueueUtilization)
                .OrderBy(g => g.Key)  // 队列利用率越低越好
                .First();

            // 如果有多个实例具有相同的最低队列利用率，随机选择一个
            int randomIndex = random.Next(bestGroup.Count());
            return bestGroup.ElementAt(randomIndex).Instance;
        }
    }

    /// <summary>
    /// 轮询选择规则。
    /// </summary>
    public class RoundRobinRule : IRule
    {
        private int _position = -1;

        /// <summary>
        /// 根据轮询规则选择一个 Discord 实例。
        /// </summary>
        /// <param name="instances">可用的 Discord 实例列表。</param>
        /// <returns>选择的 Discord 实例。</returns>
        public DiscordInstance Choose(List<DiscordInstance> instances)
        {
            if (instances.Count == 0)
            {
                return null;
            }

            int pos = Interlocked.Increment(ref _position);
            return instances[pos % instances.Count];
        }
    }

    /// <summary>
    /// 随机规则
    /// </summary>
    public class RandomRule : IRule
    {
        private static readonly Random _random = new Random();

        public DiscordInstance Choose(List<DiscordInstance> instances)
        {
            if (instances.Count == 0)
            {
                return null;
            }

            int index = _random.Next(instances.Count);
            return instances[index];
        }
    }

    /// <summary>
    /// 权重规则
    /// </summary>
    public class WeightRule : IRule
    {
        public DiscordInstance Choose(List<DiscordInstance> instances)
        {
            if (instances.Count == 0)
            {
                return null;
            }

            int totalWeight = instances.Sum(i => i.Account.Weight);
            int randomWeight = new Random().Next(totalWeight);
            int currentWeight = 0;

            foreach (var instance in instances)
            {
                currentWeight += instance.Account.Weight;
                if (randomWeight < currentWeight)
                {
                    return instance;
                }
            }

            return instances.Last();  // Fallback, should never reach here
        }
    }

}
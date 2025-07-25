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
    /// Discord 负载均衡器。
    /// </summary>
    public class DiscordLoadBalancer
    {
        private readonly IRule _rule;
        private readonly HashSet<DiscordInstance> _instances = [];

        public DiscordLoadBalancer(IRule rule)
        {
            _rule = rule;
        }

        /// <summary>
        /// 获取所有实例。
        /// </summary>
        /// <returns>所有实例列表。</returns>
        public List<DiscordInstance> GetAllInstances() => _instances.ToList();

        /// <summary>
        /// 获取存活的实例。
        /// </summary>
        /// <returns>存活的实例列表。</returns>
        public List<DiscordInstance> GetAliveInstances() =>
            _instances.Where(c => c != null && c.IsAlive == true).Where(c => c != null).ToList() ?? [];

        /// <summary>
        /// 选择一个实例。
        /// </summary>
        /// <returns>选择的实例。</returns>
        /// <param name="accountFilter"></param>
        /// <param name="isNewTask">是否过滤只接收新任务的实例</param>
        /// <param name="botType">过滤开启指定机器人的账号</param>
        /// <param name="blend">过滤支持 Blend 的账号</param>
        /// <param name="describe">过滤支持 Describe 的账号</param>
        /// <param name="isDomain">过滤垂直领域的账号</param>
        /// <param name="domainIds">过滤垂直领域 ID</param>
        /// <param name="ids">指定 ids 账号</param>
        /// <param name="shorten"></param>
        /// <param name="preferredSpeedMode">首选速度模式，优先使用此模式过滤</param>
        /// <param name="isYm">悠船/官方账号</param>
        public DiscordInstance ChooseInstance(
            AccountFilter accountFilter = null,
            bool? isNewTask = null,
            EBotType? botType = null,
            bool? blend = null,
            bool? describe = null,
            bool? isDomain = null,
            List<string> domainIds = null,
            List<string> ids = null,
            bool? shorten = null,
            GenerationSpeedMode? preferredSpeedMode = null,
            bool? isYm = null)
        {
            var list = GetAliveInstances()

                // 过滤有空闲队列的实例
                .Where(c => c.IsIdleQueue(preferredSpeedMode))

                // 允许继续绘图
                .Where(c => c.Account.IsDailyLimitContinueDrawing && c.Account.Enable == true)

                // 首选速度绘图判断
                .Where(c => c.Account.IsValidateModeContinueDrawing(preferredSpeedMode, accountFilter.Modes, out _))

                // 判断悠船或官方账号
                .WhereIf(isYm == true, c => c.Account.IsYouChuan || c.Account.IsOfficial)

                //// 允许速度模式过滤，有交集的
                //.WhereIf(accountFilter?.Modes.Count > 0, c => c.Account.AllowModes == null || c.Account.AllowModes.Count <= 0 || c.Account.AllowModes.Any(x => accountFilter.Modes.Contains(x)))

                // Discord 绘图判断
                .WhereIf(accountFilter?.Modes.Count > 0, c => c.Account.IsDiscordContinueDrawing(accountFilter.Modes.ToArray()))

                // 指定 ID 的实例
                .WhereIf(!string.IsNullOrWhiteSpace(accountFilter.InstanceId), c => c.ChannelId == accountFilter.InstanceId)

                // Midjourney Remix 过滤
                .WhereIf(accountFilter?.Remix == true, c => c.Account.MjRemixOn == accountFilter.Remix || !c.Account.RemixAutoSubmit)
                .WhereIf(accountFilter?.Remix == false, c => c.Account.MjRemixOn == accountFilter.Remix)

                // Niji Remix 过滤
                .WhereIf(accountFilter?.NijiRemix == true, c => c.Account.NijiRemixOn == accountFilter.NijiRemix || !c.Account.RemixAutoSubmit)
                .WhereIf(accountFilter?.NijiRemix == false, c => c.Account.NijiRemixOn == accountFilter.NijiRemix)

                // Remix 自动提交过滤
                .WhereIf(accountFilter?.RemixAutoConsidered.HasValue == true, c => c.Account.RemixAutoSubmit == accountFilter.RemixAutoConsidered)

                // 过滤只接收新任务的实例
                .WhereIf(isNewTask == true, c => c.Account.IsAcceptNewTask == true)

                // 过滤开启 niji mj 的账号
                .WhereIf(botType == EBotType.NIJI_JOURNEY, c => c.Account.EnableNiji == true)
                .WhereIf(botType == EBotType.MID_JOURNEY, c => c.Account.EnableMj == true)

                // 过滤开启功能的账号
                .WhereIf(blend == true, c => c.Account.IsBlend)
                .WhereIf(describe == true, c => c.Account.IsDescribe)
                .WhereIf(shorten == true, c => c.Account.IsShorten)

                // 领域过滤
                .WhereIf(isDomain == true && domainIds?.Count > 0, c => c.Account.IsVerticalDomain && c.Account.VerticalDomainIds.Any(x => domainIds.Contains(x)))
                .WhereIf(isDomain == false, c => c.Account.IsVerticalDomain != true)

                // 过滤指定账号
                .WhereIf(ids?.Count > 0, c => ids.Contains(c.Account.ChannelId))
                .ToList();

            return _rule.Choose(list);
        }

        /// <summary>
        /// 获取指定ID的实例（不判断是否存活）
        /// </summary>
        /// <param name="channelId">实例ID/渠道ID</param>
        /// <returns>实例。</returns>
        public DiscordInstance GetDiscordInstance(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return null;
            }

            return _instances.FirstOrDefault(c => c.ChannelId == channelId);
        }

        /// <summary>
        /// 获取指定ID的实例（必须是存活的）
        /// </summary>
        /// <param name="channelId">实例ID/渠道ID</param>
        /// <returns>实例。</returns>
        public DiscordInstance GetDiscordInstanceIsAlive(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return null;
            }

            return _instances.FirstOrDefault(c => c.ChannelId == channelId && c.IsAlive);
        }

        ///// <summary>
        ///// 获取排队任务的ID集合。
        ///// </summary>
        ///// <returns>排队任务的ID集合。</returns>
        //public HashSet<string> GetQueueTaskIds()
        //{
        //    var taskIds = new HashSet<string>();
        //    foreach (var instance in GetAliveInstances())
        //    {
        //        foreach (var taskId in instance.GetRunningFutures().Keys)
        //        {
        //            taskIds.Add(taskId);
        //        }
        //    }
        //    return taskIds;
        //}

        /// <summary>
        /// 获取排队任务列表。
        /// </summary>
        /// <returns>排队任务列表。</returns>
        public List<TaskInfo> GetQueueTasks()
        {
            var tasks = new List<TaskInfo>();

            var ins = GetAliveInstances();
            if (ins?.Count > 0)
            {
                foreach (var instance in ins)
                {
                    var ts = instance.GetQueueTasks();
                    if (ts?.Count > 0)
                    {
                        tasks.AddRange(ts);
                    }
                }
            }

            return tasks;
        }

        /// <summary>
        /// 获取执行中的任务列表
        /// </summary>
        /// <returns></returns>
        public List<TaskInfo> GetRunningTasks()
        {
            var tasks = new List<TaskInfo>();
            var ins = GetAliveInstances();
            if (ins?.Count > 0)
            {
                foreach (var instance in ins)
                {
                    var ts = instance.GetRunningTasks();
                    if (ts?.Count > 0)
                    {
                        tasks.AddRange(ts);
                    }
                }
            }
            return tasks;
        }

        /// <summary>
        /// 添加 Discord 实例
        /// </summary>
        /// <param name="instance"></param>
        public void AddInstance(DiscordInstance instance) => _instances.Add(instance);

        /// <summary>
        /// 移除
        /// </summary>
        /// <param name="instance"></param>
        public void RemoveInstance(DiscordInstance instance) => _instances.Remove(instance);
    }
}
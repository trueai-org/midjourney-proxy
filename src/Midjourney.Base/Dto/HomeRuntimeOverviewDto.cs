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

namespace Midjourney.Base.Dto
{
    /// <summary>
    /// 首页实时并发与队列概览。
    /// 统计范围：数据库中 <c>Enable == true</c> 的账号；每组指标均为「可用 / 已用 / 总数」：
    /// 可用数受剩余绘图次数限制（后端已按阈值计算），已用数为当前 Redis 实际占用，总数为账号配置上限之和。
    /// 图生文（describe）指标只统计开启 <c>IsDescribe</c> 的账号；其中「悠船图生文剩余次数」按悠船每日 200 次固定额度折算。
    /// 单位统一为「个」（并发槽位 / 任务数 / 次数）。
    /// </summary>
    public class HomeRuntimeOverviewDto
    {
        /// <summary>
        /// 快速并发数：所有启用账号当前已占用的快速并发槽位数之和。
        /// 口径：Redis 中 <c>CSRedisClientLock:concurrent:fast:{channelId}:{i}</c> 存在的槽位数量。
        /// 单位：个（并发槽位）。
        /// </summary>
        public int FastConcurrency { get; set; }

        /// <summary>
        /// 快速总并发数：所有启用账号的快速并发上限（<c>CoreSize</c>）之和。
        /// 单位：个（并发槽位）。
        /// </summary>
        public int FastConcurrencyTotal { get; set; }

        /// <summary>
        /// 可用快速并发数：所有启用账号的可用快速并发槽位之和。
        /// 口径：单账号快速剩余次数 &gt;= 8 次时计入 max(0, CoreSize - 已用快速并发数)，否则该账号不计入。
        /// 单位：个（并发槽位）。
        /// </summary>
        public int FastConcurrencyAvailable { get; set; }

        /// <summary>
        /// 快速队列数：所有启用账号当前快速等待队列（<c>queue:fast:{channelId}</c>）长度之和。
        /// 单位：个（任务数）。
        /// </summary>
        public int FastQueueCount { get; set; }

        /// <summary>
        /// 快速总队列数：所有启用账号的快速等待队列上限（<c>QueueSize</c>）之和。
        /// 单位：个（任务数）。
        /// </summary>
        public int FastQueueCountTotal { get; set; }

        /// <summary>
        /// 可用快速队列数：所有启用账号的可用快速队列容量之和。
        /// 口径：单账号快速剩余次数 &gt;= 8 次时计入 max(0, QueueSize - 已用快速队列数)，否则该账号不计入。
        /// 单位：个（任务数）。
        /// </summary>
        public int FastQueueAvailable { get; set; }

        /// <summary>
        /// 悠船慢速并发数：仅统计启用且为悠船（<c>IsYouChuan</c>）的账号，
        /// 当前已占用的慢速并发槽位数之和（Redis 中 <c>CSRedisClientLock:concurrent:relax:{channelId}:{i}</c> 存在的槽位数量）。
        /// 单位：个（并发槽位）。
        /// </summary>
        public int YouChuanRelaxConcurrency { get; set; }

        /// <summary>
        /// 悠船总慢速并发数：仅统计启用且为悠船的账号，慢速并发上限（<c>RelaxCoreSize</c>）之和。
        /// 单位：个（并发槽位）。
        /// </summary>
        public int YouChuanRelaxConcurrencyTotal { get; set; }

        /// <summary>
        /// 可用悠船慢速并发数：仅统计悠船账号的可用慢速并发槽位之和。
        /// 口径：单账号悠船慢速剩余次数 &gt; 0 时计入 max(0, RelaxCoreSize - 已用慢速并发数)，否则该账号不计入。
        /// 单位：个（并发槽位）。
        /// </summary>
        public int YouChuanRelaxConcurrencyAvailable { get; set; }

        /// <summary>
        /// 悠船慢速队列数：仅统计启用且为悠船的账号，当前慢速等待队列（<c>queue:relax:{channelId}</c>）长度之和。
        /// 单位：个（任务数）。
        /// </summary>
        public int YouChuanRelaxQueueCount { get; set; }

        /// <summary>
        /// 悠船总慢速队列数：仅统计启用且为悠船的账号，慢速等待队列上限（<c>RelaxQueueSize</c>）之和。
        /// 单位：个（任务数）。
        /// </summary>
        public int YouChuanRelaxQueueCountTotal { get; set; }

        /// <summary>
        /// 可用悠船慢速队列数：仅统计悠船账号的可用慢速队列容量之和。
        /// 口径：单账号悠船慢速剩余次数 &gt; 0 时计入 max(0, RelaxQueueSize - 已用慢速队列数)，否则该账号不计入。
        /// 单位：个（任务数）。
        /// </summary>
        public int YouChuanRelaxQueueAvailable { get; set; }

        /// <summary>
        /// 图生文并发数：仅统计启用且开启 <c>IsDescribe</c> 的账号，
        /// 当前已占用的图生文并发槽位数之和（Redis 中 <c>CSRedisClientLock:concurrent:describe:{channelId}:{i}</c> 存在的槽位数量）。
        /// 单位：个（并发槽位）。
        /// </summary>
        public int DescribeConcurrency { get; set; }

        /// <summary>
        /// 图生文总并发数：仅统计启用且开启 <c>IsDescribe</c> 的账号，并发上限（<c>CoreSize</c>）之和。
        /// 图生文队列与并发复用账号的 <c>CoreSize</c> / <c>QueueSize</c> 配置。
        /// 单位：个（并发槽位）。
        /// </summary>
        public int DescribeConcurrencyTotal { get; set; }

        /// <summary>
        /// 可用图生文并发数：仅统计开启 <c>IsDescribe</c> 的账号，按 (CoreSize - 已用图生文并发) 累加。
        /// 口径：悠船账号先按「今日剩余可提交次数」（200 - 今日已用 - 运行中并发 - 排队数，当日已重置按 0）截断，
        /// 可用并发不会超过该值；官方/Discord 账号不限制图生文，空闲槽位始终计入。
        /// 「可用并发 + 可用队列」不超过悠船剩余可提交次数，是否还有额度也可结合「悠船图生文剩余次数」判断。
        /// 单位：个（并发槽位）。
        /// </summary>
        public int DescribeConcurrencyAvailable { get; set; }

        /// <summary>
        /// 图生文队列数：仅统计启用且开启 <c>IsDescribe</c> 的账号，当前图生文等待队列（<c>queue:describe:{channelId}</c>）长度之和。
        /// 单位：个（任务数）。
        /// </summary>
        public int DescribeQueueCount { get; set; }

        /// <summary>
        /// 图生文总队列数：仅统计启用且开启 <c>IsDescribe</c> 的账号，图生文等待队列上限（<c>QueueSize</c>）之和。
        /// 单位：个（任务数）。
        /// </summary>
        public int DescribeQueueCountTotal { get; set; }

        /// <summary>
        /// 可用图生文队列数：仅统计开启 <c>IsDescribe</c> 的账号，按 (QueueSize - 已用图生文队列) 累加。
        /// 口径：悠船账号先扣掉可用于空闲并发的额度，剩余可提交次数才计入队列可用；
        /// 官方/Discord 账号不限制图生文，空闲队列始终计入。
        /// 单位：个（任务数）。
        /// </summary>
        public int DescribeQueueAvailable { get; set; }

        /// <summary>
        /// 悠船图生文已用次数：仅统计启用、悠船且开启 <c>IsDescribe</c> 的账号，今日已用图生文次数之和。
        /// 单位：次。
        /// </summary>
        public int YouChuanDescribeQuotaUsed { get; set; }

        /// <summary>
        /// 悠船图生文总额度：仅统计启用、悠船且开启 <c>IsDescribe</c> 的账号，每账号每日 200 次，按账号数累加。
        /// 单位：次。
        /// </summary>
        public int YouChuanDescribeQuotaTotal { get; set; }

        /// <summary>
        /// 悠船图生文剩余次数：仅统计启用、悠船且开启 <c>IsDescribe</c> 的账号，
        /// 按 max(0, 200 - 今日已用) 逐账号累加；当日已触发图生文上限重置（<c>YouChuanPicreadReset</c> 大于当天）时按 0 计。
        /// 单位：次。
        /// </summary>
        public int YouChuanDescribeQuotaRemaining { get; set; }
    }
}

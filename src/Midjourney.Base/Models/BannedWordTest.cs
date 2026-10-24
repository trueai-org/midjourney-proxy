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

namespace Midjourney.Base.Models
{
    /// <summary>
    /// 违规词测试状态
    /// </summary>
    public enum BannedWordTestStatus
    {
        /// <summary>
        /// 未开始（已入队，等待并发槽位）
        /// </summary>
        NOT_STARTED = 0,

        /// <summary>
        /// 进行中（已提交上游，等待结果）
        /// </summary>
        RUNNING = 1,

        /// <summary>
        /// 已完成，未触发上游违规拦截
        /// </summary>
        PASSED = 2,

        /// <summary>
        /// 已完成，确认触发上游违规拦截
        /// </summary>
        VIOLATION = 3,

        /// <summary>
        /// 测试失败，无法判定（无可用账号、上游异常、超时等）
        /// </summary>
        FAILED = 4
    }

    /// <summary>
    /// 违规词测试任务
    /// 一个敏感词对应一个测试任务
    /// </summary>
    public class BannedWordTestTask
    {
        /// <summary>
        /// 测试任务 ID
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 待测试的敏感词
        /// </summary>
        public string Word { get; set; }

        /// <summary>
        /// 实际提交给上游的提示词
        /// </summary>
        public string Prompt { get; set; }

        /// <summary>
        /// 测试状态
        /// </summary>
        public BannedWordTestStatus Status { get; set; } = BannedWordTestStatus.NOT_STARTED;

        /// <summary>
        /// 对应的 Midjourney 代理任务 ID（提交成功后写入）
        /// </summary>
        public string MjTaskId { get; set; }

        /// <summary>
        /// 上游返回的失败原因/测试结论
        /// </summary>
        public string FailReason { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 提交到上游的时间
        /// </summary>
        public DateTime? SubmitTime { get; set; }

        /// <summary>
        /// 测试完成时间
        /// </summary>
        public DateTime? FinishTime { get; set; }

        /// <summary>
        /// 创建时间（界面展示格式）
        /// </summary>
        public string CreateTimeFormat => CreateTime.ToString("yyyy-MM-dd HH:mm:ss");

        /// <summary>
        /// 完成时间（界面展示格式，未完成时为空）
        /// </summary>
        public string FinishTimeFormat => FinishTime?.ToString("yyyy-MM-dd HH:mm:ss");

        /// <summary>
        /// 执行时间（提交到上游的时间，界面展示格式，未提交时为空）
        /// </summary>
        public string SubmitTimeFormat => SubmitTime?.ToString("yyyy-MM-dd HH:mm:ss");
    }

    /// <summary>
    /// 确认违规记录
    /// 以敏感词为字段追加记录，同一敏感词再次命中时累加次数并刷新最近时间
    /// </summary>
    public class BannedWordTestConfirmed
    {
        /// <summary>
        /// 敏感词（小写归一化后的值，作为 Redis hash field）
        /// </summary>
        public string Word { get; set; }

        /// <summary>
        /// 最近一次确认违规的测试任务 ID
        /// </summary>
        public string TestTaskId { get; set; }

        /// <summary>
        /// 最近一次确认违规的代理任务 ID
        /// </summary>
        public string MjTaskId { get; set; }

        /// <summary>
        /// 最近一次触发违规的完整提示词
        /// </summary>
        public string Prompt { get; set; }

        /// <summary>
        /// 最近一次上游失败原因
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// 累计确认次数
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// 首次确认时间
        /// </summary>
        public DateTime FirstConfirmedTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 最近确认时间
        /// </summary>
        public DateTime LastConfirmedTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 最近确认时间（界面展示格式）
        /// </summary>
        public string LastConfirmedTimeFormat => LastConfirmedTime.ToString("yyyy-MM-dd HH:mm:ss");
    }

    /// <summary>
    /// 违规词测试创建请求
    /// </summary>
    public class BannedWordTestCreateRequest
    {
        /// <summary>
        /// 待测试的敏感词列表（多个以逗号分隔，服务端会去空、去重、转小写）
        /// </summary>
        public List<string> Words { get; set; } = [];

        /// <summary>
        /// 提示词模板，可为空；不为空时支持 {word} 占位符，未包含占位符时会把敏感词拼接到模板前面
        /// </summary>
        public string Template { get; set; }

        /// <summary>
        /// 并发数，默认 3，范围 1-20
        /// </summary>
        public int Concurrency { get; set; } = 3;
    }

    /// <summary>
    /// 违规词测试查询请求
    /// </summary>
    public class BannedWordTestQueryRequest
    {
        /// <summary>
        /// 页码，从 1 开始
        /// </summary>
        public int Current { get; set; } = 1;

        /// <summary>
        /// 每页条数，默认 10，最大 100
        /// </summary>
        public int PageSize { get; set; } = 10;

        /// <summary>
        /// 按敏感词模糊筛选，可为空
        /// </summary>
        public string Keyword { get; set; }

        /// <summary>
        /// 按状态筛选，可为空表示全部
        /// </summary>
        public BannedWordTestStatus? Status { get; set; }
    }

    /// <summary>
    /// 违规词测试列表结果
    /// </summary>
    public class BannedWordTestListResult
    {
        /// <summary>
        /// 当前页测试任务
        /// </summary>
        public List<BannedWordTestTask> List { get; set; } = [];

        /// <summary>
        /// 当前筛选条件下的总条数
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// 未开始数量（全量统计，不受筛选条件影响）
        /// </summary>
        public int NotStartedCount { get; set; }

        /// <summary>
        /// 进行中数量（全量统计，不受筛选条件影响）
        /// </summary>
        public int RunningCount { get; set; }

        /// <summary>
        /// 未触发违规数量（全量统计，不受筛选条件影响）
        /// </summary>
        public int PassedCount { get; set; }

        /// <summary>
        /// 确认违规数量（全量统计，不受筛选条件影响）
        /// </summary>
        public int ViolationCount { get; set; }

        /// <summary>
        /// 测试失败数量（全量统计，不受筛选条件影响）
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// 当前全局并发数
        /// </summary>
        public int GlobalConcurrency { get; set; } = 3;

        /// <summary>
        /// 已持久化的提示词模板，未设置时为空
        /// </summary>
        public string Template { get; set; }

        /// <summary>
        /// 确认违规词记录（按最近确认时间倒序，最多返回 200 条）
        /// </summary>
        public List<BannedWordTestConfirmed> Confirmed { get; set; } = [];
    }

    /// <summary>
    /// 违规词测试创建结果
    /// </summary>
    public class BannedWordTestCreateResult
    {
        /// <summary>
        /// 本次新增的测试任务数量
        /// </summary>
        public int AddedCount { get; set; }
    }
}

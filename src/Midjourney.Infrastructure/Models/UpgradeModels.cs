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

namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// 版本信息
    /// </summary>
    public class VersionInfo
    {
        /// <summary>
        /// 版本号
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// 发布时间
        /// </summary>
        public DateTime ReleaseDate { get; set; }

        /// <summary>
        /// 版本描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 下载URL
        /// </summary>
        public string DownloadUrl { get; set; } = string.Empty;

        /// <summary>
        /// 是否为最新版本
        /// </summary>
        public bool IsLatest { get; set; }
    }

    /// <summary>
    /// 升级结果
    /// </summary>
    public class UpgradeResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 升级任务ID
        /// </summary>
        public string TaskId { get; set; } = string.Empty;

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }
    }

    /// <summary>
    /// 升级状态
    /// </summary>
    public class UpgradeStatus
    {
        /// <summary>
        /// 任务ID
        /// </summary>
        public string TaskId { get; set; } = string.Empty;

        /// <summary>
        /// 状态
        /// </summary>
        public UpgradeState State { get; set; }

        /// <summary>
        /// 进度百分比 (0-100)
        /// </summary>
        public int Progress { get; set; }

        /// <summary>
        /// 状态消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// 升级状态枚举
    /// </summary>
    public enum UpgradeState
    {
        /// <summary>
        /// 未开始
        /// </summary>
        NotStarted = 0,

        /// <summary>
        /// 初始化中
        /// </summary>
        Initializing = 1,

        /// <summary>
        /// 检查版本中
        /// </summary>
        CheckingVersion = 2,

        /// <summary>
        /// 下载镜像中
        /// </summary>
        DownloadingImage = 3,

        /// <summary>
        /// 停止容器中
        /// </summary>
        StoppingContainer = 4,

        /// <summary>
        /// 移除旧容器中
        /// </summary>
        RemovingOldContainer = 5,

        /// <summary>
        /// 启动新容器中
        /// </summary>
        StartingNewContainer = 6,

        /// <summary>
        /// 完成
        /// </summary>
        Completed = 7,

        /// <summary>
        /// 失败
        /// </summary>
        Failed = 8,

        /// <summary>
        /// 已取消
        /// </summary>
        Cancelled = 9
    }
}
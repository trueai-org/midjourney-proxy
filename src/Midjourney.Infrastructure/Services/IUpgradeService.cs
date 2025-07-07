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

using Midjourney.Infrastructure.Models;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// 升级服务接口
    /// </summary>
    public interface IUpgradeService
    {
        /// <summary>
        /// 获取最新版本信息
        /// </summary>
        /// <returns>最新版本信息</returns>
        Task<VersionInfo> GetLatestVersionAsync();

        /// <summary>
        /// 获取当前版本信息
        /// </summary>
        /// <returns>当前版本信息</returns>
        Task<VersionInfo> GetCurrentVersionAsync();

        /// <summary>
        /// 检查是否有更新
        /// </summary>
        /// <returns>是否有更新</returns>
        Task<bool> CheckForUpdatesAsync();

        /// <summary>
        /// 开始升级
        /// </summary>
        /// <returns>升级结果</returns>
        Task<UpgradeResult> UpgradeAsync();

        /// <summary>
        /// 获取升级状态
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <returns>升级状态</returns>
        Task<UpgradeStatus> GetUpgradeStatusAsync(string taskId);

        /// <summary>
        /// 取消升级
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <returns>是否取消成功</returns>
        Task<bool> CancelUpgradeAsync(string taskId);

        /// <summary>
        /// 验证升级权限
        /// </summary>
        /// <returns>是否有权限升级</returns>
        Task<bool> ValidateUpgradePermissionAsync();

        /// <summary>
        /// 获取所有升级任务状态
        /// </summary>
        /// <returns>升级任务状态列表</returns>
        Task<List<UpgradeStatus>> GetAllUpgradeTasksAsync();
    }
}
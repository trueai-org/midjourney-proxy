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

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Midjourney.Infrastructure.Models;
using Midjourney.Infrastructure.Options;
using Midjourney.Infrastructure.Services;
using Serilog;

namespace Midjourney.API.Controllers
{
    /// <summary>
    /// 升级控制器
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class UpgradeController : ControllerBase
    {
        private readonly IUpgradeService _upgradeService;
        private readonly UpgradeOptions _options;

        public UpgradeController(IUpgradeService upgradeService, IOptions<UpgradeOptions> options)
        {
            _upgradeService = upgradeService;
            _options = options.Value;
        }

        /// <summary>
        /// 获取当前版本
        /// </summary>
        /// <returns>当前版本信息</returns>
        [HttpGet("version")]
        public async Task<ActionResult<VersionInfo>> GetVersion()
        {
            try
            {
                var version = await _upgradeService.GetCurrentVersionAsync();
                return Ok(version);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "获取当前版本失败");
                return StatusCode(500, new { message = "获取当前版本失败", error = ex.Message });
            }
        }

        /// <summary>
        /// 获取最新版本
        /// </summary>
        /// <returns>最新版本信息</returns>
        [HttpGet("latest")]
        public async Task<ActionResult<VersionInfo>> GetLatestVersion()
        {
            try
            {
                var version = await _upgradeService.GetLatestVersionAsync();
                return Ok(version);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "获取最新版本失败");
                return StatusCode(500, new { message = "获取最新版本失败", error = ex.Message });
            }
        }

        /// <summary>
        /// 检查更新
        /// </summary>
        /// <returns>更新检查结果</returns>
        [HttpGet("check")]
        public async Task<ActionResult<object>> CheckUpdate()
        {
            try
            {
                var hasUpdate = await _upgradeService.CheckForUpdatesAsync();
                var currentVersion = await _upgradeService.GetCurrentVersionAsync();
                var latestVersion = await _upgradeService.GetLatestVersionAsync();

                return Ok(new
                {
                    hasUpdate,
                    currentVersion = currentVersion.Version,
                    latestVersion = latestVersion.Version,
                    upgradeEnabled = _options.EnableUpgrade,
                    message = hasUpdate ? "发现新版本" : "已是最新版本"
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "检查更新失败");
                return StatusCode(500, new { message = "检查更新失败", error = ex.Message });
            }
        }

        /// <summary>
        /// 开始升级
        /// </summary>
        /// <returns>升级结果</returns>
        [HttpPost("start")]
        public async Task<ActionResult<UpgradeResult>> StartUpgrade()
        {
            try
            {
                if (!_options.EnableUpgrade)
                {
                    return BadRequest(new { message = "升级功能已禁用" });
                }

                // 检查是否有权限
                var hasPermission = await _upgradeService.ValidateUpgradePermissionAsync();
                if (!hasPermission)
                {
                    return Forbid("升级权限验证失败");
                }

                // 检查是否有更新
                var hasUpdate = await _upgradeService.CheckForUpdatesAsync();
                if (!hasUpdate)
                {
                    return BadRequest(new { message = "当前已是最新版本，无需升级" });
                }

                var result = await _upgradeService.UpgradeAsync();
                
                if (result.Success)
                {
                    return Ok(result);
                }
                else
                {
                    return BadRequest(result);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "启动升级失败");
                return StatusCode(500, new { message = "启动升级失败", error = ex.Message });
            }
        }

        /// <summary>
        /// 获取升级状态
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <returns>升级状态</returns>
        [HttpGet("status/{taskId}")]
        public async Task<ActionResult<UpgradeStatus>> GetUpgradeStatus(string taskId)
        {
            try
            {
                if (string.IsNullOrEmpty(taskId))
                {
                    return BadRequest(new { message = "任务ID不能为空" });
                }

                var status = await _upgradeService.GetUpgradeStatusAsync(taskId);
                return Ok(status);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "获取升级状态失败: {TaskId}", taskId);
                return StatusCode(500, new { message = "获取升级状态失败", error = ex.Message });
            }
        }

        /// <summary>
        /// 取消升级
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <returns>取消结果</returns>
        [HttpPost("cancel/{taskId}")]
        public async Task<ActionResult<object>> CancelUpgrade(string taskId)
        {
            try
            {
                if (string.IsNullOrEmpty(taskId))
                {
                    return BadRequest(new { message = "任务ID不能为空" });
                }

                var success = await _upgradeService.CancelUpgradeAsync(taskId);
                
                return Ok(new 
                { 
                    success, 
                    message = success ? "升级已取消" : "取消升级失败或任务不存在" 
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "取消升级失败: {TaskId}", taskId);
                return StatusCode(500, new { message = "取消升级失败", error = ex.Message });
            }
        }

        /// <summary>
        /// 获取所有升级任务
        /// </summary>
        /// <returns>升级任务列表</returns>
        [HttpGet("tasks")]
        public async Task<ActionResult<List<UpgradeStatus>>> GetAllUpgradeTasks()
        {
            try
            {
                var tasks = await _upgradeService.GetAllUpgradeTasksAsync();
                return Ok(tasks);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "获取升级任务列表失败");
                return StatusCode(500, new { message = "获取升级任务列表失败", error = ex.Message });
            }
        }

        /// <summary>
        /// 验证升级权限
        /// </summary>
        /// <returns>权限验证结果</returns>
        [HttpGet("permission")]
        public async Task<ActionResult<object>> ValidatePermission()
        {
            try
            {
                var hasPermission = await _upgradeService.ValidateUpgradePermissionAsync();
                
                return Ok(new 
                { 
                    hasPermission, 
                    upgradeEnabled = _options.EnableUpgrade,
                    message = hasPermission ? "具有升级权限" : "缺少升级权限" 
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "验证升级权限失败");
                return StatusCode(500, new { message = "验证升级权限失败", error = ex.Message });
            }
        }

        /// <summary>
        /// 获取升级配置
        /// </summary>
        /// <returns>升级配置信息</returns>
        [HttpGet("config")]
        public ActionResult<object> GetUpgradeConfig()
        {
            try
            {
                return Ok(new
                {
                    enableUpgrade = _options.EnableUpgrade,
                    imageRegistry = _options.ImageRegistry,
                    containerName = _options.ContainerName,
                    upgradeTimeoutMinutes = _options.UpgradeTimeoutMinutes,
                    allowedUpgradeHours = _options.AllowedUpgradeHours,
                    autoCleanOldImages = _options.AutoCleanOldImages,
                    backupConfigBeforeUpgrade = _options.BackupConfigBeforeUpgrade
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "获取升级配置失败");
                return StatusCode(500, new { message = "获取升级配置失败", error = ex.Message });
            }
        }
    }
}
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

using Microsoft.Extensions.Options;
using Midjourney.Infrastructure.Models;
using Midjourney.Infrastructure.Options;
using Newtonsoft.Json;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// 升级服务实现
    /// </summary>
    public class UpgradeService : IUpgradeService
    {
        private readonly UpgradeOptions _options;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, UpgradeStatus> _upgradeTasks;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens;

        public UpgradeService(IOptions<UpgradeOptions> options, HttpClient httpClient)
        {
            _options = options.Value;
            _httpClient = httpClient;
            _upgradeTasks = new ConcurrentDictionary<string, UpgradeStatus>();
            _cancellationTokens = new ConcurrentDictionary<string, CancellationTokenSource>();
        }

        /// <summary>
        /// 获取最新版本信息
        /// </summary>
        public async Task<VersionInfo> GetLatestVersionAsync()
        {
            try
            {
                Log.Information("获取最新版本信息");
                
                var response = await _httpClient.GetStringAsync(_options.VersionCheckUrl);
                var releaseInfo = JsonConvert.DeserializeObject<dynamic>(response);

                var version = new VersionInfo
                {
                    Version = releaseInfo.tag_name ?? "unknown",
                    ReleaseDate = DateTime.TryParse(releaseInfo.published_at?.ToString(), out DateTime date) ? date : DateTime.Now,
                    Description = releaseInfo.name ?? "Latest Release",
                    DownloadUrl = _options.ImageRegistry,
                    IsLatest = true
                };

                Log.Information("最新版本: {Version}", version.Version);
                return version;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "获取最新版本信息失败");
                throw new Exception("获取最新版本信息失败", ex);
            }
        }

        /// <summary>
        /// 获取当前版本信息
        /// </summary>
        public async Task<VersionInfo> GetCurrentVersionAsync()
        {
            try
            {
                var currentVersion = GlobalConfiguration.Version;
                
                return await Task.FromResult(new VersionInfo
                {
                    Version = currentVersion,
                    ReleaseDate = DateTime.Now, // 这里可以从程序集信息获取
                    Description = "Current Version",
                    DownloadUrl = _options.ImageRegistry,
                    IsLatest = false
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "获取当前版本信息失败");
                throw new Exception("获取当前版本信息失败", ex);
            }
        }

        /// <summary>
        /// 检查是否有更新
        /// </summary>
        public async Task<bool> CheckForUpdatesAsync()
        {
            try
            {
                var latestVersion = await GetLatestVersionAsync();
                var currentVersion = await GetCurrentVersionAsync();

                // 简单版本比较，可以改进为更复杂的版本比较逻辑
                var hasUpdate = !string.Equals(latestVersion.Version, currentVersion.Version, StringComparison.OrdinalIgnoreCase);
                
                Log.Information("版本检查: 当前版本 {CurrentVersion}, 最新版本 {LatestVersion}, 有更新: {HasUpdate}", 
                    currentVersion.Version, latestVersion.Version, hasUpdate);
                
                return hasUpdate;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "检查更新失败");
                return false;
            }
        }

        /// <summary>
        /// 开始升级
        /// </summary>
        public async Task<UpgradeResult> UpgradeAsync()
        {
            if (!_options.EnableUpgrade)
            {
                return new UpgradeResult
                {
                    Success = false,
                    Message = "升级功能已禁用",
                    StartTime = DateTime.Now
                };
            }

            if (!await ValidateUpgradePermissionAsync())
            {
                return new UpgradeResult
                {
                    Success = false,
                    Message = "升级权限验证失败",
                    StartTime = DateTime.Now
                };
            }

            // 检查是否在允许的升级时间窗口
            var currentHour = DateTime.Now.Hour;
            if (_options.AllowedUpgradeHours?.Any() == true && !_options.AllowedUpgradeHours.Contains(currentHour))
            {
                return new UpgradeResult
                {
                    Success = false,
                    Message = $"当前时间({currentHour}:00)不在允许的升级时间窗口内",
                    StartTime = DateTime.Now
                };
            }

            var taskId = Guid.NewGuid().ToString();
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(_options.UpgradeTimeoutMinutes));
            
            _cancellationTokens.TryAdd(taskId, cancellationTokenSource);

            var upgradeStatus = new UpgradeStatus
            {
                TaskId = taskId,
                State = UpgradeState.NotStarted,
                Progress = 0,
                Message = "升级任务已创建",
                StartTime = DateTime.Now
            };

            _upgradeTasks.TryAdd(taskId, upgradeStatus);

            // 异步执行升级
            _ = Task.Run(() => ExecuteUpgradeAsync(taskId, cancellationTokenSource.Token));

            return new UpgradeResult
            {
                Success = true,
                Message = "升级任务已启动",
                TaskId = taskId,
                StartTime = DateTime.Now
            };
        }

        /// <summary>
        /// 执行升级逻辑
        /// </summary>
        private async Task ExecuteUpgradeAsync(string taskId, CancellationToken cancellationToken)
        {
            try
            {
                Log.Information("开始执行升级任务: {TaskId}", taskId);
                
                await UpdateUpgradeStatus(taskId, UpgradeState.Initializing, 5, "初始化升级环境");

                // 1. 检查Docker是否可用
                if (!await IsDockerAvailableAsync())
                {
                    await UpdateUpgradeStatus(taskId, UpgradeState.Failed, 0, "Docker不可用", "Docker环境检查失败");
                    return;
                }

                await UpdateUpgradeStatus(taskId, UpgradeState.CheckingVersion, 10, "检查版本信息");

                // 2. 获取最新版本
                var latestVersion = await GetLatestVersionAsync();
                
                await UpdateUpgradeStatus(taskId, UpgradeState.DownloadingImage, 20, "拉取最新镜像");

                // 3. 拉取最新镜像
                if (!await PullLatestImageAsync(cancellationToken))
                {
                    await UpdateUpgradeStatus(taskId, UpgradeState.Failed, 20, "拉取镜像失败", "无法拉取最新镜像");
                    return;
                }

                await UpdateUpgradeStatus(taskId, UpgradeState.StoppingContainer, 50, "停止当前容器");

                // 4. 停止当前容器
                if (!await StopCurrentContainerAsync(cancellationToken))
                {
                    await UpdateUpgradeStatus(taskId, UpgradeState.Failed, 50, "停止容器失败", "无法停止当前容器");
                    return;
                }

                await UpdateUpgradeStatus(taskId, UpgradeState.RemovingOldContainer, 70, "移除旧容器");

                // 5. 移除旧容器
                if (!await RemoveOldContainerAsync(cancellationToken))
                {
                    await UpdateUpgradeStatus(taskId, UpgradeState.Failed, 70, "移除容器失败", "无法移除旧容器");
                    return;
                }

                await UpdateUpgradeStatus(taskId, UpgradeState.StartingNewContainer, 80, "启动新容器");

                // 6. 启动新容器
                if (!await StartNewContainerAsync(cancellationToken))
                {
                    await UpdateUpgradeStatus(taskId, UpgradeState.Failed, 80, "启动容器失败", "无法启动新容器");
                    return;
                }

                // 7. 清理旧镜像（可选）
                if (_options.AutoCleanOldImages)
                {
                    await CleanOldImagesAsync(cancellationToken);
                }

                await UpdateUpgradeStatus(taskId, UpgradeState.Completed, 100, "升级完成");
                
                Log.Information("升级任务完成: {TaskId}", taskId);
            }
            catch (OperationCanceledException)
            {
                await UpdateUpgradeStatus(taskId, UpgradeState.Cancelled, 0, "升级已取消", "用户取消或超时");
                Log.Warning("升级任务被取消: {TaskId}", taskId);
            }
            catch (Exception ex)
            {
                await UpdateUpgradeStatus(taskId, UpgradeState.Failed, 0, "升级失败", ex.Message);
                Log.Error(ex, "升级任务失败: {TaskId}", taskId);
            }
            finally
            {
                _cancellationTokens.TryRemove(taskId, out _);
            }
        }

        /// <summary>
        /// 更新升级状态
        /// </summary>
        private async Task UpdateUpgradeStatus(string taskId, UpgradeState state, int progress, string message, string error = null)
        {
            if (_upgradeTasks.TryGetValue(taskId, out var status))
            {
                status.State = state;
                status.Progress = progress;
                status.Message = message;
                status.Error = error;
                
                if (state == UpgradeState.Completed || state == UpgradeState.Failed || state == UpgradeState.Cancelled)
                {
                    status.EndTime = DateTime.Now;
                }

                _upgradeTasks.TryUpdate(taskId, status, status);
                Log.Information("升级状态更新: {TaskId} - {State} - {Progress}% - {Message}", taskId, state, progress, message);
            }
            
            await Task.CompletedTask;
        }

        /// <summary>
        /// 检查Docker是否可用
        /// </summary>
        private async Task<bool> IsDockerAvailableAsync()
        {
            try
            {
                var result = await ExecuteDockerCommandAsync("version", TimeSpan.FromSeconds(10));
                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 拉取最新镜像
        /// </summary>
        private async Task<bool> PullLatestImageAsync(CancellationToken cancellationToken)
        {
            try
            {
                var result = await ExecuteDockerCommandAsync($"pull {_options.ImageRegistry}", TimeSpan.FromMinutes(5), cancellationToken);
                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 停止当前容器
        /// </summary>
        private async Task<bool> StopCurrentContainerAsync(CancellationToken cancellationToken)
        {
            try
            {
                var result = await ExecuteDockerCommandAsync($"stop {_options.ContainerName}", TimeSpan.FromMinutes(2), cancellationToken);
                return result.Success || result.Output.Contains("No such container"); // 容器不存在也算成功
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 移除旧容器
        /// </summary>
        private async Task<bool> RemoveOldContainerAsync(CancellationToken cancellationToken)
        {
            try
            {
                var result = await ExecuteDockerCommandAsync($"rm {_options.ContainerName}", TimeSpan.FromMinutes(1), cancellationToken);
                return result.Success || result.Output.Contains("No such container"); // 容器不存在也算成功
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 启动新容器
        /// </summary>
        private async Task<bool> StartNewContainerAsync(CancellationToken cancellationToken)
        {
            try
            {
                var command = BuildDockerRunCommand();
                var result = await ExecuteDockerCommandAsync(command, TimeSpan.FromMinutes(2), cancellationToken);
                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 构建Docker运行命令
        /// </summary>
        private string BuildDockerRunCommand()
        {
            var commandBuilder = new StringBuilder();
            commandBuilder.Append($"run --name {_options.ContainerName} -d --restart={_options.RestartPolicy}");
            
            if (!string.IsNullOrEmpty(_options.RunAsUser))
            {
                commandBuilder.Append($" --user {_options.RunAsUser}");
            }

            if (!string.IsNullOrEmpty(_options.PortMapping))
            {
                commandBuilder.Append($" -p {_options.PortMapping}");
            }

            foreach (var volume in _options.VolumeMapping)
            {
                commandBuilder.Append($" -v {volume}");
            }

            foreach (var env in _options.EnvironmentVariables)
            {
                commandBuilder.Append($" -e {env.Key}={env.Value}");
            }

            commandBuilder.Append($" {_options.ImageRegistry}");

            return commandBuilder.ToString();
        }

        /// <summary>
        /// 清理旧镜像
        /// </summary>
        private async Task CleanOldImagesAsync(CancellationToken cancellationToken)
        {
            try
            {
                await ExecuteDockerCommandAsync("image prune -f", TimeSpan.FromMinutes(1), cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "清理旧镜像失败");
            }
        }

        /// <summary>
        /// 执行Docker命令
        /// </summary>
        private async Task<(bool Success, string Output)> ExecuteDockerCommandAsync(string arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            try
            {
                using var process = new Process();
                process.StartInfo.FileName = "docker";
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;

                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using var timeoutCts = new CancellationTokenSource(timeout);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                try
                {
                    await process.WaitForExitAsync(combinedCts.Token);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                    throw;
                }

                var allOutput = output.ToString() + error.ToString();
                var success = process.ExitCode == 0;

                Log.Information("Docker命令执行: {Command}, 成功: {Success}, 输出: {Output}", 
                    $"docker {arguments}", success, allOutput);

                return (success, allOutput);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "执行Docker命令失败: {Command}", $"docker {arguments}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// 获取升级状态
        /// </summary>
        public async Task<UpgradeStatus> GetUpgradeStatusAsync(string taskId)
        {
            _upgradeTasks.TryGetValue(taskId, out var status);
            return await Task.FromResult(status ?? new UpgradeStatus 
            { 
                TaskId = taskId, 
                State = UpgradeState.NotStarted, 
                Message = "任务不存在" 
            });
        }

        /// <summary>
        /// 取消升级
        /// </summary>
        public async Task<bool> CancelUpgradeAsync(string taskId)
        {
            if (_cancellationTokens.TryGetValue(taskId, out var cts))
            {
                cts.Cancel();
                await UpdateUpgradeStatus(taskId, UpgradeState.Cancelled, 0, "升级已取消");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 验证升级权限
        /// </summary>
        public async Task<bool> ValidateUpgradePermissionAsync()
        {
            try
            {
                // 检查Docker socket是否可访问
                var dockerSocketExists = File.Exists(_options.DockerSocketPath);
                if (!dockerSocketExists)
                {
                    Log.Warning("Docker socket不存在: {Path}", _options.DockerSocketPath);
                    return false;
                }

                // 检查Docker命令是否可用
                var dockerAvailable = await IsDockerAvailableAsync();
                if (!dockerAvailable)
                {
                    Log.Warning("Docker命令不可用");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "验证升级权限失败");
                return false;
            }
        }

        /// <summary>
        /// 获取所有升级任务状态
        /// </summary>
        public async Task<List<UpgradeStatus>> GetAllUpgradeTasksAsync()
        {
            var tasks = _upgradeTasks.Values.ToList();
            return await Task.FromResult(tasks);
        }
    }
}
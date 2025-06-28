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

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Midjourney.Infrastructure;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Midjourney.API.Controllers
{
    /// <summary>
    /// 系统信息控制器
    /// </summary>
    [ApiController]
    [Route("mj/system")]
    [AllowAnonymous]
    public class SystemInfoController : ControllerBase
    {
        /// <summary>
        /// 获取系统信息
        /// </summary>
        /// <returns>系统信息</returns>
        [HttpGet("info")]
        public Result<SystemInfoDto> GetSystemInfo()
        {
            try
            {
                var systemInfo = new SystemInfoDto
                {
                    // 程序信息
                    programStartTime = GlobalConfiguration.ProgramStartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    programUptime = GetProgramUptime(),
                    programVersion = GlobalConfiguration.Version,
                    programDirectory = GetProgramDirectory(),
                    availableMemory = GetAvailableMemory(),

                    // 系统信息
                    frontendVersion = GlobalConfiguration.Version, // 假设前端版本与程序版本相同
                    cpuCores = Environment.ProcessorCount,
                    operatingSystem = GetOperatingSystem(),
                    architecture = GetArchitecture(),
                    hostname = GetHostname(),
                    serverStartTime = GetServerStartTime(),
                    serverUptime = GetServerUptime(),
                    totalMemory = GetTotalMemory(),
                    freeMemory = GetFreeMemory(),
                    systemPlatform = GetSystemPlatform(),
                    processCount = GetProcessCount()
                };

                return Result.Ok(systemInfo);
            }
            catch (Exception ex)
            {
                return Result.Fail<SystemInfoDto>($"获取系统信息失败: {ex.Message}");
            }
        }

        private string GetProgramUptime()
        {
            var uptime = DateTime.Now - GlobalConfiguration.ProgramStartTime;
            if (uptime.TotalDays >= 1)
            {
                return $"{(int)uptime.TotalDays}天";
            }
            else if (uptime.TotalHours >= 1)
            {
                return $"{(int)uptime.TotalHours} 小时内";
            }
            else
            {
                return $"{(int)uptime.TotalMinutes} 分钟内";
            }
        }

        private string GetProgramDirectory()
        {
            try
            {
                return GlobalConfiguration.ContentRootPath ?? AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
            }
            catch
            {
                return "未知";
            }
        }

        private string GetAvailableMemory()
        {
            try
            {
                var workingSet = Environment.WorkingSet;
                return $"{workingSet / 1024 / 1024}MB";
            }
            catch
            {
                return "未知";
            }
        }

        private string GetOperatingSystem()
        {
            if (GlobalConfiguration.IsWindows())
                return "windows";
            else if (GlobalConfiguration.IsLinux())
                return "linux";
            else if (GlobalConfiguration.IsMacOS())
                return "macos";
            else
                return "unknown";
        }

        private string GetArchitecture()
        {
            try
            {
                var arch = RuntimeInformation.ProcessArchitecture;
                return arch switch
                {
                    Architecture.X64 => "amd64",
                    Architecture.Arm64 => "arm64",
                    Architecture.X86 => "x86",
                    Architecture.Arm => "arm",
                    _ => arch.ToString().ToLower()
                };
            }
            catch
            {
                return "unknown";
            }
        }

        private string GetHostname()
        {
            try
            {
                return Environment.MachineName;
            }
            catch
            {
                return "未知";
            }
        }

        private string GetServerStartTime()
        {
            try
            {
                var bootTime = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount);
                return bootTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return "未知";
            }
        }

        private string GetServerUptime()
        {
            try
            {
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount);
                return $"{(int)uptime.TotalDays}天";
            }
            catch
            {
                return "未知";
            }
        }

        private string GetTotalMemory()
        {
            try
            {
                if (GlobalConfiguration.IsWindows())
                {
                    return GetWindowsTotalMemory();
                }
                else if (GlobalConfiguration.IsLinux())
                {
                    return GetLinuxTotalMemory();
                }
                else if (GlobalConfiguration.IsMacOS())
                {
                    return GetMacOSTotalMemory();
                }
                return "未知";
            }
            catch
            {
                return "未知";
            }
        }

        private string GetFreeMemory()
        {
            try
            {
                if (GlobalConfiguration.IsLinux())
                {
                    return GetLinuxFreeMemory();
                }
                
                // 对于其他系统，使用GC信息作为近似值
                var totalMemory = GC.GetTotalMemory(false);
                return $"{totalMemory / 1024 / 1024}MB";
            }
            catch
            {
                return "未知";
            }
        }

        private string GetSystemPlatform()
        {
            try
            {
                if (GlobalConfiguration.IsLinux())
                {
                    return GetLinuxDistribution();
                }
                else if (GlobalConfiguration.IsWindows())
                {
                    return "windows";
                }
                else if (GlobalConfiguration.IsMacOS())
                {
                    return "macos";
                }
                return "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private int GetProcessCount()
        {
            try
            {
                return Process.GetProcesses().Length;
            }
            catch
            {
                return 0;
            }
        }

        private string GetWindowsTotalMemory()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "wmic",
                        Arguments = "computersystem get TotalPhysicalMemory /value",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("TotalPhysicalMemory="))
                    {
                        var memoryStr = line.Split('=')[1].Trim();
                        if (long.TryParse(memoryStr, out var memory))
                        {
                            return $"{memory / 1024 / 1024}MB";
                        }
                    }
                }
            }
            catch { }
            
            return "未知";
        }

        private string GetLinuxTotalMemory()
        {
            try
            {
                var meminfo = System.IO.File.ReadAllText("/proc/meminfo");
                var lines = meminfo.Split('\n');
                
                foreach (var line in lines)
                {
                    if (line.StartsWith("MemTotal:"))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && long.TryParse(parts[1], out var memoryKb))
                        {
                            return $"{memoryKb / 1024}MB";
                        }
                    }
                }
            }
            catch { }
            
            return "未知";
        }

        private string GetLinuxFreeMemory()
        {
            try
            {
                var meminfo = System.IO.File.ReadAllText("/proc/meminfo");
                var lines = meminfo.Split('\n');
                
                foreach (var line in lines)
                {
                    if (line.StartsWith("MemAvailable:"))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && long.TryParse(parts[1], out var memoryKb))
                        {
                            return $"{memoryKb / 1024}MB";
                        }
                    }
                }
            }
            catch { }
            
            return "未知";
        }

        private string GetMacOSTotalMemory()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sysctl",
                        Arguments = "-n hw.memsize",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                
                if (long.TryParse(output, out var memory))
                {
                    return $"{memory / 1024 / 1024}MB";
                }
            }
            catch { }
            
            return "未知";
        }

        private string GetLinuxDistribution()
        {
            try
            {
                // 尝试读取 /etc/os-release 文件
                if (System.IO.File.Exists("/etc/os-release"))
                {
                    var osRelease = System.IO.File.ReadAllText("/etc/os-release");
                    var lines = osRelease.Split('\n');
                    
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("ID="))
                        {
                            var distro = line.Split('=')[1].Trim('"').ToLower();
                            return distro;
                        }
                    }
                }
                
                // 备用方案：检查其他发行版特定文件
                if (System.IO.File.Exists("/etc/debian_version"))
                    return "debian";
                if (System.IO.File.Exists("/etc/redhat-release"))
                    return "centos";
                if (System.IO.File.Exists("/etc/alpine-release"))
                    return "alpine";
            }
            catch { }
            
            return "linux";
        }
    }
}
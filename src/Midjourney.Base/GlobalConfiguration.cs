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

global using Midjourney.Base.Models;

using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Caching.Memory;
using Midjourney.Base.Services;
using Midjourney.Base.Util;
using Serilog.Core;

namespace Midjourney.Base
{
    /// <summary>
    /// 全局配置
    /// </summary>
    public class GlobalConfiguration
    {
        /// <summary>
        /// 版本号
        /// </summary>
        public static string Version { get; set; } = $"v{(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version}";

        /// <summary>
        /// 创建一个全局可控的日志级别开关
        /// </summary>
        public static LoggingLevelSwitch LogLevel { get; set; } = new LoggingLevelSwitch();

        /// <summary>
        /// 今日绘图
        /// </summary>
        public static int TodayDraw { get; set; }

        /// <summary>
        /// 总绘图
        /// </summary>
        public static int TotalDraw { get; set; }

        /// <summary>
        /// 全局配置项
        /// </summary>
        public static Setting Setting { get; set; }

        /// <summary>
        /// 网站配置为演示模式
        /// </summary>
        public static bool IsDemoMode => Setting?.IsDemoMode ?? false;

        /// <summary>
        /// 当前节点全局最大任务并行处理上限
        /// 默认：-1 不限制，0 不处理任务
        /// </summary>
        public static int GlobalMaxConcurrent { get; set; } = -1;

        /// <summary>
        /// 全局任务并行锁
        /// </summary>
        public static AsyncParallelLock GlobalLock { get; set; }

        /// <summary>
        /// 全局缓存项
        /// </summary>
        public static IMemoryCache MemoryCache { get; set; }

        /// <summary>
        /// 全局翻译服务（使用前必须配置）
        /// </summary>
        public static ITranslateService TranslateService { get; set; }

        /// <summary>
        /// 站点根目录 wwwroot
        /// </summary>
        public static string WebRootPath { get; set; }

        /// <summary>
        /// 静态文件根目录
        /// </summary>
        public static string ContentRootPath { get; set; }

        /// <summary>
        /// 判断是否是 Windows 系统
        /// </summary>
        /// <returns></returns>
        public static bool IsWindows()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        /// <summary>
        /// 判断是否是 Linux 系统
        /// </summary>
        /// <returns></returns>
        public static bool IsLinux()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        }

        /// <summary>
        /// 判断是否是 macOS 系统
        /// </summary>
        /// <returns></returns>
        public static bool IsMacOS()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                || Environment.OSVersion.Platform == PlatformID.MacOSX;
        }
    }
}
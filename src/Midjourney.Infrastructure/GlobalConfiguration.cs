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

using Microsoft.Extensions.Caching.Memory;
using System.Runtime.InteropServices;

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// 全局配置
    /// </summary>
    public class GlobalConfiguration
    {
        /// <summary>
        /// 网站配置为演示模式
        /// </summary>
        public static bool? IsDemoMode { get; set; }

        /// <summary>
        /// 版本号
        /// </summary>
        public static string Version { get; set; } = "v7.0.1";

        /// <summary>
        /// 全局配置项
        /// </summary>
        public static Setting Setting { get; set; }

        /// <summary>
        /// 全局缓存项
        /// </summary>
        public static IMemoryCache MemoryCache { get; set; }

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
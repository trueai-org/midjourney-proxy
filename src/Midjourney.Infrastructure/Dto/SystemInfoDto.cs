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

namespace Midjourney.Infrastructure.Dto
{
    /// <summary>
    /// 系统信息
    /// </summary>
    public class SystemInfoDto
    {
        /// <summary>
        /// 程序启动时间（格式：2025-06-27 18:48:22）
        /// </summary>
        public string programStartTime { get; set; }

        /// <summary>
        /// 程序已启动时间（格式：20 小时内）
        /// </summary>
        public string programUptime { get; set; }

        /// <summary>
        /// 本地前端版本
        /// </summary>
        public string frontendVersion { get; set; }

        /// <summary>
        /// CPU 核心数
        /// </summary>
        public int cpuCores { get; set; }

        /// <summary>
        /// 操作系统类型（Linux/macOS/Windows）
        /// </summary>
        public string operatingSystem { get; set; }

        /// <summary>
        /// 操作系统架构（x64/arm64 等）
        /// </summary>
        public string architecture { get; set; }

        /// <summary>
        /// 服务器主机名
        /// </summary>
        public string hostname { get; set; }

        /// <summary>
        /// 服务器启动时间
        /// </summary>
        public string serverStartTime { get; set; }

        /// <summary>
        /// 服务器已启动天数
        /// </summary>
        public string serverUptime { get; set; }

        /// <summary>
        /// 服务器总内存
        /// </summary>
        public string totalMemory { get; set; }

        /// <summary>
        /// 服务器空闲内存
        /// </summary>
        public string freeMemory { get; set; }

        /// <summary>
        /// 服务器系统平台（debian/ubuntu/centos 等）
        /// </summary>
        public string systemPlatform { get; set; }

        /// <summary>
        /// 服务器进程数
        /// </summary>
        public int processCount { get; set; }

        /// <summary>
        /// 程序可用内存
        /// </summary>
        public string availableMemory { get; set; }

        /// <summary>
        /// 程序版本
        /// </summary>
        public string programVersion { get; set; }

        /// <summary>
        /// 程序目录
        /// </summary>
        public string programDirectory { get; set; }
    }
}
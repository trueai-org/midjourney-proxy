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

namespace Midjourney.Infrastructure.Options
{
    /// <summary>
    /// 升级配置选项
    /// </summary>
    public class UpgradeOptions
    {
        /// <summary>
        /// 是否启用升级功能
        /// </summary>
        public bool EnableUpgrade { get; set; } = true;

        /// <summary>
        /// 镜像仓库地址
        /// </summary>
        public string ImageRegistry { get; set; } = "registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy";

        /// <summary>
        /// 容器名称
        /// </summary>
        public string ContainerName { get; set; } = "mjopen";

        /// <summary>
        /// 升级超时时间（分钟）
        /// </summary>
        public int UpgradeTimeoutMinutes { get; set; } = 10;

        /// <summary>
        /// 版本检查URL
        /// </summary>
        public string VersionCheckUrl { get; set; } = "https://api.github.com/repos/trueai-org/midjourney-proxy/releases/latest";

        /// <summary>
        /// Docker Socket 路径
        /// </summary>
        public string DockerSocketPath { get; set; } = "/var/run/docker.sock";

        /// <summary>
        /// 容器端口映射
        /// </summary>
        public string PortMapping { get; set; } = "8086:8080";

        /// <summary>
        /// 容器数据卷映射
        /// </summary>
        public List<string> VolumeMapping { get; set; } = new List<string>
        {
            "/root/mjopen/logs:/app/logs:rw",
            "/root/mjopen/data:/app/data:rw",
            "/root/mjopen/attachments:/app/wwwroot/attachments:rw",
            "/root/mjopen/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw",
            "/etc/localtime:/etc/localtime:ro",
            "/etc/timezone:/etc/timezone:ro"
        };

        /// <summary>
        /// 容器环境变量
        /// </summary>
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>
        {
            { "TZ", "Asia/Shanghai" }
        };

        /// <summary>
        /// 容器重启策略
        /// </summary>
        public string RestartPolicy { get; set; } = "always";

        /// <summary>
        /// 运行用户
        /// </summary>
        public string RunAsUser { get; set; } = "root";

        /// <summary>
        /// 自动清理旧镜像
        /// </summary>
        public bool AutoCleanOldImages { get; set; } = true;

        /// <summary>
        /// 升级前备份配置
        /// </summary>
        public bool BackupConfigBeforeUpgrade { get; set; } = true;

        /// <summary>
        /// 允许的升级时间窗口（小时，24小时制）
        /// </summary>
        public int[] AllowedUpgradeHours { get; set; } = { 1, 2, 3, 4, 5 }; // 深夜1-5点允许升级
    }
}
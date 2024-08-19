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
using Midjourney.Infrastructure.Options;

namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// 系统配置
    /// </summary>
    public class Setting : ProxyProperties
    {
        /// <summary>
        /// 全局开启垂直领域
        /// </summary>
        public bool IsVerticalDomain { get; set; }

        /// <summary>
        /// 启用 Swagger
        /// </summary>
        public bool EnableSwagger { get; set; }

        /// <summary>
        /// 限流配置
        /// </summary>
        public IpRateLimitingOptions IpRateLimiting { get; set; }

        /// <summary>
        /// 黑名单限流配置
        /// </summary>
        public IpBlackRateLimitingOptions IpBlackRateLimiting { get; set; }

        /// <summary>
        /// 开启注册
        /// </summary>
        public bool EnableRegister { get; set; }

        /// <summary>
        /// 注册用户默认日绘图限制
        /// </summary>
        public int RegisterUserDefaultDayLimit { get; set; } = -1;

        /// <summary>
        /// 开启访客
        /// </summary>
        public bool EnableGuest { get; set; }

        /// <summary>
        /// 访客默认日绘图限制
        /// </summary>
        public int GuestDefaultDayLimit { get; set; } = -1;
    }
}
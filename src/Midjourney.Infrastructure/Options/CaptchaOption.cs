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
    /// 验证码配置项
    /// </summary>
    public class CaptchaOption
    {
        /// <summary>
        /// 并发数
        /// </summary>
        public int Concurrent { get; set; } = 1;

        /// <summary>
        /// 2captcha API key
        /// </summary>
        public string TwoCaptchaKey { get; set; }

        /// <summary>
        /// yescaptcha API key
        /// </summary>
        public string YesCaptchaKey { get; set; }

        /// <summary>
        /// 是否后台运行
        /// </summary>
        public bool Headless { get; set; }
    }
}

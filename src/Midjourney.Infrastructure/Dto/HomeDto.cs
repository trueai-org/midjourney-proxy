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
    /// 首页信息
    /// </summary>
    public class HomeDto
    {
        /// <summary>
        /// 是否显示注册入口
        /// </summary>
        public bool IsRegister { get; set; }

        /// <summary>
        /// 是否开启了访客入口
        /// </summary>
        public bool IsGuest { get; set; }

        /// <summary>
        /// 网站配置为演示模式
        /// </summary>
        public bool IsDemoMode { get; set; }

        /// <summary>
        /// 版本号
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// 今日绘图
        /// </summary>
        public int TodayDraw { get; set; }

        /// <summary>
        /// 昨日绘图
        /// </summary>
        public int YesterdayDraw { get; set; }

        /// <summary>
        /// 总绘图
        /// </summary>
        public int TotalDraw { get; set; }

        /// <summary>
        /// 首页公告
        /// </summary>
        public string Notify { get; set; }

        /// <summary>
        /// 绘图客户端 top 5
        /// </summary>
        public Dictionary<string, int> Tops { get; set; } = new Dictionary<string, int>();
    }
}

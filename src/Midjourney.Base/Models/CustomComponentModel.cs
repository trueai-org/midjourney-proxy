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
using System.Text.Json.Serialization;
using IdGen;

namespace Midjourney.Base.Models
{
    /// <summary>
    /// 按钮组件自定义属性。
    /// </summary>
    public class CustomComponentModel
    {
        public string CustomId { get; set; } = string.Empty;

        public string Emoji { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public int Style { get; set; }

        public int Type { get; set; }

        /// <summary>
        /// 创建视频组件
        /// </summary>
        /// <param name="id"></param>
        /// <param name="low"></param>
        /// <param name="auto"></param>
        /// <param name="videoType"></param>
        /// <returns></returns>
        public static CustomComponentModel CreateVideo(string id, string low = "Low", string auto = "Auto", string videoType = "vid_1.1_i2v_480")
        {
            var prefix = "";
            if (videoType.Contains("extend"))
            {
                prefix = "Extend ";
            }

            return new CustomComponentModel
            {
                CustomId = $"MJ::JOB::video::{low.ToLower()}::{id}::{videoType}::{auto.ToLower()}",
                Label = $"{prefix}{auto} {low} Motion",
                Emoji = "",
                Style = 2,
                Type = 2
            };
        }
    }
}
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

namespace Midjourney.Base.Models
{
    /// <summary>
    /// 按钮组件自定义属性。
    /// </summary>
    public class CustomComponentModel
    {
        /// <summary>
        /// 使用字符串插值和 char 转换
        /// </summary>
        /// <param name="codePoint"></param>
        /// <returns></returns>
        public static string GetEmojiFromCodePoint(int codePoint)
        {
            if (codePoint <= 0xFFFF)
            {
                return ((char)codePoint).ToString();
            }
            else
            {
                // 对于超过 BMP 的字符，需要使用代理对
                return char.ConvertFromUtf32(codePoint);
            }
        }


        public string CustomId { get; set; } = string.Empty;

        public string Emoji { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public int Style { get; set; } = 2;

        public int Type { get; set; } = 2;

        /// <summary>
        /// 创建 Animate 操作按钮
        /// </summary>
        /// <param name="id"></param>
        /// <param name="index">1 | 2 | 3 | 4</param>
        /// <returns></returns>
        public static List<CustomComponentModel> CreateAnimateButtons(string id, int index)
        {
            return
            [
                new CustomComponentModel
                {
                    CustomId = $"MJ::JOB::animate_high::{index}::{id}::SOLO",
                    Label = "Animate (High motion)",
                    Emoji = "🎞️",
                    Style = 2,
                    Type = 2
                },
                new CustomComponentModel
                {
                    CustomId = $"MJ::JOB::animate_low::{index}::{id}::SOLO",
                    Label = "Animate (Low motion)",
                    Emoji = "🎞️",
                    Style = 2,
                    Type = 2
                },
            ];
        }

        /// <summary>
        /// 创建 Upscale 操作按钮
        /// </summary>
        /// <param name="id"></param>
        /// <param name="index">1 | 2 | 3 | 4</param>
        /// <param name="version">v 7 | v 6.1 | niji 6</param>
        /// <returns></returns>
        public static List<CustomComponentModel> CreateUpscaleButtons(TaskInfo info, string id, int index, string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            // 不兼容 1,2,3,4 版本操作

            // --v 5
            // --v 5.1
            // --niji 5
            // v5_2x
            // v5_4x

            // --v 6
            // --niji 6
            // v6_2x_subtle
            // v6_2x_creative

            // --v 6.1
            // v6r1_2x_subtle
            // v6r1_2x_creative

            // --v 7
            // v7_2x_creative
            // v7_2x_subtle

            var button1Type = "";
            var bytton2Type = "";
            var label1 = "";
            var label2 = "";
            if (version.StartsWith("v 7") || version.StartsWith("niji 7"))
            {
                button1Type = "v7_2x_subtle";
                bytton2Type = "v7_2x_creative";
                label1 = "Upscale (Subtle)";
                label2 = "Upscale (Creative)";

                //if (info.IsPartner)
                //{
                //    button1Type = "v7_upscale_2x_subtle";
                //    bytton2Type = "v7_upscale_2x_creative";
                //}
            }
            else if (version.StartsWith("v 6.1"))
            {
                button1Type = "v6r1_2x_subtle";
                bytton2Type = "v6r1_2x_creative";
                label1 = "Upscale (Subtle)";
                label2 = "Upscale (Creative)";

                //if (info.IsPartner)
                //{
                //    button1Type = "v6r1_upscale_2x_subtle";
                //    bytton2Type = "v6r1_upscale_2x_creative";
                //}
            }
            else if (version.StartsWith("v 6") || version.StartsWith("niji 6"))
            {
                button1Type = "v6_2x_subtle";
                bytton2Type = "v6_2x_creative";
                label1 = "Upscale (Subtle)";
                label2 = "Upscale (Creative)";

                //if (info.IsPartner)
                //{
                //    button1Type = "v6_upscale_2x_subtle";
                //    bytton2Type = "v6_upscale_2x_creative";
                //}
            }
            else if (version.StartsWith("v 5") || version.StartsWith("niji 5"))
            {
                button1Type = "v5_2x";
                bytton2Type = "v5_4x";
                label1 = "Upscale (2x)";
                label2 = "Upscale (4x)";

                //if (info.IsPartner)
                //{
                //    // 悠船
                //    button1Type = "v5_upscale_2x";
                //    bytton2Type = "v5_upscale_4x";
                //}
            }
            else if (version.StartsWith("v 4") || version.StartsWith("niji 4"))
            {
                return null;
            }
            else if (version.StartsWith("v 3") || version.StartsWith("niji 3"))
            {
                return null;
            }
            else if (version.StartsWith("v 2") || version.StartsWith("niji 2"))
            {
                return null;
            }
            else if (version.StartsWith("v 1") || version.StartsWith("niji 1"))
            {
                return null;
            }
            else
            {
                // 提取版本号中的数字
                var versionNumber = version.Split(' ').LastOrDefault()?.Trim();
                if (!string.IsNullOrWhiteSpace(versionNumber))
                {
                    button1Type = $"v{versionNumber}_2x_subtle";
                    bytton2Type = $"v{versionNumber}_2x_creative";
                    label1 = "Upscale (Subtle)";
                    label2 = "Upscale (Creative)";

                    //if (info.IsPartner)
                    //{
                    //    button1Type = $"v{versionNumber}_upscale_2x_subtle";
                    //    bytton2Type = $"v{versionNumber}_upscale_2x_creative";
                    //}
                }
                else
                {
                    // 无法识别的版本号
                    return null;
                }
            }

            return
            [
                new CustomComponentModel
                {
                    CustomId = $"MJ::JOB::upsample_{button1Type}::{index}::{id}::SOLO",
                    Label = label1,
                    Emoji = "upscale_1",
                    Style = 2,
                    Type = 2
                },
                new CustomComponentModel
                {
                    CustomId = $"MJ::JOB::upsample_{bytton2Type}::{index}::{id}::SOLO",
                    Label = label2,
                    Emoji = "upscale_1",
                    Style = 2,
                    Type = 2
                }
            ];
        }

        /// <summary>
        /// 创建 Vary 操作按钮
        /// </summary>
        /// <param name="id"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public static List<CustomComponentModel> CreateVaryButtons(string id, int index, string version)
        {
            var list = new List<CustomComponentModel>()
            {
                new CustomComponentModel
                {
                    CustomId = $"MJ::JOB::low_variation::{index}::{id}::SOLO",
                    Label = "Vary (Subtle)",
                    Emoji = GetEmojiFromCodePoint(0x1FA84),
                    Style = 2,
                    Type = 2
                },
                new CustomComponentModel
                {
                    CustomId = $"MJ::JOB::high_variation::{index}::{id}::SOLO",
                    Label = "Vary (Strong)",
                    Emoji = GetEmojiFromCodePoint(0x1FA84),
                    Style = 2,
                    Type = 2
                }
            };

            // >= 5 支持局部重绘
            var regionButton = CreateVaryRegionButton(id, index, version);
            if (regionButton != null)
            {
                list.Add(regionButton);
            }

            return list;
        }

        /// <summary>
        /// 创建 Vary Region 按钮
        /// </summary>
        /// <param name="id"></param>
        /// <param name="index"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        public static CustomComponentModel CreateVaryRegionButton(string id, int index, string version)
        {
            // >= 5 支持局部重绘
            var versionNumber = version.Split(' ').LastOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(versionNumber) || !double.TryParse(versionNumber, out double v) || v < 5)
            {
                return null;
            }

            return new CustomComponentModel
            {
                CustomId = $"MJ::Inpaint::{index}::{id}::SOLO",
                Label = "Vary (Region)",
                Emoji = "🖌️",
                Style = 2,
                Type = 2
            };
        }

        /// <summary>
        /// 创建 Zoom 操作按钮
        /// </summary>
        /// <param name="id"></param>
        /// <param name="index"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        public static List<CustomComponentModel> CreateZoomButtons(string id, int index, string version)
        {
            // >= 5 支持
            var versionNumber = version.Split(' ').LastOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(versionNumber)
                || !double.TryParse(versionNumber, out double v)
                || v < 5)
            {
                return null;
            }

            return
            [
                new CustomComponentModel
                {
                    CustomId = $"MJ::Outpaint::50::{index}::{id}::SOLO",
                    Emoji = "🔍",
                    Label = "Zoom Out 2x",
                    Style = 2,
                    Type = 2
                },
                new CustomComponentModel
                {
                    CustomId = $"MJ::Outpaint::75::{index}::{id}::SOLO",
                    Emoji = "🔍",
                    Label = "Zoom Out 1.5x",
                    Style = 2,
                    Type = 2
                },
                new CustomComponentModel
                {
                    CustomId = $"MJ::CustomZoom::{index}::{id}",
                    Emoji = "🔍",
                    Label = "Custom Zoom",
                    Style = 2,
                    Type = 2
                }
            ];
        }

        /// <summary>
        /// 创建 Pan 操作按钮
        /// </summary>
        /// <param name="id"></param>
        /// <param name="index"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        public static List<CustomComponentModel> CreatePanButtons(string id, int index, string version)
        {
            // >= 5 支持
            var versionNumber = version.Split(' ').LastOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(versionNumber) || !double.TryParse(versionNumber, out double v) || v < 5)
            {
                return null;
            }
            {
                return
                [
                    new CustomComponentModel
                {
                    CustomId = $"MJ::JOB::pan_left::{index}::{id}::SOLO",
                    Emoji = "⬅️",
                    Label = "",
                    Style = 2,
                    Type = 2
                },
                new CustomComponentModel
                {
                    CustomId = $"MJ::JOB::pan_right::{index}::{id}::SOLO",
                    Emoji = "➡️",
                    Label = "",
                    Style = 2,
                    Type = 2
                },
                new CustomComponentModel
                {
                    CustomId = $"MJ::JOB::pan_up::{index}::{id}::SOLO",
                    Emoji = "⬆️",
                    Label = "",
                    Style = 2,
                    Type = 2
                },
                new CustomComponentModel
                {
                    CustomId = $"MJ::JOB::pan_down::{index}::{id}::SOLO",
                    Emoji = "⬇️",
                    Label = "",
                    Style = 2,
                    Type = 2
                }
                ];
            }
        }

        /// <summary>
        /// 创建重绘操作按钮
        /// </summary>
        /// <param name="id"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public static CustomComponentModel CreateRerollButtons(string id)
        {
            return new CustomComponentModel
            {
                CustomId = $"MJ::JOB::reroll::0::{id}::SOLO",
                Label = "",
                Emoji = "\uD83D\uDD04",
                Style = 2,
                Type = 2
            };
        }
    }
}
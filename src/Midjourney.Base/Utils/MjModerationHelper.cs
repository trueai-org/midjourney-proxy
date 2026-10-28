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
using System.Text.RegularExpressions;
using Midjourney.Base.Dto;

namespace Midjourney.Base.Util
{
    /// <summary>
    /// MJ 审核消息（Pending mod message）辅助类
    /// </summary>
    public static class MjModerationHelper
    {
        /// <summary>
        /// 审核消息标题
        /// </summary>
        public const string PENDING_MOD_MESSAGE_TITLE = "Pending mod message";

        /// <summary>
        /// 审核消息确认按钮文本
        /// </summary>
        public const string ACKNOWLEDGE_LABEL = "Acknowledge";

        /// <summary>
        /// 例如：blocked from accessing Midjourney for 1 hour / for 24 hours / for 3 days / for 30 minutes
        /// </summary>
        private static readonly Regex BlockDurationRegex = new(
            @"blocked\s+from\s+accessing\s+Midjourney\s+for\s+(?<num>\d+)\s+(?<unit>minute|hour|day|week)s?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 从审核消息描述中解析临时封禁时长，未包含封禁时长时返回 null
        /// </summary>
        /// <param name="description">embed 描述</param>
        /// <returns></returns>
        public static TimeSpan? TryParseBlockDuration(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            var match = BlockDurationRegex.Match(description);
            if (!match.Success || !int.TryParse(match.Groups["num"].Value, out var num) || num <= 0)
            {
                return null;
            }

            return match.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "minute" => TimeSpan.FromMinutes(num),
                "hour" => TimeSpan.FromHours(num),
                "day" => TimeSpan.FromDays(num),
                "week" => TimeSpan.FromDays(num * 7),
                _ => null
            };
        }

        /// <summary>
        /// 在消息组件中查找指定文本的按钮，返回其 custom_id，未找到返回 null
        /// </summary>
        /// <param name="components">消息组件（action row 列表）</param>
        /// <param name="label">按钮文本</param>
        /// <returns></returns>
        public static string FindButtonCustomId(List<Component> components, string label)
        {
            if (components == null || components.Count == 0 || string.IsNullOrWhiteSpace(label))
            {
                return null;
            }

            foreach (var row in components)
            {
                if (row == null)
                {
                    continue;
                }

                // 按钮可能直接在顶层，也可能在 action row 内
                if (string.Equals(row.Label, label, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(row.CustomId))
                {
                    return row.CustomId;
                }

                var button = row.Components?.FirstOrDefault(c =>
                    string.Equals(c?.Label, label, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(c.CustomId));

                if (button != null)
                {
                    return button.CustomId;
                }
            }

            return null;
        }
    }
}

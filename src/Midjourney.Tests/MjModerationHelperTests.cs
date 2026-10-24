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
using System.Text.Json;
using Midjourney.Base.Dto;
using Midjourney.Base.Util;

namespace Midjourney.Tests
{
    /// <summary>
    /// MjModerationHelper 单元测试
    /// </summary>
    public class MjModerationHelperTests
    {
        private const string BlockedDescription =
            "You have a pending moderation message. Please acknowledge it before using Midjourney further.\n" +
            "After reviewing your gallery, you have been blocked from accessing Midjourney for 1 hour for generating content that violates our Community Guidelines. " +
            "You can read them here: https://docs.midjourney.com/docs/community-guidelines..\n\n" +
            "*Please review the Midjourney [Community Guidelines](https://docs.midjourney.com/docs/community-guidelines).*";

        [Fact]
        public void TryParseBlockDuration_OneHour()
        {
            var duration = MjModerationHelper.TryParseBlockDuration(BlockedDescription);

            Assert.Equal(TimeSpan.FromHours(1), duration);
        }

        [Theory]
        [InlineData("blocked from accessing Midjourney for 24 hours for generating", 24 * 60)]
        [InlineData("blocked from accessing Midjourney for 30 minutes for generating", 30)]
        [InlineData("blocked from accessing Midjourney for 3 days for generating", 3 * 24 * 60)]
        [InlineData("blocked from accessing Midjourney for 1 week for generating", 7 * 24 * 60)]
        public void TryParseBlockDuration_Units(string description, int expectedMinutes)
        {
            var duration = MjModerationHelper.TryParseBlockDuration(description);

            Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), duration);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("You have a pending moderation message. Please acknowledge it before using Midjourney further.")]
        [InlineData("blocked from accessing Midjourney for 0 hours")]
        public void TryParseBlockDuration_NoBlock_ReturnsNull(string description)
        {
            Assert.Null(MjModerationHelper.TryParseBlockDuration(description));
        }

        [Fact]
        public void FindButtonCustomId_FromDiscordPayload()
        {
            // Discord MESSAGE_CREATE 的 components 结构：action row -> buttons
            var json = """
            {
              "flags": 64,
              "components": [
                {
                  "type": 1,
                  "components": [
                    { "type": 2, "style": 3, "label": "Acknowledge", "custom_id": "MJ::Moderation::Acknowledge::abc123" },
                    { "type": 2, "style": 2, "label": "Resubmit", "custom_id": "MJ::Moderation::Resubmit::abc123" }
                  ]
                }
              ]
            }
            """;

            var data = JsonSerializer.Deserialize<EventData>(json);

            var customId = MjModerationHelper.FindButtonCustomId(data.Components, MjModerationHelper.ACKNOWLEDGE_LABEL);

            Assert.Equal("MJ::Moderation::Acknowledge::abc123", customId);
            Assert.Equal(64, data.Flags);
        }

        [Fact]
        public void FindButtonCustomId_MissingButton_ReturnsNull()
        {
            var components = new List<Component>
            {
                new Component
                {
                    Type = 1,
                    Components = new List<Component>
                    {
                        new Component { Type = 2, Label = "Resubmit", CustomId = "MJ::Moderation::Resubmit::abc123" }
                    }
                }
            };

            Assert.Null(MjModerationHelper.FindButtonCustomId(components, MjModerationHelper.ACKNOWLEDGE_LABEL));
            Assert.Null(MjModerationHelper.FindButtonCustomId(null, MjModerationHelper.ACKNOWLEDGE_LABEL));
        }
    }
}

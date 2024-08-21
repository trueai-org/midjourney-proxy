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
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Handle
{
    /// <summary>
    /// Blend 事件处理
    /// </summary>
    public class UserBlendSuccessHandler : UserMessageHandler
    {
        private const string CONTENT_REGEX = "\\*\\*(.*)\\*\\* - (.*?)<@\\d+> \\((.*?)\\)";
        private const int MIN_URLS = 2;
        private const int MAX_URLS = 5;

        public UserBlendSuccessHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
        : base(discordLoadBalancer, discordHelper)
        {
        }

        public override int Order() => 99998;

        public override void Handle(DiscordInstance instance, MessageType messageType, EventData message)
        {
            var content = GetMessageContent(message);
            var parseData = GetParseData(content);
            var urls = ExtractUrls(content);
            var prompt = parseData?.Prompt.FormatPrompt();

            if (messageType == MessageType.CREATE
                && string.IsNullOrWhiteSpace(prompt)
                && HasImage(message)
                && parseData != null
                && urls.Count >= MIN_URLS && urls.Count <= MAX_URLS
                && message.Author.Bot == true && message.Author.Username.Contains("journey Bot", StringComparison.OrdinalIgnoreCase))
            {
                FindAndFinishImageTask(instance, TaskAction.BLEND, parseData.Prompt, message);
            }
        }

        private ContentParseData GetParseData(string content)
        {
            return ConvertUtils.ParseContent(content, CONTENT_REGEX);
        }

        private List<string> ExtractUrls(string content)
        {
            var urls = new List<string>();
            var regex = new Regex(@"https?://[^\s>]+");
            var matches = regex.Matches(content);

            foreach (Match match in matches)
            {
                urls.Add(match.Value);
            }

            return urls;
        }
    }
}
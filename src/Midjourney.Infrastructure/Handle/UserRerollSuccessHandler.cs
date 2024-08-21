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
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using Serilog;

namespace Midjourney.Infrastructure.Handle
{
    public class UserRerollSuccessHandler : UserMessageHandler
    {
        private const string CONTENT_REGEX_0 = "\\*\\*(.*)\\*\\* - (.*?)<@\\d+> \\((.*?)\\)";
        private const string CONTENT_REGEX_1 = "\\*\\*(.*)\\*\\* - <@\\d+> \\((.*?)\\)";
        private const string CONTENT_REGEX_2 = "\\*\\*(.*)\\*\\* - Variations by <@\\d+> \\((.*?)\\)";
        private const string CONTENT_REGEX_3 = "\\*\\*(.*)\\*\\* - Variations \\(.*?\\) by <@\\d+> \\((.*?)\\)";

        public UserRerollSuccessHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
        : base(discordLoadBalancer, discordHelper)
        {
        }

        public override void Handle(DiscordInstance instance, MessageType messageType, EventData message)
        {
            // 判断消息是否处理过了
            CacheHelper<string, bool>.TryAdd(message.Id.ToString(), false);
            if (CacheHelper<string, bool>.Get(message.Id.ToString()))
            {
                Log.Debug("USER 消息已经处理过了 {@0}", message.Id);
                return;
            }

            if (message.Author == null || message.Author.Bot != true)
            {
                return;
            }

            var content = GetMessageContent(message);

            if (message.Author.Id.ToString() == Constants.MJ_APPLICATION_ID)
            {
                // MJ
                var parseData = GetParseData(content);
                if (messageType == MessageType.CREATE && HasImage(message) && parseData != null)
                {
                    FindAndFinishImageTask(instance, TaskAction.REROLL, parseData.Prompt, message);
                }
            }
            else if (message.Author.Id.ToString() == Constants.NIJI_APPLICATION_ID
                && message.Type == (int)Discord.MessageType.Reply)
            {
                // 特殊处理 -> U -> PAN -> R
                // NIJI
                var parseData = ConvertUtils.ParseContent(content, CONTENT_REGEX_0);
                if (messageType == MessageType.CREATE && HasImage(message) && parseData != null)
                {
                    FindAndFinishImageTask(instance, TaskAction.REROLL, parseData.Prompt, message);
                }
            }
        }

        private ContentParseData GetParseData(string content)
        {
            var parseData = ConvertUtils.ParseContent(content, CONTENT_REGEX_1)
                ?? ConvertUtils.ParseContent(content, CONTENT_REGEX_2)
                ?? ConvertUtils.ParseContent(content, CONTENT_REGEX_3);

            return parseData;
        }
    }
}
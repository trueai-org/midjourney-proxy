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
using Discord.WebSocket;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;

namespace Midjourney.Infrastructure.Handle
{
    public class BotImagineSuccessHandler : BotMessageHandler
    {
        private const string CONTENT_REGEX = "\\*\\*(.*)\\*\\* - <@\\d+> \\((.*?)\\)";

        public BotImagineSuccessHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
            : base(discordLoadBalancer, discordHelper)
        {
        }

        public override int Order() => 101;

        public override void Handle(DiscordInstance instance, MessageType messageType, SocketMessage message)
        {
            var content = GetMessageContent(message);
            var parseData = ConvertUtils.ParseContent(content, CONTENT_REGEX);
            if (messageType == MessageType.CREATE && parseData != null && HasImage(message))
            {
                FindAndFinishImageTask(instance, TaskAction.IMAGINE, parseData.Prompt, message);
            }
        }
    }
}
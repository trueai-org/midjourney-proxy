﻿// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
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

using Midjourney.Infrastructure.LoadBalancer;
using Serilog;

namespace Midjourney.Infrastructure.Handle
{
    /// <summary>
    /// 图生文完成处理程序。
    /// </summary>
    public class UserDescribeSuccessHandler : UserMessageHandler
    {
        public UserDescribeSuccessHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
        : base(discordLoadBalancer, discordHelper)
        {
        }

        public override int Order() => 88888;

        public override void Handle(DiscordInstance instance, MessageType messageType, EventData message)
        {
            // 跳过 Waiting to start 消息
            if (!string.IsNullOrWhiteSpace(message.Content) && message.Content.Contains("(Waiting to start)"))
            {
                return;
            }

            // 判断消息是否处理过了
            CacheHelper<string, bool>.TryAdd(message.Id, false);
            if (CacheHelper<string, bool>.Get(message.Id))
            {
                Log.Debug("USER 消息已经处理过了 {@0}", message.Id);
                return;
            }

            if (messageType == MessageType.CREATE
                && message.Author.Bot == true
                && message.Author.Username.Contains("journey Bot", StringComparison.OrdinalIgnoreCase))
            {
                // 图生文完成
                if (message.Embeds.Count > 0 && !string.IsNullOrWhiteSpace(message.Embeds.FirstOrDefault()?.Image?.Url))
                {
                    var msgId = GetMessageId(message);

                    var task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.MessageId == msgId).FirstOrDefault();
                    if (task == null && !string.IsNullOrWhiteSpace(message.InteractionMetadata?.Id))
                    {
                        task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                        c.InteractionMetadataId == message.InteractionMetadata.Id).FirstOrDefault();
                    }

                    if (task == null )
                    {
                        return;
                    }

                    var imageUrl = message.Embeds.First().Image?.Url;
                    var messageHash = discordHelper.GetMessageHash(imageUrl);

                    var finalPrompt = message.Embeds.First().Description;

                    task.PromptEn = finalPrompt;
                    task.MessageId = msgId;

                    if (!task.MessageIds.Contains(msgId))
                        task.MessageIds.Add(msgId);

                    task.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
                    task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, finalPrompt);
                    task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);

                    task.ImageUrl = imageUrl;
                    task.JobId = messageHash;

                    FinishTask(task, message);
                    task.Awake();
                }
            }
        }
    }
}
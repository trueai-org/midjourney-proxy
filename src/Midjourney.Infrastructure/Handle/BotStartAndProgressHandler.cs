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
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using Serilog;

namespace Midjourney.Infrastructure.Handle
{
    public class BotStartAndProgressHandler : BotMessageHandler
    {
        public BotStartAndProgressHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
            : base(discordLoadBalancer, discordHelper)
        {
        }

        public override int Order() => 90;

        public override void Handle(DiscordInstance instance, MessageType messageType, SocketMessage message)
        {
            // 跳过 Waiting to start 消息
            if (!string.IsNullOrWhiteSpace(message.Content) && message.Content.Contains("(Waiting to start)"))
            {
                return;
            }

            // 判断消息是否处理过了
            CacheHelper<string, bool>.TryAdd(message.Id.ToString(), false);
            if (CacheHelper<string, bool>.Get(message.Id.ToString()))
            {
                Log.Debug("BOT 消息已经处理过了 {@0}", message.Id);
                return;
            }

            var msgId = GetMessageId(message);
            var content = GetMessageContent(message);
            var parseData = ConvertUtils.ParseContent(content);

            if (messageType == MessageType.CREATE && !string.IsNullOrWhiteSpace(msgId))
            {
                // 任务开始
                var task = instance.GetRunningTaskByMessageId(msgId);
                var fullPrompt = GetFullPrompt(message);

                if (task == null && message is SocketUserMessage umsg && umsg != null && umsg.InteractionMetadata?.Id != null)
                {
                    task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.InteractionMetadataId == umsg.InteractionMetadata.Id.ToString()).FirstOrDefault();

                    // 如果通过 meta id 找到任务，但是 full prompt 为空，则更新 full prompt
                    if (task != null && string.IsNullOrWhiteSpace(task.PromptFull))
                    {
                        task.PromptFull = fullPrompt;
                    }
                }

                var botType = GetBotType(message);
                if (task == null)
                {
                    if (!string.IsNullOrWhiteSpace(fullPrompt))
                    {
                        task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && (c.BotType == botType || c.RealBotType == botType) && c.PromptFull == fullPrompt)
                        .OrderBy(c => c.StartTime).FirstOrDefault();
                    }
                }

                if (task == null || task.Status == TaskStatus.SUCCESS || task.Status == TaskStatus.FAILURE)
                {
                    return;
                }

                //task.MessageId = msgId;

                if (!task.MessageIds.Contains(msgId))
                    task.MessageIds.Add(msgId);

                task.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
                task.SetProperty(Constants.TASK_PROPERTY_PROGRESS_MESSAGE_ID, message.Id.ToString());

                // 兼容少数content为空的场景
                if (parseData != null)
                {
                    task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, parseData.Prompt);
                }
                task.Status = TaskStatus.IN_PROGRESS;
                task.Awake();
            }
            else if (messageType == MessageType.UPDATE && parseData != null)
            {
                // 任务进度
                if (parseData.Status == "Stopped")
                    return;

                var task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                c.MessageId == msgId).FirstOrDefault();
                if (task == null && message is SocketUserMessage umsg && umsg != null && umsg.InteractionMetadata?.Id != null)
                {
                    task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                    c.InteractionMetadataId == umsg.InteractionMetadata.Id.ToString()).FirstOrDefault();
                }

                var botType = GetBotType(message);
                if (task == null)
                {
                    var fullPrompt = GetFullPrompt(message);
                    if (!string.IsNullOrWhiteSpace(fullPrompt))
                    {
                        task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && (c.BotType == botType || c.RealBotType == botType) && c.PromptFull == fullPrompt)
                        .OrderBy(c => c.StartTime).FirstOrDefault();
                    }
                }

                if (task == null || task.Status == TaskStatus.SUCCESS || task.Status == TaskStatus.FAILURE)
                {
                    return;
                }

                //task.MessageId = msgId;

                if (!task.MessageIds.Contains(msgId))
                    task.MessageIds.Add(msgId);

                task.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
                task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, parseData.Prompt);
                task.Status = TaskStatus.IN_PROGRESS;
                task.Progress = parseData.Status;

                string imageUrl = GetImageUrl(message);

                // 如果启用保存过程图片
                if (GlobalConfiguration.Setting.EnableSaveIntermediateImage
                    && !string.IsNullOrWhiteSpace(imageUrl))
                {
                    var ff = new FileFetchHelper();
                    var url = ff.FetchFileToStorageAsync(imageUrl).ConfigureAwait(false).GetAwaiter().GetResult();
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        imageUrl = url;
                    }

                    // 必须确保任务仍是 IN_PROGRESS 状态
                    if (task.Status == TaskStatus.IN_PROGRESS)
                    {
                        task.ImageUrl = imageUrl;
                        task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, discordHelper.GetMessageHash(imageUrl));
                        task.Awake();
                    }
                }
                else
                {
                    task.ImageUrl = imageUrl;
                    task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, discordHelper.GetMessageHash(imageUrl));
                    task.Awake();
                }
            }
        }
    }
}
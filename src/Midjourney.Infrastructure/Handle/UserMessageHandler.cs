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
using Discord;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using Serilog;
using Attachment = Midjourney.Infrastructure.Dto.Attachment;

namespace Midjourney.Infrastructure.Handle
{
    /// <summary>
    /// 用户消息处理程序
    /// </summary>
    public abstract class UserMessageHandler
    {
        protected DiscordLoadBalancer discordLoadBalancer;
        protected DiscordHelper discordHelper;

        public UserMessageHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
        {
            this.discordLoadBalancer = discordLoadBalancer;
            this.discordHelper = discordHelper;
        }

        public abstract void Handle(DiscordInstance instance, MessageType messageType, EventData message);

        public virtual int Order() => 100;

        protected string GetMessageContent(EventData message)
        {
            return message.Content;
        }

        protected string GetFullPrompt(EventData message)
        {
            return ConvertUtils.GetFullPrompt(message.Content);
        }

        protected string GetMessageId(EventData message)
        {
            return message.Id.ToString();
        }

        protected string GetInteractionName(EventData message)
        {
            return message?.Interaction?.Name ?? string.Empty;
        }

        protected string GetReferenceMessageId(EventData message)
        {
            return message?.Id.ToString() ?? string.Empty;
        }

        protected EBotType? GetBotType(EventData message)
        {
            var botId = message.Author?.Id.ToString();
            EBotType? botType = null;
            if (botId == Constants.NIJI_APPLICATION_ID)
            {
                botType = EBotType.NIJI_JOURNEY;
            }
            else if (botId == Constants.MJ_APPLICATION_ID)
            {
                botType = EBotType.MID_JOURNEY;
            }

            return botType;
        }

        protected void FindAndFinishImageTask(DiscordInstance instance, TaskAction action, string finalPrompt, EventData message)
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

            if (string.IsNullOrWhiteSpace(finalPrompt))
                return;

            var msgId = GetMessageId(message);
            var fullPrompt = GetFullPrompt(message);

            string imageUrl = GetImageUrl(message);
            string messageHash = discordHelper.GetMessageHash(imageUrl);

            var task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.MessageId == msgId).FirstOrDefault();
            if (task == null && !string.IsNullOrWhiteSpace(message.InteractionMetadata?.Id))
            {
                task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.InteractionMetadataId == message.InteractionMetadata.Id).FirstOrDefault();

                // 如果通过 meta id 找到任务，但是 full prompt 为空，则更新 full prompt
                if (task != null && string.IsNullOrWhiteSpace(task.PromptFull))
                {
                    task.PromptFull = fullPrompt;
                }
            }

            // 如果依然找不到任务，可能是 NIJI 任务
            // 不判断 && botType == EBotType.NIJI_JOURNEY
            var botType = GetBotType(message);

            // 优先使用 full prompt 进行匹配
            if (task == null)
            {
                if (!string.IsNullOrWhiteSpace(fullPrompt))
                {
                    task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && (c.BotType == botType || c.RealBotType == botType) && c.PromptFull == fullPrompt)
                    .OrderBy(c => c.StartTime).FirstOrDefault();
                }
            }

            if (task == null)
            {
                var prompt = finalPrompt.FormatPrompt();

                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    task = instance
                        .FindRunningTask(c =>
                        (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED)
                        && (c.BotType == botType || c.RealBotType == botType)
                        && !string.IsNullOrWhiteSpace(c.PromptEn)
                        && (c.PromptEn.FormatPrompt() == prompt || c.PromptEn.FormatPrompt().EndsWith(prompt) || prompt.StartsWith(c.PromptEn.FormatPrompt())))
                        .OrderBy(c => c.StartTime).FirstOrDefault();
                }
                else
                {
                    // 如果最终提示词为空，则可能是重绘、混图等任务
                    task = instance
                        .FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED)
                        && (c.BotType == botType || c.RealBotType == botType) && c.Action == action)
                        .OrderBy(c => c.StartTime).FirstOrDefault();
                }
            }


            // 如果依然找不到任务，保留 prompt link 进行匹配
            if (task == null)
            {
                var prompt = finalPrompt.FormatPromptParam();
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    task = instance
                            .FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                            (c.BotType == botType || c.RealBotType == botType) && !string.IsNullOrWhiteSpace(c.PromptEn)
                            && (c.PromptEn.FormatPromptParam() == prompt || c.PromptEn.FormatPromptParam().EndsWith(prompt) || prompt.StartsWith(c.PromptEn.FormatPromptParam())))
                            .OrderBy(c => c.StartTime).FirstOrDefault();
                }
            }

            if (task == null || task.Status == TaskStatus.SUCCESS || task.Status == TaskStatus.FAILURE)
            {
                return;
            }

            task.MessageId = msgId;

            if (!task.MessageIds.Contains(msgId))
                task.MessageIds.Add(msgId);

            message.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);

            task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, finalPrompt);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_CONTENT, message.Content);

            task.ImageUrl = imageUrl;
            task.JobId = messageHash;

            FinishTask(task, message);
            task.Awake();
        }

        protected void FinishTask(TaskInfo task, EventData message)
        {
            // 设置图片信息
            var image = message.Attachments?.FirstOrDefault();
            if (task != null && image != null)
            {
                task.Width = image.Width;
                task.Height = image.Height;
                task.Url = image.Url;
                task.ProxyUrl = image.ProxyUrl;
                task.Size = image.Size;
                task.ContentType = image.ContentType;
            }

            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, message.Id);
            task.SetProperty(Constants.TASK_PROPERTY_FLAGS, Convert.ToInt32(message.Flags));
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, discordHelper.GetMessageHash(task.ImageUrl));

            task.Buttons = message.Components.SelectMany(x => x.Components)
                .Select(btn =>
                {
                    return new CustomComponentModel
                    {
                        CustomId = btn.CustomId ?? string.Empty,
                        Emoji = btn.Emoji?.Name ?? string.Empty,
                        Label = btn.Label ?? string.Empty,
                        Style = (int?)btn.Style ?? 0,
                        Type = (int?)btn.Type ?? 0,
                    };
                }).Where(c => c != null && !string.IsNullOrWhiteSpace(c.CustomId)).ToList();

            if (string.IsNullOrWhiteSpace(task.Description))
            {
                task.Description = "Submit success";
            }

            if (string.IsNullOrWhiteSpace(task.FailReason))
            {
                task.FailReason = "";
            }

            if (string.IsNullOrWhiteSpace(task.State))
            {
                task.State = "";
            }

            task.Success();

            // 表示消息已经处理过了
            CacheHelper<string, bool>.AddOrUpdate(message.Id.ToString(), true);

            Log.Debug("由 USER 确认消息处理完成 {@0}", message.Id);
        }

        protected bool HasImage(EventData message)
        {
            return message?.Attachments?.Count > 0;
        }

        protected string GetImageUrl(EventData message)
        {
            if (message?.Attachments?.Count > 0)
            {
                return ReplaceCdnUrl(message.Attachments.FirstOrDefault()?.Url);
            }

            return default;
        }

        protected string ReplaceCdnUrl(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return imageUrl;

            string cdn = discordHelper.GetCdn();
            if (imageUrl.StartsWith(cdn)) return imageUrl;

            return imageUrl.Replace(DiscordHelper.DISCORD_CDN_URL, cdn);
        }
    }
}
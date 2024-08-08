﻿using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using Serilog;

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

        public abstract void Handle(IDiscordInstance instance, MessageType messageType, EventData message);

        public virtual int Order() => 100;

        protected string GetMessageContent(EventData message)
        {
            return message.Content;
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

        protected void FindAndFinishImageTask(IDiscordInstance instance, TaskAction action, string finalPrompt, EventData message)
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

            string imageUrl = GetImageUrl(message);
            string messageHash = discordHelper.GetMessageHash(imageUrl);

            var task = instance.FindRunningTask(c => c.MessageId == msgId).FirstOrDefault();

            if (task == null && message.InteractionMetadata?.Id != null)
            {
                task = instance.FindRunningTask(c => c.InteractionMetadataId == message.InteractionMetadata.Id.ToString()).FirstOrDefault();
            }

            // 如果依然找不到任务，可能是 NIJI 任务
            // 不判断 && botType == EBotType.NIJI_JOURNEY
            var botType = GetBotType(message);
            if (task == null)
            {
                var prompt = finalPrompt.FormatPrompt();

                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    task = instance
                        .FindRunningTask(c => c.BotType == botType && (c.PromptEn.FormatPrompt() == prompt || c.PromptEn.FormatPrompt().EndsWith(prompt) || prompt.StartsWith(c.PromptEn.FormatPrompt())))
                        .OrderBy(c => c.StartTime).FirstOrDefault();
                }
                else
                {
                    // 如果最终提示词为空，则可能是重绘、混图等任务
                    task = instance
                        .FindRunningTask(c => c.BotType == botType && c.Action == action)
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
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, message.Id.ToString());
            task.SetProperty(Constants.TASK_PROPERTY_FLAGS, Convert.ToInt32(message.Flags));
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, discordHelper.GetMessageHash(task.ImageUrl));

            task.Buttons = message.Components.SelectMany(x => x.Components)
                .Select(btn =>
                {
                    return new CustomComponentModel
                    {
                        CustomId = btn.CustomId?.ToString() ?? string.Empty,
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

            var customCdn = discordHelper.GetCustomCdn();
            var toLocal = discordHelper.GetSaveToLocal();

            task.Success(customCdn, toLocal);

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
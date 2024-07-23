using Discord;
using Discord.WebSocket;
using Midjourney.Infrastructure.LoadBalancer;

namespace Midjourney.Infrastructure.Handle
{
    public abstract class MessageHandler
    {
        protected DiscordLoadBalancer discordLoadBalancer;
        protected DiscordHelper discordHelper;

        public MessageHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
        {
            this.discordLoadBalancer = discordLoadBalancer;
            this.discordHelper = discordHelper;
        }

        public abstract void Handle(IDiscordInstance instance, MessageType messageType, SocketMessage message);

        public virtual int Order() => 100;

        protected string GetMessageContent(SocketMessage message)
        {
            return message.Content;
        }

        protected string GetMessageId(SocketMessage message)
        {
            return message.Id.ToString();
        }

        protected string GetInteractionName(SocketMessage message)
        {
            return message?.Interaction?.Name ?? string.Empty;
        }

        protected string GetReferenceMessageId(SocketMessage message)
        {
            return message?.Reference?.MessageId.ToString() ?? string.Empty;
        }

        protected EBotType? GetBotType(SocketMessage message)
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

        protected void FindAndFinishImageTask(IDiscordInstance instance, TaskAction action, string finalPrompt, SocketMessage message)
        {
            if (string.IsNullOrWhiteSpace(finalPrompt))
                return;

            var msgId = GetMessageId(message);

            string imageUrl = GetImageUrl(message);
            string messageHash = discordHelper.GetMessageHash(imageUrl);

            var task = instance.FindRunningTask(c => c.MessageId == msgId).FirstOrDefault();

            if (task == null && message is SocketUserMessage umsg && umsg != null
                && umsg.InteractionMetadata?.Id != null)
            {
                task = instance.FindRunningTask(c => c.InteractionMetadataId == umsg.InteractionMetadata.Id.ToString()).FirstOrDefault();
            }

            // 如果依然找不到任务，可能是 NIJI 任务
            // 不判断 && botType == EBotType.NIJI_JOURNEY
            var botType = GetBotType(message);
            if (task == null)
            {
                task = instance.FindRunningTask(c => c.BotType == botType && (c.PromptEn.FormatPrompt().EndsWith(finalPrompt.FormatPrompt()) || finalPrompt.FormatPrompt().StartsWith(c.PromptEn.FormatPrompt())))
                    .OrderBy(c => c.StartTime).FirstOrDefault();
            }

            if (task == null)
            {
                return;
            }

            task.MessageId = msgId;

            if (!task.MessageIds.Contains(msgId))
                task.MessageIds.Add(msgId);

            task.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
            task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, finalPrompt);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_CONTENT, message.Content);

            task.ImageUrl = imageUrl;
            FinishTask(task, message);
            task.Awake();
        }

        protected void FinishTask(TaskInfo task, SocketMessage message)
        {
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, message.Id.ToString());
            task.SetProperty(Constants.TASK_PROPERTY_FLAGS, Convert.ToInt32(message.Flags));
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, discordHelper.GetMessageHash(task.ImageUrl));

            task.Buttons = message.Components.SelectMany(x => x.Components)
                .Select(c =>
                {
                    if (c is ButtonComponent btn)
                    {
                        return new CustomComponentModel
                        {
                            CustomId = btn.CustomId?.ToString() ?? string.Empty,
                            Emoji = btn.Emote?.Name ?? string.Empty,
                            Label = btn.Label ?? string.Empty,
                            Style = (int?)btn.Style ?? 0,
                            Type = (int?)btn.Type ?? 0,
                        };
                    }
                    return null;
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
        }

        protected bool HasImage(SocketMessage message)
        {
            return message?.Attachments?.Count > 0;
        }

        protected string GetImageUrl(SocketMessage message)
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
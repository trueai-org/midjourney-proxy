using Microsoft.Extensions.Logging;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;

namespace Midjourney.Infrastructure.Handle
{
    public class UserErrorMessageHandler : UserMessageHandler
    {
        private readonly ILogger<UserErrorMessageHandler> _logger;

        public UserErrorMessageHandler(DiscordLoadBalancer discordLoadBalancer,
            DiscordHelper discordHelper, ILogger<UserErrorMessageHandler> logger)
            : base(discordLoadBalancer, discordHelper)
        {
            _logger = logger;
        }

        public override int Order() => 2;

        public override void Handle(IDiscordInstance instance, MessageType messageType, EventData message)
        {
            // 不需要处理，因为已经处理了
            return;

            /*
            var content = GetMessageContent(message);
            var msgId = GetMessageId(message);
            if (content.StartsWith("Failed"))
            {
                var task = instance.GetRunningTaskByMessageId(msgId);

                if (task == null && message.InteractionMetadata?.Id != null)
                {
                    task = instance.FindRunningTask(c => c.InteractionMetadataId == message.InteractionMetadata.Id.ToString()).FirstOrDefault();
                }

                if (task != null)
                {
                    task.MessageId = msgId;

                    if (!task.MessageIds.Contains(msgId))
                        task.MessageIds.Add(msgId);

                    // mj官方异常
                    task.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
                    task.Fail(content);
                    task.Awake();
                }
                return;
            }

            var embedsOptional = message.Embeds;
            if (embedsOptional == null || !embedsOptional.Any())
                return;

            var embed = embedsOptional.FirstOrDefault();
            string title = embed.Title;
            if (string.IsNullOrWhiteSpace(title)) return;

            string description = embed.Description;
            string footerText = embed.Footer?.Text ?? string.Empty;
            var color = embed.Color?.RawValue ?? 0;

            if (color == 16239475)
            {
                _logger.LogWarning($"{instance.GetInstanceId} - MJ警告信息: {title}\n{description}\nfooter: {footerText}");
            }
            else if (color == 16711680)
            {
                _logger.LogError($"{instance.GetInstanceId} - MJ异常信息: {title}\n{description}\nfooter: {footerText}");

                var taskInfo = FindTaskWhenError(instance, messageType, message);
                if (taskInfo == null && message.InteractionMetadata?.Id != null)
                {
                    taskInfo = instance.FindRunningTask(c => c.InteractionMetadataId == message.InteractionMetadata.Id.ToString()).FirstOrDefault();
                }

                if (taskInfo != null)
                {
                    taskInfo.MessageId = msgId;

                    if (!taskInfo.MessageIds.Contains(msgId))
                        taskInfo.MessageIds.Add(msgId);

                    taskInfo.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
                    taskInfo.Fail($"[{title}] {description}");
                    taskInfo.Awake();
                }
            }
            else
            {
                if (embed.Type == Discord.EmbedType.Link || string.IsNullOrWhiteSpace(description))
                    return;

                var taskInfo = FindTaskWhenError(instance, messageType, message);
                if (taskInfo == null && message.InteractionMetadata?.Id != null)
                {
                    taskInfo = instance.FindRunningTask(c => c.InteractionMetadataId == message.InteractionMetadata.Id.ToString()).FirstOrDefault();
                }

                if (taskInfo != null)
                {
                    taskInfo.MessageId = msgId;

                    if (!taskInfo.MessageIds.Contains(msgId))
                        taskInfo.MessageIds.Add(msgId);

                    _logger.LogWarning($"{instance.GetInstanceId} - MJ可能的异常信息: {title}\n{description}\nfooter: {footerText}");

                    taskInfo.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
                    taskInfo.Fail($"[{title}] {description}");
                    taskInfo.Awake();
                }
            }


            */
        }

        private TaskInfo FindTaskWhenError(IDiscordInstance instance, MessageType messageType, EventData message)
        {
            string progressMessageId = messageType switch
            {
                MessageType.CREATE => GetReferenceMessageId(message),
                MessageType.UPDATE => message.Id.ToString(),
                _ => null
            };

            if (string.IsNullOrWhiteSpace(progressMessageId))
                return null;

            return instance.FindRunningTask(c => c.MessageId == progressMessageId).FirstOrDefault();
        }
    }
}
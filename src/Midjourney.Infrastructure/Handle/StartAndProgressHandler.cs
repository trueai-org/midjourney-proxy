using Discord.WebSocket;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;

namespace Midjourney.Infrastructure.Handle
{
    public class StartAndProgressHandler : MessageHandler
    {
        public StartAndProgressHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
            : base(discordLoadBalancer, discordHelper)
        {
        }

        public override int Order() => 90;

        public override void Handle(IDiscordInstance instance, MessageType messageType, SocketMessage message)
        {
            var msgId = GetMessageId(message);
            var content = GetMessageContent(message);
            var parseData = ConvertUtils.ParseContent(content);

            if (messageType == MessageType.CREATE && !string.IsNullOrWhiteSpace(msgId))
            {
                // 任务开始
                var task = instance.GetRunningTaskByMessageId(msgId);
                if (task == null && message is SocketUserMessage umsg && umsg != null && umsg.InteractionMetadata?.Id != null)
                {
                    task = instance.FindRunningTask(c => c.InteractionMetadataId == umsg.InteractionMetadata.Id.ToString()).FirstOrDefault();
                }

                if (task == null)
                {
                    return;
                }

                task.MessageId = msgId;

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


                var task = instance.FindRunningTask(c => c.MessageId == msgId).FirstOrDefault();
                if (task == null && message is SocketUserMessage umsg && umsg != null && umsg.InteractionMetadata?.Id != null)
                {
                    task = instance.FindRunningTask(c => c.InteractionMetadataId == umsg.InteractionMetadata.Id.ToString()).FirstOrDefault();
                }

                if (task == null)
                {
                    return;
                }

                task.MessageId = msgId;

                if (!task.MessageIds.Contains(msgId))
                    task.MessageIds.Add(msgId);

                task.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
                task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, parseData.Prompt);
                task.Status = TaskStatus.IN_PROGRESS;
                task.Progress = parseData.Status;

                string imageUrl = GetImageUrl(message);
                task.ImageUrl = imageUrl;
                task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, discordHelper.GetMessageHash(imageUrl));
                task.Awake();
            }
        }
    }
}
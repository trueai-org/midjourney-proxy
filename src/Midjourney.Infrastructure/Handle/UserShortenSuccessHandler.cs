using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using Serilog;

namespace Midjourney.Infrastructure.Handle
{
    /// <summary>
    /// prompt 分析
    /// </summary>
    public class UserShortenSuccessHandler : UserMessageHandler
    {
        public UserShortenSuccessHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
        : base(discordLoadBalancer, discordHelper)
        {
        }

        public override int Order() => 68888;

        public override void Handle(IDiscordInstance instance, MessageType messageType, EventData message)
        {
            if (message.InteractionMetadata?.Name != "shorten"
                && message.Embeds?.FirstOrDefault()?.Footer?.Text.Contains("Click on a button to imagine one of the shortened prompts") != true)
            {
                return;
            }

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
                // 分析 prompt 完成
                if (message.Embeds.Count > 0)
                {
                    var msgId = GetMessageId(message);

                    var task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.MessageId == msgId).FirstOrDefault();

                    if (task == null && !string.IsNullOrWhiteSpace(message.InteractionMetadata?.Id))
                    {
                        task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                        c.InteractionMetadataId == message.InteractionMetadata.Id).FirstOrDefault();
                    }

                    if (task == null)
                    {
                        return;
                    }

                    var desc = message.Embeds.First().Description;

                    task.Description = desc;
                    task.MessageId = msgId;

                    if (!task.MessageIds.Contains(msgId))
                        task.MessageIds.Add(msgId);

                    task.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
                    task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, desc);

                    FinishTask(task, message);
                    task.Awake();
                }
            }
        }
    }
}
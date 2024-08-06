using Discord.WebSocket;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using Serilog;

namespace Midjourney.Infrastructure.Handle
{
    /// <summary>
    /// 图生文完成处理程序。
    /// </summary>
    public class BotDescribeSuccessHandler : BotMessageHandler
    {
        public BotDescribeSuccessHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
        : base(discordLoadBalancer, discordHelper)
        {
        }

        public override int Order() => 88888;

        public override void Handle(IDiscordInstance instance, MessageType messageType, SocketMessage message)
        {
            // 判断消息是否处理过了
            CacheHelper<string, bool>.TryAdd(message.Id.ToString(), false);
            if (CacheHelper<string, bool>.Get(message.Id.ToString()))
            {
                Log.Debug("BOT 消息已经处理过了 {@0}", message.Id);
                return;
            }

            if (messageType == MessageType.CREATE
                && message.Author.IsBot
                && message.Author.Username.Contains("journey Bot", StringComparison.OrdinalIgnoreCase)
                && message is SocketUserMessage msg && msg != null)
            {
                // 图生文完成
                if (msg.Embeds.Count > 0 && !string.IsNullOrWhiteSpace(msg.Embeds.FirstOrDefault()?.Image?.Url))
                {
                    var msgId = GetMessageId(message);

                    var task = instance.FindRunningTask(c => c.MessageId == msgId).FirstOrDefault();
                    if (task == null && msg.InteractionMetadata?.Id != null)
                    {
                        task = instance.FindRunningTask(c => c.InteractionMetadataId == msg.InteractionMetadata.Id.ToString()).FirstOrDefault();
                    }

                    if (task == null || task.Status == TaskStatus.SUCCESS)
                    {
                        return;
                    }

                    var imageUrl = msg.Embeds.First().Image.Value.Url;
                    var messageHash = discordHelper.GetMessageHash(imageUrl);

                    var finalPrompt = msg.Embeds.First().Description;

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
using Discord.WebSocket;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Handle
{
    public class UpscaleSuccessHandler : MessageHandler
    {
        private const string CONTENT_REGEX_1 = "\\*\\*(.*)\\*\\* - Upscaled \\(.*?\\) by <@\\d+> \\((.*?)\\)";
        private const string CONTENT_REGEX_2 = "\\*\\*(.*)\\*\\* - Upscaled by <@\\d+> \\((.*?)\\)";
        private const string CONTENT_REGEX_U = "\\*\\*(.*)\\*\\* - Image #(\\d) <@\\d+>";

        public UpscaleSuccessHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
            : base(discordLoadBalancer, discordHelper)
        {
        }

        public override void Handle(IDiscordInstance instance, MessageType messageType, SocketMessage message)
        {
            string content = GetMessageContent(message);
            var parseData = GetParseData(content);
            if (messageType == MessageType.CREATE && parseData != null && HasImage(message))
            {
                if (parseData is UContentParseData uContentParseData)
                {
                    FindAndFinishUTask(instance, uContentParseData.Prompt, uContentParseData.Index, message);
                }
                else
                {
                    FindAndFinishImageTask(instance, TaskAction.UPSCALE, parseData.Prompt, message);
                }
            }
        }

        private void FindAndFinishUTask(IDiscordInstance instance, string finalPrompt, int index, SocketMessage message)
        {
            string imageUrl = GetImageUrl(message);
            string messageHash = discordHelper.GetMessageHash(imageUrl);

            var msgId = GetMessageId(message);
            var task = instance.FindRunningTask(c => c.MessageId == msgId).FirstOrDefault();

            if (task == null && message is SocketUserMessage umsg && umsg != null && umsg.InteractionMetadata?.Id != null)
            {
                task = instance.FindRunningTask(c => c.InteractionMetadataId == umsg.InteractionMetadata.Id.ToString()).FirstOrDefault();
            }

            // 如果依然找不到任务，可能是 NIJI 任务
            var botType = GetBotType(message);
            if (task == null && botType == EBotType.NIJI_JOURNEY)
            {
                task = instance.FindRunningTask(c => c.BotType == botType && (c.PromptEn.RemoveWhitespace().EndsWith(finalPrompt.RemoveWhitespace()) || finalPrompt.RemoveWhitespace().StartsWith(c.PromptEn.RemoveWhitespace())))
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

        public static ContentParseData GetParseData(string content)
        {
            var parseData = ConvertUtils.ParseContent(content, CONTENT_REGEX_1)
                ?? ConvertUtils.ParseContent(content, CONTENT_REGEX_2);
            if (parseData != null) return parseData;

            var matcher = Regex.Match(content, CONTENT_REGEX_U);
            if (!matcher.Success) return null;

            var uContentParseData = new UContentParseData
            {
                Prompt = matcher.Groups[1].Value,
                Index = int.Parse(matcher.Groups[2].Value),
                Status = "done"
            };
            return uContentParseData;
        }

        public class UContentParseData : ContentParseData
        {
            public int Index { get; set; }
        }
    }
}
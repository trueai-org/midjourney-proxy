using Discord.WebSocket;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;

namespace Midjourney.Infrastructure.Handle
{
    public class RerollSuccessHandler : MessageHandler
    {
        private const string CONTENT_REGEX_0 = "\\*\\*(.*)\\*\\* - (.*?)<@\\d+> \\((.*?)\\)";
        private const string CONTENT_REGEX_1 = "\\*\\*(.*)\\*\\* - <@\\d+> \\((.*?)\\)";
        private const string CONTENT_REGEX_2 = "\\*\\*(.*)\\*\\* - Variations by <@\\d+> \\((.*?)\\)";
        private const string CONTENT_REGEX_3 = "\\*\\*(.*)\\*\\* - Variations \\(.*?\\) by <@\\d+> \\((.*?)\\)";

        public RerollSuccessHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
        : base(discordLoadBalancer, discordHelper)
        {
        }

        public override void Handle(IDiscordInstance instance, MessageType messageType, SocketMessage message)
        {
            if (message.Author == null || !message.Author.IsBot)
            {
                return;
            }

            var content = GetMessageContent(message);

            if (message.Author.Id.ToString() == Constants.MJ_APPLICATION_ID)
            {
                // MJ
                var parseData = GetParseData(content);
                if (messageType == MessageType.CREATE && HasImage(message) && parseData != null)
                {
                    FindAndFinishImageTask(instance, TaskAction.REROLL, parseData.Prompt, message);
                }
            }
            else if (message.Author.Id.ToString() == Constants.NIJI_APPLICATION_ID && message.Type == Discord.MessageType.Reply)
            {
                // 特殊处理 -> U -> PAN -> R
                // NIJI
                var parseData = ConvertUtils.ParseContent(content, CONTENT_REGEX_0);
                if (messageType == MessageType.CREATE && HasImage(message) && parseData != null)
                {
                    FindAndFinishImageTask(instance, TaskAction.REROLL, parseData.Prompt, message);
                }
            }
        }

        private ContentParseData GetParseData(string content)
        {
            var parseData = ConvertUtils.ParseContent(content, CONTENT_REGEX_1)
                ?? ConvertUtils.ParseContent(content, CONTENT_REGEX_2)
                ?? ConvertUtils.ParseContent(content, CONTENT_REGEX_3);

            return parseData;
        }
    }
}
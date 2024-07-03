using Discord.WebSocket;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;

namespace Midjourney.Infrastructure.Handle
{
    public class RerollSuccessHandler : MessageHandler
    {
        private const string CONTENT_REGEX_1 = "\\*\\*(.*)\\*\\* - <@\\d+> \\((.*?)\\)";
        private const string CONTENT_REGEX_2 = "\\*\\*(.*)\\*\\* - Variations by <@\\d+> \\((.*?)\\)";
        private const string CONTENT_REGEX_3 = "\\*\\*(.*)\\*\\* - Variations \\(.*?\\) by <@\\d+> \\((.*?)\\)";

        public RerollSuccessHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
        : base(discordLoadBalancer, discordHelper)
        {
        }

        public override void Handle(IDiscordInstance instance, MessageType messageType, SocketMessage message)
        {
            string content = GetMessageContent(message);
            var parseData = GetParseData(content);
            if (messageType == MessageType.CREATE && parseData != null && HasImage(message))
            {
                FindAndFinishImageTask(instance, TaskAction.REROLL, parseData.Prompt, message);
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
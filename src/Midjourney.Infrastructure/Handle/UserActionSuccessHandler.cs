using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;

namespace Midjourney.Infrastructure.Handle
{
    public class UserActionSuccessHandler : UserMessageHandler
    {
        private const string CONTENT_REGEX = "\\*\\*(.*)\\*\\* - (.*?)<@\\d+> \\((.*?)\\)";

        public UserActionSuccessHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
        : base(discordLoadBalancer, discordHelper)
        {
        }

        public override int Order() => 99999;

        public override void Handle(IDiscordInstance instance, MessageType messageType, EventData message)
        {
            var content = GetMessageContent(message);
            var parseData = GetParseData(content);
            var parseActionData = GetActionContent(content);

            if (messageType == MessageType.CREATE && HasImage(message)
                && parseData != null && parseActionData != null
                && message.Author.Bot == true && message.Author.Username.Contains("journey Bot", StringComparison.OrdinalIgnoreCase))
            {
                FindAndFinishImageTask(instance, parseActionData.Action, parseData.Prompt, message);
            }
        }

        private ContentParseData GetParseData(string content)
        {
            return ConvertUtils.ParseContent(content, CONTENT_REGEX);
        }

        private ContentActionData GetActionContent(string content)
        {
            return ConvertUtils.ParseActionContent(content);
        }
    }
}
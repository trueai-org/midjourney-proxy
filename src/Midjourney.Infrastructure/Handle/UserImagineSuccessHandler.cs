using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;

namespace Midjourney.Infrastructure.Handle
{
    public class UserImagineSuccessHandler : UserMessageHandler
    {
        private const string CONTENT_REGEX = "\\*\\*(.*)\\*\\* - <@\\d+> \\((.*?)\\)";

        public UserImagineSuccessHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
            : base(discordLoadBalancer, discordHelper)
        {
        }

        public override int Order() => 101;

        public override void Handle(IDiscordInstance instance, MessageType messageType, EventData message)
        {
            var content = GetMessageContent(message);
            var parseData = ConvertUtils.ParseContent(content, CONTENT_REGEX);
            if (messageType == MessageType.CREATE && parseData != null && HasImage(message))
            {
                FindAndFinishImageTask(instance, TaskAction.IMAGINE, parseData.Prompt, message);
            }
        }
    }
}
using Discord.WebSocket;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;

namespace Midjourney.Infrastructure.Handle
{
    public class ImagineSuccessHandler : MessageHandler
    {
        private const string CONTENT_REGEX = "\\*\\*(.*)\\*\\* - <@\\d+> \\((.*?)\\)";

        public ImagineSuccessHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
            : base(discordLoadBalancer, discordHelper)
        {
        }

        public override int Order() => 101;

        public override void Handle(IDiscordInstance instance, MessageType messageType, SocketMessage message)
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
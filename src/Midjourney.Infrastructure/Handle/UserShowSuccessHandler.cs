using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;

namespace Midjourney.Infrastructure.Handle
{
    public class UserShowSuccessHandler : UserMessageHandler
    {
        private const string ACTION_CONTENT_REGEX = "\\*\\*(.*)\\*\\* - (.*?)<@\\d+>";

        private const string IMAGINE_CONTENT_REGEX = "\\*\\*(.*)\\*\\* - <@\\d+>";

        private const string CONTENT_REGEX = "\\*\\*(.*)\\*\\* - <@\\d+>";

        public UserShowSuccessHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
        : base(discordLoadBalancer, discordHelper)
        {
        }

        public override int Order() => 77777;

        public override void Handle(IDiscordInstance instance, MessageType messageType, EventData message)
        {
            var content = GetMessageContent(message);

            var imagineParseData = ConvertUtils.ParseContent(content, IMAGINE_CONTENT_REGEX);
            var actionParseData = ConvertUtils.ParseContent(content, ACTION_CONTENT_REGEX);

            var actionParseData2 = ConvertUtils.ParseActionContent(content);
            var actionParseData3 = ConvertUtils.ParseContent(content, CONTENT_REGEX);

            if (messageType == MessageType.CREATE && HasImage(message)
                && message.Author.Bot == true && message.Author.Username.Contains("journey Bot", StringComparison.OrdinalIgnoreCase)
                && (imagineParseData != null || actionParseData != null || actionParseData2 != null || actionParseData3 != null))
            {
                FindAndFinishImageTask(instance, TaskAction.SHOW, imagineParseData?.Prompt ?? actionParseData?.Prompt ?? actionParseData2?.Prompt ?? actionParseData3?.Prompt, message);
            }
        }
    }
}
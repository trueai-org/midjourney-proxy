using Discord.WebSocket;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Handle
{
    /// <summary>
    /// Blend 事件处理
    /// </summary>
    public class BotBlendSuccessHandler : BotMessageHandler
    {
        private const string CONTENT_REGEX = "\\*\\*(.*)\\*\\* - (.*?)<@\\d+> \\((.*?)\\)";
        private const int MIN_URLS = 2;
        private const int MAX_URLS = 5;

        public BotBlendSuccessHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
        : base(discordLoadBalancer, discordHelper)
        {
        }

        public override int Order() => 99998;

        public override void Handle(IDiscordInstance instance, MessageType messageType, SocketMessage message)
        {
            var content = GetMessageContent(message);
            var parseData = GetParseData(content);
            var urls = ExtractUrls(content);
            var prompt = parseData?.Prompt.FormatPrompt();

            if (messageType == MessageType.CREATE
                && string.IsNullOrWhiteSpace(prompt)
                && HasImage(message)
                && parseData != null
                && urls.Count >= MIN_URLS && urls.Count <= MAX_URLS
                && message.Author.IsBot && message.Author.Username.Contains("journey Bot", StringComparison.OrdinalIgnoreCase))
            {
                FindAndFinishImageTask(instance, TaskAction.BLEND, parseData.Prompt, message);
            }
        }

        private ContentParseData GetParseData(string content)
        {
            return ConvertUtils.ParseContent(content, CONTENT_REGEX);
        }

        private List<string> ExtractUrls(string content)
        {
            var urls = new List<string>();
            var regex = new Regex(@"https?://[^\s>]+");
            var matches = regex.Matches(content);

            foreach (Match match in matches)
            {
                urls.Add(match.Value);
            }

            return urls;
        }
    }
}
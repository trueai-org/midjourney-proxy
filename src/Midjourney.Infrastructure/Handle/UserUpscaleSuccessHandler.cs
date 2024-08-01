using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using Serilog;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Handle
{
    /// <summary>
    /// 用户放大成功处理程序
    /// </summary>
    public class UserUpscaleSuccessHandler : UserMessageHandler
    {
        private const string CONTENT_REGEX_1 = "\\*\\*(.*)\\*\\* - Upscaled \\(.*?\\) by <@\\d+> \\((.*?)\\)";
        private const string CONTENT_REGEX_2 = "\\*\\*(.*)\\*\\* - Upscaled by <@\\d+> \\((.*?)\\)";
        private const string CONTENT_REGEX_U = "\\*\\*(.*)\\*\\* - Image #(\\d) <@\\d+>";

        public UserUpscaleSuccessHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
            : base(discordLoadBalancer, discordHelper)
        {
        }

        public override void Handle(IDiscordInstance instance, MessageType messageType, EventData message)
        {
            // 判断消息是否处理过了
            CacheHelper<string, bool>.TryAdd(message.Id.ToString(), false);
            if (CacheHelper<string, bool>.Get(message.Id.ToString()))
            {
                Log.Debug("USER 消息已经处理过了 {@0}", message.Id);
                return;
            }

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

        private void FindAndFinishUTask(IDiscordInstance instance, string finalPrompt, int index, EventData message)
        {
            string imageUrl = GetImageUrl(message);
            string messageHash = discordHelper.GetMessageHash(imageUrl);

            var msgId = GetMessageId(message);
            var task = instance.FindRunningTask(c => c.MessageId == msgId).FirstOrDefault();

            if (task == null && message.InteractionMetadata?.Id != null)
            {
                task = instance.FindRunningTask(c => c.InteractionMetadataId == message.InteractionMetadata.Id.ToString()).FirstOrDefault();
            }

            // 如果依然找不到任务，可能是 NIJI 任务
            // 不判断 && botType == EBotType.NIJI_JOURNEY
            var botType = GetBotType(message);
            if (task == null)
            {
                var prompt = finalPrompt.FormatPrompt();

                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    task = instance
                        .FindRunningTask(c => c.BotType == botType && !string.IsNullOrWhiteSpace(c.PromptEn)
                        && (c.PromptEn.FormatPrompt() == prompt || c.PromptEn.FormatPrompt().EndsWith(prompt) || prompt.StartsWith(c.PromptEn.FormatPrompt())))
                        .OrderBy(c => c.StartTime).FirstOrDefault();
                }
                else
                {
                    // 放大时，提示词不可为空
                    return;
                }
            }

            if (task == null || task.Status == TaskStatus.SUCCESS)
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
            task.JobId = messageHash;

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
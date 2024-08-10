using Midjourney.Infrastructure.Data;
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
            // 跳过 Waiting to start 消息
            if (!string.IsNullOrWhiteSpace(message.Content) && message.Content.Contains("(Waiting to start)"))
            {
                return;
            }

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

        /// <summary>
        /// 注意处理混图放大的情况，混图放大是没有提示词的
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="finalPrompt"></param>
        /// <param name="index"></param>
        /// <param name="message"></param>
        private void FindAndFinishUTask(IDiscordInstance instance, string finalPrompt, int index, EventData message)
        {
            string imageUrl = GetImageUrl(message);
            string messageHash = discordHelper.GetMessageHash(imageUrl);

            var msgId = GetMessageId(message);
            var task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
            c.MessageId == msgId).FirstOrDefault();

            if (task == null && message.InteractionMetadata?.Id != null)
            {
                task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                c.InteractionMetadataId == message.InteractionMetadata.Id.ToString()).FirstOrDefault();
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
                        .FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                        c.BotType == botType && !string.IsNullOrWhiteSpace(c.PromptEn)
                        && (c.PromptEn.FormatPrompt() == prompt || c.PromptEn.FormatPrompt().EndsWith(prompt) || prompt.StartsWith(c.PromptEn.FormatPrompt())))
                        .OrderBy(c => c.StartTime).FirstOrDefault();
                }

                // 有可能为 kong blend 时
                //else
                //{
                //    // 放大时，提示词不可为空
                //    return;
                //}
            }

            // 如果依然找不到任务，保留 prompt link 进行匹配
            if (task == null)
            {
                var prompt = finalPrompt.FormatPromptParam();
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    task = instance
                            .FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                            c.BotType == botType && !string.IsNullOrWhiteSpace(c.PromptEn)
                            && (c.PromptEn.FormatPromptParam() == prompt || c.PromptEn.FormatPromptParam().EndsWith(prompt) || prompt.StartsWith(c.PromptEn.FormatPromptParam())))
                            .OrderBy(c => c.StartTime).FirstOrDefault();
                }
            }

            if (task == null || task.Status == TaskStatus.SUCCESS || task.Status == TaskStatus.FAILURE)
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
            if (parseData != null)
            {
                return parseData;
            }

            var matcher = Regex.Match(content, CONTENT_REGEX_U);
            if (!matcher.Success)
            {
                return null;
            }

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
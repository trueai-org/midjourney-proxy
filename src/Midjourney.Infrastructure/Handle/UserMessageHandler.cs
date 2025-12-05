// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
// Copyright (C) 2024 trueai.org

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// Additional Terms:
// This software shall not be used for any illegal activities. 
// Users must comply with all applicable laws and regulations,
// particularly those related to image and video processing. 
// The use of this software for any form of illegal face swapping,
// invasion of privacy, or any other unlawful purposes is strictly prohibited. 
// Violation of these terms may result in termination of the license and may subject the violator to legal action.

using Midjourney.Infrastructure.LoadBalancer;
using Serilog;

namespace Midjourney.Infrastructure.Handle
{
    /// <summary>
    /// 用户消息处理程序
    /// </summary>
    public abstract class UserMessageHandler
    {
        protected DiscordLoadBalancer discordLoadBalancer;
        protected DiscordHelper discordHelper;

        public UserMessageHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
        {
            this.discordLoadBalancer = discordLoadBalancer;
            this.discordHelper = discordHelper;
        }

        public abstract void Handle(DiscordInstance instance, MessageType messageType, EventData message);

        public virtual int Order() => 100;

        protected string GetMessageContent(EventData message)
        {
            return message.Content;
        }

        protected string GetFullPrompt(EventData message)
        {
            return ConvertUtils.GetFullPrompt(message.Content);
        }

        protected string GetMessageId(EventData message)
        {
            return message.Id.ToString();
        }

        protected string GetInteractionName(EventData message)
        {
            return message?.Interaction?.Name ?? string.Empty;
        }

        protected string GetReferenceMessageId(EventData message)
        {
            return message?.Id.ToString() ?? string.Empty;
        }

        protected EBotType? GetBotType(EventData message)
        {
            var botId = message.Author?.Id.ToString();
            EBotType? botType = null;
            if (botId == Constants.NIJI_APPLICATION_ID)
            {
                botType = EBotType.NIJI_JOURNEY;
            }
            else if (botId == Constants.MJ_APPLICATION_ID)
            {
                botType = EBotType.MID_JOURNEY;
            }

            return botType;
        }

        protected void FindAndFinishImageTask(DiscordInstance instance, TaskAction action, string finalPrompt, EventData message)
        {
            // 跳过 Waiting to start 消息
            if (!string.IsNullOrWhiteSpace(message.Content) && message.Content.Contains("(Waiting to start)"))
            {
                return;
            }

            // 判断消息是否处理过了
            CacheHelper<string, bool>.TryAdd(message.Id, false);
            if (CacheHelper<string, bool>.Get(message.Id))
            {
                Log.Debug("USER 消息已经处理过了 {@0}", message.Id);
                return;
            }

            if (string.IsNullOrWhiteSpace(finalPrompt))
                return;

            var msgId = GetMessageId(message);
            var fullPrompt = GetFullPrompt(message);

            string imageUrl = GetImageUrl(message);
            string messageHash = discordHelper.GetMessageHash(imageUrl);

            // 优先级1: 通过MessageId匹配
            var task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.MessageId == msgId).FirstOrDefault();

            // 优先级2: 通过InteractionMetadataId匹配
            if (task == null && !string.IsNullOrWhiteSpace(message.InteractionMetadata?.Id))
            {
                task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.InteractionMetadataId == message.InteractionMetadata.Id).FirstOrDefault();

                // 如果通过 meta id 找到任务，但是 full prompt 为空，则更新 full prompt
                if (task != null && string.IsNullOrWhiteSpace(task.PromptFull))
                {
                    task.PromptFull = fullPrompt;
                }
            }

            var botType = GetBotType(message);

            // 优先级3: 通过PromptFull匹配（严格模式：多个候选任务时不匹配）
            if (task == null)
            {
                if (!string.IsNullOrWhiteSpace(fullPrompt))
                {
                    var candidateTasks = instance
                        .FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && (c.BotType == botType || c.RealBotType == botType) && c.PromptFull == fullPrompt)
                        .OrderByDescending(c => c.Status == TaskStatus.SUBMITTED ? 1 : 0)
                        .ThenByDescending(c => c.SubmitTime ?? 0)
                        .ToList();

                    if (candidateTasks.Count > 0)
                    {
                        task = candidateTasks.First();
                        if (candidateTasks.Count > 1)
                        {
                            Log.Warning("USER PromptFull匹配发现多个相同提示词的任务, Count: {Count}, MessageId: {MessageId}, 选择最近提交的任务: {TaskId} (SubmitTime: {SubmitTime})",
                                candidateTasks.Count, msgId, task.Id, task.SubmitTime?.ToDateTimeString() ?? "N/A");
                        }
                    }
                }
            }

            // 优先级4: 通过FormatPrompt匹配（仅精确匹配，避免误匹配）
            if (task == null)
            {
                var prompt = finalPrompt.FormatPrompt();

                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    var candidateTasks = instance
                        .FindRunningTask(c =>
                        (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED)
                        && (c.BotType == botType || c.RealBotType == botType)
                        && !string.IsNullOrWhiteSpace(c.PromptEn)
                        && c.PromptEn.FormatPrompt() == prompt)  // ✅ 仅精确匹配，移除危险的EndsWith/StartsWith
                        .OrderByDescending(c => c.Status == TaskStatus.SUBMITTED ? 1 : 0)
                        .ThenByDescending(c => c.SubmitTime ?? 0)
                        .ToList();

                    if (candidateTasks.Count > 0)
                    {
                        task = candidateTasks.First();
                        if (candidateTasks.Count > 1)
                        {
                            Log.Warning("USER FormatPrompt匹配发现多个相同提示词的任务, Count: {Count}, MessageId: {MessageId}, Prompt: {Prompt}, 选择最近提交的任务: {TaskId} (SubmitTime: {SubmitTime})",
                                candidateTasks.Count, msgId, prompt.Substring(0, Math.Min(50, prompt.Length)), task.Id, task.SubmitTime?.ToDateTimeString() ?? "N/A");
                        }
                    }
                }
            }

            // 优先级5: 通过FormatPromptParam匹配（仅精确匹配，避免误匹配）
            if (task == null)
            {
                var prompt = finalPrompt.FormatPromptParam();
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    var candidateTasks = instance
                            .FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                            (c.BotType == botType || c.RealBotType == botType) && !string.IsNullOrWhiteSpace(c.PromptEn)
                            && c.PromptEn.FormatPromptParam() == prompt)  // ✅ 仅精确匹配，移除危险的EndsWith/StartsWith
                            .OrderByDescending(c => c.Status == TaskStatus.SUBMITTED ? 1 : 0)
                            .ThenByDescending(c => c.SubmitTime ?? 0)
                            .ToList();

                    if (candidateTasks.Count > 0)
                    {
                        task = candidateTasks.First();
                        if (candidateTasks.Count > 1)
                        {
                            Log.Warning("USER FormatPromptParam匹配发现多个相同提示词的任务, Count: {Count}, MessageId: {MessageId}, Prompt: {Prompt}, 选择最近提交的任务: {TaskId} (SubmitTime: {SubmitTime})",
                                candidateTasks.Count, msgId, prompt.Substring(0, Math.Min(50, prompt.Length)), task.Id, task.SubmitTime?.ToDateTimeString() ?? "N/A");
                        }
                    }
                }
            }

            // 优先级6: 改进的空prompt匹配逻辑
            if (task == null)
            {
                // 对于特定的任务类型，当prompt为空时提供更精确的匹配
                if (action == TaskAction.VIDEO || action == TaskAction.VIDEO_EXTEND ||
                    action == TaskAction.BLEND || action == TaskAction.DESCRIBE ||
                    action == TaskAction.ACTION)
                {
                    // 首先尝试通过imageUrl匹配，如果任务的prompt包含相同的URL
                    if (!string.IsNullOrWhiteSpace(imageUrl))
                    {
                        task = instance.FindRunningTask(c =>
                            (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                            (c.BotType == botType || c.RealBotType == botType) &&
                            c.Action == action &&
                            !string.IsNullOrWhiteSpace(c.PromptEn) && c.PromptEn.Contains(imageUrl))
                            .OrderBy(c => c.StartTime).FirstOrDefault();
                    }

                    // 如果通过URL匹配失败，尝试通过messageHash匹配
                    if (task == null && !string.IsNullOrWhiteSpace(messageHash))
                    {
                        task = instance.FindRunningTask(c =>
                            (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                            (c.BotType == botType || c.RealBotType == botType) &&
                            c.Action == action &&
                            (c.JobId == messageHash || c.MessageId == messageHash))
                            .OrderBy(c => c.StartTime).FirstOrDefault();
                    }

                    // 最后才使用原有的模糊匹配，但增加时间窗口限制和唯一性保证
                    if (task == null)
                    {
                        // 缩短时间窗口到2分钟，减少误匹配概率
                        var cutoffTime = DateTimeOffset.Now.AddMinutes(-2).ToUnixTimeMilliseconds();
                        var candidateTasks = instance.FindRunningTask(c =>
                            (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                            (c.BotType == botType || c.RealBotType == botType) &&
                            c.Action == action &&
                            c.StartTime >= cutoffTime)
                            .OrderBy(c => c.StartTime)
                            .ToList();

                        // 如果只有一个候选任务，才认为匹配成功；如果有多个，说明无法准确区分，记录警告
                        if (candidateTasks.Count == 1)
                        {
                            task = candidateTasks.First();
                            Log.Warning("USER 使用模糊匹配找到任务, TaskId: {TaskId}, Action: {Action}, 建议优化任务提交时的唯一标识",
                                task.Id, action);
                        }
                        else if (candidateTasks.Count > 1)
                        {
                            Log.Error("USER 发现多个候选任务无法区分, Count: {Count}, Action: {Action}, MessageId: {MessageId}, 可能导致任务混淆！",
                                candidateTasks.Count, action, msgId);
                            // 不匹配任何任务，避免错误匹配
                            task = null;
                        }
                    }
                }
                else
                {
                    // 其他任务类型使用原有逻辑，但增加时间窗口限制和唯一性保证
                    var cutoffTime = DateTimeOffset.Now.AddMinutes(-2).ToUnixTimeMilliseconds();
                    var candidateTasks = instance.FindRunningTask(c =>
                        (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                        (c.BotType == botType || c.RealBotType == botType) &&
                        c.Action == action &&
                        c.StartTime >= cutoffTime)
                        .OrderBy(c => c.StartTime)
                        .ToList();

                    // 如果只有一个候选任务，才认为匹配成功；如果有多个，说明无法准确区分，记录警告
                    if (candidateTasks.Count == 1)
                    {
                        task = candidateTasks.First();
                        Log.Warning("USER 使用模糊匹配找到任务, TaskId: {TaskId}, Action: {Action}, 建议优化任务提交时的唯一标识",
                            task.Id, action);
                    }
                    else if (candidateTasks.Count > 1)
                    {
                        Log.Error("USER 发现多个候选任务无法区分, Count: {Count}, Action: {Action}, MessageId: {MessageId}, 可能导致任务混淆！",
                            candidateTasks.Count, action, msgId);
                        // 不匹配任务，避免错误匹配
                        task = null;
                    }
                }
            }

            if (task == null || task.Status == TaskStatus.SUCCESS || task.Status == TaskStatus.FAILURE)
            {
                return;
            }

            task.MessageId = msgId;

            if (!task.MessageIds.Contains(msgId))
                task.MessageIds.Add(msgId);

            message.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);

            task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, finalPrompt);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_CONTENT, message.Content);

            task.ImageUrl = imageUrl;
            task.JobId = messageHash;

            FinishTask(task, message);
            task.Awake();
        }

        /// <summary>
        /// 检查并触发视频扩展操作
        /// </summary>
        protected void CheckAndTriggerVideoExtend(DiscordInstance instance, TaskInfo upscaleTask, string messageHash)
        {
            try
            {
                // 检查任务是否有视频扩展标记
                var videoExtendTargetTaskId = upscaleTask.GetProperty<string>(Constants.TASK_PROPERTY_VIDEO_EXTEND_TARGET_TASK_ID, default);
                if (string.IsNullOrWhiteSpace(videoExtendTargetTaskId))
                {
                    return;
                }

                // 获取扩展相关参数
                var extendPrompt = upscaleTask.GetProperty<string>(Constants.TASK_PROPERTY_VIDEO_EXTEND_PROMPT, default);
                var extendMotion = upscaleTask.GetProperty<string>(Constants.TASK_PROPERTY_VIDEO_EXTEND_MOTION, default);
                var extendIndex = upscaleTask.GetProperty<int>(Constants.TASK_PROPERTY_VIDEO_EXTEND_INDEX, 1);

                if (string.IsNullOrWhiteSpace(extendMotion))
                {
                    extendMotion = "high";
                }

                Log.Information("🎬 视频放大完成，准备触发扩展操作: UpscaleTaskId={UpscaleTaskId}, TargetTaskId={TargetTaskId}, Motion={Motion}, Index={Index}, ButtonsCount={ButtonsCount}",
                    upscaleTask.Id, videoExtendTargetTaskId, extendMotion, extendIndex, upscaleTask.Buttons?.Count ?? 0);

                // 🎯 关键改进：从 Buttons 中查找正确的 extend customId，而不是自己构建
                // 因为 upscale 后的 JobId 可能不是正确的 hash 值
                var extendButton = upscaleTask.Buttons?.FirstOrDefault(x =>
                    x.CustomId?.Contains($"animate_{extendMotion}_extend") == true);

                if (extendButton == null || string.IsNullOrWhiteSpace(extendButton.CustomId))
                {
                    Log.Warning("❌ 找不到 extend 按钮: UpscaleTaskId={TaskId}, Motion={Motion}, Buttons={@Buttons}",
                        upscaleTask.Id, extendMotion, upscaleTask.Buttons);

                    // 标记任务失败
                    upscaleTask.Status = TaskStatus.FAILURE;
                    upscaleTask.FailReason = $"找不到 extend 按钮 (motion: {extendMotion})";
                    DbHelper.Instance.TaskStore.Update(upscaleTask);
                    upscaleTask.Awake();
                    return;
                }

                var extendCustomId = extendButton.CustomId;

                // 异步触发扩展操作
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 等待 1.5 秒，确保消息已完全处理
                        await Task.Delay(1500);

                        // 创建一个新的 nonce 用于 extend 操作
                        var extendNonce = SnowFlake.NextId();

                        // 更新当前任务（upscaleTask 就是用户看到的任务）
                        upscaleTask.Nonce = extendNonce;
                        upscaleTask.Status = TaskStatus.SUBMITTED;
                        upscaleTask.Action = TaskAction.VIDEO;
                        upscaleTask.Description = "/video extend";
                        upscaleTask.Progress = "0%";
                        upscaleTask.PromptEn = extendPrompt;
                        upscaleTask.RemixAutoSubmit = instance.Account.RemixAutoSubmit && (instance.Account.MjRemixOn || instance.Account.NijiRemixOn);

                        upscaleTask.SetProperty(Constants.TASK_PROPERTY_CUSTOM_ID, extendCustomId);
                        upscaleTask.SetProperty(Constants.TASK_PROPERTY_NONCE, extendNonce);
                        upscaleTask.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, upscaleTask.MessageId);
                        upscaleTask.SetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_PROMPT, extendPrompt);

                        // 清除 video extend 标记，避免任务完成时再次触发
                        upscaleTask.SetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_TARGET_TASK_ID, null);
                        upscaleTask.SetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_MOTION, null);
                        upscaleTask.SetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_INDEX, null);

                        // 如果开启了 remix 自动提交，标记任务状态
                        if (upscaleTask.RemixAutoSubmit)
                        {
                            upscaleTask.RemixModaling = true;
                        }

                        // 调用 Action 接口触发扩展
                        var result = await instance.ActionAsync(upscaleTask.MessageId, extendCustomId,
                            upscaleTask.GetProperty<int>(Constants.TASK_PROPERTY_FLAGS, 0),
                            extendNonce, upscaleTask);

                        if (result.Code == ReturnCode.SUCCESS)
                        {
                            Log.Information("视频扩展 extend action 触发成功: TaskId={TaskId}", upscaleTask.Id);
                        }
                        else
                        {
                            Log.Error("视频扩展 extend action 触发失败: TaskId={TaskId}, Error={Error}",
                                upscaleTask.Id, result.Description);
                            upscaleTask.Fail(result.Description);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "执行视频扩展操作时发生异常: UpscaleTaskId={UpscaleTaskId}", upscaleTask.Id);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "检查视频扩展时发生异常: UpscaleTaskId={UpscaleTaskId}", upscaleTask.Id);
            }
        }

        protected void FinishTask(TaskInfo task, EventData message)
        {
            // 设置图片信息
            var image = message.Attachments?.FirstOrDefault();
            if (task != null && image != null)
            {
                task.Width = image.Width;
                task.Height = image.Height;
                task.Url = image.Url;
                task.ProxyUrl = image.ProxyUrl;
                task.Size = image.Size;
                task.ContentType = image.ContentType;
            }

            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, message.Id);
            task.SetProperty(Constants.TASK_PROPERTY_FLAGS, Convert.ToInt32(message.Flags));
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, discordHelper.GetMessageHash(task.ImageUrl));

            task.Buttons = message.Components.SelectMany(x => x.Components)
                .Select(btn =>
                {
                    return new CustomComponentModel
                    {
                        CustomId = btn.CustomId ?? string.Empty,
                        Emoji = btn.Emoji?.Name ?? string.Empty,
                        Label = btn.Label ?? string.Empty,
                        Style = (int?)btn.Style ?? 0,
                        Type = (int?)btn.Type ?? 0,
                    };
                }).Where(c => c != null && !string.IsNullOrWhiteSpace(c.CustomId)).ToList();

            if (string.IsNullOrWhiteSpace(task.Description))
            {
                task.Description = "Submit success";
            }

            if (string.IsNullOrWhiteSpace(task.FailReason))
            {
                task.FailReason = "";
            }

            if (string.IsNullOrWhiteSpace(task.State))
            {
                task.State = "";
            }

            task.Success();

            // 表示消息已经处理过了
            CacheHelper<string, bool>.AddOrUpdate(message.Id.ToString(), true);

            Log.Debug("由 USER 确认消息处理完成 {@0}", message.Id);
        }

        protected bool HasImage(EventData message)
        {
            return message?.Attachments?.Count > 0;
        }

        protected string GetImageUrl(EventData message)
        {
            if (message?.Attachments?.Count > 0)
            {
                return ReplaceCdnUrl(message.Attachments.FirstOrDefault()?.Url);
            }

            return default;
        }

        protected string ReplaceCdnUrl(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl)) return imageUrl;

            string cdn = discordHelper.GetCdn();
            if (imageUrl.StartsWith(cdn)) return imageUrl;

            return imageUrl.Replace(DiscordHelper.DISCORD_CDN_URL, cdn);
        }
    }
}
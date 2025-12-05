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
    public class UserStartAndProgressHandler : UserMessageHandler
    {
        public UserStartAndProgressHandler(DiscordLoadBalancer discordLoadBalancer, DiscordHelper discordHelper)
            : base(discordLoadBalancer, discordHelper)
        {
        }

        public override int Order() => 90;

        public override void Handle(DiscordInstance instance, MessageType messageType, EventData message)
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

            var msgId = GetMessageId(message);
            var content = GetMessageContent(message);
            var parseData = ConvertUtils.ParseContent(content);


            // 放宽进入条件：CREATE 即使缺少 msgId，但有 InteractionMetadata.Id 也尝试强键绑定
            if (messageType == MessageType.CREATE && (!string.IsNullOrWhiteSpace(msgId) || !string.IsNullOrWhiteSpace(message.InteractionMetadata?.Id)))
            {
                var fullPrompt = GetFullPrompt(message);

                // 任务开始
                TaskInfo task = null;
                // 优先用 InteractionMetadataId 命中（更稳定）
                if (!string.IsNullOrWhiteSpace(message.InteractionMetadata?.Id))
                {
                    task = instance.FindRunningTask(c =>
                        (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED)
                        && c.InteractionMetadataId == message.InteractionMetadata.Id).FirstOrDefault();

                    // 如果通过 meta id 找到任务，但是 full prompt 为空，则更新 full prompt
                    if (task != null && string.IsNullOrWhiteSpace(task.PromptFull))
                    {
                        task.PromptFull = fullPrompt;
                    }
                }
                // 其次再尝试用 MessageId 命中（当 msgId 存在时）
                if (task == null && !string.IsNullOrWhiteSpace(msgId))
                {
                    task = instance.GetRunningTaskByMessageId(msgId);
                }

                var botType = GetBotType(message);

                // 🔧 增强容错：如果强键匹配失败，尝试基于 PromptFull 的回退匹配（仅当任务状态为 SUBMITTED 且等待时间较长时）
                if (task == null && !string.IsNullOrWhiteSpace(fullPrompt))
                {
                    // 只对 SUBMITTED 状态的任务进行回退匹配，避免并发串单
                    var fallbackTasks = instance.FindRunningTask(c =>
                        c.Status == TaskStatus.SUBMITTED
                        && (c.BotType == botType || c.RealBotType == botType)
                        && c.PromptFull == fullPrompt)
                        .OrderBy(c => c.StartTime)
                        .ToList();

                    if (fallbackTasks.Count == 1)
                    {
                        task = fallbackTasks.First();
                        Log.Warning("⚠️ Start: 通过 PromptFull 回退匹配到任务 {TaskId}, msgId={MsgId}, metaId={MetaId}, 可能强键未正确设置",
                            task.Id, msgId, message.InteractionMetadata?.Id);
                    }
                    else if (fallbackTasks.Count > 1)
                    {
                        Log.Warning("⚠️ Start: 通过 PromptFull 匹配到多个任务 ({Count}个), 忽略回退匹配以避免串单。msgId={MsgId}, metaId={MetaId}",
                            fallbackTasks.Count, msgId, message.InteractionMetadata?.Id);
                    }
                }

                if (task == null || task.Status == TaskStatus.SUCCESS || task.Status == TaskStatus.FAILURE)
                {
                    if (task == null)
                    {
                        Log.Debug("Start: 未通过强键命中任务，忽略。msgId={MsgId}, metaId={MetaId}", msgId, message.InteractionMetadata?.Id);
                    }
                    return;
                }

                //task.MessageId = msgId;

                if (!string.IsNullOrWhiteSpace(msgId) && !task.MessageIds.Contains(msgId))
                    task.MessageIds.Add(msgId);

                task.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
                task.SetProperty(Constants.TASK_PROPERTY_PROGRESS_MESSAGE_ID, message.Id.ToString());

                // 兼容少数content为空的场景
                if (parseData != null)
                {
                    task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, parseData.Prompt);
                }
                task.Status = TaskStatus.IN_PROGRESS;
                task.Awake();
            }
            else if (messageType == MessageType.UPDATE && parseData != null)
            {
                // 任务进度
                if (parseData.Status == "Stopped")
                    return;

                var fullPrompt = GetFullPrompt(message);

                // 先用 InteractionMetadataId 命中，再退到 MessageId
                TaskInfo task = null;
                if (!string.IsNullOrWhiteSpace(message.InteractionMetadata?.Id))
                {
                    task = instance.FindRunningTask(c =>
                        (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED)
                        && c.InteractionMetadataId == message.InteractionMetadata.Id).FirstOrDefault();

                    // 如果通过 meta id 找到任务，但是 full prompt 为空，则更新 full prompt
                    if (task != null && string.IsNullOrWhiteSpace(task.PromptFull))
                    {
                        task.PromptFull = fullPrompt;
                    }
                }
                if (task == null)
                {
                    task = instance.FindRunningTask(c =>
                        (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED)
                        && c.MessageId == msgId).FirstOrDefault();
                }

                var botType = GetBotType(message);

                // 🔧 增强容错：如果强键匹配失败，尝试基于 PromptFull 的回退匹配（仅当任务状态为 SUBMITTED 且等待时间较长时）
                if (task == null && !string.IsNullOrWhiteSpace(fullPrompt))
                {
                    // 只对 SUBMITTED 状态的任务进行回退匹配，避免并发串单
                    var fallbackTasks = instance.FindRunningTask(c =>
                        c.Status == TaskStatus.SUBMITTED
                        && (c.BotType == botType || c.RealBotType == botType)
                        && c.PromptFull == fullPrompt)
                        .OrderBy(c => c.StartTime)
                        .ToList();

                    if (fallbackTasks.Count == 1)
                    {
                        task = fallbackTasks.First();
                        Log.Warning("⚠️ Progress: 通过 PromptFull 回退匹配到任务 {TaskId}, msgId={MsgId}, metaId={MetaId}, 可能强键未正确设置",
                            task.Id, msgId, message.InteractionMetadata?.Id);
                    }
                    else if (fallbackTasks.Count > 1)
                    {
                        Log.Warning("⚠️ Progress: 通过 PromptFull 匹配到多个任务 ({Count}个), 忽略回退匹配以避免串单。msgId={MsgId}, metaId={MetaId}",
                            fallbackTasks.Count, msgId, message.InteractionMetadata?.Id);
                    }
                }

                if (task == null || task.Status == TaskStatus.SUCCESS || task.Status == TaskStatus.FAILURE)
                {
                    if (task == null)
                    {
                        Log.Debug("Progress: 未通过强键命中任务，忽略。msgId={MsgId}, metaId={MetaId}", msgId, message.InteractionMetadata?.Id);
                    }
                    return;
                }

                //task.MessageId = msgId;

                if (!string.IsNullOrWhiteSpace(msgId) && !task.MessageIds.Contains(msgId))
                    task.MessageIds.Add(msgId);

                task.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
                task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, parseData.Prompt);
                task.Status = TaskStatus.IN_PROGRESS;
                task.Progress = parseData.Status;

                string imageUrl = GetImageUrl(message);

                // 如果启用保存过程图片
                if (GlobalConfiguration.Setting.EnableSaveIntermediateImage
                    && !string.IsNullOrWhiteSpace(imageUrl))
                {
                    var ff = new FileFetchHelper();
                    var url = ff.FetchFileToStorageAsync(imageUrl).ConfigureAwait(false).GetAwaiter().GetResult();
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        imageUrl = url;
                    }

                    // 必须确保任务仍是 IN_PROGRESS 状态
                    if (task.Status == TaskStatus.IN_PROGRESS)
                    {
                        task.ImageUrl = imageUrl;
                        task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, discordHelper.GetMessageHash(imageUrl));
                        task.Awake();
                    }
                }
                else
                {
                    task.ImageUrl = imageUrl;
                    task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, discordHelper.GetMessageHash(imageUrl));
                    task.Awake();
                }
            }
        }
    }
}
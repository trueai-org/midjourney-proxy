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

using Microsoft.Extensions.Caching.Memory;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using Serilog;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// 任务服务实现类，处理任务的具体操作
    /// </summary>
    public class TaskService : ITaskService
    {
        private readonly IMemoryCache _memoryCache;
        private readonly ITaskStoreService _taskStoreService;
        private readonly DiscordLoadBalancer _discordLoadBalancer;

        public TaskService(ITaskStoreService taskStoreService, DiscordLoadBalancer discordLoadBalancer, IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
            _taskStoreService = taskStoreService;
            _discordLoadBalancer = discordLoadBalancer;
        }

        /// <summary>
        /// 获取领域缓存
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, HashSet<string>> GetDomainCache()
        {
            return _memoryCache.GetOrCreate("domains", c =>
            {
                c.SetAbsoluteExpiration(TimeSpan.FromMinutes(30));
                var list = DbHelper.Instance.DomainStore.GetAll().Where(c => c.Enable);

                var dict = new Dictionary<string, HashSet<string>>();
                foreach (var item in list)
                {
                    var keywords = item.Keywords.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().ToList();
                    dict[item.Id] = new HashSet<string>(keywords);
                }

                return dict;
            });
        }

        /// <summary>
        /// 清除领域缓存
        /// </summary>
        public void ClearDomainCache()
        {
            _memoryCache.Remove("domains");
        }

        /// <summary>
        /// 违规词缓存
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, HashSet<string>> GetBannedWordsCache()
        {
            return _memoryCache.GetOrCreate("bannedWords", c =>
            {
                c.SetAbsoluteExpiration(TimeSpan.FromMinutes(30));
                var list = DbHelper.Instance.BannedWordStore.GetAll().Where(c => c.Enable);

                var dict = new Dictionary<string, HashSet<string>>();
                foreach (var item in list)
                {
                    var keywords = item.Keywords.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().ToList();
                    dict[item.Id] = new HashSet<string>(keywords);
                }

                return dict;
            });
        }

        /// <summary>
        /// 清除违规词缓存
        /// </summary>
        public void ClearBannedWordsCache()
        {
            _memoryCache.Remove("bannedWords");
        }

        /// <summary>
        /// 验证违规词
        /// </summary>
        /// <param name="promptEn"></param>
        /// <exception cref="BannedPromptException"></exception>
        public void CheckBanned(string promptEn)
        {
            var finalPromptEn = promptEn.ToLower(CultureInfo.InvariantCulture);

            var dic = GetBannedWordsCache();
            foreach (var item in dic)
            {
                foreach (string word in item.Value)
                {
                    var regex = new Regex($"\\b{Regex.Escape(word)}\\b", RegexOptions.IgnoreCase);
                    var match = regex.Match(finalPromptEn);
                    if (match.Success)
                    {
                        int index = finalPromptEn.IndexOf(word, StringComparison.OrdinalIgnoreCase);

                        throw new BannedPromptException(promptEn.Substring(index, word.Length));
                    }
                }
            }
        }

        /// <summary>
        /// 提交 imagine 任务。
        /// </summary>
        /// <param name="info"></param>
        /// <param name="dataUrls"></param>
        /// <returns></returns>
        public SubmitResultVO SubmitImagine(TaskInfo info, List<DataUrl> dataUrls)
        {
            // 判断是否开启垂直领域
            var domainIds = new List<string>();
            var isDomain = GlobalConfiguration.Setting.IsVerticalDomain;
            if (isDomain)
            {
                // 对 Promat 分割为单个单词
                // 以 ',' ' ' '.' '-' 为分隔符
                // 并且过滤为空的字符串
                var prompts = info.Prompt.Split(new char[] { ',', ' ', '.', '-' })
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c?.Trim()?.ToLower())
                    .Distinct().ToList();

                var domains = GetDomainCache();
                foreach (var prompt in prompts)
                {
                    foreach (var domain in domains)
                    {
                        if (domain.Value.Contains(prompt) || domain.Value.Contains($"{prompt}s"))
                        {
                            domainIds.Add(domain.Key);
                        }
                    }
                }

                // 如果没有找到领域，则不使用领域账号
                if (domainIds.Count == 0)
                {
                    isDomain = false;
                }
            }

            var instance = _discordLoadBalancer.ChooseInstance(info.AccountFilter,
                isNewTask: true,
                botType: info.RealBotType ?? info.BotType,
                isDomain: isDomain,
                domainIds: domainIds);

            if (instance == null || !instance.Account.IsAcceptNewTask)
            {
                if (isDomain && domainIds.Count > 0)
                {
                    // 说明没有获取到符合领域的账号，再次获取不带领域的账号
                    instance = _discordLoadBalancer.ChooseInstance(info.AccountFilter,
                        isNewTask: true,
                        botType: info.RealBotType ?? info.BotType,
                        isDomain: false);
                }
            }

            if (instance == null || !instance.Account.IsAcceptNewTask)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            info.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.ChannelId);
            info.InstanceId = instance.ChannelId;

            return instance.SubmitTaskAsync(info, async () =>
            {
                var imageUrls = new List<string>();
                foreach (var dataUrl in dataUrls)
                {
                    var taskFileName = $"{info.Id}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                    var uploadResult = await instance.UploadAsync(taskFileName, dataUrl);
                    if (uploadResult.Code != ReturnCode.SUCCESS)
                    {
                        return Message.Of(uploadResult.Code, uploadResult.Description);
                    }

                    if (uploadResult.Description.StartsWith("http"))
                    {
                        imageUrls.Add(uploadResult.Description);
                    }
                    else
                    {
                        var finalFileName = uploadResult.Description;
                        var sendImageResult = await instance.SendImageMessageAsync("upload image: " + finalFileName, finalFileName);
                        if (sendImageResult.Code != ReturnCode.SUCCESS)
                        {
                            return Message.Of(sendImageResult.Code, sendImageResult.Description);
                        }
                        imageUrls.Add(sendImageResult.Description);
                    }
                }
                if (imageUrls.Any())
                {
                    info.Prompt = string.Join(" ", imageUrls) + " " + info.Prompt;
                    info.PromptEn = string.Join(" ", imageUrls) + " " + info.PromptEn;
                    info.Description = "/imagine " + info.Prompt;
                    _taskStoreService.Save(info);
                }
                return await instance.ImagineAsync(info, info.PromptEn,
                    info.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default));
            });
        }

        /// <summary>
        /// 提交 show 任务
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        public SubmitResultVO ShowImagine(TaskInfo info)
        {
            var instance = _discordLoadBalancer.ChooseInstance(info.AccountFilter,
                botType:info.RealBotType ?? info.BotType);

            if (instance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            info.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.ChannelId);
            info.InstanceId = instance.ChannelId;

            return instance.SubmitTaskAsync(info, async () =>
            {
                return await instance.ShowAsync(info.JobId,
                    info.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default),info.RealBotType ?? info.BotType);
            });
        }

        public SubmitResultVO SubmitUpscale(TaskInfo task, string targetMessageId, string targetMessageHash, int index, int messageFlags)
        {
            var instanceId = task.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default);
            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(instanceId);
            if (discordInstance == null || !discordInstance.IsAlive)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "账号不可用: " + instanceId);
            }
            return discordInstance.SubmitTaskAsync(task, async () =>
                await discordInstance.UpscaleAsync(targetMessageId, index, targetMessageHash, messageFlags,
                task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.RealBotType ?? task.BotType));
        }

        public SubmitResultVO SubmitVariation(TaskInfo task, string targetMessageId, string targetMessageHash, int index, int messageFlags)
        {
            var instanceId = task.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default);
            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(instanceId);
            if (discordInstance == null || !discordInstance.IsAlive)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "账号不可用: " + instanceId);
            }
            return discordInstance.SubmitTaskAsync(task, async () =>
                await discordInstance.VariationAsync(targetMessageId, index, targetMessageHash, messageFlags,
                task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.RealBotType ?? task.BotType));
        }

        /// <summary>
        /// 提交重新生成任务。
        /// </summary>
        /// <param name="task"></param>
        /// <param name="targetMessageId"></param>
        /// <param name="targetMessageHash"></param>
        /// <param name="messageFlags"></param>
        /// <returns></returns>
        public SubmitResultVO SubmitReroll(TaskInfo task, string targetMessageId, string targetMessageHash, int messageFlags)
        {
            var instanceId = task.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default);
            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(instanceId);
            if (discordInstance == null || !discordInstance.IsAlive)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "账号不可用: " + instanceId);
            }
            return discordInstance.SubmitTaskAsync(task, async () =>
                await discordInstance.RerollAsync(targetMessageId, targetMessageHash, messageFlags,
                task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.RealBotType ?? task.BotType));
        }

        /// <summary>
        /// 提交Describe任务
        /// </summary>
        /// <param name="task"></param>
        /// <param name="dataUrl"></param>
        /// <returns></returns>
        public SubmitResultVO SubmitDescribe(TaskInfo task, DataUrl dataUrl)
        {
            var discordInstance = _discordLoadBalancer.ChooseInstance(task.AccountFilter,
                isNewTask: true,
                botType: task.RealBotType ?? task.BotType,
                describe: true);

            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);
            task.InstanceId = discordInstance.ChannelId;

            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                var uploadResult = await discordInstance.UploadAsync(taskFileName, dataUrl);
                if (uploadResult.Code != ReturnCode.SUCCESS)
                {
                    return Message.Of(uploadResult.Code, uploadResult.Description);
                }

                var link = "";
                if (uploadResult.Description.StartsWith("http"))
                {
                    link = uploadResult.Description;
                }
                else
                {
                    var finalFileName = uploadResult.Description;
                    var sendImageResult = await discordInstance.SendImageMessageAsync("upload image: " + finalFileName, finalFileName);
                    if (sendImageResult.Code != ReturnCode.SUCCESS)
                    {
                        return Message.Of(sendImageResult.Code, sendImageResult.Description);
                    }
                    link = sendImageResult.Description;
                }

                //var taskFileName = $"{task.Id}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                //var uploadResult = await discordInstance.UploadAsync(taskFileName, dataUrl);
                //if (uploadResult.Code != ReturnCode.SUCCESS)
                //{
                //    return Message.Of(uploadResult.Code, uploadResult.Description);
                //}
                //var finalFileName = uploadResult.Description;
                //return await discordInstance.DescribeAsync(finalFileName, task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default),
                //  task.RealBotType ?? task.BotType);

                return await discordInstance.DescribeByLinkAsync(link, task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default),
                   task.RealBotType ?? task.BotType);
            });
        }

        /// <summary>
        /// 上传一个较长的提示词，mj 可以返回一组简要的提示词
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        public SubmitResultVO ShortenAsync(TaskInfo task)
        {
            var discordInstance = _discordLoadBalancer.ChooseInstance(task.AccountFilter,
                isNewTask: true,
                botType: task.RealBotType ?? task.BotType,
                shorten: true);

            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);
            task.InstanceId = discordInstance.ChannelId;

            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                return await discordInstance.ShortenAsync(task, task.PromptEn, task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.RealBotType ?? task.BotType);
            });
        }

        /// <summary>
        /// 提交混合任务
        /// </summary>
        /// <param name="task"></param>
        /// <param name="dataUrls"></param>
        /// <param name="dimensions"></param>
        /// <returns></returns>
        public SubmitResultVO SubmitBlend(TaskInfo task, List<DataUrl> dataUrls, BlendDimensions dimensions)
        {
            var discordInstance = _discordLoadBalancer.ChooseInstance(task.AccountFilter,
                isNewTask: true,
                botType: task.RealBotType ?? task.BotType,
                blend: true);

            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }
            task.InstanceId = discordInstance.ChannelId;
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);
            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                var finalFileNames = new List<string>();
                foreach (var dataUrl in dataUrls)
                {
                    var taskFileName = $"{task.Id}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";

                    var uploadResult = await discordInstance.UploadAsync(taskFileName, dataUrl, useDiscordUpload: true);
                    if (uploadResult.Code != ReturnCode.SUCCESS)
                    {
                        return Message.Of(uploadResult.Code, uploadResult.Description);
                    }

                    finalFileNames.Add(uploadResult.Description);
                }
                return await discordInstance.BlendAsync(finalFileNames, dimensions,
                    task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.RealBotType ?? task.BotType);
            });
        }

        /// <summary>
        /// 执行动作
        /// </summary>
        /// <param name="task"></param>
        /// <param name="submitAction"></param>
        /// <returns></returns>
        public SubmitResultVO SubmitAction(TaskInfo task, SubmitActionDTO submitAction)
        {
            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(task.SubInstanceId ?? task.InstanceId);
            if (discordInstance == null)
            {
                // 如果主实例没有找子实例
                var ids = new List<string>();
                var list = _discordLoadBalancer.GetAliveInstances().ToList();
                foreach (var item in list)
                {
                    if (item.Account.SubChannelValues.ContainsKey(task.SubInstanceId ?? task.InstanceId))
                    {
                        ids.Add(item.ChannelId);
                    }
                }

                // 通过子频道过滤可用账号
                if (ids.Count > 0)
                {
                    discordInstance = _discordLoadBalancer.ChooseInstance(accountFilter: task.AccountFilter,
                        botType: task.RealBotType ?? task.BotType, ids: ids);

                    if (discordInstance != null)
                    {
                        // 如果找到了，则标记当前任务的子频道信息
                        task.SubInstanceId = task.SubInstanceId ?? task.InstanceId;
                    }
                }
            }

            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            task.InstanceId = discordInstance.ChannelId;
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);

            var targetTask = _taskStoreService.Get(submitAction.TaskId)!;
            var messageFlags = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_FLAGS, default)?.ToInt() ?? 0;
            var messageId = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_ID, default);

            task.BotType = targetTask.BotType;
            task.RealBotType = targetTask.RealBotType;

            task.SetProperty(Constants.TASK_PROPERTY_BOT_TYPE, targetTask.BotType.GetDescription());
            task.SetProperty(Constants.TASK_PROPERTY_CUSTOM_ID, submitAction.CustomId);

            // 设置任务的提示信息 = 父级任务的提示信息
            task.Prompt = targetTask.Prompt;

            // 上次的最终词作为变化的 prompt
            // 移除速度模式参数
            task.PromptEn = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_FINAL_PROMPT, default)?.Replace("--fast", "")?.Replace("--relax", "")?.Replace("--turbo", "")?.Trim();

            // 但是如果父级任务是 blend 任务，可能 prompt 为空
            if (string.IsNullOrWhiteSpace(task.PromptEn))
            {
                task.PromptEn = targetTask.PromptEn;
            }

            // 点击喜欢
            if (submitAction.CustomId.Contains("MJ::BOOKMARK"))
            {
                var res = discordInstance.ActionAsync(messageId ?? targetTask.MessageId,
                    submitAction.CustomId, messageFlags,
                    task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

                // 这里不需要保存任务
                if (res.Code == ReturnCode.SUCCESS)
                {
                    return SubmitResultVO.Of(ReturnCode.SUCCESS, "成功", task.ParentId);
                }
                else
                {
                    return SubmitResultVO.Of(ReturnCode.VALIDATION_ERROR, res.Description, task.ParentId);
                }
            }

            // 如果是 Modal 作业，则直接返回
            if (submitAction.CustomId.StartsWith("MJ::CustomZoom::")
                || submitAction.CustomId.StartsWith("MJ::Inpaint::"))
            {
                // 如果是局部重绘，则设置任务状态为 modal
                if (task.Action == TaskAction.INPAINT)
                {
                    task.Status = TaskStatus.MODAL;
                    task.Prompt = "";
                    task.PromptEn = "";
                }

                task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                task.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                _taskStoreService.Save(task);

                // 状态码为 21
                // 重绘、自定义变焦始终 remix 为true
                return SubmitResultVO.Of(ReturnCode.EXISTED, "Waiting for window confirm", task.Id)
                    .SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, task.PromptEn)
                    .SetProperty(Constants.TASK_PROPERTY_REMIX, true);
            }
            // describe 全部重新生成绘图
            else if (submitAction.CustomId?.Contains("MJ::Job::PicReader::all") == true)
            {
                var prompts = targetTask.PromptEn.Split('\n').Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
                var ids = new List<string>();
                var count = prompts.Length >= 4 ? 4 : prompts.Length;
                for (int i = 0; i < count; i++)
                {
                    var prompt = prompts[i].Substring(prompts[i].IndexOf(' ')).Trim();

                    var subTask = new TaskInfo()
                    {
                        Id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{RandomUtils.RandomNumbers(3)}",
                        SubmitTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        State = $"{task.State}::{i + 1}",
                        ParentId = targetTask.Id,
                        Action = task.Action,
                        BotType = task.BotType,
                        RealBotType = task.RealBotType,
                        InstanceId = task.InstanceId,
                        Prompt = prompt,
                        PromptEn = prompt,
                        Status = TaskStatus.NOT_START,
                        Mode = task.Mode,
                        RemixAutoSubmit = true,
                        SubInstanceId = task.SubInstanceId,
                    };

                    subTask.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);
                    subTask.SetProperty(Constants.TASK_PROPERTY_BOT_TYPE, targetTask.BotType.GetDescription());

                    var nonce = SnowFlake.NextId();
                    subTask.Nonce = nonce;
                    subTask.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);
                    subTask.SetProperty(Constants.TASK_PROPERTY_CUSTOM_ID, $"MJ::Job::PicReader::{i + 1}");

                    subTask.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                    subTask.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                    _taskStoreService.Save(subTask);

                    var res = SubmitModal(subTask, new SubmitModalDTO()
                    {
                        NotifyHook = submitAction.NotifyHook,
                        TaskId = subTask.Id,
                        Prompt = subTask.PromptEn,
                        State = subTask.State
                    });
                    ids.Add(subTask.Id);

                    Thread.Sleep(200);

                    if (res.Code != ReturnCode.SUCCESS && res.Code != ReturnCode.EXISTED && res.Code != ReturnCode.IN_QUEUE)
                    {
                        return SubmitResultVO.Of(ReturnCode.SUCCESS, "成功", string.Join(",", ids));
                    }
                }

                return SubmitResultVO.Of(ReturnCode.SUCCESS, "成功", string.Join(",", ids));
            }
            // 如果是 PicReader 作业，则直接返回
            // 图生文 -> 生图
            else if (submitAction.CustomId?.StartsWith("MJ::Job::PicReader::") == true)
            {
                var index = int.Parse(submitAction.CustomId.Split("::").LastOrDefault().Trim());
                var pre = targetTask.PromptEn.Split('\n').Where(c => !string.IsNullOrWhiteSpace(c)).ToArray()[index - 1].Trim();
                var prompt = pre.Substring(pre.IndexOf(' ')).Trim();

                task.Status = TaskStatus.MODAL;
                task.Prompt = prompt;
                task.PromptEn = prompt;

                task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                task.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                _taskStoreService.Save(task);

                // 状态码为 21
                // 重绘、自定义变焦始终 remix 为true
                return SubmitResultVO.Of(ReturnCode.EXISTED, "Waiting for window confirm", task.Id)
                    .SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, task.PromptEn)
                    .SetProperty(Constants.TASK_PROPERTY_REMIX, true);
            }
            // prompt shorten -> 生图
            else if (submitAction.CustomId.StartsWith("MJ::Job::PromptAnalyzer::"))
            {
                var index = int.Parse(submitAction.CustomId.Split("::").LastOrDefault().Trim());
                var si = targetTask.Description.IndexOf("Shortened prompts");
                if (si >= 0)
                {
                    var pre = targetTask.Description.Substring(si).Trim().Split('\n')
                     .Where(c => !string.IsNullOrWhiteSpace(c)).ToArray()[index].Trim();

                    var prompt = pre.Substring(pre.IndexOf(' ')).Trim();

                    task.Status = TaskStatus.MODAL;
                    task.Prompt = prompt;
                    task.PromptEn = prompt;

                    task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                    task.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                    // 如果开启了 remix 自动提交
                    if (discordInstance.Account.RemixAutoSubmit)
                    {
                        task.RemixAutoSubmit = true;
                        _taskStoreService.Save(task);

                        return SubmitModal(task, new SubmitModalDTO()
                        {
                            TaskId = task.Id,
                            NotifyHook = submitAction.NotifyHook,
                            Prompt = targetTask.PromptEn,
                            State = submitAction.State
                        });
                    }
                    else
                    {
                        _taskStoreService.Save(task);

                        // 状态码为 21
                        // 重绘、自定义变焦始终 remix 为true
                        return SubmitResultVO.Of(ReturnCode.EXISTED, "Waiting for window confirm", task.Id)
                            .SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, task.PromptEn)
                            .SetProperty(Constants.TASK_PROPERTY_REMIX, true);
                    }
                }
                else
                {
                    return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "未找到 Shortened prompts");
                }
            }
            // REMIX 处理
            else if (task.Action == TaskAction.PAN || task.Action == TaskAction.VARIATION || task.Action == TaskAction.REROLL)
            {
                task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                task.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                if (discordInstance.Account.RemixAutoSubmit)
                {
                    // 如果开启了 remix 自动提交
                    // 并且已开启 remix 模式
                    if (((task.RealBotType ?? task.BotType) == EBotType.MID_JOURNEY && discordInstance.Account.MjRemixOn)
                        || (task.BotType == EBotType.NIJI_JOURNEY && discordInstance.Account.NijiRemixOn))
                    {
                        task.RemixAutoSubmit = true;

                        _taskStoreService.Save(task);

                        return SubmitModal(task, new SubmitModalDTO()
                        {
                            TaskId = task.Id,
                            NotifyHook = submitAction.NotifyHook,
                            Prompt = targetTask.PromptEn,
                            State = submitAction.State
                        });
                    }
                }
                else
                {
                    // 未开启 remix 自动提交
                    // 并且已开启 remix 模式
                    if (((task.RealBotType ?? task.BotType) == EBotType.MID_JOURNEY && discordInstance.Account.MjRemixOn)
                        || (task.BotType == EBotType.NIJI_JOURNEY && discordInstance.Account.NijiRemixOn))
                    {
                        // 如果是 REMIX 任务，则设置任务状态为 modal
                        task.Status = TaskStatus.MODAL;
                        _taskStoreService.Save(task);

                        // 状态码为 21
                        return SubmitResultVO.Of(ReturnCode.EXISTED, "Waiting for window confirm", task.Id)
                            .SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, task.PromptEn)
                            .SetProperty(Constants.TASK_PROPERTY_REMIX, true);
                    }
                }
            }

            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                return await discordInstance.ActionAsync(
                    messageId ?? targetTask.MessageId,
                    submitAction.CustomId,
                    messageFlags,
                    task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task);
            });
        }

        /// <summary>
        /// 执行 Modal
        /// </summary>
        /// <param name="task"></param>
        /// <param name="submitAction"></param>
        /// <param name="dataUrl"></param>
        /// <returns></returns>
        public SubmitResultVO SubmitModal(TaskInfo task, SubmitModalDTO submitAction, DataUrl dataUrl = null)
        {
            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(task.SubInstanceId ?? task.InstanceId);
            if (discordInstance == null)
            {
                // 如果主实例没有找子实例
                var ids = new List<string>();
                var list = _discordLoadBalancer.GetAliveInstances().ToList();
                foreach (var item in list)
                {
                    if (item.Account.SubChannelValues.ContainsKey(task.SubInstanceId ?? task.InstanceId))
                    {
                        ids.Add(item.ChannelId);
                    }
                }

                // 通过子频道过滤可用账号
                if (ids.Count > 0)
                {
                    discordInstance = _discordLoadBalancer.ChooseInstance(accountFilter: task.AccountFilter,
                        botType: task.RealBotType ?? task.BotType, ids: ids);

                    if (discordInstance != null)
                    {
                        // 如果找到了，则标记当前任务的子频道信息
                        task.SubInstanceId = task.SubInstanceId ?? task.InstanceId;
                    }
                }
            }

            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            task.InstanceId = discordInstance.ChannelId;
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);

            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                var customId = task.GetProperty<string>(Constants.TASK_PROPERTY_CUSTOM_ID, default);
                var messageFlags = task.GetProperty<string>(Constants.TASK_PROPERTY_FLAGS, default)?.ToInt() ?? 0;
                var messageId = task.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_ID, default);
                var nonce = task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default);

                // 弹窗确认
                task = discordInstance.GetRunningTask(task.Id);
                task.RemixModaling = true;
                var res = await discordInstance.ActionAsync(messageId, customId, messageFlags, nonce, task);
                if (res.Code != ReturnCode.SUCCESS)
                {
                    return res;
                }

                // 等待获取 messageId 和交互消息 id
                // 等待最大超时 5min
                var sw = new Stopwatch();
                sw.Start();
                do
                {
                    // 等待 2.5s
                    Thread.Sleep(2500);
                    task = discordInstance.GetRunningTask(task.Id);

                    if (string.IsNullOrWhiteSpace(task.RemixModalMessageId) || string.IsNullOrWhiteSpace(task.InteractionMetadataId))
                    {
                        if (sw.ElapsedMilliseconds > 300000)
                        {
                            return Message.Of(ReturnCode.NOT_FOUND, "超时，未找到消息 ID");
                        }
                    }
                } while (string.IsNullOrWhiteSpace(task.RemixModalMessageId) || string.IsNullOrWhiteSpace(task.InteractionMetadataId));

                // 等待 1.2s
                Thread.Sleep(1200);

                task.RemixModaling = false;

                // 自定义变焦
                if (customId.StartsWith("MJ::CustomZoom::"))
                {
                    nonce = SnowFlake.NextId();
                    task.Nonce = nonce;
                    task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                    return await discordInstance.ZoomAsync(task, task.RemixModalMessageId, customId, task.PromptEn, nonce);
                }
                // 局部重绘
                else if (customId.StartsWith("MJ::Inpaint::"))
                {
                    var ifarmeCustomId = task.GetProperty<string>(Constants.TASK_PROPERTY_IFRAME_MODAL_CREATE_CUSTOM_ID, default);
                    return await discordInstance.InpaintAsync(task, ifarmeCustomId, task.PromptEn, submitAction.MaskBase64);
                }
                // 图生文 -> 文生图
                else if (customId.StartsWith("MJ::Job::PicReader::"))
                {
                    nonce = SnowFlake.NextId();
                    task.Nonce = nonce;
                    task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                    return await discordInstance.PicReaderAsync(task, task.RemixModalMessageId, customId, task.PromptEn, nonce, task.RealBotType ?? task.BotType);
                }
                // prompt shorten -> 生图
                else if (customId.StartsWith("MJ::Job::PromptAnalyzer::"))
                {
                    nonce = SnowFlake.NextId();
                    task.Nonce = nonce;
                    task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                    // MJ::ImagineModal::1265485889606516808
                    customId = $"MJ::ImagineModal::{messageId}";
                    var modal = "MJ::ImagineModal::new_prompt";

                    return await discordInstance.RemixAsync(task, task.Action.Value, task.RemixModalMessageId, modal,
                        customId, task.PromptEn, nonce, task.RealBotType ?? task.BotType);
                }
                // Remix mode
                else if (task.Action == TaskAction.VARIATION || task.Action == TaskAction.REROLL || task.Action == TaskAction.PAN)
                {
                    nonce = SnowFlake.NextId();
                    task.Nonce = nonce;
                    task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                    var action = task.Action;

                    TaskInfo parentTask = null;
                    if (!string.IsNullOrWhiteSpace(task.ParentId))
                    {
                        parentTask = _taskStoreService.Get(task.ParentId);
                        if (parentTask == null)
                        {
                            return Message.Of(ReturnCode.NOT_FOUND, "未找到父级任务");
                        }
                    }

                    var prevCustomId = parentTask?.GetProperty<string>(Constants.TASK_PROPERTY_REMIX_CUSTOM_ID, default);
                    var prevModal = parentTask?.GetProperty<string>(Constants.TASK_PROPERTY_REMIX_MODAL, default);

                    var modal = "MJ::RemixModal::new_prompt";
                    if (action == TaskAction.REROLL)
                    {
                        // 如果是首次提交，则使用交互 messageId
                        if (string.IsNullOrWhiteSpace(prevCustomId))
                        {
                            // MJ::ImagineModal::1265485889606516808
                            customId = $"MJ::ImagineModal::{messageId}";
                            modal = "MJ::ImagineModal::new_prompt";
                        }
                        else
                        {
                            modal = prevModal;

                            if (prevModal.Contains("::PanModal"))
                            {
                                // 如果是 pan, pan 是根据放大图片的 CUSTOM_ID 进行重绘处理
                                var cus = parentTask?.GetProperty<string>(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, default);
                                if (string.IsNullOrWhiteSpace(cus))
                                {
                                    return Message.Of(ReturnCode.VALIDATION_ERROR, "未找到目标图片的 U 操作");
                                }

                                // MJ::JOB::upsample::3::10f78893-eddb-468f-a0fb-55643a94e3b4
                                var arr = cus.Split("::");
                                var hash = arr[4];
                                var i = arr[3];

                                var prevArr = prevCustomId.Split("::");
                                var convertedString = $"MJ::PanModal::{prevArr[2]}::{hash}::{i}";
                                customId = convertedString;

                                // 在进行 U 时，记录目标图片的 U 的 customId
                                task.SetProperty(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, parentTask?.GetProperty<string>(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, default));
                            }
                            else
                            {
                                customId = prevCustomId;
                            }

                            task.SetProperty(Constants.TASK_PROPERTY_REMIX_CUSTOM_ID, customId);
                            task.SetProperty(Constants.TASK_PROPERTY_REMIX_MODAL, modal);
                        }
                    }
                    else if (action == TaskAction.VARIATION)
                    {
                        var suffix = "0";

                        // 如果全局开启了高变化，则高变化
                        if ((task.RealBotType ?? task.BotType) == EBotType.MID_JOURNEY)
                        {
                            if (discordInstance.Account.Buttons.Any(x => x.CustomId == "MJ::Settings::HighVariabilityMode::1" && x.Style == 3))
                            {
                                suffix = "1";
                            }
                        }
                        else
                        {
                            if (discordInstance.Account.NijiButtons.Any(x => x.CustomId == "MJ::Settings::HighVariabilityMode::1" && x.Style == 3))
                            {
                                suffix = "1";
                            }
                        }

                        // 低变化
                        if (customId.Contains("low_variation"))
                        {
                            suffix = "0";
                        }
                        // 如果是高变化
                        else if (customId.Contains("high_variation"))
                        {
                            suffix = "1";
                        }

                        var parts = customId.Split("::");
                        var convertedString = $"MJ::RemixModal::{parts[4]}::{parts[3]}::{suffix}";
                        customId = convertedString;

                        task.SetProperty(Constants.TASK_PROPERTY_REMIX_CUSTOM_ID, customId);
                        task.SetProperty(Constants.TASK_PROPERTY_REMIX_MODAL, modal);
                    }
                    else if (action == TaskAction.PAN)
                    {
                        modal = "MJ::PanModal::prompt";

                        // MJ::JOB::pan_left::1::f58e98cb-e76b-4ffa-9ed2-74f0c3fefa5c::SOLO
                        // to
                        // MJ::PanModal::left::f58e98cb-e76b-4ffa-9ed2-74f0c3fefa5c::1

                        var parts = customId.Split("::");
                        var convertedString = $"MJ::PanModal::{parts[2].Split('_')[1]}::{parts[4]}::{parts[3]}";
                        customId = convertedString;

                        task.SetProperty(Constants.TASK_PROPERTY_REMIX_CUSTOM_ID, customId);
                        task.SetProperty(Constants.TASK_PROPERTY_REMIX_MODAL, modal);

                        // 在进行 U 时，记录目标图片的 U 的 customId
                        task.SetProperty(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, parentTask?.GetProperty<string>(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, default));
                    }
                    else
                    {
                        return Message.Failure("未知操作");
                    }

                    return await discordInstance.RemixAsync(task, task.Action.Value, task.RemixModalMessageId, modal,
                        customId, task.PromptEn, nonce, task.RealBotType ?? task.BotType);
                }
                else
                {
                    // 不支持
                    return Message.Of(ReturnCode.NOT_FOUND, "不支持的操作");
                }
            });
        }

        /// <summary>
        /// 获取图片 seed
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> SubmitSeed(TaskInfo task)
        {
            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(task.InstanceId);
            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            // 请配置私聊频道
            var privateChannelId = string.Empty;

            if ((task.RealBotType ?? task.BotType) == EBotType.MID_JOURNEY)
            {
                privateChannelId = discordInstance.Account.PrivateChannelId;
            }
            else
            {
                privateChannelId = discordInstance.Account.NijiBotChannelId;
            }

            if (string.IsNullOrWhiteSpace(privateChannelId))
            {
                return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "请配置私聊频道");
            }

            try
            {
                discordInstance.AddRunningTask(task);

                var hash = task.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_HASH, default);

                var nonce = SnowFlake.NextId();
                task.Nonce = nonce;
                task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                // /show job_id
                // https://discord.com/api/v9/interactions
                var res = await discordInstance.SeedAsync(hash, nonce, task.RealBotType ?? task.BotType);
                if (res.Code != ReturnCode.SUCCESS)
                {
                    return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, res.Description);
                }

                // 等待获取 seed messageId
                // 等待最大超时 5min
                var sw = new Stopwatch();
                sw.Start();

                do
                {
                    Thread.Sleep(50);
                    task = discordInstance.GetRunningTask(task.Id);

                    if (string.IsNullOrWhiteSpace(task.SeedMessageId))
                    {
                        if (sw.ElapsedMilliseconds > 1000 * 60 * 3)
                        {
                            return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "超时，未找到 seed messageId");
                        }
                    }
                } while (string.IsNullOrWhiteSpace(task.SeedMessageId));

                // 添加反应
                // https://discord.com/api/v9/channels/1256495659683676190/messages/1260598192333127701/reactions/✉️/@me?location=Message&type=0
                var url = $"https://discord.com/api/v9/channels/{privateChannelId}/messages/{task.SeedMessageId}/reactions/%E2%9C%89%EF%B8%8F/%40me?location=Message&type=0";
                var msgRes = await discordInstance.SeedMessagesAsync(url);
                if (msgRes.Code != ReturnCode.SUCCESS)
                {
                    return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, res.Description);
                }

                sw.Start();
                do
                {
                    Thread.Sleep(50);
                    task = discordInstance.GetRunningTask(task.Id);

                    if (string.IsNullOrWhiteSpace(task.Seed))
                    {
                        if (sw.ElapsedMilliseconds > 1000 * 60 * 3)
                        {
                            return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "超时，未找到 seed");
                        }
                    }
                } while (string.IsNullOrWhiteSpace(task.Seed));

                // 保存任务
                _taskStoreService.Save(task);
            }
            finally
            {
                discordInstance.RemoveRunningTask(task);
            }

            return SubmitResultVO.Of(ReturnCode.SUCCESS, "成功", task.Seed);
        }

        /// <summary>
        /// 执行 info setting 操作
        /// </summary>
        /// <returns></returns>
        public async Task InfoSetting(string id)
        {
            var model = DbHelper.Instance.AccountStore.Get(id);
            if (model == null)
            {
                throw new LogicException("未找到账号实例");
            }

            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(model.ChannelId);
            if (discordInstance == null)
            {
                throw new LogicException("无可用的账号实例");
            }

            if (discordInstance.Account.EnableMj == true)
            {
                var res3 = await discordInstance.SettingAsync(SnowFlake.NextId(), EBotType.MID_JOURNEY);
                if (res3.Code != ReturnCode.SUCCESS)
                {
                    throw new LogicException(res3.Description);
                }
                Thread.Sleep(2500);

                var res0 = await discordInstance.InfoAsync(SnowFlake.NextId(), EBotType.MID_JOURNEY);
                if (res0.Code != ReturnCode.SUCCESS)
                {
                    throw new LogicException(res0.Description);
                }
                Thread.Sleep(2500);
            }

            if (discordInstance.Account.EnableNiji == true)
            {
                var res2 = await discordInstance.SettingAsync(SnowFlake.NextId(), EBotType.NIJI_JOURNEY);
                if (res2.Code != ReturnCode.SUCCESS)
                {
                    throw new LogicException(res2.Description);
                }
                Thread.Sleep(2500);

                var res = await discordInstance.InfoAsync(SnowFlake.NextId(), EBotType.NIJI_JOURNEY);
                if (res.Code != ReturnCode.SUCCESS)
                {
                    throw new LogicException(res.Description);
                }
                Thread.Sleep(2500);
            }
        }

        /// <summary>
        /// 修改版本
        /// </summary>
        /// <param name="id"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        public async Task AccountChangeVersion(string id, string version)
        {
            var model = DbHelper.Instance.AccountStore.Get(id);
            if (model == null)
            {
                throw new LogicException("未找到账号实例");
            }

            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(model.ChannelId);
            if (discordInstance == null)
            {
                throw new LogicException("无可用的账号实例");
            }

            var accsount = discordInstance.Account;

            var nonce = SnowFlake.NextId();
            accsount.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);
            var res = await discordInstance.SettingSelectAsync(nonce, version);
            if (res.Code != ReturnCode.SUCCESS)
            {
                throw new LogicException(res.Description);
            }

            Thread.Sleep(2000);

            await InfoSetting(id);
        }

        /// <summary>
        /// 执行操作
        /// </summary>
        /// <param name="id"></param>
        /// <param name="customId"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        public async Task AccountAction(string id, string customId, EBotType botType)
        {
            var model = DbHelper.Instance.AccountStore.Get(id);
            if (model == null)
            {
                throw new LogicException("未找到账号实例");
            }

            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(model.ChannelId);
            if (discordInstance == null)
            {
                throw new LogicException("无可用的账号实例");
            }

            var accsount = discordInstance.Account;

            var nonce = SnowFlake.NextId();
            accsount.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);
            var res = await discordInstance.SettingButtonAsync(nonce, customId, botType);
            if (res.Code != ReturnCode.SUCCESS)
            {
                throw new LogicException(res.Description);
            }

            Thread.Sleep(2000);

            await InfoSetting(id);
        }

        /// <summary>
        /// MJ Plus 数据迁移
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        public async Task MjPlusMigration(MjPlusMigrationDto dto)
        {
            var key = "mjplus";
            var islock = AsyncLocalLock.IsLockAvailable(key);
            if (!islock)
            {
                throw new LogicException("迁移任务执行中...");
            }

            _ = Task.Run(async () =>
            {
                var isLock = await AsyncLocalLock.TryLockAsync("mjplus", TimeSpan.FromMilliseconds(3), async () =>
                {
                    try
                    {
                        // 账号迁移
                        if (true)
                        {
                            var ids = DbHelper.Instance.AccountStore.GetAllIds().ToHashSet<string>();

                            var path = "/mj/account/query";
                            var pageNumber = 0;
                            var pageSize = 100;
                            var isLastPage = false;
                            var sort = 0;

                            while (!isLastPage)
                            {
                                var responseContent = await MjPlusPageData(dto, path, pageSize, pageNumber);
                                var responseObject = JObject.Parse(responseContent);
                                var contentArray = (JArray)responseObject["content"];

                                if (contentArray.Count <= 0)
                                {
                                    break;
                                }

                                foreach (var item in contentArray)
                                {
                                    // 反序列化基础 JSON
                                    var json = item.ToString();
                                    var accountJson = JsonConvert.DeserializeObject<dynamic>(json);

                                    // 创建
                                    // 创建 DiscordAccount 实例
                                    var acc = new DiscordAccount
                                    {
                                        Sponsor = "by mjplus",
                                        DayDrawLimit = -1, // 默认值 -1

                                        ChannelId = accountJson.channelId,
                                        GuildId = accountJson.guildId,
                                        PrivateChannelId = accountJson.mjBotChannelId,
                                        NijiBotChannelId = accountJson.nijiBotChannelId,
                                        UserToken = accountJson.userToken,
                                        BotToken = null,
                                        UserAgent = accountJson.userAgent,
                                        Enable = accountJson.enable,
                                        EnableMj = true,
                                        EnableNiji = true,
                                        CoreSize = accountJson.coreSize ?? 3, // 默认值 3
                                        Interval = 1.2m, // 默认值 1.2
                                        AfterIntervalMin = 1.2m, // 默认值 1.2
                                        AfterIntervalMax = 1.2m, // 默认值 1.2
                                        QueueSize = accountJson.queueSize ?? 10, // 默认值 10
                                        MaxQueueSize = 100, // 默认值 100
                                        TimeoutMinutes = accountJson.timeoutMinutes ?? 5, // 默认值 5
                                        Remark = accountJson.remark,

                                        DateCreated = DateTimeOffset.FromUnixTimeMilliseconds((long)accountJson.dateCreated).DateTime,
                                        Weight = 1, // 假设 weight 来自 properties
                                        WorkTime = null,
                                        FishingTime = null,
                                        Sort = ++sort,
                                        RemixAutoSubmit = accountJson.remixAutoSubmit,
                                        Mode = Enum.TryParse<GenerationSpeedMode>((string)accountJson.mode, out var mode) ? mode : (GenerationSpeedMode?)null,
                                        AllowModes = new List<GenerationSpeedMode>(),
                                        Components = new List<Component>(),
                                        IsBlend = true, // 默认 true
                                        IsDescribe = true, // 默认 true
                                        IsVerticalDomain = false, // 默认 false
                                        IsShorten = true,
                                        VerticalDomainIds = new List<string>(),
                                        SubChannels = new List<string>(),
                                        SubChannelValues = new Dictionary<string, string>(),

                                        Id = accountJson.id,
                                    };

                                    if (!ids.Contains(acc.Id))
                                    {
                                        DbHelper.Instance.AccountStore.Add(acc);
                                        ids.Add(acc.Id);
                                    }
                                }

                                isLastPage = (bool)responseObject["last"];
                                pageNumber++;

                                Log.Information($"账号迁移进度, 第 {pageNumber} 页, 每页 {pageSize} 条, 已完成");
                            }

                            Log.Information("账号迁移完成");
                        }

                        // 任务迁移
                        if (true)
                        {
                            var accounts = DbHelper.Instance.AccountStore.GetAll();

                            var ids = DbHelper.Instance.TaskStore.GetAllIds().ToHashSet<string>();

                            var path = "/mj/task-admin/query";
                            var pageNumber = 0;
                            var pageSize = 100;
                            var isLastPage = false;

                            while (!isLastPage)
                            {
                                var responseContent = await MjPlusPageData(dto, path, pageSize, pageNumber);
                                var responseObject = JObject.Parse(responseContent);
                                var contentArray = (JArray)responseObject["content"];

                                if (contentArray.Count <= 0)
                                {
                                    break;
                                }

                                foreach (var item in contentArray)
                                {
                                    // 反序列化基础 JSON
                                    var json = item.ToString();
                                    var jsonObject = JsonConvert.DeserializeObject<dynamic>(json);

                                    string aid = jsonObject.properties?.discordInstanceId;
                                    var acc = accounts.FirstOrDefault(x => x.Id == aid);

                                    // 创建 TaskInfo 实例
                                    var taskInfo = new TaskInfo
                                    {
                                        FinishTime = jsonObject.finishTime,
                                        PromptEn = jsonObject.promptEn,
                                        Description = jsonObject.description,
                                        SubmitTime = jsonObject.submitTime,
                                        ImageUrl = jsonObject.imageUrl,
                                        Action = Enum.TryParse<TaskAction>((string)jsonObject.action, out var action) ? action : (TaskAction?)null,
                                        Progress = jsonObject.progress,
                                        StartTime = jsonObject.startTime,
                                        FailReason = jsonObject.failReason,
                                        Id = jsonObject.id,
                                        State = jsonObject.state,
                                        Prompt = jsonObject.prompt,
                                        Status = Enum.TryParse<TaskStatus>((string)jsonObject.status, out var status) ? status : (TaskStatus?)null,
                                        Nonce = jsonObject.properties?.nonce,
                                        MessageId = jsonObject.properties?.messageId,
                                        BotType = Enum.TryParse<EBotType>((string)jsonObject.properties?.botType, out var botType) ? botType : EBotType.MID_JOURNEY,
                                        InstanceId = acc?.ChannelId,
                                        Buttons = JsonConvert.DeserializeObject<List<CustomComponentModel>>(JsonConvert.SerializeObject(jsonObject.buttons)),
                                        Properties = JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(jsonObject.properties)),
                                    };

                                    aid = taskInfo.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default);
                                    if (!string.IsNullOrWhiteSpace(aid))
                                    {
                                        acc = accounts.FirstOrDefault(x => x.Id == aid);
                                        if (acc != null)
                                        {
                                            taskInfo.InstanceId = acc.ChannelId;
                                        }
                                    }

                                    if (!ids.Contains(taskInfo.Id))
                                    {
                                        DbHelper.Instance.TaskStore.Add(taskInfo);
                                        ids.Add(taskInfo.Id);
                                    }
                                }

                                isLastPage = (bool)responseObject["last"];
                                pageNumber++;

                                Log.Information($"任务迁移进度, 第 {pageNumber} 页, 每页 {pageSize} 条, 已完成");
                            }

                            Log.Information("任务迁移完成");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "mjplus 迁移执行异常");
                    }
                });

                if (!islock)
                {
                    Log.Warning("迁移任务执行中...");
                }
            });

            await Task.CompletedTask;
        }

        /// <summary>
        /// 获取分页数据
        /// </summary>
        /// <param name="dto"></param>
        /// <param name="path"></param>
        /// <param name="pageSize"></param>
        /// <param name="pageNumber"></param>
        /// <returns></returns>
        private static async Task<string> MjPlusPageData(MjPlusMigrationDto dto, string path, int pageSize, int pageNumber)
        {
            var options = new RestClientOptions(dto.Host)
            {
                MaxTimeout = -1,
            };
            var client = new RestClient(options);
            var request = new RestRequest(path, Method.Post);
            request.AddHeader("Content-Type", "application/json");

            if (!string.IsNullOrWhiteSpace(dto.ApiSecret))
            {
                request.AddHeader("mj-api-secret", dto.ApiSecret);
            }
            var body = new JObject
            {
                ["pageSize"] = pageSize,
                ["pageNumber"] = pageNumber
            }.ToString();

            request.AddStringBody(body, DataFormat.Json);
            var response = await client.ExecuteAsync(request);
            return response.Content;
        }
    }
}
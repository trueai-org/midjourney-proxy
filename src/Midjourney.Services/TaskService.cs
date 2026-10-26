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

using System.Diagnostics;
using System.Text.RegularExpressions;
using RestSharp;
using Serilog;

namespace Midjourney.Services
{
    /// <summary>
    /// 任务服务实现类，处理任务的具体操作
    /// </summary>
    public class TaskService : ITaskService
    {
        private readonly DiscordAccountService _accountService;
        private readonly IFreeSql _freeSql = FreeSqlHelper.FreeSql;

        public TaskService(DiscordAccountService accountService)
        {
            _accountService = accountService;
        }

        /// <summary>
        /// 清除违规词缓存
        /// </summary>
        public void ClearBannedWordsCache()
        {
            RedisHelper.Instance.Del(BannedWord.CacheKey);
        }

        /// <summary>
        /// 提交 imagine 任务。
        /// </summary>
        /// <param name="info"></param>
        /// <param name="dataUrls"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> SubmitImagine(TaskInfo info, List<DataUrl> dataUrls)
        {
            var setting = GlobalConfiguration.Setting;
            var imageCount = dataUrls?.Count ?? 0;
            var urlOnlyCount = dataUrls?.Count(c => c != null
                && !string.IsNullOrWhiteSpace(c.Url)
                && (c.Data == null || c.Data.Length == 0)) ?? 0;
            var promptEn = info.PromptEn;
            if (promptEn.Contains("--video", StringComparison.OrdinalIgnoreCase))
            {
                info.Action = TaskAction.VIDEO;

                if (!setting.EnableVideo)
                {
                    return SubmitResultVO.Fail(ReturnCode.FAILURE, "视频功能未启用");
                }
            }

            var chooseSw = Stopwatch.StartNew();
            var (instance, mode) = _accountService.ChooseInstance(info.AccountFilter,
                isNewTask: true,
                botType: info.RealBotType ?? info.BotType,
                isVideo: info.Action == TaskAction.VIDEO);

            if (instance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            info.Mode = mode;

            info.IsPartner = instance.Account.IsYouChuan;
            info.IsOfficial = instance.Account.IsOfficial;
            info.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.ChannelId);
            info.InstanceId = instance.ChannelId;

            Log.Information("SubmitImagine 开始: TaskId={TaskId}, IsYouChuan={IsYouChuan}, IsOfficial={IsOfficial}, Images={ImageCount}, UrlOnly={UrlOnlyCount}, ChooseMs={ChooseMs}",
                info.Id, instance.Account.IsYouChuan, instance.Account.IsOfficial, imageCount, urlOnlyCount, chooseSw.ElapsedMilliseconds);


            // 垫图抓取/转换/上传全部在 worker 中异步完成；请求线程只做解码+暂存
            return await SubmitImagineWithStagedInputsAsync(info, dataUrls, instance);
        }

        /// <summary>
        /// 提交编辑任务。
        /// </summary>
        /// <param name="info"></param>
        /// <param name="dataUrl"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> SubmitEdit(TaskInfo info, DataUrl dataUrl)
        {
            var (instance, mode) = _accountService.ChooseInstance(info.AccountFilter,
                isNewTask: true,
                botType: info.RealBotType ?? info.BotType,
                isYm: true);

            if (instance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }
            if (instance.Account.IsDiscord)
            {
                return SubmitResultVO.Fail(ReturnCode.FAILURE, "当前账号不支持编辑/重绘任务");
            }

            info.IsOfficial = instance.Account.IsOfficial;
            info.IsPartner = instance.Account.IsYouChuan;
            info.Mode = mode;
            info.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.ChannelId);
            info.InstanceId = instance.ChannelId;

            var stageResult = StageBaseImageInput(info, dataUrl, out var stagedKeys);
            if (stageResult != null)
            {
                return stageResult;
            }

            info.Description = "/edit " + info.Prompt;

            // 入队前不保存
            //_taskStoreService.Save(info);

            try
            {
                var submitResult = await instance.RedisEnqueue(new TaskInfoQueue()
                {
                    Info = info,
                    Function = TaskInfoQueueFunction.EDIT
                });

                if (submitResult?.Code != ReturnCode.SUCCESS && submitResult?.Code != ReturnCode.IN_QUEUE)
                {
                    DeleteStagedImagineInputs(info.Id, stagedKeys);
                }

                return submitResult;
            }
            catch
            {
                DeleteStagedImagineInputs(info.Id, stagedKeys);
                throw;
            }
        }

        /// <summary>
        /// 提交转绘任务。
        /// </summary>
        /// <param name="info"></param>
        /// <param name="dataUrl"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> SubmitRetexture(TaskInfo info, DataUrl dataUrl)
        {
            var (instance, mode) = _accountService.ChooseInstance(info.AccountFilter,
                isNewTask: true,
                botType: info.RealBotType ?? info.BotType,
                isYm: true);

            if (instance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }
            if (instance.Account.IsDiscord)
            {
                return SubmitResultVO.Fail(ReturnCode.FAILURE, "当前账号不支持编辑/重绘任务");
            }

            info.IsOfficial = instance.Account.IsOfficial;
            info.IsPartner = instance.Account.IsYouChuan;
            info.Mode = mode;
            info.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.ChannelId);
            info.InstanceId = instance.ChannelId;

            var stageResult = StageBaseImageInput(info, dataUrl, out var stagedKeys);
            if (stageResult != null)
            {
                return stageResult;
            }

            info.Description = "/retexture " + info.Prompt;

            // --dref 由工作线程在图片上传完成后追加，避免请求线程等待图片处理
            // 入队前不保存
            //_taskStoreService.Save(info);

            try
            {
                var submitResult = await instance.RedisEnqueue(new TaskInfoQueue()
                {
                    Info = info,
                    Function = TaskInfoQueueFunction.RETEXTURE
                });

                if (submitResult?.Code != ReturnCode.SUCCESS && submitResult?.Code != ReturnCode.IN_QUEUE)
                {
                    DeleteStagedImagineInputs(info.Id, stagedKeys);
                }

                return submitResult;
            }
            catch
            {
                DeleteStagedImagineInputs(info.Id, stagedKeys);
                throw;
            }
        }

        /// <summary>
        /// 提交视频任务
        /// </summary>
        /// <param name="task"></param>
        /// <param name="startUrl"></param>
        /// <param name="endUrl"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> SubmitVideo(TaskInfo info, TaskInfo targetTask, DataUrl startUrl, DataUrl endUrl, SubmitVideoDTO videoDTO)
        {
            DiscordService instance;
            GenerationSpeedMode? mode = null;

            // 高清视频
            var isHdVideo = videoDTO.VideoType == "vid_1.1_i2v_720";

            if (videoDTO.Action?.Equals("extend", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (targetTask == null)
                {
                    return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "目标任务不存在");
                }

                instance = GetInstanceByTask(info, targetTask, out var submitResult);
                if (instance == null || submitResult.Code != ReturnCode.SUCCESS)
                {
                    return submitResult;
                }

                info.SetProperty(Constants.TASK_PROPERTY_BOT_TYPE, targetTask.BotType.GetDescription());
            }
            else
            {
                var (okInstance, okMode) = _accountService.ChooseInstance(info.AccountFilter,
                 isNewTask: true,
                 botType: info.RealBotType ?? info.BotType,
                 isVideo: true,
                 isHdVideo: isHdVideo);

                instance = okInstance;
                mode = okMode;
            }

            if (instance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            // 必须开启 REMIX 模式才支持 API 视频拓展
            var botType = info.RealBotType ?? info.BotType;

            if (instance.Account.IsDiscord)
            {
                if (botType == EBotType.MID_JOURNEY && !instance.Account.MjRemixOn)
                {
                    return SubmitResultVO.Fail(ReturnCode.FAILURE, "未开启 REMIX，无法使用视频扩展功能");
                }
                else if (botType == EBotType.NIJI_JOURNEY && !instance.Account.NijiRemixOn)
                {
                    return SubmitResultVO.Fail(ReturnCode.FAILURE, "未开启 REMIX，无法使用视频扩展功能");
                }
            }

            // 如果要求 HD，但是没有 HD
            if (isHdVideo && !instance.Account.IsHdVideo)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            if (info.Mode == null)
            {
                info.Mode = mode;
            }

            info.InstanceId = instance.ChannelId;
            info.IsOfficial = instance.Account.IsOfficial;
            info.IsPartner = instance.Account.IsYouChuan;
            info.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.ChannelId);

            var startToken = ImagineInputRef.BuildVideoInputToken(info.Id, 0);
            var endToken = ImagineInputRef.BuildVideoInputToken(info.Id, 1);
            var prompt = info.Prompt ?? string.Empty;
            DataUrl startInput = null;
            DataUrl endInput = null;

            // 开始图片：URL 已在提示词中时直接复用，其余交给工作线程解析/上传
            if (startUrl != null
                && (!string.IsNullOrWhiteSpace(startUrl.Url) || startUrl.Data?.Length > 0))
            {
                if (!string.IsNullOrWhiteSpace(startUrl.Url)
                    && prompt.Contains(startUrl.Url, StringComparison.OrdinalIgnoreCase))
                {
                    info.BaseImageUrl = startUrl.Url;
                }
                else
                {
                    startInput = startUrl;
                    prompt = $"{startToken} {prompt}";

                    if (!string.IsNullOrWhiteSpace(startUrl.Url))
                    {
                        info.BaseImageUrl = startUrl.Url;
                    }
                }
            }

            // 如果是视频任务，添加 --video 参数
            if (!prompt.Contains("--video", StringComparison.OrdinalIgnoreCase))
            {
                prompt += " --video 1";
            }

            // loop
            if (videoDTO.Loop && !prompt.Contains("--end", StringComparison.OrdinalIgnoreCase))
            {
                prompt += " --end loop";
            }

            // 如果有结束图片，则添加 --end 参数；图片链接由工作线程解析后替换
            if (endUrl != null
                && (!string.IsNullOrWhiteSpace(endUrl.Url) || endUrl.Data?.Length > 0)
                && !prompt.Contains("--end", StringComparison.OrdinalIgnoreCase))
            {
                endInput = endUrl;
                prompt += $" --end {endToken}";
            }

            // motion
            if (!string.IsNullOrWhiteSpace(videoDTO.Motion) && !prompt.Contains("--motion", StringComparison.OrdinalIgnoreCase))
            {
                prompt += $" --motion {videoDTO.Motion}";
            }

            // 如果有设置 --bs
            if (videoDTO.BatchSize > 0 && !prompt.Contains("--bs", StringComparison.OrdinalIgnoreCase))
            {
                // 默认 4 不需要添加参数
                var allowBatchSize = new int[] { 1, 2 };
                if (allowBatchSize.Contains(videoDTO.BatchSize.Value))
                {
                    prompt += $" --bs {videoDTO.BatchSize}";
                }
            }

            var stageResult = StageVideoInputs(info, startInput, endInput, out var stagedKeys);
            if (stageResult != null)
            {
                return stageResult;
            }

            if (videoDTO.Action?.Equals("extend", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (info.IsPartner || info.IsOfficial)
                {
                    var customId = "";
                    if (info.IsPartner)
                    {
                        customId = $"MJ::JOB::animate_{(videoDTO.Motion ?? "high").ToLower()}_extend::{videoDTO.Index + 1}::{targetTask.PartnerTaskId}";
                    }
                    else if (info.IsOfficial)
                    {
                        customId = $"MJ::JOB::animate_{(videoDTO.Motion ?? "high").ToLower()}_extend::{videoDTO.Index + 1}::{targetTask.OfficialTaskId}";
                    }

                    info.SetProperty(Constants.TASK_PROPERTY_CUSTOM_ID, customId);
                    info.Prompt = prompt;
                    info.PromptEn = prompt;
                    info.VideoType = videoDTO.VideoType;
                    info.Description = "/video " + info.Prompt;

                    // 不赋值，由于视频 api 接口和 视频放大 -> 再拓展获取索引规则不同，因此使用 customId 来携带索引信息，后续根据 customId 解析出索引进行处理
                    //info.SetProperty(Constants.TASK_PROPERTY_ACTION_INDEX, videoDTO.Index + 1);

                    // 入队前不保存
                    //_taskStoreService.Save(info);

                    //return await instance.YmTaskService.SubmitActionAsync(info, new SubmitActionDTO()
                    //{
                    //    TaskId = targetTask.Id,
                    //    State = info.State,
                    //    CustomId = customId
                    //}, targetTask, _taskStoreService, instance);

                    return await EnqueueVideoAsync(info, instance, new TaskInfoQueue()
                    {
                        Info = info,
                        Function = TaskInfoQueueFunction.ACTION,
                        ActionParam = new TaskInfoQueue.TaskInfoQueueActionParam()
                        {
                            Dto = new SubmitActionDTO()
                            {
                                TaskId = targetTask.Id,
                                State = info.State,
                                CustomId = customId
                            },
                            TargetTask = targetTask
                        }
                    }, stagedKeys);
                }
                else
                {
                    // 对于 Discord 账号，需要分三步：
                    // 1. 先调用 video_virtual_upscale 按钮进行放大
                    // 2. 放大完成后，在新任务上调用 animate_{motion}_extend 按钮
                    // 3. 如果开启了 remix 模式，还需要处理 modal 弹窗

                    // 获取目标任务的 OfficialTaskId 或 MessageHash
                    var taskId = targetTask.OfficialTaskId;
                    if (string.IsNullOrWhiteSpace(taskId))
                    {
                        taskId = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_HASH, default);
                    }
                    if (string.IsNullOrWhiteSpace(taskId))
                    {
                        taskId = targetTask.JobId;
                    }

                    if (string.IsNullOrWhiteSpace(taskId))
                    {
                        DeleteStagedImagineInputs(info.Id, stagedKeys);
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, "目标任务缺少必要的ID信息");
                    }

                    // 构造 video_virtual_upscale 的 customId
                    var upscaleCustomId = $"MJ::JOB::video_virtual_upscale::{videoDTO.Index + 1}::{taskId}";

                    // 保存扩展相关的信息，以便在放大完成后继续执行扩展
                    info.SetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_TARGET_TASK_ID, info.Id);
                    info.SetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_PROMPT, prompt);
                    info.SetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_MOTION, videoDTO.Motion?.ToLower() ?? "high");
                    info.SetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_INDEX, videoDTO.Index + 1);
                    info.SetProperty(Constants.TASK_PROPERTY_CUSTOM_ID, upscaleCustomId);
                    info.Action = TaskAction.UPSCALE;
                    info.Prompt = prompt;
                    info.PromptEn = prompt;
                    info.VideoType = videoDTO.VideoType;
                    info.Description = "/video extend";

                    //_taskStoreService.Save(info);

                    //// 先执行放大操作
                    //return await instance.ActionAsync(targetTask.MessageId, upscaleCustomId,
                    //    targetTask.GetProperty<int>(Constants.TASK_PROPERTY_FLAGS, default),
                    //    info.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), info);

                    return await EnqueueVideoAsync(info, instance, new TaskInfoQueue()
                    {
                        Info = info,
                        Function = TaskInfoQueueFunction.ACTION,
                        ActionParam = new TaskInfoQueue.TaskInfoQueueActionParam()
                        {
                            MessageId = targetTask.MessageId,
                            CustomId = upscaleCustomId,
                            MessageFlags = targetTask.GetProperty<int>(Constants.TASK_PROPERTY_FLAGS, default),
                            Nonce = info.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default)
                        }
                    }, stagedKeys);
                }
            }
            else
            {
                info.Prompt = prompt;
                info.PromptEn = prompt;
                info.Description = "/video " + info.Prompt;

                // 入队前不保存
                //_taskStoreService.Save(info);

                //return await instance.YmTaskService.SubmitTaskAsync(info, _taskStoreService, instance);

                return await EnqueueVideoAsync(info, instance, new TaskInfoQueue()
                {
                    Info = info,
                    Function = TaskInfoQueueFunction.VIDEO
                }, stagedKeys);
            }
        }

        /// <summary>
        /// 提交Describe任务
        /// </summary>
        /// <param name="task"></param>
        /// <param name="dataUrl"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> SubmitDescribe(TaskInfo task, DataUrl dataUrl)
        {
            var discordInstance = _accountService.GetDescribeInstance(task.AccountFilter?.InstanceId);
            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);
            task.InstanceId = discordInstance.ChannelId;
            task.IsPartner = discordInstance.Account.IsYouChuan;
            task.IsOfficial = discordInstance.Account.IsOfficial;

            var stagedKeys = new List<string>();

            if (dataUrl.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
            {
                task.ImageUrl = dataUrl.Url;
            }
            else if (dataUrl.Data?.Length > 0)
            {
                var ttl = ImagineInputStager.GetTtl();
                var stage = ImagineInputStager.Stage(
                    task,
                    [dataUrl],
                    (int)ttl.TotalSeconds,
                    (key, bytes, seconds) => RedisHelper.Instance.Set(key, bytes, seconds));

                if (!stage.Success)
                {
                    Log.Warning("Describe 垫图暂存失败: TaskId={TaskId}, Error={Error}, Staged={Staged}", task.Id, stage.Error, stage.StagedKeys.Count);
                    DeleteStagedImagineInputs(task.Id, stage.StagedKeys);
                    return SubmitResultVO.Fail(ReturnCode.FAILURE, "垫图暂存失败，请稍后重试");
                }

                stagedKeys = stage.StagedKeys;
            }
            else
            {
                return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "图片参数错误");
            }

            task.Status = TaskStatus.NOT_START;

            try
            {
                var submitResult = await discordInstance.RedisEnqueue(new TaskInfoQueue()
                {
                    Info = task,
                    Function = TaskInfoQueueFunction.DESCRIBE
                });

                if (submitResult?.Code != ReturnCode.SUCCESS && submitResult?.Code != ReturnCode.IN_QUEUE)
                {
                    DeleteStagedImagineInputs(task.Id, stagedKeys);
                }

                return submitResult;
            }
            catch
            {
                DeleteStagedImagineInputs(task.Id, stagedKeys);
                throw;
            }
        }

        /// <summary>
        /// 上传一个较长的提示词，mj 可以返回一组简要的提示词
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> ShortenAsync(TaskInfo task)
        {
            var (discordInstance, mode) = _accountService.ChooseInstance(task.AccountFilter,
                isNewTask: true,
                botType: task.RealBotType ?? task.BotType,
                shorten: true);

            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            task.Mode = mode;
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);
            task.InstanceId = discordInstance.ChannelId;

            task.Status = TaskStatus.NOT_START;

            // 入队前不保存
            //_taskStoreService.Save(task);

            return await discordInstance.RedisEnqueue(new TaskInfoQueue()
            {
                Info = task,
                Function = TaskInfoQueueFunction.SHORTEN
            });
        }

        /// <summary>
        /// 提交混合任务
        /// </summary>
        /// <param name="task"></param>
        /// <param name="dataUrls"></param>
        /// <param name="dimensions"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> SubmitBlend(TaskInfo task, List<DataUrl> dataUrls, BlendDimensions dimensions)
        {
            var (discordInstance, mode) = _accountService.ChooseInstance(task.AccountFilter,
                isNewTask: true,
                botType: task.RealBotType ?? task.BotType,
                blend: true);

            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            task.Mode = mode;
            task.IsPartner = discordInstance.Account.IsYouChuan;
            task.IsOfficial = discordInstance.Account.IsOfficial;
            task.InstanceId = discordInstance.ChannelId;
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);

            var finalFileNames = new List<string>();
            var base64DataUrls = new List<DataUrl>();
            var stagedKeys = new List<string>();

            foreach (var dataUrl in dataUrls)
            {
                if (dataUrl.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                {
                    finalFileNames.Add(dataUrl.Url);
                    continue;
                }

                if (dataUrl.Data?.Length > 0)
                {
                    base64DataUrls.Add(dataUrl);
                }
            }

            if (base64DataUrls.Count > 0)
            {
                var ttl = ImagineInputStager.GetTtl();
                var stage = ImagineInputStager.Stage(
                    task,
                    base64DataUrls,
                    (int)ttl.TotalSeconds,
                    (key, bytes, seconds) => RedisHelper.Instance.Set(key, bytes, seconds));

                if (!stage.Success)
                {
                    Log.Warning("Blend 垫图暂存失败: TaskId={TaskId}, Error={Error}, Staged={Staged}", task.Id, stage.Error, stage.StagedKeys.Count);
                    DeleteStagedImagineInputs(task.Id, stage.StagedKeys);
                    return SubmitResultVO.Fail(ReturnCode.FAILURE, "垫图暂存失败，请稍后重试");
                }

                stagedKeys = stage.StagedKeys;
            }

            task.Action = TaskAction.BLEND;
            task.Prompt = string.Join(" ", finalFileNames) + " " + (task.PromptEn ?? task.Prompt);

            if (!task.Prompt.Contains("--ar"))
            {
                switch (dimensions)
                {
                    case BlendDimensions.PORTRAIT:
                        task.Prompt += " --ar 2:3";
                        break;

                    case BlendDimensions.SQUARE:
                        task.Prompt += " --ar 1:1";
                        break;

                    case BlendDimensions.LANDSCAPE:
                        task.Prompt += " --ar 3:2";
                        break;

                    default:
                        break;
                }
            }
            task.PromptEn = task.Prompt;

            try
            {
                var submitResult = await discordInstance.RedisEnqueue(new TaskInfoQueue()
                {
                    Info = task,
                    Function = TaskInfoQueueFunction.BLEND
                });

                if (submitResult?.Code != ReturnCode.SUCCESS && submitResult?.Code != ReturnCode.IN_QUEUE)
                {
                    DeleteStagedImagineInputs(task.Id, stagedKeys);
                }

                return submitResult;
            }
            catch(Exception ex)
            {
                DeleteStagedImagineInputs(task.Id, stagedKeys);

                Log.Error(ex, "SubmitBlend failed");

                return SubmitResultVO.Fail(ReturnCode.FAILURE, "提交混图任务失败");
            }
        }

        /// <summary>
        /// 根据任务获取可用的 Discord 实例（自动选择可用实例，适用于图片变化、视频扩展、弹窗）
        /// </summary>
        /// <param name="task"></param>
        /// <param name="parentTaskId"></param>
        /// <param name="submitResult"></param>
        /// <returns></returns>
        public DiscordService GetInstanceByTask(TaskInfo task, TaskInfo targetTask, out SubmitResultVO submitResult)
        {
            GenerationSpeedMode? mode = null;

            if (targetTask == null)
            {
                submitResult = SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "目标任务不存在");
                return null;
            }

            // 不需要配置，每个任务都是独立计算速度模式的
            //// 如果没有设置模式，则使用目标任务的模式
            //if (task.Mode == null)
            //{
            //    task.Mode = targetTask.Mode;
            //}

            //if (task.RequestMode == null)
            //{
            //    task.RequestMode = targetTask.RequestMode;
            //}

            task.IsPartner = targetTask.IsPartner;
            task.IsOfficial = targetTask.IsOfficial;
            task.BotType = targetTask.BotType;
            task.RealBotType = targetTask.RealBotType;

            task.AccountFilter ??= new AccountFilter();
            task.AccountFilter.Modes ??= [];

            var modes = new List<GenerationSpeedMode>(task.AccountFilter.Modes.Distinct());
            if (modes.Count == 0)
            {
                // 如果没有速度模式，则添加默认的速度模式
                modes = [GenerationSpeedMode.FAST, GenerationSpeedMode.TURBO, GenerationSpeedMode.RELAX];
            }

            var instance = _accountService.GetDiscordInstanceIsAlive(task.SubInstanceId ?? task.InstanceId);

            // 悠船慢速变化任务特殊处理
            // 如果父级是悠船慢速任务，且当前账号没有慢速额度的情况下，交由其他慢速账号出图
            if (task.IsPartner && task.Action != null)
            {
                if (targetTask.Mode == GenerationSpeedMode.RELAX
                    && task.Action != TaskAction.VIDEO
                    && task.Action != TaskAction.UPSCALE)
                {
                    // Vary Region、Vary Subtle, Vary Strong、缩放、平移 -> 可以在当前账号快速模式下继续执行

                    // 如果父级是慢速，且属于以下操作，则只能通过其他慢速账号继续执行
                    // UPSCALE_HD
                    // V1, V2, V3, V4
                    // ::reroll
                    var relaxActions = new List<TaskAction>()
                    {
                        TaskAction.UPSCALE_HD,
                    };

                    var forceRelax = false;

                    var customId = task.GetProperty<string>(Constants.TASK_PROPERTY_CUSTOM_ID, default);

                    // 低变化/高变化 - 暂时也强制慢速
                    if (relaxActions.Contains(task.Action.Value)
                        || customId?.Contains("::low_variation") == true
                        || customId?.Contains("::high_variation") == true
                        || customId?.Contains("::variation") == true
                        || customId?.Contains("::reroll") == true)
                    {
                        forceRelax = true;
                    }
                    else
                    {
                        // 如果没有快速或极速，则依然强制慢速处理
                        if (!modes.Contains(GenerationSpeedMode.FAST)
                            && !modes.Contains(GenerationSpeedMode.TURBO))
                        {
                            forceRelax = true;
                        }
                    }

                    // 还是精确一些 - 如果后续处理麻烦，可以统一慢速处理
                    // 简单点 -> 虽然 Vary Subtle、Vary Strong、缩放、平移 可以通过快速模式继续生成，但是原始任务是慢速任务
                    // 简单点统一用慢速变化操作

                    // 强制慢速账号衍生
                    if (forceRelax)
                    {
                        mode = GenerationSpeedMode.RELAX;
                        modes = [GenerationSpeedMode.RELAX];

                        task.AccountFilter ??= new();
                        task.AccountFilter.Modes = modes;

                        // 判断此账号是否有慢速额度，如果没有则需要换账号
                        if (instance != null && instance.IsAllowContinue(GenerationSpeedMode.RELAX) && instance.Account.IsAcceptActionTask())
                        {
                            // 允许在当前账号继续执行
                        }
                        else
                        {
                            // 不允许在当前账号继续执行，交由其他慢速账号出图
                            instance = null;
                        }
                    }
                }
            }

            // 是否允许继续任务
            var isContinue = false;

            // 非放大任务，判断是否允许继续
            if (task.Action != TaskAction.UPSCALE)
            {
                foreach (var m in modes)
                {
                    if (instance != null && instance.IsAllowContinue(m) && instance.Account.IsAcceptActionTask())
                    {
                        if (task.Action == TaskAction.VIDEO)
                        {
                            // 判断是否允许视频操作
                            if (instance.IsAllowGenerateVideo(m))
                            {
                                isContinue = true;
                                mode = m;
                                break;
                            }
                        }
                        else
                        {
                            isContinue = true;
                            mode = m;
                            break;
                        }
                    }
                }
            }
            else
            {
                // 放大任务，实例不为空则允许继续
                if (instance != null)
                {
                    isContinue = true;
                }
            }

            if (instance == null || !isContinue)
            {
                // discord 账号通过子频道重新获取新的实例
                if (task.IsDiscord)
                {
                    // 如果主实例没有找子实例
                    var ids = new List<string>();
                    var list = _accountService.GetAliveInstances().ToList();
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
                        // 清除指定实例
                        task.AccountFilter.InstanceId = null;

                        var (okInstance, okMode) = _accountService.ChooseInstance(
                             isDiscord: true,
                             isActionTask: true,
                             accountFilter: task.AccountFilter,
                             botType: task.RealBotType ?? task.BotType,
                             instanceIds: ids,
                             isUpscale: task.Action == TaskAction.UPSCALE,
                             isVideo: task.Action == TaskAction.VIDEO,
                             notInstanceIds: [task.SubInstanceId ?? task.InstanceId]);

                        if (okInstance != null)
                        {
                            // Discord 标记子频道为原主实例
                            task.SubInstanceId = task.SubInstanceId ?? task.InstanceId;

                            instance = okInstance;
                            mode = okMode;
                        }
                    }
                }

                // 悠船账号始终允许跨账号操作
                if (task.IsPartner)
                {
                    // 清除指定实例
                    task.AccountFilter.InstanceId = null;

                    var (okInstance, okMode) = _accountService.ChooseInstance(
                        isYouChuan: true,
                        isActionTask: true,
                        accountFilter: task.AccountFilter,
                        botType: task.RealBotType ?? task.BotType,
                        isUpscale: task.Action == TaskAction.UPSCALE,
                        isVideo: task.Action == TaskAction.VIDEO,
                        notInstanceIds: [task.SubInstanceId ?? task.InstanceId]);

                    if (okInstance != null)
                    {
                        // 悠船重设实例 ID
                        task.InstanceId = okInstance.ChannelId;

                        instance = okInstance;
                        mode = okMode;
                    }
                }

                // 非放大任务，判断是否允许继续
                if (task.Action != TaskAction.UPSCALE)
                {
                    foreach (var m in modes)
                    {
                        if (instance != null && instance.IsAllowContinue(m) && instance.Account.IsAcceptActionTask())
                        {
                            if (task.Action == TaskAction.VIDEO)
                            {
                                // 判断是否允许视频操作
                                if (instance.IsAllowGenerateVideo(m))
                                {
                                    isContinue = true;
                                    mode = m;
                                    break;
                                }
                            }
                            else
                            {
                                isContinue = true;
                                mode = m;
                                break;
                            }
                        }
                    }
                }
            }

            // 放大任务特殊处理
            // 放大任务，实例不为空则允许继续
            if (task.Action == TaskAction.UPSCALE)
            {
                if (instance != null)
                {
                    isContinue = true;
                }
            }

            if (instance == null || !isContinue)
            {
                submitResult = SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
                return null;
            }

            // 如果当前账号是固定模式
            if (instance.Account.Mode != null)
            {
                mode = instance.Account.Mode;
            }

            task.Mode = mode ?? GenerationSpeedMode.FAST;

            submitResult = SubmitResultVO.Of(ReturnCode.SUCCESS, "成功", "");

            return instance;
        }

        /// <summary>
        /// 执行动作
        /// </summary>
        /// <param name="task"></param>
        /// <param name="submitAction"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> SubmitAction(TaskInfo task, SubmitActionDTO submitAction)
        {
            var setting = GlobalConfiguration.Setting;

            var targetTask = _freeSql.Get<TaskInfo>(submitAction.TaskId)!;
            if (targetTask == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "目标任务不存在");
            }
            task.IsOfficial = targetTask.IsOfficial;
            task.IsPartner = targetTask.IsPartner;

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

            var instance = GetInstanceByTask(task, targetTask, out var submitResult);
            if (instance == null || submitResult.Code != ReturnCode.SUCCESS)
            {
                // 如果是放大任务，且账号不可用时
                // 如果悠船、官方
                if (task.Action == TaskAction.UPSCALE)
                {
                    if (task.IsOfficial || task.IsPartner)
                    {
                        task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, targetTask.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, ""));
                        task.InstanceId = targetTask.InstanceId;
                        task.IsPartner = targetTask.IsPartner;
                        task.IsOfficial = targetTask.IsOfficial;

                        var info = task;
                        var index = info.GetProperty(Constants.TASK_PROPERTY_ACTION_INDEX, -1) - 1;

                        if (task.IsPartner)
                        {
                            if (index >= 0 && targetTask.PartnerTaskInfo != null && targetTask.PartnerTaskInfo.ImgUrls.Count >= index + 1)
                            {
                                var image = targetTask.PartnerTaskInfo.ImgUrls[index];

                                // 链接转换
                                image.Url = info.TransformUrl(image.Url);

                                info.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
                                info.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, targetTask.GetProperty<string>(Constants.TASK_PROPERTY_FINAL_PROMPT, default));
                                info.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_HASH, default));
                                info.SetProperty(Constants.TASK_PROPERTY_MESSAGE_CONTENT, targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_CONTENT, default));
                                info.SetProperty(Constants.TASK_PROPERTY_ACTION_INDEX, index + 1);

                                var prompt = targetTask.PartnerTaskInfo.FullCommand;

                                info.PartnerTaskId = targetTask.PartnerTaskId;
                                info.PartnerTaskInfo = targetTask.PartnerTaskInfo;
                                info.ImageUrl = image.Url;
                                info.JobId = targetTask.JobId;
                                info.Width = targetTask.Width;
                                info.Height = targetTask.Height;

                                // 非高级任务，添加变化操作按钮
                                if (targetTask.Action != TaskAction.EDIT && targetTask.Action != TaskAction.RETEXTURE && targetTask.Action != TaskAction.VIDEO)
                                {
                                    // 设置放大操作按钮
                                    info.OnUpscaleButtons(targetTask.PartnerTaskId, index + 1, prompt);
                                }

                                // 如果是视频放大
                                if (targetTask.Action == TaskAction.VIDEO)
                                {
                                    info.VideoDuration = targetTask.VideoDuration;
                                    info.VideoGenOriginImageUrl = targetTask.VideoGenOriginImageUrl;
                                    info.ContentType = "video/mp4";
                                    info.FrameCount = targetTask.FrameCount;
                                    info.ImageUrls = targetTask.ImageUrls;
                                    info.VideoUrls = targetTask.VideoUrls;

                                    info.SetVideoExtendButtons(targetTask.PartnerTaskId);
                                }

                                await info.SuccessAsync();

                                // 输出链接替换规则
                                info.ApplyOutputLinkReplaceRules();

                                _freeSql.Save(info);

                                return SubmitResultVO.Of(ReturnCode.SUCCESS, "提交成功", info.Id)
                                    .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, info.InstanceId);
                            }
                        }
                        else if (task.IsOfficial)
                        {
                            if (index >= 0 && targetTask.OfficialTaskInfo != null && targetTask.OfficialTaskInfo.ImgUrls.Count >= index + 1)
                            {
                                var image = targetTask.OfficialTaskInfo.ImgUrls[index];

                                // 链接转换
                                image.Url = info.TransformUrl(image.Url);

                                try
                                {
                                    // 没有转换存储方式，则抓取
                                    if (info.StorageOption == null)
                                    {
                                        // 随便获取一个官方账号实例来下载图片
                                        var firInc = _accountService.GetAliveInstances().Where(c => c.Account.IsOfficial && c.IsAlive).OrderBy(c => Guid.NewGuid()).FirstOrDefault();
                                        if (firInc != null)
                                        {
                                            var resultUrl = await firInc.YmTaskService?.DownloadImageToStorage(image.Url, targetTask.OfficialTaskId);
                                            if (!string.IsNullOrWhiteSpace(resultUrl))
                                            {
                                                image.Url = resultUrl;
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // 允许失败，使用原始链接
                                    Log.Error(ex, "下载图片失败 {@0}", image.Url);
                                }

                                info.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
                                info.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, targetTask.GetProperty<string>(Constants.TASK_PROPERTY_FINAL_PROMPT, default));
                                info.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_HASH, default));
                                info.SetProperty(Constants.TASK_PROPERTY_MESSAGE_CONTENT, targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_CONTENT, default));
                                info.SetProperty(Constants.TASK_PROPERTY_ACTION_INDEX, index + 1);

                                var prompt = targetTask.OfficialTaskInfo.FullCommand;

                                info.OfficialTaskId = targetTask.OfficialTaskId;
                                info.OfficialTaskInfo = targetTask.OfficialTaskInfo;
                                info.ImageUrl = image.Url;
                                info.JobId = targetTask.JobId;
                                info.Width = targetTask.Width;
                                info.Height = targetTask.Height;

                                // 非高级任务，添加变化操作按钮
                                if (targetTask.Action != TaskAction.EDIT && targetTask.Action != TaskAction.RETEXTURE && targetTask.Action != TaskAction.VIDEO)
                                {
                                    // 设置放大操作按钮
                                    info.OnUpscaleButtons(targetTask.OfficialTaskId, index + 1, prompt);
                                }

                                // 如果是视频放大
                                if (targetTask.Action == TaskAction.VIDEO)
                                {
                                    info.VideoDuration = targetTask.VideoDuration;
                                    info.VideoGenOriginImageUrl = targetTask.VideoGenOriginImageUrl;
                                    info.ContentType = "video/mp4";
                                    info.FrameCount = targetTask.FrameCount;
                                    info.ImageUrls = targetTask.ImageUrls;
                                    info.VideoUrls = targetTask.VideoUrls;

                                    info.SetVideoExtendButtons(targetTask.OfficialTaskId);
                                }

                                await info.SuccessAsync();

                                // 输出链接替换规则
                                info.ApplyOutputLinkReplaceRules();

                                _freeSql.Save(info);

                                return SubmitResultVO.Of(ReturnCode.SUCCESS, "提交成功", info.Id)
                                    .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, info.InstanceId);
                            }
                        }
                    }
                }

                return submitResult;
            }

            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.ChannelId);

            task.InstanceId = instance.ChannelId;
            task.IsPartner = instance.Account.IsYouChuan;
            task.IsOfficial = instance.Account.IsOfficial;

            // 如果是 Modal 作业，则直接返回
            if (submitAction.CustomId.StartsWith("MJ::CustomZoom::") || submitAction.CustomId.StartsWith("MJ::Inpaint::"))
            {
                // fix: 这里应该始终为 modal 状态，等待前端弹窗确认后再真正提交任务，避免用户误操作导致任务错误
                task.Status = TaskStatus.MODAL;

                // 如果是局部重绘，则设置任务状态为 modal
                if (submitAction.CustomId.StartsWith("MJ::Inpaint::"))
                {
                    task.Prompt = "";
                    task.PromptEn = "";
                }

                task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                task.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                _freeSql.Save(task);

                // 状态码为 21
                // 重绘、自定义变焦始终 remix 为true
                return SubmitResultVO.Of(ReturnCode.EXISTED, "Waiting for window confirm", task.Id)
                    .SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, task.PromptEn)
                    .SetProperty(Constants.TASK_PROPERTY_REMIX, true);
            }

            // 如果是 remix
            if (submitAction.EnableRemix == true)
            {
                if (instance.Account.IsDiscord)
                {
                    var bt = task.RealBotType ?? task.BotType;

                    // discord 账号判断是否开启 remix
                    if (bt == EBotType.MID_JOURNEY && !instance.Account.MjRemixOn)
                    {
                        return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "当前账号不支持 remix，请开启 remix 功能");
                    }

                    if (bt == EBotType.NIJI_JOURNEY && !instance.Account.NijiRemixOn)
                    {
                        return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "当前账号不支持 remix，请开启 remix 功能");
                    }
                }

                // 悠船、官方账号不判断 remix
                if (task.Action == TaskAction.PAN || task.Action == TaskAction.VARIATION || submitAction.CustomId.StartsWith("MJ::JOB::reroll") || task.Action == TaskAction.VIDEO)
                {
                    task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                    task.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                    // 如果是 REMIX 任务，则设置任务状态为 modal
                    task.Status = TaskStatus.MODAL;
                    _freeSql.Save(task);

                    // 状态码为 21
                    return SubmitResultVO.Of(ReturnCode.EXISTED, "Waiting for window confirm", task.Id)
                        .SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, task.PromptEn)
                        .SetProperty(Constants.TASK_PROPERTY_REMIX, true);
                }
            }

            // 点击喜欢
            if (submitAction.CustomId.Contains("MJ::BOOKMARK"))
            {
                var res = instance.ActionAsync(messageId ?? targetTask.MessageId,
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

            // 手动视频
            else if (submitAction.CustomId.StartsWith("MJ::JOB::video::") && submitAction.CustomId.Contains("::manual"))
            {
                var cmd = targetTask.IsPartner ? targetTask.PartnerTaskInfo?.FullCommand
                    : targetTask.IsOfficial ? targetTask.OfficialTaskInfo?.FullCommand
                    : targetTask.PromptFull;

                if (submitAction.CustomId.Contains("low"))
                {
                    if (!cmd.Contains("--motion"))
                    {
                        cmd += " --motion low";
                    }
                }
                else
                {
                    if (!cmd.Contains("--motion"))
                    {
                        cmd += " --motion high";
                    }
                }

                if (!cmd.Contains("--video"))
                {
                    cmd += " --video 1";
                }

                task.Prompt = cmd;
                task.PromptEn = cmd;
                task.Status = TaskStatus.MODAL;
                task.Action = TaskAction.VIDEO;

                _freeSql.Save(task);

                // 状态码为 21
                // 重绘、自定义变焦始终 remix 为true
                return SubmitResultVO.Of(ReturnCode.EXISTED, "Waiting for window confirm", task.Id)
                    .SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, task.PromptEn)
                    .SetProperty(Constants.TASK_PROPERTY_REMIX, true);
            }
            // describe 全部重新生成绘图
            else if (submitAction.CustomId.Contains("MJ::Job::PicReader::all"))
            {
                var prompts = targetTask.PromptEn.Split('\n').Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
                var ids = new List<string>();
                var count = prompts.Length >= 4 ? 4 : prompts.Length;
                for (int i = 0; i < count; i++)
                {
                    var prompt = prompts[i].Substring(prompts[i].IndexOf(' ')).Trim();

                    var subTask = new TaskInfo()
                    {
                        Id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{RandomHelper.RandomNumbers(3)}",
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
                        RequestMode = task.RequestMode,
                        RemixAutoSubmit = true,
                        SubInstanceId = task.SubInstanceId,
                    };

                    subTask.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.ChannelId);
                    subTask.SetProperty(Constants.TASK_PROPERTY_BOT_TYPE, targetTask.BotType.GetDescription());

                    var nonce = SnowFlake.NextId();
                    subTask.Nonce = nonce;
                    subTask.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);
                    subTask.SetProperty(Constants.TASK_PROPERTY_CUSTOM_ID, $"MJ::Job::PicReader::{i + 1}");

                    subTask.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                    subTask.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                    _freeSql.Save(subTask);

                    var res = await SubmitModal(subTask, new SubmitModalDTO()
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
            else if (submitAction.CustomId.StartsWith("MJ::Job::PicReader::"))
            {
                // 使用正则提取 V index
                var match = Regex.Match(submitAction.CustomId, @"MJ::Job::PicReader::(\d+)");
                if (match.Success)
                {
                    var index = match.Groups[1].Value;
                    if (int.TryParse(index, out int ir) && ir > 0)
                    {
                        var arr = targetTask.PromptEn.Split('\n').Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
                        if (ir > arr.Length)
                        {
                            return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "无效的 PicReader 索引");
                        }

                        var prompt = arr[ir - 1].Trim();

                        task.Prompt = prompt;
                        task.PromptEn = prompt;
                        task.Status = TaskStatus.MODAL;

                        task.SetProperty(Constants.TASK_PROPERTY_ACTION_INDEX, index + 1);
                        task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                        task.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                        _freeSql.Save(task);

                        // 状态码为 21
                        // 重绘、自定义变焦始终 remix 为true
                        return SubmitResultVO.Of(ReturnCode.EXISTED, "Waiting for window confirm", task.Id)
                            .SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, task.PromptEn)
                            .SetProperty(Constants.TASK_PROPERTY_REMIX, true);
                    }
                }

                return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "无效的 PicReader 索引");
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
                    if (instance.Account.RemixAutoSubmit)
                    {
                        task.RemixAutoSubmit = true;
                        _freeSql.Save(task);

                        return await SubmitModal(task, new SubmitModalDTO()
                        {
                            TaskId = task.Id,
                            NotifyHook = submitAction.NotifyHook,
                            Prompt = targetTask.PromptEn,
                            State = submitAction.State
                        });
                    }
                    else
                    {
                        _freeSql.Save(task);

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
            else if (task.Action == TaskAction.PAN
                || task.Action == TaskAction.VARIATION
                || task.Action == TaskAction.VIDEO
                || submitAction.CustomId.StartsWith("MJ::JOB::reroll"))
            {
                task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                task.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                if (instance.Account.RemixAutoSubmit)
                {
                    // 如果开启了 remix 自动提交
                    // 并且已开启 remix 模式
                    if (((task.RealBotType ?? task.BotType) == EBotType.MID_JOURNEY && instance.Account.MjRemixOn)
                        || (task.BotType == EBotType.NIJI_JOURNEY && instance.Account.NijiRemixOn))
                    {
                        task.RemixAutoSubmit = true;

                        _freeSql.Save(task);

                        return await SubmitModal(task, new SubmitModalDTO()
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
                    if (((task.RealBotType ?? task.BotType) == EBotType.MID_JOURNEY && instance.Account.MjRemixOn)
                        || (task.BotType == EBotType.NIJI_JOURNEY && instance.Account.NijiRemixOn))
                    {
                        // 如果是 REMIX 任务，则设置任务状态为 modal
                        task.Status = TaskStatus.MODAL;

                        _freeSql.Save(task);

                        // 状态码为 21
                        return SubmitResultVO.Of(ReturnCode.EXISTED, "Waiting for window confirm", task.Id)
                            .SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, task.PromptEn)
                            .SetProperty(Constants.TASK_PROPERTY_REMIX, true);
                    }
                }
            }

            // 悠船 | 官方
            if (task.IsPartner || task.IsOfficial)
            {
                return await instance.RedisEnqueue(new TaskInfoQueue()
                {
                    Info = task,
                    Function = TaskInfoQueueFunction.ACTION,
                    ActionParam = new TaskInfoQueue.TaskInfoQueueActionParam()
                    {
                        Dto = submitAction,
                        TargetTask = targetTask
                    }
                });
            }
            else
            {
                return await instance.RedisEnqueue(new TaskInfoQueue()
                {
                    Info = task,
                    Function = TaskInfoQueueFunction.ACTION,
                    ActionParam = new TaskInfoQueue.TaskInfoQueueActionParam()
                    {
                        MessageId = messageId ?? targetTask.MessageId,
                        CustomId = submitAction.CustomId,
                        MessageFlags = messageFlags,
                        Nonce = task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default)
                    }
                });
            }
        }

        /// <summary>
        /// 执行 Modal
        /// </summary>
        /// <param name="task"></param>
        /// <param name="submitAction"></param>
        /// <param name="dataUrl"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> SubmitModal(TaskInfo task, SubmitModalDTO submitAction, DataUrl dataUrl = null)
        {
            var setting = GlobalConfiguration.Setting;

            var parentTask = _freeSql.Get<TaskInfo>(task.ParentId);

            var instance = GetInstanceByTask(task, parentTask, out var submitResult);
            if (instance == null || submitResult.Code != ReturnCode.SUCCESS)
            {
                return submitResult;
            }

            task.InstanceId = instance.ChannelId;
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.ChannelId);

            if (task.IsPartner || task.IsOfficial)
            {
                return await instance.RedisEnqueue(new TaskInfoQueue()
                {
                    Info = task,
                    Function = TaskInfoQueueFunction.MODAL,
                    ModalParam = new TaskInfoQueue.TaskInfoQueueModalParam()
                    {
                        TargetTask = parentTask,
                        Dto = submitAction,
                    }
                });
            }

            return await instance.RedisEnqueue(new TaskInfoQueue()
            {
                Info = task,
                Function = TaskInfoQueueFunction.MODAL,
                ModalParam = new TaskInfoQueue.TaskInfoQueueModalParam()
                {
                    Dto = submitAction,
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
            var setting = GlobalConfiguration.Setting;

            var discordInstance = _accountService.GetDiscordInstanceIsAlive(task.InstanceId);
            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            // 获取 seed 不需要判断额度队列等

            // redis 模式
            // 如果是悠船则直接获取
            if (task.IsPartner)
            {
                var seek = await discordInstance.YmTaskService.GetSeed(task);
                if (!string.IsNullOrWhiteSpace(seek))
                {
                    task.Seed = seek;
                    _freeSql.Save(task);
                    return SubmitResultVO.Of(ReturnCode.SUCCESS, "成功", seek);
                }
                else
                {
                    return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "未找到 seed");
                }
            }
            else
            {
                // 清空错误
                task.SeedError = null;
                _freeSql.Save(task);

                // 否则由其他节点队列处理
                // 发送 redis 消息后并等待结果返回
                var notification = new RedisNotification
                {
                    Type = ENotificationType.SeedTaskInfo,
                    TaskInfo = task,
                    ChannelId = task.InstanceId
                };
                RedisHelper.Publish(RedisHelper.Prefix + Constants.REDIS_NOTIFY_CHANNEL, notification.ToJson());

                var maxDelay = 1000 * 60;
                do
                {
                    await Task.Delay(500);
                    maxDelay -= 500;

                    task = _freeSql.Get<TaskInfo>(task.Id);
                    if (!string.IsNullOrWhiteSpace(task.Seed))
                    {
                        return SubmitResultVO.Of(ReturnCode.SUCCESS, "成功", task.Seed);
                    }
                    else if (!string.IsNullOrWhiteSpace(task.SeedError))
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, task.SeedError);
                    }
                } while (string.IsNullOrWhiteSpace(task.Seed) && maxDelay > 0);

                task = _freeSql.Get<TaskInfo>(task.Id);

                if (!string.IsNullOrWhiteSpace(task.Seed))
                {
                    return SubmitResultVO.Of(ReturnCode.SUCCESS, "成功", task.Seed);
                }
                else if (!string.IsNullOrWhiteSpace(task.SeedError))
                {
                    return SubmitResultVO.Fail(ReturnCode.FAILURE, task.SeedError);
                }
                else
                {
                    return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "未找到 seed");
                }
            }
        }

        /// <summary>
        /// 执行 info setting 操作
        /// </summary>
        /// <returns></returns>
        public async Task<bool> SyncInfoSetting(string id, bool isClearCache = false)
        {
            var model = _freeSql.Get<DiscordAccount>(id);
            if (model == null)
            {
                throw new LogicException("未找到账号实例");
            }

            var discordInstance = _accountService.GetDiscordInstance(model.ChannelId);
            if (discordInstance == null)
            {
                throw new LogicException("无可用的账号实例");
            }

            if (isClearCache)
            {
                discordInstance.ClearSyncInfoCache();
            }

            return await discordInstance.SyncInfoSetting(isClearCache);
        }

        /// <summary>
        /// 修改版本
        /// </summary>
        /// <param name="id"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        public async Task<bool> AccountChangeVersion(string id, string version)
        {
            var model = _freeSql.Get<DiscordAccount>(id);
            if (model == null)
            {
                throw new LogicException("未找到账号实例");
            }

            var discordInstance = _accountService.GetDiscordInstanceIsAlive(model.ChannelId);
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

            Thread.Sleep(5000);

            return await SyncInfoSetting(id, true);
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
            var model = _freeSql.Get<DiscordAccount>(id);
            if (model == null)
            {
                throw new LogicException("未找到账号实例");
            }

            var discordInstance = _accountService.GetDiscordInstanceIsAlive(model.ChannelId);
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

            await SyncInfoSetting(id);
        }

        /// <summary>
        /// 暂存编辑/转绘的基础图片。base64 写入 Redis，由工作线程负责 WebP 转换和上传；URL 直接复用。
        /// </summary>
        private static SubmitResultVO StageBaseImageInput(TaskInfo info, DataUrl dataUrl, out List<string> stagedKeys)
        {
            stagedKeys = [];

            if (dataUrl?.Data?.Length > 0)
            {
                var ttl = ImagineInputStager.GetTtl();
                var stage = ImagineInputStager.Stage(
                    info,
                    [dataUrl],
                    (int)ttl.TotalSeconds,
                    (key, bytes, seconds) => RedisHelper.Instance.Set(key, bytes, seconds));

                if (!stage.Success)
                {
                    Log.Warning("编辑图片暂存失败: TaskId={TaskId}, Error={Error}, Staged={Staged}", info.Id, stage.Error, stage.StagedKeys.Count);
                    DeleteStagedImagineInputs(info.Id, stage.StagedKeys);
                    return SubmitResultVO.Fail(ReturnCode.FAILURE, "图片暂存失败，请稍后重试");
                }

                stagedKeys = stage.StagedKeys;
                return null;
            }

            if (!string.IsNullOrWhiteSpace(dataUrl?.Url))
            {
                info.BaseImageUrl = dataUrl.Url;
                return null;
            }

            return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "图片参数错误");
        }

        /// <summary>
        /// 暂存视频首帧/尾帧。base64 写入 Redis，URL 作为引用交给工作线程解析。
        /// </summary>
        private static SubmitResultVO StageVideoInputs(TaskInfo info, DataUrl startInput, DataUrl endInput, out List<string> stagedKeys)
        {
            stagedKeys = [];

            if (startInput == null && endInput == null)
            {
                return null;
            }

            var ttl = ImagineInputStager.GetTtl();
            var stage = ImagineInputStager.Stage(
                info,
                [startInput, endInput],
                (int)ttl.TotalSeconds,
                (key, bytes, seconds) => RedisHelper.Instance.Set(key, bytes, seconds),
                urlOnlyAsPrompt: false);

            if (!stage.Success)
            {
                Log.Warning("视频图片暂存失败: TaskId={TaskId}, Error={Error}, Staged={Staged}", info.Id, stage.Error, stage.StagedKeys.Count);
                DeleteStagedImagineInputs(info.Id, stage.StagedKeys);
                return SubmitResultVO.Fail(ReturnCode.FAILURE, "视频图片暂存失败，请稍后重试");
            }

            stagedKeys = stage.StagedKeys;
            return null;
        }

        /// <summary>
        /// 视频入队；失败时清理已暂存的图片数据。
        /// </summary>
        private async Task<SubmitResultVO> EnqueueVideoAsync(
            TaskInfo info,
            DiscordService instance,
            TaskInfoQueue queue,
            List<string> stagedKeys)
        {
            try
            {
                var submitResult = await instance.RedisEnqueue(queue);
                if (submitResult?.Code != ReturnCode.SUCCESS && submitResult?.Code != ReturnCode.IN_QUEUE)
                {
                    DeleteStagedImagineInputs(info.Id, stagedKeys);
                }

                return submitResult;
            }
            catch
            {
                DeleteStagedImagineInputs(info.Id, stagedKeys);
                throw;
            }
        }

        /// <summary>
        /// 入队 imagine 任务
        /// </summary>
        private static async Task<SubmitResultVO> EnqueueImagineAsync(TaskInfo info, DiscordService instance)
        {
            var enqueueSw = Stopwatch.StartNew();
            var submitResult = await instance.RedisEnqueue(new TaskInfoQueue()
            {
                Info = info,
                Function = TaskInfoQueueFunction.SUBMIT
            });
            Log.Information("SubmitImagine 入队完成: TaskId={TaskId}, Elapsed={Elapsed}ms, Code={Code}",
                info.Id, enqueueSw.ElapsedMilliseconds, submitResult?.Code);
            return submitResult;
        }

        /// <summary>
        /// 异步垫图提交：URL 直接写入提示词，base64 原始字节暂存到 Redis。
        /// 暂存失败直接返回失败，不再回退同步上传。
        /// </summary>
        private static async Task<SubmitResultVO> SubmitImagineWithStagedInputsAsync(
            TaskInfo info,
            List<DataUrl> dataUrls,
            DiscordService instance)
        {
            var sw = Stopwatch.StartNew();
            var originalPrompt = info.Prompt;
            var originalPromptEn = info.PromptEn;
            var originalDescription = info.Description;
            var ttl = ImagineInputStager.GetTtl();
            ImagineStageResult stage;

            try
            {
                stage = ImagineInputStager.Stage(
                    info,
                    dataUrls,
                    (int)ttl.TotalSeconds,
                    (key, bytes, seconds) => RedisHelper.Instance.Set(key, bytes, seconds));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SubmitImagine 异步垫图暂存异常: TaskId={TaskId}", info.Id);

                info.Prompt = originalPrompt;
                info.PromptEn = originalPromptEn;
                info.Description = originalDescription;
                info.RemoveProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS);
                info.RemoveProperty(Constants.TASK_PROPERTY_IMAGINE_NEED_PREPARE);

                return SubmitResultVO.Fail(ReturnCode.FAILURE, "垫图暂存失败，请稍后重试");
            }

            if (!stage.Success)
            {
                Log.Warning("SubmitImagine 异步垫图暂存失败: TaskId={TaskId}, Error={Error}, Staged={Staged}",
                    info.Id, stage.Error, stage.StagedKeys.Count);

                info.Prompt = originalPrompt;
                info.PromptEn = originalPromptEn;
                info.Description = originalDescription;
                info.RemoveProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS);
                info.RemoveProperty(Constants.TASK_PROPERTY_IMAGINE_NEED_PREPARE);

                DeleteStagedImagineInputs(info.Id, stage.StagedKeys);
                return SubmitResultVO.Fail(ReturnCode.FAILURE, "垫图暂存失败，请稍后重试");
            }

            var stagedKeys = stage.StagedKeys;

            // 无垫图任务不存在暂存工作，不打印该日志，避免所有 imagine 任务都产生一条无意义的“暂存完成”
            if (stage.Refs.Count > 0 || stage.UrlOnly.Count > 0)
            {
                Log.Information("SubmitImagine 异步垫图暂存完成: TaskId={TaskId}, Staged={Staged}, UrlOnly={UrlOnly}, TtlMinutes={TtlMinutes}, Elapsed={Elapsed}ms",
                    info.Id, stage.Refs.Count, stage.UrlOnly.Count, (int)ttl.TotalMinutes, sw.ElapsedMilliseconds);
            }

            // 暂存成功后再入队。入队异常不能回退同步上传，否则会重复上传图片。
            try
            {
                var submitResult = await EnqueueImagineAsync(info, instance);
                if (submitResult?.Code != ReturnCode.SUCCESS && submitResult?.Code != ReturnCode.IN_QUEUE)
                {
                    DeleteStagedImagineInputs(info.Id, stagedKeys);
                }

                return submitResult;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SubmitImagine 入队异常: TaskId={TaskId}, Staged={Staged}", info.Id, stagedKeys.Count);

                info.Prompt = originalPrompt;
                info.PromptEn = originalPromptEn;
                info.Description = originalDescription;
                info.RemoveProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS);
                info.RemoveProperty(Constants.TASK_PROPERTY_IMAGINE_NEED_PREPARE);

                DeleteStagedImagineInputs(info.Id, stagedKeys);
                return SubmitResultVO.Fail(ReturnCode.FAILURE, "提交任务失败，请稍后重试");
            }
        }

        /// <summary>
        /// 删除暂存的垫图数据
        /// </summary>
        private static void DeleteStagedImagineInputs(string taskId, List<string> keys)
        {
            if (keys == null || keys.Count == 0)
            {
                return;
            }

            try
            {
                RedisHelper.Instance.Del(keys.ToArray());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "删除异步垫图暂存失败: TaskId={TaskId}, Count={Count}", taskId, keys.Count);
            }
        }

    }
}

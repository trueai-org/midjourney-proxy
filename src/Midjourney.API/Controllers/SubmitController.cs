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

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.Util;
using System.Net;
using System.Text.RegularExpressions;

using TaskStatus = Midjourney.Infrastructure.TaskStatus;

namespace Midjourney.API.Controllers
{
    /// <summary>
    /// 任务提交控制器
    /// </summary>
    [ApiController]
    [Route("mj/submit")]
    [Route("mj-fast/mj/submit")]
    [Route("mj-turbo/mj/submit")]
    [Route("mj-relax/mj/submit")]
    public class SubmitController : ControllerBase
    {
        private readonly ITranslateService _translateService;
        private readonly ITaskStoreService _taskStoreService;

        private readonly DiscordHelper _discordHelper;
        private readonly ProxyProperties _properties;
        private readonly ITaskService _taskService;
        private readonly ILogger<SubmitController> _logger;
        private readonly string _ip;

        private readonly DiscordLoadBalancer _discordLoadBalancer;
        private readonly GenerationSpeedMode? _mode;
        private readonly WorkContext _workContext;
        private readonly IMemoryCache _memoryCache;

        public SubmitController(
            ITranslateService translateService,
            ITaskStoreService taskStoreService,
            ITaskService taskService,
            ILogger<SubmitController> logger,
            DiscordHelper discordHelper,
            IHttpContextAccessor httpContextAccessor,
            WorkContext workContext,
            DiscordLoadBalancer discordLoadBalancer,
            IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
            _translateService = translateService;
            _taskStoreService = taskStoreService;
            _properties = GlobalConfiguration.Setting;
            _taskService = taskService;
            _logger = logger;
            _discordHelper = discordHelper;
            _workContext = workContext;
            _discordLoadBalancer = discordLoadBalancer;

            var user = _workContext.GetUser();

            // 如果非演示模式、未开启访客，如果没有登录，直接返回 403 错误
            if (GlobalConfiguration.IsDemoMode != true
                && GlobalConfiguration.Setting.EnableGuest != true)
            {
                if (user == null)
                {
                    // 如果是普通用户, 并且不是匿名控制器，则返回 403
                    httpContextAccessor.HttpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    httpContextAccessor.HttpContext.Response.WriteAsync("未登录");
                    return;
                }
            }

            _ip = httpContextAccessor.HttpContext.Request.GetIP();

            var mode = httpContextAccessor.HttpContext.Items["Mode"]?.ToString()?.ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(mode))
            {
                _mode = mode switch
                {
                    "turbo" => GenerationSpeedMode.TURBO,
                    "relax" => GenerationSpeedMode.RELAX,
                    "fast" => GenerationSpeedMode.FAST,
                    _ => null
                };
            }
        }

        /// <summary>
        /// 提交Imagine任务
        /// </summary>
        /// <param name="imagineDTO">提交Imagine任务的DTO</param>
        /// <returns>提交结果</returns>
        [HttpPost("imagine")]
        public ActionResult<SubmitResultVO> Imagine([FromBody] SubmitImagineDTO imagineDTO)
        {
            string prompt = imagineDTO.Prompt;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "prompt不能为空"));
            }

            var base64Array = imagineDTO.Base64Array ?? [];

            var setting = GlobalConfiguration.Setting;
            if (!setting.EnableUserCustomUploadBase64 && base64Array.Count > 0)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "禁止上传"));
            }

            prompt = prompt.Trim();
            var task = NewTask(imagineDTO);
            task.Action = TaskAction.IMAGINE;
            task.Prompt = prompt;
            task.BotType = GetBotType(imagineDTO.BotType);

            // 转换 --niji 为 Niji Bot
            if (GlobalConfiguration.Setting.EnableConvertNijiToNijiBot
                && prompt.Contains("--niji")
                && task.BotType == EBotType.MID_JOURNEY)
            {
                task.BotType = EBotType.NIJI_JOURNEY;
            }

            string promptEn = TranslatePrompt(prompt, task.RealBotType ?? task.BotType);
            try
            {
                _taskService.CheckBanned(promptEn);
            }
            catch (BannedPromptException e)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.BANNED_PROMPT, "可能包含敏感词")
                    .SetProperty("promptEn", promptEn)
                    .SetProperty("bannedWord", e.Message));
            }

            List<DataUrl> dataUrls = new List<DataUrl>();
            try
            {
                dataUrls = ConvertUtils.ConvertBase64Array(base64Array);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "base64格式转换异常");
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "base64格式错误"));
            }

            task.PromptEn = promptEn;
            task.Description = $"/imagine {prompt}";

            NewTaskDoFilter(task, imagineDTO.AccountFilter);

            var data = _taskService.SubmitImagine(task, dataUrls);
            return Ok(data);
        }

        /// <summary>
        /// 上传图片到 Discord
        /// </summary>
        /// <param name="imagineDTO"></param>
        /// <returns></returns>
        [HttpPost("upload-discord-images")]
        public async Task<ActionResult<SubmitResultVO>> ImagineDiscordImages([FromBody] SubmitUploadDiscordDto imagineDTO)
        {
            var setting = GlobalConfiguration.Setting;
            if (!setting.EnableUserCustomUploadBase64)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "禁止上传"));
            }

            var base64Array = imagineDTO.Base64Array ?? new List<string>();
            if (base64Array.Count <= 0)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "base64 参数错误"));
            }

            var dataUrls = new List<DataUrl>();
            try
            {
                dataUrls = ConvertUtils.ConvertBase64Array(base64Array);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "base64格式转换异常");
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "base64格式错误"));
            }

            var instance = _discordLoadBalancer.ChooseInstance(imagineDTO.AccountFilter);
            if (instance == null)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "实例不存在或不可用"));
            }

            var imageUrls = new List<string>();
            foreach (var dataUrl in dataUrls)
            {
                var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                var uploadResult = await instance.UploadAsync(taskFileName, dataUrl);
                if (uploadResult.Code != ReturnCode.SUCCESS)
                {
                    return Ok(Message.Of(uploadResult.Code, uploadResult.Description));
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
                        return Ok(Message.Of(sendImageResult.Code, sendImageResult.Description));
                    }
                    imageUrls.Add(sendImageResult.Description);
                }
            }

            var info = SubmitResultVO.Of(ReturnCode.SUCCESS, "提交成功", imageUrls);
            return Ok(info);
        }

        /// <summary>
        /// 提交 show 任务
        /// </summary>
        /// <param name="imagineDTO"></param>
        /// <returns></returns>
        [HttpPost("show")]
        public ActionResult<SubmitResultVO> Show([FromBody] SubmitShowDTO imagineDTO)
        {
            string jobId = imagineDTO.JobId;
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "请填写 job id 或 url"));
            }
            jobId = jobId.Trim();

            if (jobId.Length != 36)
            {
                jobId = _discordHelper.GetMessageHash(jobId);
            }

            if (string.IsNullOrWhiteSpace(jobId))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "job id 格式错误"));
            }

            var model = DbHelper.Instance.TaskStore.Where(c => c.JobId == jobId && c.Status == TaskStatus.SUCCESS).FirstOrDefault();
            if (model != null)
            {
                var info = SubmitResultVO.Of(ReturnCode.SUCCESS, "提交成功", model.Id)
                    .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, model.InstanceId);
                return Ok(info);
            }

            if (string.IsNullOrWhiteSpace(imagineDTO.AccountFilter?.InstanceId))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "show 命令必须指定实例"));
            }

            var task = NewTask(imagineDTO);

            task.Action = TaskAction.SHOW;
            task.BotType = GetBotType(imagineDTO.BotType);
            task.Description = $"/show {jobId}";
            task.JobId = jobId;

            NewTaskDoFilter(task, imagineDTO.AccountFilter);

            var data = _taskService.ShowImagine(task);
            return Ok(data);
        }

        /// <summary>
        /// 简单变化
        /// </summary>
        /// <param name="simpleChangeDTO">提交简单变化任务的DTO</param>
        /// <returns>提交结果</returns>
        [HttpPost("simple-change")]
        public ActionResult<SubmitResultVO> SimpleChange([FromBody] SubmitSimpleChangeDTO simpleChangeDTO)
        {
            var changeParams = ConvertUtils.ConvertChangeParams(simpleChangeDTO.Content);
            if (changeParams == null)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "content 参数错误"));
            }
            var changeDTO = new SubmitChangeDTO
            {
                Action = changeParams.Action,
                TaskId = changeParams.Id,
                Index = changeParams.Index,
                State = simpleChangeDTO.State,
                NotifyHook = simpleChangeDTO.NotifyHook
            };
            return Change(changeDTO);
        }

        /// <summary>
        /// 任务变化
        /// </summary>
        /// <param name="changeDTO">提交变化任务的DTO</param>
        /// <returns>提交结果</returns>
        [HttpPost("change")]
        public ActionResult<SubmitResultVO> Change([FromBody] SubmitChangeDTO changeDTO)
        {
            if (string.IsNullOrWhiteSpace(changeDTO.TaskId))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "taskId不能为空"));
            }
            if (!new[] { TaskAction.UPSCALE, TaskAction.VARIATION, TaskAction.REROLL }.Contains(changeDTO.Action))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "action参数错误"));
            }
            string description = $"/up {changeDTO.TaskId}";
            if (changeDTO.Action == TaskAction.REROLL)
            {
                description += " R";
            }
            else
            {
                description += $" {changeDTO.Action.ToString()[0]}{changeDTO.Index}";
            }
            var targetTask = _taskStoreService.Get(changeDTO.TaskId);
            if (targetTask == null)
            {
                return NotFound(SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "关联任务不存在或已失效"));
            }
            if (targetTask.Status != TaskStatus.SUCCESS)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "关联任务状态错误"));
            }
            if (!new[] { TaskAction.IMAGINE, TaskAction.VARIATION, TaskAction.REROLL, TaskAction.BLEND }.Contains(targetTask.Action.Value))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "关联任务不允许执行变化"));
            }
            var task = NewTask(changeDTO);

            task.Action = changeDTO.Action;
            task.BotType = targetTask.BotType;
            task.RealBotType = targetTask.RealBotType;
            task.ParentId = targetTask.Id;
            task.Prompt = targetTask.Prompt;
            task.PromptEn = targetTask.PromptEn;

            task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, targetTask.GetProperty<string>(Constants.TASK_PROPERTY_FINAL_PROMPT, default));
            task.SetProperty(Constants.TASK_PROPERTY_PROGRESS_MESSAGE_ID, targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_ID, default));
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, targetTask.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default));

            task.InstanceId = targetTask.InstanceId;
            task.Description = description;

            var messageFlags = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_FLAGS, default)?.ToInt() ?? 0;
            var messageId = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_ID, default);
            var messageHash = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_HASH, default);
            task.SetProperty(Constants.TASK_PROPERTY_REFERENCED_MESSAGE_ID, messageId);
            if (changeDTO.Action == TaskAction.UPSCALE)
            {
                return Ok(_taskService.SubmitUpscale(task, messageId, messageHash, changeDTO.Index ?? 1, messageFlags));
            }
            else if (changeDTO.Action == TaskAction.VARIATION)
            {
                return Ok(_taskService.SubmitVariation(task, messageId, messageHash, changeDTO.Index ?? 1, messageFlags));
            }
            else
            {
                return Ok(_taskService.SubmitReroll(task, messageId, messageHash, messageFlags));
            }
        }

        /// <summary>
        /// 提交Describe任务
        /// </summary>
        /// <param name="describeDTO">提交Describe任务的DTO</param>
        /// <returns>提交结果</returns>
        [HttpPost("describe")]
        public ActionResult<SubmitResultVO> Describe([FromBody] SubmitDescribeDTO describeDTO)
        {
            if (string.IsNullOrWhiteSpace(describeDTO.Base64))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "base64不能为空"));
            }

            var setting = GlobalConfiguration.Setting;
            if (!setting.EnableUserCustomUploadBase64)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "禁止上传"));
            }

            DataUrl dataUrl;
            try
            {
                dataUrl = DataUrl.Parse(describeDTO.Base64);
            }
            catch
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "base64格式错误"));
            }

            var task = NewTask(describeDTO);

            task.BotType = GetBotType(describeDTO.BotType);
            task.Action = TaskAction.DESCRIBE;

            string taskFileName = $"{task.Id}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
            task.Description = $"/describe {taskFileName}";

            NewTaskDoFilter(task, describeDTO.AccountFilter);

            return Ok(_taskService.SubmitDescribe(task, dataUrl));
        }

        /// <summary>
        /// 上传一个较长的提示词，mj 可以返回一组简要的提示词
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        [HttpPost("shorten")]
        public ActionResult<SubmitResultVO> Shorten([FromBody] SubmitImagineDTO dto)
        {
            var task = NewTask(dto);

            task.BotType = GetBotType(dto.BotType);
            task.Action = TaskAction.SHORTEN;

            var prompt = dto.Prompt;
            task.Prompt = prompt;

            var promptEn = TranslatePrompt(prompt, task.RealBotType ?? task.BotType);
            try
            {
                _taskService.CheckBanned(promptEn);
            }
            catch (BannedPromptException e)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.BANNED_PROMPT, "可能包含敏感词")
                    .SetProperty("promptEn", promptEn)
                    .SetProperty("bannedWord", e.Message));
            }

            task.PromptEn = promptEn;
            task.Description = $"/shorten {prompt}";

            NewTaskDoFilter(task, dto.AccountFilter);

            return Ok(_taskService.ShortenAsync(task));
        }

        /// <summary>
        /// 提交Blend任务
        /// </summary>
        /// <param name="blendDTO">提交Blend任务的DTO</param>
        /// <returns>提交结果</returns>
        [HttpPost("blend")]
        public ActionResult<SubmitResultVO> Blend([FromBody] SubmitBlendDTO blendDTO)
        {
            List<string> base64Array = blendDTO.Base64Array;
            if (base64Array == null || base64Array.Count < 2 || base64Array.Count > 5)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "base64List参数错误"));
            }

            // blend 不限制上传
            //var setting = GlobalConfiguration.Setting;
            //if (!setting.EnableUserCustomUploadBase64)
            //{
            //    return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "禁止上传"));
            //}

            if (blendDTO.Dimensions == null)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "dimensions参数错误"));
            }

            List<DataUrl> dataUrlList = new List<DataUrl>();
            try
            {
                dataUrlList = ConvertUtils.ConvertBase64Array(base64Array);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "base64格式错误");

                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "base64格式错误"));
            }
            var task = NewTask(blendDTO);

            task.BotType = GetBotType(blendDTO.BotType);
            task.Action = TaskAction.BLEND;
            task.Description = $"/blend {task.Id} {dataUrlList.Count}";

            NewTaskDoFilter(task, blendDTO.AccountFilter);

            return Ok(_taskService.SubmitBlend(task, dataUrlList, blendDTO.Dimensions.Value));
        }

        /// <summary>
        /// 执行动作
        /// MJ::JOB::upsample::2::3dbbd469-36af-4a0f-8f02-df6c579e7011
        /// </summary>
        /// <param name="actionDTO"></param>
        /// <returns></returns>
        [HttpPost("action")]
        public ActionResult<SubmitResultVO> Action([FromBody] SubmitActionDTO actionDTO)
        {
            if (string.IsNullOrWhiteSpace(actionDTO.TaskId) || string.IsNullOrWhiteSpace(actionDTO.CustomId))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "参数错误"));
            }

            var targetTask = _taskStoreService.Get(actionDTO.TaskId);
            if (targetTask == null)
            {
                return NotFound(SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "关联任务不存在或已失效"));
            }

            if (targetTask.Status != TaskStatus.SUCCESS)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "关联任务状态错误"));
            }

            var task = NewTask(actionDTO);

            task.InstanceId = targetTask.InstanceId;
            task.ParentId = targetTask.Id;
            task.BotType = targetTask.BotType;
            task.RealBotType = targetTask.RealBotType;
            task.SubInstanceId = targetTask.SubInstanceId;

            // 识别 mj action

            // 放大
            // MJ::JOB::upsample::2::898416ec-7c18-4762-bf03-8e428fee1860
            if (actionDTO.CustomId.StartsWith("MJ::JOB::upsample::"))
            {
                task.Action = TaskAction.UPSCALE;

                // 在进行 U 时，记录目标图片的 U 的 customId
                task.SetProperty(Constants.TASK_PROPERTY_REMIX_U_CUSTOM_ID, actionDTO.CustomId);
            }
            // 微调
            // MJ::JOB::variation::2::898416ec-7c18-4762-bf03-8e428fee1860
            else if (actionDTO.CustomId.StartsWith("MJ::JOB::variation::"))
            {
                task.Action = TaskAction.VARIATION;
            }
            // 重绘
            // MJ::JOB::reroll::0::898416ec-7c18-4762-bf03-8e428fee1860::SOLO
            else if (actionDTO.CustomId.StartsWith("MJ::JOB::reroll::"))
            {
                task.Action = TaskAction.REROLL;
            }
            // 强变化
            // MJ::JOB::high_variation::1::7af96d1a-67c7-4d74-b173-8430c98c7631::SOLO
            else if (actionDTO.CustomId.StartsWith("MJ::JOB::high_variation::"))
            {
                task.Action = TaskAction.VARIATION;
            }
            // 弱变化
            // MJ::JOB::low_variation::1::7af96d1a-67c7-4d74-b173-8430c98c7631::SOLO
            else if (actionDTO.CustomId.StartsWith("MJ::JOB::low_variation::"))
            {
                task.Action = TaskAction.VARIATION;
            }
            // 变焦
            // MJ::Outpaint::50::1::7af96d1a-67c7-4d74-b173-8430c98c7631::SOLO
            else if (actionDTO.CustomId.StartsWith("MJ::Outpaint::"))
            {
                task.Action = TaskAction.ACTION;
            }
            // 平移
            // MJ::JOB::pan_left::1::7af96d1a-67c7-4d74-b173-8430c98c7631::SOLO
            else if (actionDTO.CustomId.StartsWith("MJ::JOB::pan_"))
            {
                task.Action = TaskAction.PAN;
            }
            // 高清 2x / 2x 创意
            // MJ::JOB::upsample_v6_2x_subtle::1::7af96d1a-67c7-4d74-b173-8430c98c7631::SOLO
            // MJ::JOB::upsample_v6_2x_creative::1::7af96d1a-67c7-4d74-b173-8430c98c7631::SOLO
            else if (actionDTO.CustomId.StartsWith("MJ::JOB::upsample_"))
            {
                task.Action = TaskAction.ACTION;
            }
            // 自定义变焦
            // "MJ::CustomZoom::439f8670-52e8-4f57-afaa-fa08f6d6c751"
            else if (actionDTO.CustomId.StartsWith("MJ::CustomZoom::"))
            {
                task.Action = TaskAction.ACTION;
                task.Description = "Waiting for window confirm";
            }
            // 局部绘制
            // MJ::Inpaint::1::da2b1fda-0455-4952-9f0e-d4cb891f8b1e::SOLO
            else if (actionDTO.CustomId.StartsWith("MJ::Inpaint::"))
            {
                task.Action = TaskAction.INPAINT;
            }
            // describe 重新提交
            else if (actionDTO.CustomId.Contains("MJ::Picread::Retry"))
            {
                task.ImageUrl = targetTask.ImageUrl;
                task.Action = TaskAction.DESCRIBE;
            }
            else
            {
                task.Action = TaskAction.ACTION;
            }

            return Ok(_taskService.SubmitAction(task, actionDTO));
        }

        /// <summary>
        /// 提交 Modal
        /// </summary>
        /// <param name="actionDTO"></param>
        /// <returns></returns>
        [HttpPost("modal")]
        public ActionResult<SubmitResultVO> Modal([FromBody] SubmitModalDTO actionDTO)
        {
            if (string.IsNullOrWhiteSpace(actionDTO.TaskId) || string.IsNullOrWhiteSpace(actionDTO.Prompt))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "参数错误"));
            }

            var targetTask = _taskStoreService.Get(actionDTO.TaskId);
            if (targetTask == null)
            {
                return NotFound(SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "关联任务不存在或已失效"));
            }

            var prompt = actionDTO.Prompt;
            var task = targetTask;

            var promptEn = TranslatePrompt(prompt, task.RealBotType ?? task.BotType);
            try
            {
                _taskService.CheckBanned(promptEn);
            }
            catch (BannedPromptException e)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.BANNED_PROMPT, "可能包含敏感词")
                    .SetProperty("promptEn", promptEn)
                    .SetProperty("bannedWord", e.Message));
            }

            // 不检查
            DataUrl dataUrl = null;
            try
            {
                //dataUrl = DataUrl.Parse(actionDTO.MaskBase64);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "base64格式转换异常");

                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "base64格式错误"));
            }

            if (string.IsNullOrWhiteSpace(task.Prompt))
            {
                task.Prompt = prompt;
            }

            task.PromptEn = promptEn;

            // 提交 modal 指示为 true
            task.RemixAutoSubmit = true;

            return Ok(_taskService.SubmitModal(task, actionDTO, dataUrl));
        }

        /// <summary>
        /// 创建新的任务对象
        /// </summary>
        /// <param name="baseDTO"></param>
        /// <returns></returns>
        private TaskInfo NewTask(BaseSubmitDTO baseDTO)
        {
            var user = _workContext.GetUser();

            var task = new TaskInfo
            {
                Id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{RandomUtils.RandomNumbers(3)}",
                SubmitTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                State = baseDTO.State,
                Status = TaskStatus.NOT_START,
                ClientIp = _ip,
                Mode = _mode,
                UserId = user?.Id,
                IsWhite = user?.IsWhite ?? false
            };

            // niji 转 mj
            if (GlobalConfiguration.Setting.EnableConvertNijiToMj)
            {
                task.RealBotType = EBotType.MID_JOURNEY;
            }

            var now = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();

            // 计算当前 ip 当日第几次绘图
            // 如果不是白名单用户，则计算 ip 绘图限制
            if (user == null || user.IsWhite != true)
            {
                // 访客绘图绘图上限验证
                if (user == null)
                {
                    if (GlobalConfiguration.Setting.GuestDefaultDayLimit > 0)
                    {
                        var ipTodayDrawCount = (int)DbHelper.Instance.TaskStore.Count(x => x.SubmitTime >= now && x.ClientIp == _ip);
                        if (ipTodayDrawCount > GlobalConfiguration.Setting.GuestDefaultDayLimit)
                        {
                            throw new LogicException("今日绘图次数已达上限");
                        }
                    }
                }

                var ban = GlobalConfiguration.Setting.BannedLimiting;
                if (ban?.Enable == true && ban?.Rules?.Count > 0)
                {
                    // 用户封锁验证
                    if (!string.IsNullOrWhiteSpace(task.UserId))
                    {
                        // user band
                        var bandKey = $"banned:{DateTime.Now.Date:yyyyMMdd}:{task.UserId}";
                        _memoryCache.TryGetValue(bandKey, out int limit);
                        if (limit > 0)
                        {
                            var lockKey = $"banned:lock:{task.UserId}";
                            if (_memoryCache.TryGetValue(lockKey, out int lockValue) && lockValue > 0)
                            {
                                throw new LogicException("账号已被临时封锁，请勿使用违规词作图");
                            }

                            foreach (var item in ban.Rules.OrderByDescending(c => c.Key))
                            {
                                // 触发次数
                                // 设置封锁时间
                                if (limit >= item.Key && item.Value > 0)
                                {
                                    _memoryCache.Set(lockKey, limit, TimeSpan.FromMinutes(item.Value));
                                    break;
                                }
                            }

                            if (_memoryCache.TryGetValue(lockKey, out int lockValue2) && lockValue2 > 0)
                            {
                                throw new LogicException("账号已被临时封锁，请勿使用违规词作图");
                            }
                        }
                    }

                    // 访客封锁验证
                    if (user == null)
                    {
                        // ip band
                        var bandKey = $"banned:{DateTime.Now.Date:yyyyMMdd}:{task.ClientIp}";
                        _memoryCache.TryGetValue(bandKey, out int limit);
                        if (limit > 0)
                        {
                            var lockKey = $"banned:lock:{task.ClientIp}";
                            if (_memoryCache.TryGetValue(lockKey, out int lockValue) && lockValue > 0)
                            {
                                throw new LogicException("账号已被临时封锁，请勿使用违规词作图");
                            }

                            foreach (var item in ban.Rules.OrderByDescending(c => c.Key))
                            {
                                // 触发次数
                                // 设置封锁时间
                                if (limit >= item.Key && item.Value > 0)
                                {
                                    _memoryCache.Set(lockKey, limit, TimeSpan.FromMinutes(item.Value));
                                    break;
                                }
                            }

                            if (_memoryCache.TryGetValue(lockKey, out int lockValue2) && lockValue2 > 0)
                            {
                                throw new LogicException("账号已被临时封锁，请勿使用违规词作图");
                            }
                        }
                    }
                }
            }

            // 计算当前用户当日第几次绘图
            if (!string.IsNullOrWhiteSpace(user?.Id))
            {
                if (user.DayDrawLimit > 0)
                {
                    var userTodayDrawCount = (int)DbHelper.Instance.TaskStore.Count(x => x.SubmitTime >= now && x.UserId == user.Id);
                    if (userTodayDrawCount > user.DayDrawLimit)
                    {
                        throw new LogicException("今日绘图次数已达上限");
                    }
                }
            }

            var notifyHook = string.IsNullOrWhiteSpace(baseDTO.NotifyHook) ? _properties.NotifyHook : baseDTO.NotifyHook;
            task.SetProperty(Constants.TASK_PROPERTY_NOTIFY_HOOK, notifyHook);

            var nonce = SnowFlake.NextId();
            task.Nonce = nonce;

            task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

            return task;
        }

        /// <summary>
        /// 处理 account filter 和 mode
        /// </summary>
        /// <param name="task"></param>
        /// <param name="accountFilter"></param>
        private void NewTaskDoFilter(TaskInfo task, AccountFilter accountFilter)
        {
            task.AccountFilter = accountFilter;
            task.SetProperty(Constants.TASK_PROPERTY_BOT_TYPE, task.BotType.GetDescription());

            if (_mode != null)
            {
                if (task.AccountFilter == null)
                {
                    task.AccountFilter = new AccountFilter();
                }

                if (!task.AccountFilter.Modes.Contains(_mode.Value))
                {
                    task.AccountFilter.Modes.Add(_mode.Value);
                }
            }
        }

        /// <summary>
        /// 翻译提示词
        /// </summary>
        /// <param name="prompt">提示词</param>
        /// <param name="botType"></param>
        /// <returns>翻译后的提示词</returns>
        private string TranslatePrompt(string prompt, EBotType botType)
        {
            var setting = GlobalConfiguration.Setting;

            if (_properties.TranslateWay == TranslateWay.NULL || string.IsNullOrWhiteSpace(prompt) || !_translateService.ContainsChinese(prompt))
            {
                return prompt;
            }

            // 未开启 mj 翻译
            if (botType == EBotType.MID_JOURNEY && !setting.EnableMjTranslate)
            {
                return prompt;
            }
            // 未开启 niji 翻译
            else if (botType == EBotType.NIJI_JOURNEY && !setting.EnableNijiTranslate)
            {
                return prompt;
            }
            else if (botType == EBotType.INSIGHT_FACE)
            {
                return prompt;
            }

            string paramStr = "";
            var paramMatcher = Regex.Match(prompt, "\\x20+--[a-z]+.*$", RegexOptions.IgnoreCase);
            if (paramMatcher.Success)
            {
                paramStr = paramMatcher.Value;
            }
            string promptWithoutParam = prompt.Substring(0, prompt.Length - paramStr.Length);
            List<string> imageUrls = new List<string>();
            var imageMatcher = Regex.Matches(promptWithoutParam, "https?://[a-z0-9-_:@&?=+,.!/~*'%$]+\\x20+", RegexOptions.IgnoreCase);
            foreach (Match match in imageMatcher)
            {
                imageUrls.Add(match.Value);
            }
            string text = promptWithoutParam;
            foreach (string imageUrl in imageUrls)
            {
                text = text.Replace(imageUrl, "");
            }
            if (!string.IsNullOrWhiteSpace(text))
            {
                text = _translateService.TranslateToEnglish(text).Trim();
            }
            if (!string.IsNullOrWhiteSpace(paramStr))
            {
                var paramNomatcher = Regex.Match(paramStr, "--no\\s+(.*?)(?=--|$)");
                if (paramNomatcher.Success)
                {
                    string paramNoStr = paramNomatcher.Groups[1].Value.Trim();
                    string paramNoStrEn = _translateService.TranslateToEnglish(paramNoStr).Trim();
                    paramStr = paramNomatcher.Result("--no " + paramNoStrEn + " ");
                }
            }
            return string.Concat(imageUrls) + text + paramStr;
        }

        /// <summary>
        /// 获取机器人类型
        /// </summary>
        /// <param name="botType"></param>
        /// <returns></returns>
        private EBotType GetBotType(string botType)
        {
            return botType switch
            {
                "niji" => EBotType.NIJI_JOURNEY,
                "NIJI_JOURNEY" => EBotType.NIJI_JOURNEY,
                "mj" => EBotType.MID_JOURNEY,
                "MID_JOURNEY" => EBotType.MID_JOURNEY,
                _ => EBotType.MID_JOURNEY
            };
        }
    }
}
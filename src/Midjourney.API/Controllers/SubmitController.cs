﻿using Microsoft.AspNetCore.Mvc;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
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

        private readonly GenerationSpeedMode? _mode;
        private readonly WorkContext _workContext;

        public SubmitController(
            ITranslateService translateService,
            ITaskStoreService taskStoreService,
            ITaskService taskService,
            ILogger<SubmitController> logger,
            DiscordHelper discordHelper,
            IHttpContextAccessor httpContextAccessor,
            WorkContext workContext)
        {
            _translateService = translateService;
            _taskStoreService = taskStoreService;
            _properties = GlobalConfiguration.Setting;
            _taskService = taskService;
            _logger = logger;
            _discordHelper = discordHelper;
            _workContext = workContext;

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

            var mode = httpContextAccessor.HttpContext.Items["Mode"]?.ToString();
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
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "prompt不能为空"));
            }
            prompt = prompt.Trim();
            var task = NewTask(imagineDTO);
            task.Action = TaskAction.IMAGINE;
            task.Prompt = prompt;

            string promptEn = TranslatePrompt(prompt);
            try
            {
                BannedPromptUtils.CheckBanned(promptEn);
            }
            catch (BannedPromptException e)
            {
                return BadRequest(SubmitResultVO.Fail(ReturnCode.BANNED_PROMPT, "可能包含敏感词")
                    .SetProperty("promptEn", promptEn)
                    .SetProperty("bannedWord", e.Message));
            }

            List<string> base64Array = imagineDTO.Base64Array ?? new List<string>();

            List<DataUrl> dataUrls = new List<DataUrl>();
            try
            {
                dataUrls = ConvertUtils.ConvertBase64Array(base64Array);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "base64格式转换异常");
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "base64格式错误"));
            }

            task.BotType = GetBotType(imagineDTO.BotType);
            task.PromptEn = promptEn;
            task.Description = $"/imagine {prompt}";
            task.AccountFilter = imagineDTO.AccountFilter;

            var data = _taskService.SubmitImagine(task, dataUrls);
            return Ok(data);
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
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "请填写 job id 或 url"));
            }
            jobId = jobId.Trim();

            if (jobId.Length != 36)
            {
                jobId = _discordHelper.GetMessageHash(jobId);
            }

            if (string.IsNullOrWhiteSpace(jobId))
            {
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "job id 格式错误"));
            }

            var model = DbHelper.TaskStore.GetCollection().Query().Where(c => c.JobId == jobId && c.Status == TaskStatus.SUCCESS).FirstOrDefault();
            if (model != null)
            {
                var info = SubmitResultVO.Of(ReturnCode.SUCCESS, "提交成功", model.Id)
                    .SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, model.InstanceId);
                return Ok(info);
            }

            if (string.IsNullOrWhiteSpace(imagineDTO.AccountFilter?.InstanceId))
            {
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "show 命令必须指定实例"));
            }

            var task = NewTask(imagineDTO);

            task.Action = TaskAction.SHOW;
            task.BotType = GetBotType(imagineDTO.BotType);
            task.Description = $"/show {jobId}";
            task.AccountFilter = imagineDTO.AccountFilter;
            task.JobId = jobId;

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
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "content参数错误"));
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
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "taskId不能为空"));
            }
            if (!new[] { TaskAction.UPSCALE, TaskAction.VARIATION, TaskAction.REROLL }.Contains(changeDTO.Action))
            {
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "action参数错误"));
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
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "关联任务状态错误"));
            }
            if (!new[] { TaskAction.IMAGINE, TaskAction.VARIATION, TaskAction.REROLL, TaskAction.BLEND }.Contains(targetTask.Action.Value))
            {
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "关联任务不允许执行变化"));
            }
            var task = NewTask(changeDTO);

            task.Action = changeDTO.Action;
            task.BotType = targetTask.BotType;
            task.ParentId = targetTask.Id;
            task.Prompt = targetTask.Prompt;
            task.PromptEn = targetTask.PromptEn;

            task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, targetTask.GetProperty<string>(Constants.TASK_PROPERTY_FINAL_PROMPT, default));
            task.SetProperty(Constants.TASK_PROPERTY_PROGRESS_MESSAGE_ID, targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_ID, default));
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, targetTask.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default));

            task.InstanceId = targetTask.InstanceId;
            task.Description = description;
            int messageFlags = targetTask.GetProperty<int>(Constants.TASK_PROPERTY_FLAGS, default);
            string messageId = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_ID, default);
            string messageHash = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_HASH, default);
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
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "base64不能为空"));
            }

            DataUrl dataUrl;
            try
            {
                dataUrl = DataUrl.Parse(describeDTO.Base64);
            }
            catch
            {
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "base64格式错误"));
            }

            var task = NewTask(describeDTO);

            task.BotType = GetBotType(describeDTO.BotType);
            task.Action = TaskAction.DESCRIBE;

            string taskFileName = $"{task.Id}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
            task.Description = $"/describe {taskFileName}";
            task.AccountFilter = describeDTO.AccountFilter;

            return Ok(_taskService.SubmitDescribe(task, dataUrl));
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
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "base64List参数错误"));
            }

            if (blendDTO.Dimensions == null)
            {
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "dimensions参数错误"));
            }

            List<DataUrl> dataUrlList = new List<DataUrl>();
            try
            {
                dataUrlList = ConvertUtils.ConvertBase64Array(base64Array);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "base64格式错误");

                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "base64格式错误"));
            }
            var task = NewTask(blendDTO);

            task.BotType = GetBotType(blendDTO.BotType);
            task.Action = TaskAction.BLEND;
            task.Description = $"/blend {task.Id} {dataUrlList.Count}";
            task.AccountFilter = blendDTO.AccountFilter;
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
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "参数错误"));
            }

            var targetTask = _taskStoreService.Get(actionDTO.TaskId);
            if (targetTask == null)
            {
                return NotFound(SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "关联任务不存在或已失效"));
            }

            if (targetTask.Status != TaskStatus.SUCCESS)
            {
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "关联任务状态错误"));
            }

            var task = NewTask(actionDTO);
            task.InstanceId = targetTask.InstanceId;
            task.ParentId = targetTask.Id;
            task.BotType = targetTask.BotType;

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
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "参数错误"));
            }

            var targetTask = _taskStoreService.Get(actionDTO.TaskId);
            if (targetTask == null)
            {
                return NotFound(SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "关联任务不存在或已失效"));
            }

            var prompt = actionDTO.Prompt;
            var task = targetTask;

            var promptEn = TranslatePrompt(prompt);
            try
            {
                BannedPromptUtils.CheckBanned(promptEn);
            }
            catch (BannedPromptException e)
            {
                return BadRequest(SubmitResultVO.Fail(ReturnCode.BANNED_PROMPT, "可能包含敏感词")
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
                return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "base64格式错误"));
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
                UserId = user?.Id
            };

            var now = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();

            // 计算当前 ip 当日第几次绘图
            // 如果不是白名单用户，则计算 ip 绘图限制
            if (user == null || user.IsWhite != true)
            {
                var ipTodayDrawCount = (int)DbHelper.TaskStore.Count(x => x.SubmitTime >= now && x.ClientIp == _ip);
                if (GlobalConfiguration.Setting.GuestDefaultDayLimit > 0)
                {
                    if (ipTodayDrawCount > GlobalConfiguration.Setting.GuestDefaultDayLimit)
                    {
                        throw new LogicException("今日绘图次数已达上限");
                    }
                }
            }

            // 计算当前用户当日第几次绘图
            if (!string.IsNullOrWhiteSpace(user?.Id))
            {
                if (user.DayDrawLimit > 0)
                {
                    var userTodayDrawCount = (int)DbHelper.TaskStore.Count(x => x.SubmitTime >= now && x.UserId == user.Id);
                    if (userTodayDrawCount > user.DayDrawLimit)
                    {
                        throw new LogicException("今日绘图次数已达上限");
                    }
                }
            }

            if (_mode != null)
            {
                task.AccountFilter ??= new AccountFilter();

                if (!task.AccountFilter.Modes.Contains(_mode.Value))
                {
                    task.AccountFilter.Modes.Add(_mode.Value);
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
        /// 翻译提示词
        /// </summary>
        /// <param name="prompt">提示词</param>
        /// <returns>翻译后的提示词</returns>
        private string TranslatePrompt(string prompt)
        {
            if (_properties.TranslateWay == TranslateWay.NULL || string.IsNullOrWhiteSpace(prompt) || !_translateService.ContainsChinese(prompt))
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
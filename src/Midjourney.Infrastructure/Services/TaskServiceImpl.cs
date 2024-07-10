using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using Serilog;
using System.Diagnostics;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// 任务服务实现类，处理任务的具体操作。
    /// </summary>
    public class TaskServiceImpl : ITaskService
    {
        private readonly ITaskStoreService _taskStoreService;
        private readonly DiscordLoadBalancer _discordLoadBalancer;
        private readonly ILogger _logger;

        /// <summary>
        /// 初始化 TaskServiceImpl 类的新实例。
        /// </summary>
        public TaskServiceImpl(ITaskStoreService taskStoreService, DiscordLoadBalancer discordLoadBalancer)
        {
            _taskStoreService = taskStoreService;
            _discordLoadBalancer = discordLoadBalancer;
            _logger = Log.Logger;
        }

        public SubmitResultVO SubmitImagine(TaskInfo info, List<DataUrl> dataUrls)
        {
            var instance = _discordLoadBalancer.ChooseInstance();
            if (instance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            info.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.GetInstanceId());

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
                    var finalFileName = uploadResult.Description;
                    var sendImageResult = await instance.SendImageMessageAsync("upload image: " + finalFileName, finalFileName);
                    if (sendImageResult.Code != ReturnCode.SUCCESS)
                    {
                        return Message.Of(sendImageResult.Code, sendImageResult.Description);
                    }
                    imageUrls.Add(sendImageResult.Description);
                }
                if (imageUrls.Any())
                {
                    info.Prompt = string.Join(" ", imageUrls) + " " + info.Prompt;
                    info.PromptEn = string.Join(" ", imageUrls) + " " + info.PromptEn;
                    info.Description = "/imagine " + info.Prompt;
                    _taskStoreService.Save(info);
                }
                return await instance.ImagineAsync(info.PromptEn, info.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default));
            });
        }

        public SubmitResultVO SubmitUpscale(TaskInfo task, string targetMessageId, string targetMessageHash, int index, int messageFlags)
        {
            var instanceId = task.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default);
            var discordInstance = _discordLoadBalancer.GetDiscordInstance(instanceId);
            if (discordInstance == null || !discordInstance.IsAlive())
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "账号不可用: " + instanceId);
            }
            return discordInstance.SubmitTaskAsync(task, async () =>
                await discordInstance.UpscaleAsync(targetMessageId, index, targetMessageHash, messageFlags, task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default)));
        }

        public SubmitResultVO SubmitVariation(TaskInfo task, string targetMessageId, string targetMessageHash, int index, int messageFlags)
        {
            var instanceId = task.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default);
            var discordInstance = _discordLoadBalancer.GetDiscordInstance(instanceId);
            if (discordInstance == null || !discordInstance.IsAlive())
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "账号不可用: " + instanceId);
            }
            return discordInstance.SubmitTaskAsync(task, async () =>
                await discordInstance.VariationAsync(targetMessageId, index, targetMessageHash, messageFlags, task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default)));
        }

        public SubmitResultVO SubmitReroll(TaskInfo task, string targetMessageId, string targetMessageHash, int messageFlags)
        {
            var instanceId = task.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default);
            var discordInstance = _discordLoadBalancer.GetDiscordInstance(instanceId);
            if (discordInstance == null || !discordInstance.IsAlive())
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "账号不可用: " + instanceId);
            }
            return discordInstance.SubmitTaskAsync(task, async () =>
                await discordInstance.RerollAsync(targetMessageId, targetMessageHash, messageFlags, task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default)));
        }

        public SubmitResultVO SubmitDescribe(TaskInfo task, DataUrl dataUrl)
        {
            var discordInstance = _discordLoadBalancer.ChooseInstance();
            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.GetInstanceId());
            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                var taskFileName = $"{task.Id}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                var uploadResult = await discordInstance.UploadAsync(taskFileName, dataUrl);
                if (uploadResult.Code != ReturnCode.SUCCESS)
                {
                    return Message.Of(uploadResult.Code, uploadResult.Description);
                }
                var finalFileName = uploadResult.Description;
                return await discordInstance.DescribeAsync(finalFileName, task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default));
            });
        }

        public SubmitResultVO SubmitBlend(TaskInfo task, List<DataUrl> dataUrls, BlendDimensions dimensions)
        {
            var discordInstance = _discordLoadBalancer.ChooseInstance();
            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.GetInstanceId());
            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                var finalFileNames = new List<string>();
                foreach (var dataUrl in dataUrls)
                {
                    var taskFileName = $"{task.Id}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                    var uploadResult = await discordInstance.UploadAsync(taskFileName, dataUrl);
                    if (uploadResult.Code != ReturnCode.SUCCESS)
                    {
                        return Message.Of(uploadResult.Code, uploadResult.Description);
                    }
                    finalFileNames.Add(uploadResult.Description);
                }
                return await discordInstance.BlendAsync(finalFileNames, dimensions, task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default));
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
            var discordInstance = _discordLoadBalancer.ChooseInstance();
            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.GetInstanceId());

            var targetTask = _taskStoreService.Get(submitAction.TaskId)!;
            var messageFlags = targetTask.GetProperty<int>(Constants.TASK_PROPERTY_FLAGS, default);
            var messageId = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_ID, default);

            task.BotType = targetTask.BotType;
            task.SetProperty(Constants.TASK_PROPERTY_BOT_TYPE, targetTask.BotType);
            task.SetProperty(Constants.TASK_PROPERTY_CUSTOM_ID, submitAction.CustomId);

            // 设置任务的提示信息 = 父级任务的提示信息
            task.Prompt = targetTask.Prompt;
            task.PromptEn = targetTask.PromptEn;

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
                return SubmitResultVO.Of(ReturnCode.EXISTED, "Waiting for window confirm", task.Id)
                    .SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, task.PromptEn)
                    .SetProperty(Constants.TASK_PROPERTY_REMIX, true);
            }

            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                return await discordInstance.ActionAsync(messageId ?? targetTask.MessageId,
                    submitAction.CustomId, messageFlags, task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default));
            });
        }

        /// <summary>
        /// 执行 Modal
        /// </summary>
        /// <param name="task"></param>
        /// <param name="submitAction"></param>
        /// <returns></returns>
        public SubmitResultVO SubmitModal(TaskInfo task, SubmitModalDTO submitAction, DataUrl dataUrl = null)
        {
            var discordInstance = _discordLoadBalancer.ChooseInstance();
            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.GetInstanceId());

            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                var messageFlags = task.GetProperty<int>(Constants.TASK_PROPERTY_FLAGS, default);
                var messageId = task.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_ID, default);

                var customId = task.GetProperty<string>(Constants.TASK_PROPERTY_CUSTOM_ID, default);
                var nonce = task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default);

                // 弹出，再执行变焦
                var res = await discordInstance.ActionAsync(messageId, customId, messageFlags, nonce);
                if (res.Code != ReturnCode.SUCCESS)
                {
                    return res;
                }

                // 等待获取 messageId
                // 等待最大超时 5min
                var sw = new Stopwatch();
                sw.Start();

                do
                {
                    Thread.Sleep(500);
                    task = discordInstance.GetRunningTask(task.Id);

                    if (string.IsNullOrWhiteSpace(task.MessageId))
                    {
                        if (sw.ElapsedMilliseconds > 300000)
                        {
                            return Message.Of(ReturnCode.NOT_FOUND, "超时，未找到消息 ID");
                        }
                    }
                } while (string.IsNullOrWhiteSpace(task.MessageId));

                // 自定义变焦
                if (customId.StartsWith("MJ::CustomZoom::"))
                {
                    nonce = SnowFlake.NextId();
                    task.Nonce = nonce;
                    task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                    return await discordInstance.ZoomAsync(task.MessageId, customId, task.PromptEn, nonce);
                }
                // 局部重绘
                else if (customId.StartsWith("MJ::Inpaint::"))
                {
                    var ifarmeCustomId = task.GetProperty<string>(Constants.TASK_PROPERTY_IFRAME_MODAL_CREATE_CUSTOM_ID, default);
                    return await discordInstance.InpaintAsync(ifarmeCustomId, task.PromptEn, submitAction.MaskBase64);
                }
                else
                {
                    // 不支持
                    return Message.Of(ReturnCode.NOT_FOUND, "不支持的操作");
                }
            });
        }
    }
}
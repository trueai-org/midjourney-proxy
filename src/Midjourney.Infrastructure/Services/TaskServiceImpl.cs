using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using Serilog;
using System.Diagnostics;
using System.Threading.Tasks;

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

        /// <summary>
        /// 提交 imagine 任务。
        /// </summary>
        /// <param name="info"></param>
        /// <param name="dataUrls"></param>
        /// <returns></returns>
        public SubmitResultVO SubmitImagine(TaskInfo info, List<DataUrl> dataUrls)
        {
            var instance = _discordLoadBalancer.ChooseInstance(info.AccountFilter);
            if (instance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            info.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.GetInstanceId);
            info.InstanceId = instance.GetInstanceId;

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
                return await instance.ImagineAsync(info.PromptEn,
                    info.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), info.BotType);
            });
        }

        /// <summary>
        /// 提交 show 任务
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        public SubmitResultVO ShowImagine(TaskInfo info)
        {
            var instance = _discordLoadBalancer.ChooseInstance(info.AccountFilter);
            if (instance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            info.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.GetInstanceId);
            info.InstanceId = instance.GetInstanceId;

            return instance.SubmitTaskAsync(info, async () =>
            {
                return await instance.ShowAsync(info.JobId,
                    info.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), info.BotType);
            });
        }

        public SubmitResultVO SubmitUpscale(TaskInfo task, string targetMessageId, string targetMessageHash, int index, int messageFlags)
        {
            var instanceId = task.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default);
            var discordInstance = _discordLoadBalancer.GetDiscordInstance(instanceId);
            if (discordInstance == null || !discordInstance.IsAlive)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "账号不可用: " + instanceId);
            }
            return discordInstance.SubmitTaskAsync(task, async () =>
                await discordInstance.UpscaleAsync(targetMessageId, index, targetMessageHash, messageFlags,
                task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.BotType));
        }

        public SubmitResultVO SubmitVariation(TaskInfo task, string targetMessageId, string targetMessageHash, int index, int messageFlags)
        {
            var instanceId = task.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default);
            var discordInstance = _discordLoadBalancer.GetDiscordInstance(instanceId);
            if (discordInstance == null || !discordInstance.IsAlive)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "账号不可用: " + instanceId);
            }
            return discordInstance.SubmitTaskAsync(task, async () =>
                await discordInstance.VariationAsync(targetMessageId, index, targetMessageHash, messageFlags,
                task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.BotType));
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
            var discordInstance = _discordLoadBalancer.GetDiscordInstance(instanceId);
            if (discordInstance == null || !discordInstance.IsAlive)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "账号不可用: " + instanceId);
            }
            return discordInstance.SubmitTaskAsync(task, async () =>
                await discordInstance.RerollAsync(targetMessageId, targetMessageHash, messageFlags,
                task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.BotType));
        }

        /// <summary>
        /// 提交Describe任务
        /// </summary>
        /// <param name="task"></param>
        /// <param name="dataUrl"></param>
        /// <returns></returns>
        public SubmitResultVO SubmitDescribe(TaskInfo task, DataUrl dataUrl)
        {
            var discordInstance = _discordLoadBalancer.ChooseInstance(task.AccountFilter);
            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.GetInstanceId);
            task.InstanceId = discordInstance.GetInstanceId;

            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                var taskFileName = $"{task.Id}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                var uploadResult = await discordInstance.UploadAsync(taskFileName, dataUrl);
                if (uploadResult.Code != ReturnCode.SUCCESS)
                {
                    return Message.Of(uploadResult.Code, uploadResult.Description);
                }
                var finalFileName = uploadResult.Description;
                return await discordInstance.DescribeAsync(finalFileName, task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default),
                    task.BotType);
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
            var discordInstance = _discordLoadBalancer.ChooseInstance(task.AccountFilter);
            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }
            task.InstanceId = discordInstance.GetInstanceId;
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.GetInstanceId);
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
                return await discordInstance.BlendAsync(finalFileNames, dimensions,
                    task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.BotType);
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
            var discordInstance = _discordLoadBalancer.GetDiscordInstance(task.InstanceId);
            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            task.InstanceId = discordInstance.GetInstanceId;
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.GetInstanceId);

            var targetTask = _taskStoreService.Get(submitAction.TaskId)!;
            var messageFlags = targetTask.GetProperty<int>(Constants.TASK_PROPERTY_FLAGS, default);
            var messageId = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_ID, default);

            task.BotType = targetTask.BotType;
            task.SetProperty(Constants.TASK_PROPERTY_BOT_TYPE, targetTask.BotType);
            task.SetProperty(Constants.TASK_PROPERTY_CUSTOM_ID, submitAction.CustomId);

            // 设置任务的提示信息 = 父级任务的提示信息
            task.Prompt = targetTask.Prompt;
            task.PromptEn = targetTask.PromptEn;

            // 点击喜欢
            if (submitAction.CustomId.Contains("MJ::BOOKMARK"))
            {
                var res = discordInstance.ActionAsync(messageId ?? targetTask.MessageId,
                    submitAction.CustomId, messageFlags,
                    task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.BotType)
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
                        Id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{RandomUtils.RandomNumbers(3)}",
                        SubmitTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        State = $"{task.State}::{i + 1}",
                        ParentId = targetTask.Id,
                        Action = task.Action,
                        BotType = task.BotType,
                        InstanceId = task.InstanceId,
                        Prompt = prompt,
                        PromptEn = prompt,
                        Status = TaskStatus.NOT_START
                    };

                    subTask.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.GetInstanceId);
                    subTask.SetProperty(Constants.TASK_PROPERTY_BOT_TYPE, targetTask.BotType);

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
            else if (submitAction.CustomId.StartsWith("MJ::Job::PicReader::"))
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
            // REMIX 处理
            else if (task.Action == TaskAction.PAN || task.Action == TaskAction.VARIATION || task.Action == TaskAction.REROLL)
            {
                task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                task.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                // 如果开启了 remix 自动提交
                if (discordInstance.Account.RemixAutoSubmit)
                {
                    // 并且已开启 remix 模式
                    if ((task.BotType == EBotType.MID_JOURNEY && discordInstance.Account.MjRemixOn)
                        || (task.BotType == EBotType.NIJI_JOURNEY && discordInstance.Account.NijiRemixOn))
                    {
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
                    if ((task.BotType == EBotType.MID_JOURNEY && discordInstance.Account.MjRemixOn)
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
                return await discordInstance.ActionAsync(messageId ?? targetTask.MessageId,
                    submitAction.CustomId, messageFlags,
                    task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.BotType);
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
            var discordInstance = _discordLoadBalancer.GetDiscordInstance(task.InstanceId);
            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            task.InstanceId = discordInstance.GetInstanceId;
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.GetInstanceId);

            return discordInstance.SubmitTaskAsync(task, async () =>
            {
                var customId = task.GetProperty<string>(Constants.TASK_PROPERTY_CUSTOM_ID, default);
                var messageFlags = task.GetProperty<int>(Constants.TASK_PROPERTY_FLAGS, default);
                var messageId = task.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_ID, default);
                var nonce = task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default);

                // 弹窗确认
                var res = await discordInstance.ActionAsync(messageId, customId, messageFlags, nonce, task.BotType);
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

                    if (string.IsNullOrWhiteSpace(task.MessageId) || string.IsNullOrWhiteSpace(task.InteractionMetadataId))
                    {
                        if (sw.ElapsedMilliseconds > 300000)
                        {
                            return Message.Of(ReturnCode.NOT_FOUND, "超时，未找到消息 ID");
                        }
                    }

                } while (string.IsNullOrWhiteSpace(task.MessageId) || string.IsNullOrWhiteSpace(task.InteractionMetadataId));

                // 自定义变焦
                if (customId.StartsWith("MJ::CustomZoom::"))
                {
                    nonce = SnowFlake.NextId();
                    task.Nonce = nonce;
                    task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                    return await discordInstance.ZoomAsync(task.MessageId, customId, task.PromptEn, nonce, task.BotType);
                }
                // 局部重绘
                else if (customId.StartsWith("MJ::Inpaint::"))
                {
                    var ifarmeCustomId = task.GetProperty<string>(Constants.TASK_PROPERTY_IFRAME_MODAL_CREATE_CUSTOM_ID, default);
                    return await discordInstance.InpaintAsync(ifarmeCustomId, task.PromptEn, submitAction.MaskBase64, task.BotType);
                }
                // 图生文 -> 文生图
                else if (customId.StartsWith("MJ::Job::PicReader::"))
                {
                    nonce = SnowFlake.NextId();
                    task.Nonce = nonce;
                    task.SetProperty(Constants.TASK_PROPERTY_NONCE, nonce);

                    return await discordInstance.PicReaderAsync(task.MessageId, customId, task.PromptEn, nonce, task.BotType);
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
                        if (task.BotType == EBotType.MID_JOURNEY)
                        {
                            if (discordInstance.Account.Buttons.Any(x => x.Label == "High Variation Mode" && x.Style == 3))
                            {
                                suffix = "1";
                            }
                        }
                        else
                        {
                            if (discordInstance.Account.NijiButtons.Any(x => x.Label == "High Variation Mode" && x.Style == 3))
                            {
                                suffix = "1";
                            }
                        }

                        // 低变化
                        if (customId.Contains("low_variation"))
                        {
                            suffix = "0";
                        }
                        // 如果是高变化或 niji bot
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

                    return await discordInstance.RemixAsync(task.Action.Value, task.MessageId, modal,
                        customId, task.PromptEn, nonce, task.BotType);
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
            var discordInstance = _discordLoadBalancer.GetDiscordInstance(task.InstanceId);
            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            // 请配置私聊频道
            var privateChannelId = string.Empty;

            if (task.BotType == EBotType.MID_JOURNEY)
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
                var res = await discordInstance.SeedAsync(hash, nonce, task.BotType);
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
            var discordInstance = _discordLoadBalancer.GetDiscordInstance(id);
            if (discordInstance == null)
            {
                throw new LogicException("无可用的账号实例");
            }

            // 只有配置 NIJI 才请求     
            if (!string.IsNullOrWhiteSpace(discordInstance.Account.NijiBotChannelId))
            {
                var res = await discordInstance.InfoAsync(SnowFlake.NextId(), EBotType.NIJI_JOURNEY);
                if (res.Code != ReturnCode.SUCCESS)
                {
                    throw new LogicException(res.Description);
                }
                Thread.Sleep(2500);
            }

            var res0 = await discordInstance.InfoAsync(SnowFlake.NextId(), EBotType.MID_JOURNEY);
            if (res0.Code != ReturnCode.SUCCESS)
            {
                throw new LogicException(res0.Description);
            }
            Thread.Sleep(2500);

            // 只有配置 NIJI 才请求            
            if (!string.IsNullOrWhiteSpace(discordInstance.Account.NijiBotChannelId))
            {
                var res2 = await discordInstance.SettingAsync(SnowFlake.NextId(), EBotType.NIJI_JOURNEY);
                if (res2.Code != ReturnCode.SUCCESS)
                {
                    throw new LogicException(res2.Description);
                }
                Thread.Sleep(2500);
            }

            var res3 = await discordInstance.SettingAsync(SnowFlake.NextId(), EBotType.MID_JOURNEY);
            if (res3.Code != ReturnCode.SUCCESS)
            {
                throw new LogicException(res3.Description);
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
            var discordInstance = _discordLoadBalancer.GetDiscordInstance(id);
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
            var discordInstance = _discordLoadBalancer.GetDiscordInstance(id);
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
    }
}
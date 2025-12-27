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

using System.Globalization;
using System.Text.RegularExpressions;
using Midjourney.Infrastructure.LoadBalancer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using Serilog;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// 任务服务实现类，处理任务的具体操作
    /// </summary>
    public class TaskService : ITaskService
    {
        private readonly ITaskStoreService _taskStoreService;
        private readonly DiscordLoadBalancer _discordLoadBalancer;
        private readonly IFreeSql _freeSql = FreeSqlHelper.FreeSql;

        public TaskService(ITaskStoreService taskStoreService, DiscordLoadBalancer discordLoadBalancer)
        {
            _taskStoreService = taskStoreService;
            _discordLoadBalancer = discordLoadBalancer;
        }

        /// <summary>
        /// 获取领域缓存
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, HashSet<string>> GetDomainCache()
        {
            return AdaptiveCache.GetOrCreate("domains", () =>
            {
                var list = _freeSql.Select<DomainTag>().Where(c => c.Enable).ToList();

                var dict = new Dictionary<string, HashSet<string>>();
                foreach (var item in list)
                {
                    var keywords = item.Keywords.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().ToList();
                    dict[item.Id] = new HashSet<string>(keywords);
                }

                return dict;
            }, TimeSpan.FromMinutes(30));
        }

        /// <summary>
        /// 清除领域缓存
        /// </summary>
        public void ClearDomainCache()
        {
            AdaptiveCache.Remove("domains");
        }

        /// <summary>
        /// 违规词缓存
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, HashSet<string>> GetBannedWordsCache()
        {
            return AdaptiveCache.GetOrCreate("bannedWords", () =>
            {
                var list = _freeSql.Select<BannedWord>().Where(c => c.Enable).ToList();

                var dict = new Dictionary<string, HashSet<string>>();
                foreach (var item in list)
                {
                    var keywords = item.Keywords.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct().ToList();
                    dict[item.Id] = new HashSet<string>(keywords);
                }

                return dict;
            }, TimeSpan.FromMinutes(30));
        }

        /// <summary>
        /// 清除违规词缓存
        /// </summary>
        public void ClearBannedWordsCache()
        {
            AdaptiveCache.Remove("bannedWords");
        }

        /// <summary>
        /// 验证违规词
        /// </summary>
        /// <param name="promptEn"></param>
        /// <exception cref="BannedPromptException"></exception>
        public string CheckBanned(string promptEn)
        {
            // 如果开启了自动清除用户违规词，则通过正则替换忽略违词，并忽略大小写
            var setting = GlobalConfiguration.Setting;
            if (setting.EnableAutoClearUserBannedWords)
            {
                var dic = GetBannedWordsCache();
                var finalPromptEn = promptEn;
                foreach (var item in dic)
                {
                    foreach (string word in item.Value)
                    {
                        var regex = new Regex($"\\b{Regex.Escape(word)}\\b", RegexOptions.IgnoreCase);
                        finalPromptEn = regex.Replace(finalPromptEn, "");
                    }
                }
                // 去除多余的空格
                finalPromptEn = Regex.Replace(finalPromptEn, @"\s+", " ").Trim();
                return finalPromptEn;
            }
            else
            {
                var dic = GetBannedWordsCache();
                var finalPromptEn = promptEn.ToLower(CultureInfo.InvariantCulture);
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

                return promptEn;
            }
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
            var promptEn = info.PromptEn;
            if (promptEn.Contains("--video", StringComparison.OrdinalIgnoreCase))
            {
                info.Action = TaskAction.VIDEO;

                if (!setting.EnableVideo)
                {
                    return SubmitResultVO.Fail(ReturnCode.FAILURE, "视频功能未启用");
                }
            }

            var (instance, mode) = _discordLoadBalancer.ChooseInstance(info.AccountFilter,
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

            if (instance.Account.IsYouChuan || instance.Account.IsOfficial)
            {
                var imageUrls = new List<string>();
                foreach (var dataUrl in dataUrls)
                {
                    if (instance.Account.IsYouChuan)
                    {
                        var link = "";
                        // 悠船
                        if (dataUrl.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            link = dataUrl.Url;

                            if (setting.EnableYouChuanPromptLink && !link.Contains("youchuan"))
                            {
                                // 悠船官网链接转换
                                var ff = new FileFetchHelper();
                                var res = await ff.FetchFileAsync(link);
                                if (res.Success && !string.IsNullOrWhiteSpace(res.Url))
                                {
                                    link = res.Url;
                                }
                                else if (res.Success && res.FileBytes.Length > 0)
                                {
                                    var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                                    link = await instance.YmTaskService.UploadFile(info, res.FileBytes, taskFileName);
                                }
                            }
                        }
                        else
                        {
                            var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                            link = await instance.YmTaskService.UploadFile(info, dataUrl.Data, taskFileName);
                        }

                        imageUrls.Add(link);
                    }
                    else
                    {
                        var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                        var uploadResult = await instance.UploadAsync(taskFileName, dataUrl);
                        if (uploadResult.Code != ReturnCode.SUCCESS)
                        {
                            return SubmitResultVO.Fail(ReturnCode.FAILURE, uploadResult.Description);
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
                                return SubmitResultVO.Fail(ReturnCode.FAILURE, sendImageResult.Description);
                            }
                            imageUrls.Add(sendImageResult.Description);
                        }
                    }
                }

                if (imageUrls.Any())
                {
                    info.Prompt = string.Join(" ", imageUrls) + " " + info.Prompt;
                    info.PromptEn = string.Join(" ", imageUrls) + " " + info.PromptEn;
                    info.Description = "/imagine " + info.Prompt;
                }

                return await instance.RedisEnqueue(new TaskInfoQueue()
                {
                    Info = info,
                    Function = TaskInfoQueueFunction.SUBMIT
                });
            }
            else
            {
                var imageUrls = new List<string>();
                foreach (var dataUrl in dataUrls)
                {
                    var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                    var uploadResult = await instance.UploadAsync(taskFileName, dataUrl);
                    if (uploadResult.Code != ReturnCode.SUCCESS)
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, uploadResult.Description);
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
                            return SubmitResultVO.Fail(ReturnCode.FAILURE, sendImageResult.Description);
                        }
                        imageUrls.Add(sendImageResult.Description);
                    }
                }

                if (imageUrls.Any())
                {
                    info.Prompt = string.Join(" ", imageUrls) + " " + info.Prompt;
                    info.PromptEn = string.Join(" ", imageUrls) + " " + info.PromptEn;
                    info.Description = "/imagine " + info.Prompt;
                }

                return await instance.RedisEnqueue(new TaskInfoQueue()
                {
                    Info = info,
                    Function = TaskInfoQueueFunction.SUBMIT
                });
            }
        }

        /// <summary>
        /// 提交编辑任务。
        /// </summary>
        /// <param name="info"></param>
        /// <param name="dataUrl"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> SubmitEdit(TaskInfo info, DataUrl dataUrl)
        {
            var setting = GlobalConfiguration.Setting;
            var (instance, mode) = _discordLoadBalancer.ChooseInstance(info.AccountFilter,
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

            if (instance.Account.IsYouChuan)
            {
                var link = "";

                // 悠船
                if (dataUrl.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                {
                    link = dataUrl.Url;

                    if (setting.EnableYouChuanPromptLink && !link.Contains("youchuan"))
                    {
                        // 悠船官网链接转换
                        var ff = new FileFetchHelper();
                        var res = await ff.FetchFileAsync(link);
                        if (res.Success && !string.IsNullOrWhiteSpace(res.Url))
                        {
                            link = res.Url;
                        }
                        else if (res.Success && res.FileBytes.Length > 0)
                        {
                            var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                            link = await instance.YmTaskService.UploadFile(info, res.FileBytes, taskFileName);
                        }
                    }
                }
                else
                {
                    var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                    link = await instance.YmTaskService.UploadFile(info, dataUrl.Data, taskFileName);
                }

                info.BaseImageUrl = link;
            }
            else
            {
                if (dataUrl.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                {
                    info.BaseImageUrl = dataUrl.Url;
                }
                else
                {
                    var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                    var uploadResult = await instance.UploadAsync(taskFileName, dataUrl);
                    if (uploadResult.Code != ReturnCode.SUCCESS)
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, uploadResult.Description);
                    }

                    var finalFileName = uploadResult.Description;
                    var sendImageResult = await instance.SendImageMessageAsync("upload image: " + finalFileName, finalFileName);
                    if (sendImageResult.Code != ReturnCode.SUCCESS)
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, sendImageResult.Description);
                    }

                    info.BaseImageUrl = sendImageResult.Description;
                }
            }

            info.Description = "/edit " + info.Prompt;

            // 入队前不保存
            //_taskStoreService.Save(info);

            if (string.IsNullOrWhiteSpace(info.BaseImageUrl))
            {
                return SubmitResultVO.Fail(ReturnCode.FAILURE, "BaseImageUrl is empty, please check the uploaded image.");
            }

            return await instance.RedisEnqueue(new TaskInfoQueue()
            {
                Info = info,
                Function = TaskInfoQueueFunction.EDIT
            });
        }

        /// <summary>
        /// 提交转绘任务。
        /// </summary>
        /// <param name="info"></param>
        /// <param name="dataUrl"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> SubmitRetexture(TaskInfo info, DataUrl dataUrl)
        {
            var setting = GlobalConfiguration.Setting;
            var (instance, mode) = _discordLoadBalancer.ChooseInstance(info.AccountFilter,
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

            if (instance.Account.IsYouChuan)
            {
                var link = "";

                // 悠船
                if (dataUrl.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                {
                    link = dataUrl.Url;

                    if (setting.EnableYouChuanPromptLink && !link.Contains("youchuan"))
                    {
                        // 悠船官网链接转换
                        var ff = new FileFetchHelper();
                        var res = await ff.FetchFileAsync(link);
                        if (res.Success && !string.IsNullOrWhiteSpace(res.Url))
                        {
                            link = res.Url;
                        }
                        else if (res.Success && res.FileBytes.Length > 0)
                        {
                            var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                            link = await instance.YmTaskService.UploadFile(info, res.FileBytes, taskFileName);
                        }
                    }
                }
                else
                {
                    var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                    link = await instance.YmTaskService.UploadFile(info, dataUrl.Data, taskFileName);
                }

                info.BaseImageUrl = link;
            }
            else
            {
                if (dataUrl.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                {
                    info.BaseImageUrl = dataUrl.Url;
                }
                else
                {
                    var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                    var uploadResult = await instance.UploadAsync(taskFileName, dataUrl);
                    if (uploadResult.Code != ReturnCode.SUCCESS)
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, uploadResult.Description);
                    }

                    var finalFileName = uploadResult.Description;
                    var sendImageResult = await instance.SendImageMessageAsync("upload image: " + finalFileName, finalFileName);
                    if (sendImageResult.Code != ReturnCode.SUCCESS)
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, sendImageResult.Description);
                    }

                    info.BaseImageUrl = sendImageResult.Description;
                }
            }

            info.Description = "/retexture " + info.Prompt;
            info.PromptEn = info.PromptEn + " --dref " + info.BaseImageUrl;

            // 入队前不保存
            //_taskStoreService.Save(info);

            if (string.IsNullOrWhiteSpace(info.BaseImageUrl))
            {
                //return Message.Failure("BaseImageUrl is empty, please check the uploaded image.");
                return SubmitResultVO.Fail(ReturnCode.FAILURE, "BaseImageUrl is empty, please check the uploaded image.");
            }

            //return await instance.YmTaskService.SubmitTaskAsync(info, _taskStoreService, instance);
            return await instance.RedisEnqueue(new TaskInfoQueue()
            {
                Info = info,
                Function = TaskInfoQueueFunction.RETEXTURE
            });
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
            var setting = GlobalConfiguration.Setting;

            DiscordInstance instance;
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
                var (okInstance, okMode) = _discordLoadBalancer.ChooseInstance(info.AccountFilter,
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

            var startImageUrl = "";
            var endImageUrl = "";

            if (instance.Account.IsYouChuan)
            {
                // 开始图片
                if (startUrl?.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                {
                    startImageUrl = startUrl.Url;

                    if (setting.EnableYouChuanPromptLink && !startImageUrl.Contains("youchuan"))
                    {
                        var ff = new FileFetchHelper();
                        var res = await ff.FetchFileAsync(startImageUrl);
                        if (res.Success && !string.IsNullOrWhiteSpace(res.Url))
                        {
                            startImageUrl = res.Url;
                        }
                        else if (res.Success && res.FileBytes.Length > 0)
                        {
                            var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(startUrl.MimeType)}";
                            startImageUrl = await instance.YmTaskService.UploadFile(info, res.FileBytes, taskFileName);
                        }
                    }
                }
                else if (startUrl?.Data != null && startUrl.Data.Length > 0)
                {
                    var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(startUrl.MimeType)}";
                    startImageUrl = await instance.YmTaskService.UploadFile(info, startUrl.Data, taskFileName);
                }

                // 结束图片
                if (endUrl?.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                {
                    endImageUrl = endUrl.Url;

                    if (setting.EnableYouChuanPromptLink && !endImageUrl.Contains("youchuan"))
                    {
                        var ff = new FileFetchHelper();
                        var res = await ff.FetchFileAsync(endImageUrl);
                        if (res.Success && !string.IsNullOrWhiteSpace(res.Url))
                        {
                            endImageUrl = res.Url;
                        }
                        else if (res.Success && res.FileBytes.Length > 0)
                        {
                            var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(endUrl.MimeType)}";
                            endImageUrl = await instance.YmTaskService.UploadFile(info, res.FileBytes, taskFileName);
                        }
                    }
                }
                else if (endUrl?.Data != null && endUrl.Data.Length > 0)
                {
                    var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(endUrl.MimeType)}";
                    endImageUrl = await instance.YmTaskService.UploadFile(info, endUrl.Data, taskFileName);
                }
            }
            else if (instance.Account.IsOfficial)
            {
                // 开始图片
                if (startUrl?.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                {
                    startImageUrl = startUrl.Url;
                }
                else if (startUrl?.Data != null && startUrl.Data.Length > 0)
                {
                    var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(startUrl.MimeType)}";
                    var uploadResult = await instance.UploadAsync(taskFileName, startUrl);
                    if (uploadResult.Code != ReturnCode.SUCCESS)
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, uploadResult.Description);
                    }

                    var finalFileName = uploadResult.Description;
                    var sendImageResult = await instance.SendImageMessageAsync(("upload image: " + finalFileName), finalFileName);
                    if (sendImageResult.Code != ReturnCode.SUCCESS)
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, sendImageResult.Description);
                    }

                    startImageUrl = sendImageResult.Description;
                }

                // 结束图片
                if (endUrl?.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                {
                    endImageUrl = endUrl.Url;
                }
                else if (endUrl?.Data != null && endUrl.Data.Length > 0)
                {
                    var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(endUrl.MimeType)}";
                    var uploadResult = await instance.UploadAsync(taskFileName, endUrl);
                    if (uploadResult.Code != ReturnCode.SUCCESS)
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, uploadResult.Description);
                    }
                    var finalFileName = uploadResult.Description;
                    var sendImageResult = await instance.SendImageMessageAsync(("upload image: " + finalFileName), finalFileName);
                    if (sendImageResult.Code != ReturnCode.SUCCESS)
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, sendImageResult.Description);
                    }
                    endImageUrl = sendImageResult.Description;
                }
            }
            else
            {
                // 开始图片
                if (startUrl?.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                {
                    startImageUrl = startUrl.Url;
                }
                else if (startUrl?.Data != null && startUrl.Data.Length > 0)
                {
                    var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(startUrl.MimeType)}";
                    var uploadResult = await instance.UploadAsync(taskFileName, startUrl);
                    if (uploadResult.Code != ReturnCode.SUCCESS)
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, uploadResult.Description);
                    }

                    if (GlobalConfiguration.Setting.EnableSaveUserUploadBase64)
                    {
                        startImageUrl = uploadResult.Description;
                    }
                    else
                    {
                        var finalFileName = uploadResult.Description;
                        var sendImageResult = await instance.SendImageMessageAsync(("upload image: " + finalFileName), finalFileName);
                        if (sendImageResult.Code != ReturnCode.SUCCESS)
                        {
                            return SubmitResultVO.Fail(sendImageResult.Code, sendImageResult.Description);
                        }
                        startImageUrl = sendImageResult.Description;
                    }
                }

                // 结束图片
                if (endUrl?.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                {
                    endImageUrl = endUrl.Url;
                }
                else if (endUrl?.Data != null && endUrl.Data.Length > 0)
                {
                    var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(endUrl.MimeType)}";
                    var uploadResult = await instance.UploadAsync(taskFileName, endUrl);
                    if (uploadResult.Code != ReturnCode.SUCCESS)
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, uploadResult.Description);
                    }
                    if (GlobalConfiguration.Setting.EnableSaveUserUploadBase64)
                    {
                        endImageUrl = uploadResult.Description;
                    }
                    else
                    {
                        var finalFileName = uploadResult.Description;
                        var sendImageResult = await instance.SendImageMessageAsync(("upload image: " + finalFileName), finalFileName);
                        if (sendImageResult.Code != ReturnCode.SUCCESS)
                        {
                            return SubmitResultVO.Fail(sendImageResult.Code, sendImageResult.Description);
                        }
                        endImageUrl = sendImageResult.Description;
                    }
                }

                if (!string.IsNullOrWhiteSpace(startImageUrl))
                {
                    info.BaseImageUrl = startImageUrl;
                }
            }

            if (!string.IsNullOrWhiteSpace(startImageUrl))
            {
                info.BaseImageUrl = startImageUrl;
            }

            // 提示词拼接
            var prompt = info.PromptEn;

            // 开始图片
            if (!string.IsNullOrWhiteSpace(startImageUrl) && !prompt.Contains(startImageUrl, StringComparison.OrdinalIgnoreCase))
            {
                prompt = $"{startImageUrl} {prompt}";
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

            // 如果有结束图片，则添加 --end 参数
            if (!string.IsNullOrWhiteSpace(endImageUrl) && !prompt.Contains("--end", StringComparison.OrdinalIgnoreCase))
            {
                prompt += $" --end {endImageUrl}";
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

            if (videoDTO.Action?.Equals("extend", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (info.IsPartner || info.IsOfficial)
                {
                    var customId = "";
                    if (info.IsPartner)
                    {
                        customId = $"MJ::JOB::animate_{videoDTO.Motion.ToLower()}_extend::{videoDTO.Index + 1}::{targetTask.PartnerTaskId}";
                    }
                    else if (info.IsOfficial)
                    {
                        customId = $"MJ::JOB::animate_{videoDTO.Motion.ToLower()}_extend::{videoDTO.Index + 1}::{targetTask.OfficialTaskId}";
                    }

                    info.SetProperty(Constants.TASK_PROPERTY_CUSTOM_ID, customId);
                    info.PromptEn = prompt;
                    info.VideoType = videoDTO.VideoType;
                    info.Description = "/video " + info.Prompt;

                    // 入队前不保存
                    //_taskStoreService.Save(info);

                    //return await instance.YmTaskService.SubmitActionAsync(info, new SubmitActionDTO()
                    //{
                    //    TaskId = targetTask.Id,
                    //    State = info.State,
                    //    CustomId = customId
                    //}, targetTask, _taskStoreService, instance);

                    return await instance.RedisEnqueue(new TaskInfoQueue()
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
                    });
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
                    info.PromptEn = prompt;
                    info.VideoType = videoDTO.VideoType;
                    info.Description = "/video extend";

                    //_taskStoreService.Save(info);

                    //// 先执行放大操作
                    //return await instance.ActionAsync(targetTask.MessageId, upscaleCustomId,
                    //    targetTask.GetProperty<int>(Constants.TASK_PROPERTY_FLAGS, default),
                    //    info.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), info);

                    return await instance.RedisEnqueue(new TaskInfoQueue()
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
                    });
                }
            }
            else
            {
                info.PromptEn = prompt;
                info.Description = "/video " + info.Prompt;

                // 入队前不保存
                //_taskStoreService.Save(info);

                //return await instance.YmTaskService.SubmitTaskAsync(info, _taskStoreService, instance);

                return await instance.RedisEnqueue(new TaskInfoQueue()
                {
                    Info = info,
                    Function = TaskInfoQueueFunction.VIDEO
                });
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
            var setting = GlobalConfiguration.Setting;

            var discordInstance = _discordLoadBalancer.GetDescribeInstance(task.AccountFilter?.InstanceId);
            if (discordInstance == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "无可用的账号实例");
            }

            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, discordInstance.ChannelId);
            task.InstanceId = discordInstance.ChannelId;
            task.IsPartner = discordInstance.Account.IsYouChuan;
            task.IsOfficial = discordInstance.Account.IsOfficial;

            var link = "";

            if (task.IsPartner || task.IsOfficial)
            {
                if (task.IsPartner)
                {
                    // 悠船
                    if (dataUrl.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        link = dataUrl.Url;
                    }
                    else
                    {
                        var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                        link = await discordInstance.YmTaskService.UploadFile(task, dataUrl.Data, taskFileName);
                    }
                }
                else
                {
                    // 官方
                    if (dataUrl.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        link = dataUrl.Url;
                    }
                    else
                    {
                        var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                        var uploadResult = await discordInstance.UploadAsync(taskFileName, dataUrl);
                        if (uploadResult.Code != ReturnCode.SUCCESS)
                        {
                            return SubmitResultVO.Fail(ReturnCode.FAILURE, uploadResult.Description);
                        }

                        if (uploadResult.Description.StartsWith("http"))
                        {
                            link = uploadResult.Description;
                        }
                        else
                        {
                            return SubmitResultVO.Fail(ReturnCode.FAILURE, "上传失败，未返回有效链接");
                        }
                    }
                }

                task.ImageUrl = link;
                task.Status = TaskStatus.NOT_START;

                return await discordInstance.RedisEnqueue(new TaskInfoQueue()
                {
                    Info = task,
                    Function = TaskInfoQueueFunction.DESCRIBE
                });
            }

            if (dataUrl.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
            {
                // 是否转换链接
                link = dataUrl.Url;

                // 如果转换用户链接到文件存储
                if (GlobalConfiguration.Setting.EnableSaveUserUploadLink)
                {
                    var ff = new FileFetchHelper();
                    var url = await ff.FetchFileToStorageAsync(link);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        link = url;
                    }
                }
            }
            else
            {
                var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                var uploadResult = await discordInstance.UploadAsync(taskFileName, dataUrl);
                if (uploadResult.Code != ReturnCode.SUCCESS)
                {
                    return SubmitResultVO.Fail(ReturnCode.FAILURE, uploadResult.Description);
                }

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
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, sendImageResult.Description);
                    }
                    link = sendImageResult.Description;
                }
            }

            task.ImageUrl = link;
            task.Status = TaskStatus.NOT_START;

            return await discordInstance.RedisEnqueue(new TaskInfoQueue()
            {
                Info = task,
                Function = TaskInfoQueueFunction.DESCRIBE
            });
        }

        /// <summary>
        /// 上传一个较长的提示词，mj 可以返回一组简要的提示词
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> ShortenAsync(TaskInfo task)
        {
            var (instance, mode) = _discordLoadBalancer.ChooseInstance(task.AccountFilter,
                isNewTask: true,
                botType: task.RealBotType ?? task.BotType,
                shorten: true);

            task.Mode = mode;
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.ChannelId);
            task.InstanceId = instance.ChannelId;

            task.Status = TaskStatus.NOT_START;

            // 入队前不保存
            //_taskStoreService.Save(task);

            return await instance.RedisEnqueue(new TaskInfoQueue()
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
            var setting = GlobalConfiguration.Setting;

            var (instance, mode) = _discordLoadBalancer.ChooseInstance(task.AccountFilter,
                isNewTask: true,
                botType: task.RealBotType ?? task.BotType,
                blend: true);

            task.Mode = mode;
            task.IsPartner = instance.Account.IsYouChuan;
            task.IsOfficial = instance.Account.IsOfficial;
            task.InstanceId = instance.ChannelId;
            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.ChannelId);

            var isYm = task.IsPartner || task.IsOfficial;
            // youchuan | mj
            if (isYm)
            {
                var finalFileNames = new List<string>();

                if (task.IsPartner)
                {
                    var link = "";
                    foreach (var dataUrl in dataUrls)
                    {
                        // 悠船
                        if (dataUrl.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            link = dataUrl.Url;
                        }
                        else
                        {
                            var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                            link = await instance.YmTaskService.UploadFile(task, dataUrl.Data, taskFileName);
                        }

                        if (string.IsNullOrWhiteSpace(link))
                        {
                            return SubmitResultVO.Fail(ReturnCode.FAILURE, "上传失败，未返回有效链接");
                        }

                        finalFileNames.Add(link);
                    }
                }
                else
                {
                    var link = "";
                    foreach (var dataUrl in dataUrls)
                    {
                        // 官方
                        if (dataUrl.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            link = dataUrl.Url;
                        }
                        else
                        {
                            var taskFileName = $"{Guid.NewGuid():N}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";
                            var uploadResult = await instance.UploadAsync(taskFileName, dataUrl);
                            if (uploadResult.Code != ReturnCode.SUCCESS)
                            {
                                return SubmitResultVO.Fail(ReturnCode.FAILURE, uploadResult.Description);
                            }

                            if (uploadResult.Description.StartsWith("http"))
                            {
                                link = uploadResult.Description;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(link))
                        {
                            return SubmitResultVO.Fail(ReturnCode.FAILURE, "上传失败，未返回有效链接");
                        }

                        finalFileNames.Add(link);
                    }
                }

                task.Action = TaskAction.BLEND;
                task.PromptEn = string.Join(" ", finalFileNames) + " " + task.PromptEn;

                if (!task.PromptEn.Contains("--ar"))
                {
                    switch (dimensions)
                    {
                        case BlendDimensions.PORTRAIT:
                            task.PromptEn += " --ar 2:3";
                            break;

                        case BlendDimensions.SQUARE:
                            task.PromptEn += " --ar 1:1";
                            break;

                        case BlendDimensions.LANDSCAPE:
                            task.PromptEn += " --ar 3:2";
                            break;

                        default:
                            break;
                    }
                }

                // 入队前不保存
                //_taskStoreService.Save(task);

                //return await discordInstance.YmTaskService.SubmitTaskAsync(task, _taskStoreService, discordInstance);

                return await instance.RedisEnqueue(new TaskInfoQueue()
                {
                    Info = task,
                    Function = TaskInfoQueueFunction.BLEND
                });
            }
            else
            {
                var finalFileNames = new List<string>();
                foreach (var item in dataUrls)
                {
                    var dataUrl = item;

                    // discord 混图只能通过 base64
                    if (dataUrl.Url?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        // 将 url 转为 bytes
                        var ff = new FileFetchHelper();
                        var res = await ff.FetchFileAsync(dataUrl.Url);
                        if (res.Success && res.FileBytes?.Length > 0)
                        {
                            dataUrl = new DataUrl(res.ContentType, res.FileBytes);
                        }
                        else
                        {
                            //return Message.Failure("Fetch image from url failed: " + dataUrl.Url);

                            return SubmitResultVO.Fail(ReturnCode.FAILURE, "获取图片失败 " + dataUrl.Url);
                        }
                    }

                    var guid = "";
                    if (dataUrls.Count > 0)
                    {
                        guid = "-" + Guid.NewGuid().ToString("N");
                    }

                    var taskFileName = $"{task.Id}{guid}.{MimeTypeUtils.GuessFileSuffix(dataUrl.MimeType)}";

                    var uploadResult = await instance.UploadAsync(taskFileName, dataUrl, useDiscordUpload: !isYm);
                    if (uploadResult.Code != ReturnCode.SUCCESS)
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, uploadResult.Description);
                    }

                    finalFileNames.Add(uploadResult.Description);
                }

                //return await discordInstance.BlendAsync(finalFileNames, dimensions,
                //    task.GetProperty<string>(Constants.TASK_PROPERTY_NONCE, default), task.RealBotType ?? task.BotType);

                return await instance.RedisEnqueue(new TaskInfoQueue()
                {
                    Info = task,
                    Function = TaskInfoQueueFunction.BLEND,
                    BlendParam = new TaskInfoQueue.TaskInfoQueueBlendParam()
                    {
                        FinalFileNames = finalFileNames,
                        Dimensions = dimensions
                    }
                });
            }
        }

        /// <summary>
        /// 根据任务获取可用的 Discord 实例（自动选择可用实例，适用于图片变化、视频扩展、弹窗）
        /// </summary>
        /// <param name="task"></param>
        /// <param name="parentTaskId"></param>
        /// <param name="submitResult"></param>
        /// <returns></returns>
        public DiscordInstance GetInstanceByTask(TaskInfo task, TaskInfo targetTask, out SubmitResultVO submitResult)
        {
            GenerationSpeedMode? mode = null;

            if (targetTask == null)
            {
                submitResult = SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "目标任务不存在");
                return null;
            }

            // 如果没有设置模式，则使用目标任务的模式
            if (task.Mode == null)
            {
                task.Mode = targetTask.Mode;
            }

            if (task.RequestMode == null)
            {
                task.RequestMode = targetTask.RequestMode;
            }

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

            var instance = _discordLoadBalancer.GetDiscordInstanceIsAlive(task.SubInstanceId ?? task.InstanceId);

            // 是否允许继续任务
            var isContinue = false;

            // 非放大任务，判断是否允许继续
            if (task.Action != TaskAction.UPSCALE)
            {
                foreach (var m in modes)
                {
                    if (instance != null && instance.IsAllowContinue(m) && instance.Account.IsAcceptActionTask())
                    {
                        isContinue = true;
                        mode = m;
                        break;
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
                        // 清除指定实例
                        task.AccountFilter.InstanceId = null;
                        var (okInstance, okMode) = _discordLoadBalancer.ChooseInstance(
                             isDiscord: true,
                             isActionTask: true,
                             accountFilter: task.AccountFilter,
                             botType: task.RealBotType ?? task.BotType,
                             instanceIds: ids,
                             isUpscale: task.Action == TaskAction.UPSCALE,
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

                    var (okInstance, okMode) = _discordLoadBalancer.ChooseInstance(
                        isYouChuan: true,
                        isActionTask: true,
                        accountFilter: task.AccountFilter,
                        botType: task.RealBotType ?? task.BotType,
                        isUpscale: task.Action == TaskAction.UPSCALE,
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
                            isContinue = true;
                            mode = m;
                            break;
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
            GenerationSpeedMode? mode = null;

            var targetTask = _taskStoreService.Get(submitAction.TaskId)!;
            if (targetTask == null)
            {
                return SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "目标任务不存在");
            }

            var instance = GetInstanceByTask(task, targetTask, out var submitResult);
            if (instance == null || submitResult.Code != ReturnCode.SUCCESS)
            {
                return submitResult;
            }

            // 判断是否允许视频操作
            if (task.Action == TaskAction.VIDEO && !instance.Account.IsAllowGenerateVideo())
            {
                return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "无可用的账号实例");
            }

            task.InstanceId = instance.ChannelId;
            task.IsPartner = instance.Account.IsYouChuan;
            task.IsOfficial = instance.Account.IsOfficial;

            var messageFlags = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_FLAGS, default)?.ToInt() ?? 0;
            var messageId = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_ID, default);

            task.Mode = mode;

            task.SetProperty(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, instance.ChannelId);
            task.IsOfficial = targetTask.IsOfficial;
            task.IsPartner = targetTask.IsPartner;
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

            // 如果是 remix
            if (submitAction.EnableRemix == true)
            {
                if (!instance.Account.IsOfficial && !instance.Account.IsYouChuan)
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

                // 悠船、管饭账号不判断 remix
                if (task.Action == TaskAction.PAN || task.Action == TaskAction.VARIATION || task.Action == TaskAction.REROLL || task.Action == TaskAction.VIDEO)
                {
                    task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, targetTask.MessageId);
                    task.SetProperty(Constants.TASK_PROPERTY_FLAGS, messageFlags);

                    // 如果是 REMIX 任务，则设置任务状态为 modal
                    task.Status = TaskStatus.MODAL;
                    _taskStoreService.Save(task);

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
            // 手动视频
            else if (submitAction.CustomId.StartsWith("MJ::JOB::video::")
                && submitAction.CustomId.Contains("::manual"))
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

                    _taskStoreService.Save(subTask);

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

                        _taskStoreService.Save(task);

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
                        _taskStoreService.Save(task);

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

                if (instance.Account.RemixAutoSubmit)
                {
                    // 如果开启了 remix 自动提交
                    // 并且已开启 remix 模式
                    if (((task.RealBotType ?? task.BotType) == EBotType.MID_JOURNEY && instance.Account.MjRemixOn)
                        || (task.BotType == EBotType.NIJI_JOURNEY && instance.Account.NijiRemixOn))
                    {
                        task.RemixAutoSubmit = true;

                        _taskStoreService.Save(task);

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

                        _taskStoreService.Save(task);

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
            GenerationSpeedMode? mode = null;

            var parentTask = _taskStoreService.Get(task.ParentId);

            var instance = GetInstanceByTask(task, parentTask, out var submitResult);
            if (instance == null || submitResult.Code != ReturnCode.SUCCESS)
            {
                return submitResult;
            }

            task.InstanceId = instance.ChannelId;
            task.Mode = mode;
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

            var discordInstance = _discordLoadBalancer.GetDiscordInstanceIsAlive(task.InstanceId);
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
                    _taskStoreService.Save(task);
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
                _taskStoreService.Save(task);

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

                    task = _taskStoreService.Get(task.Id);
                    if (!string.IsNullOrWhiteSpace(task.Seed))
                    {
                        return SubmitResultVO.Of(ReturnCode.SUCCESS, "成功", task.Seed);
                    }
                    else if (!string.IsNullOrWhiteSpace(task.SeedError))
                    {
                        return SubmitResultVO.Fail(ReturnCode.FAILURE, task.SeedError);
                    }
                } while (string.IsNullOrWhiteSpace(task.Seed) && maxDelay > 0);

                task = _taskStoreService.Get(task.Id);

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

            var discordInstance = _discordLoadBalancer.GetDiscordInstance(model.ChannelId);
            if (discordInstance == null)
            {
                throw new LogicException("无可用的账号实例");
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

            await SyncInfoSetting(id);
        }

        ///// <summary>
        ///// MJ Plus 数据迁移
        ///// </summary>
        ///// <param name="dto"></param>
        ///// <returns></returns>
        //public async Task MjPlusMigration(MjPlusMigrationDto dto)
        //{
        //    var key = "mjplus";
        //    var islock = AsyncLocalLock.HasActiveReference(key);
        //    if (!islock)
        //    {
        //        throw new LogicException("迁移任务执行中...");
        //    }

        //    _ = Task.Run(async () =>
        //    {
        //        var isLock = await AsyncLocalLock.TryLockAsync("mjplus", TimeSpan.FromMilliseconds(3), async () =>
        //        {
        //            try
        //            {
        //                // 账号迁移
        //                if (true)
        //                {
        //                    var ids = _freeSql.Select<DiscordAccount>().ToList(c => c.Id).ToHashSet<string>();

        //                    var path = "/mj/account/query";
        //                    var pageNumber = 0;
        //                    var pageSize = 100;
        //                    var isLastPage = false;
        //                    var sort = 0;

        //                    while (!isLastPage)
        //                    {
        //                        var responseContent = await MjPlusPageData(dto, path, pageSize, pageNumber);
        //                        var responseObject = JObject.Parse(responseContent);
        //                        var contentArray = (JArray)responseObject["content"];

        //                        if (contentArray.Count <= 0)
        //                        {
        //                            break;
        //                        }

        //                        foreach (var item in contentArray)
        //                        {
        //                            // 反序列化基础 JSON
        //                            var json = item.ToString();
        //                            var accountJson = JsonConvert.DeserializeObject<dynamic>(json);

        //                            // 创建
        //                            // 创建 DiscordAccount 实例
        //                            var acc = new DiscordAccount
        //                            {
        //                                Sponsor = "by mjplus",
        //                                DayDrawLimit = -1, // 默认值 -1

        //                                ChannelId = accountJson.channelId,
        //                                GuildId = accountJson.guildId,
        //                                PrivateChannelId = accountJson.mjBotChannelId,
        //                                NijiBotChannelId = accountJson.nijiBotChannelId,
        //                                UserToken = accountJson.userToken,
        //                                BotToken = null,
        //                                UserAgent = accountJson.userAgent,
        //                                Enable = accountJson.enable,
        //                                EnableMj = true,
        //                                EnableNiji = true,
        //                                CoreSize = accountJson.coreSize ?? 3, // 默认值 3
        //                                Interval = 1.2m, // 默认值 1.2
        //                                AfterIntervalMin = 1.2m, // 默认值 1.2
        //                                AfterIntervalMax = 1.2m, // 默认值 1.2
        //                                QueueSize = accountJson.queueSize ?? 10, // 默认值 10
        //                                TimeoutMinutes = accountJson.timeoutMinutes ?? 5, // 默认值 5
        //                                Remark = accountJson.remark,

        //                                DateCreated = DateTimeOffset.FromUnixTimeMilliseconds((long)accountJson.dateCreated).DateTime,
        //                                Weight = 1, // 假设 weight 来自 properties
        //                                WorkTime = null,
        //                                FishingTime = null,
        //                                Sort = ++sort,
        //                                RemixAutoSubmit = accountJson.remixAutoSubmit,
        //                                Mode = Enum.TryParse<GenerationSpeedMode>((string)accountJson.mode, out var mode) ? mode : (GenerationSpeedMode?)null,
        //                                AllowModes = new List<GenerationSpeedMode>(),
        //                                Components = new List<Component>(),
        //                                IsBlend = true, // 默认 true
        //                                IsDescribe = true, // 默认 true
        //                                IsVerticalDomain = false, // 默认 false
        //                                IsShorten = true,
        //                                VerticalDomainIds = new List<string>(),
        //                                SubChannels = new List<string>(),
        //                                SubChannelValues = new Dictionary<string, string>(),

        //                                Id = accountJson.id,
        //                            };

        //                            if (!ids.Contains(acc.Id))
        //                            {
        //                                DbHelper.Instance.AccountStore.Add(acc);
        //                                ids.Add(acc.Id);
        //                            }
        //                        }

        //                        isLastPage = (bool)responseObject["last"];
        //                        pageNumber++;

        //                        Log.Information($"账号迁移进度, 第 {pageNumber} 页, 每页 {pageSize} 条, 已完成");
        //                    }

        //                    Log.Information("账号迁移完成");
        //                }

        //                // 任务迁移
        //                if (true)
        //                {
        //                    var accounts = _freeSql.Get < DiscordAccount > All();

        //                    var ids = _freeSql.Select<TaskInfo>().ToList(c => c.Id).ToHashSet<string>();

        //                    var path = "/mj/task-admin/query";
        //                    var pageNumber = 0;
        //                    var pageSize = 100;
        //                    var isLastPage = false;

        //                    while (!isLastPage)
        //                    {
        //                        var responseContent = await MjPlusPageData(dto, path, pageSize, pageNumber);
        //                        var responseObject = JObject.Parse(responseContent);
        //                        var contentArray = (JArray)responseObject["content"];

        //                        if (contentArray.Count <= 0)
        //                        {
        //                            break;
        //                        }

        //                        foreach (var item in contentArray)
        //                        {
        //                            // 反序列化基础 JSON
        //                            var json = item.ToString();
        //                            var jsonObject = JsonConvert.DeserializeObject<dynamic>(json);

        //                            string aid = jsonObject.properties?.discordInstanceId;
        //                            var acc = accounts.FirstOrDefault(x => x.Id == aid);

        //                            // 创建 TaskInfo 实例
        //                            var taskInfo = new TaskInfo
        //                            {
        //                                FinishTime = jsonObject.finishTime,
        //                                PromptEn = jsonObject.promptEn,
        //                                Description = jsonObject.description,
        //                                SubmitTime = jsonObject.submitTime,
        //                                ImageUrl = jsonObject.imageUrl,
        //                                Action = Enum.TryParse<TaskAction>((string)jsonObject.action, out var action) ? action : (TaskAction?)null,
        //                                Progress = jsonObject.progress,
        //                                StartTime = jsonObject.startTime,
        //                                FailReason = jsonObject.failReason,
        //                                Id = jsonObject.id,
        //                                State = jsonObject.state,
        //                                Prompt = jsonObject.prompt,
        //                                Status = Enum.TryParse<TaskStatus>((string)jsonObject.status, out var status) ? status : (TaskStatus?)null,
        //                                Nonce = jsonObject.properties?.nonce,
        //                                MessageId = jsonObject.properties?.messageId,
        //                                BotType = Enum.TryParse<EBotType>((string)jsonObject.properties?.botType, out var botType) ? botType : EBotType.MID_JOURNEY,
        //                                InstanceId = acc?.ChannelId,
        //                                Buttons = JsonConvert.DeserializeObject<List<CustomComponentModel>>(JsonConvert.SerializeObject(jsonObject.buttons)),
        //                                Properties = JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(jsonObject.properties)),
        //                            };

        //                            aid = taskInfo.GetProperty<string>(Constants.TASK_PROPERTY_DISCORD_INSTANCE_ID, default);
        //                            if (!string.IsNullOrWhiteSpace(aid))
        //                            {
        //                                acc = accounts.FirstOrDefault(x => x.Id == aid);
        //                                if (acc != null)
        //                                {
        //                                    taskInfo.InstanceId = acc.ChannelId;
        //                                }
        //                            }

        //                            if (!ids.Contains(taskInfo.Id))
        //                            {
        //                                _freeSql.Add(taskInfo);
        //                                ids.Add(taskInfo.Id);
        //                            }
        //                        }

        //                        isLastPage = (bool)responseObject["last"];
        //                        pageNumber++;

        //                        Log.Information($"任务迁移进度, 第 {pageNumber} 页, 每页 {pageSize} 条, 已完成");
        //                    }

        //                    Log.Information("任务迁移完成");
        //                }
        //            }
        //            catch (Exception ex)
        //            {
        //                Log.Error(ex, "mjplus 迁移执行异常");
        //            }
        //        });

        //        if (!islock)
        //        {
        //            Log.Warning("迁移任务执行中...");
        //        }
        //    });

        //    await Task.CompletedTask;
        //}

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
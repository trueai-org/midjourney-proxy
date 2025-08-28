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

using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FreeSql.DataAnnotations;
using Microsoft.Extensions.Caching.Memory;
using Midjourney.Base.Data;
using Midjourney.Base.Dto;
using Midjourney.Base.Storage;
using Serilog;

namespace Midjourney.Base.Models
{
    /// <summary>
    /// 任务类，表示一个任务的基本信息。
    /// </summary>
    [BsonCollection("task")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreExtraElements]
    [Serializable]
    [Index("i_UserId", "UserId")]
    [Index("i_ClientIp", "ClientIp")]
    [Index("i_InstanceId", "InstanceId")]
    [Index("i_SubmitTime", "SubmitTime")]
    [Index("i_Status", "Status")]
    [Index("i_Action", "Action")]
    [Index("i_ParentId", "ParentId")]
    [Index("i_SubmitTime_UserId", "SubmitTime,UserId")]
    [Index("i_SubmitTime_InstanceId", "SubmitTime,InstanceId")]
    [Index("i_State", "State")]
    [Index("i_Mode", "Mode")]
    [Index("i_Nonce", "Nonce")]
    [Index("i_IsPartner", "IsPartner")]
    [Index("i_PartnerTaskId", "PartnerTaskId")]
    [Index("i_IsOfficial", "IsOfficial")]
    [Index("i_OfficialTaskId", "OfficialTaskId")]
    public class TaskInfo : DomainObject
    {
        /// <summary>
        /// Midjourney CDN 域名
        /// </summary>
        public const string MIDJOURNEY_CDN = "cdn.midjourney.com";

        /// <summary>
        /// 版本号匹配正则表达式。
        /// </summary>
        private const string VERSION_PATTERN = @"--(?<flag>v|niji)\s*(?<version>\d+(?:\.\d+)?)";

        /// <summary>
        /// 版本号匹配正则表达式实例。
        /// </summary>
        private static readonly Regex VersionRegex = new Regex(VERSION_PATTERN, RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public TaskInfo()
        {
        }

        /// <summary>
        /// 父级 ID
        /// </summary>
        public string ParentId { get; set; }

        /// <summary>
        /// bot 类型，mj(默认)或niji
        /// MID_JOURNEY | 枚举值: NIJI_JOURNEY
        /// </summary>
        public EBotType BotType { get; set; }

        /// <summary>
        /// 真实的 bot 类型，mj(默认)或niji
        /// MID_JOURNEY | 枚举值: NIJI_JOURNEY
        /// 当开启 niji 转 mj 时，这里记录的是 mj bot
        /// </summary>
        public EBotType? RealBotType { get; set; }

        /// <summary>
        /// 绘画用户 ID
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// 白名单用户（加入白名单不受限流控制）
        /// </summary>
        public bool IsWhite { get; set; } = false;

        /// <summary>
        /// 提交作业的唯一ID。
        /// </summary>
        public string Nonce { get; set; }

        /// <summary>
        /// 与 MJ 交互成功后消息 ID。
        /// INTERACTION_SUCCESS
        /// </summary>
        public string InteractionMetadataId { get; set; }

        /// <summary>
        /// 消息 ID（MJ 消息 ID，Nonce 与 MessageId 对应）
        /// 最终消息 ID
        /// </summary>
        public string MessageId { get; set; }

        /// <summary>
        /// Remix 模式时，返回的消息 ID
        /// Remix Modal 消息 ID
        /// </summary>
        public string RemixModalMessageId { get; set; }

        /// <summary>
        /// 表示是否为 Remix 自动提交任务
        /// </summary>
        public bool RemixAutoSubmit { get; set; }

        /// <summary>
        /// Remix 模式，处于弹窗模式中时
        /// </summary>
        public bool RemixModaling { get; set; }

        /// <summary>
        /// 账号实例 ID = 账号渠道 ID = ChannelId
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// 子频道 ID
        /// </summary>
        public string SubInstanceId { get; set; }

        /// <summary>
        /// 消息 ID
        /// 创建消息 ID -> 进度消息 ID -> 完成消息 ID
        /// </summary>
        [JsonMap]
        public List<string> MessageIds { get; set; } = new List<string>();

        /// <summary>
        /// 任务类型。
        /// </summary>
        public TaskAction? Action { get; set; }

        /// <summary>
        /// 任务状态。
        /// </summary>
        public TaskStatus? Status { get; set; }

        /// <summary>
        /// 提示词。
        /// </summary>
        [Column(StringLength = -1)]
        public string Prompt { get; set; }

        /// <summary>
        /// 提示词（英文）。
        /// </summary>
        [Column(StringLength = -1)]
        public string PromptEn { get; set; }

        /// <summary>
        /// 提示词（由 mj 返回的完整提示词）
        /// </summary>
        [Column(StringLength = -1)]
        public string PromptFull { get; set; }

        /// <summary>
        /// 任务描述。
        /// </summary>
        [Column(StringLength = -1)]
        public string Description { get; set; }

        /// <summary>
        /// 自定义参数。
        /// </summary>
        public string State { get; set; }

        /// <summary>
        /// 提交时间。
        /// </summary>
        public long? SubmitTime { get; set; }

        /// <summary>
        /// 开始执行时间。
        /// </summary>
        public long? StartTime { get; set; }

        /// <summary>
        /// 结束时间。
        /// </summary>
        public long? FinishTime { get; set; }

        /// <summary>
        /// 图片URL。
        /// </summary>
        [Column(StringLength = 1024)]
        public string ImageUrl { get; set; }

        /// <summary>
        /// 基础图片 URL/垫图 URL
        /// </summary>
        [Column(StringLength = 1024)]
        public string BaseImageUrl { get; set; }

        /// <summary>
        /// 图像URL列表
        /// </summary>
        [JsonMap]
        public List<TaskInfoImageUrl> ImageUrls { get; set; }

        /// <summary>
        /// 缩略图 url
        /// </summary>
        [Column(StringLength = 1024)]
        public string ThumbnailUrl { get; set; }

        /// <summary>
        /// 任务进度。
        /// </summary>
        public string Progress { get; set; }

        /// <summary>
        /// 失败原因。
        /// </summary>
        [Column(StringLength = -1)]
        public string FailReason { get; set; }

        /// <summary>
        /// 是否为悠船任务
        /// </summary>
        public bool IsPartner { get; set; }

        /// <summary>
        /// 是否为悠船放松模式任务
        /// </summary>
        public bool IsPartnerRelax => IsPartner && Mode == GenerationSpeedMode.RELAX;

        /// <summary>
        /// 悠船任务 ID
        /// </summary>
        public string PartnerTaskId { get; set; }

        /// <summary>
        /// 悠船任务
        /// </summary>
        [JsonMap]
        public YouChuanTask PartnerTaskInfo { get; set; }

        /// <summary>
        /// 是否为官方任务
        /// </summary>
        public bool IsOfficial { get; set; }

        /// <summary>
        /// 官方任务 ID
        /// </summary>
        public string OfficialTaskId { get; set; }

        /// <summary>
        /// 官方任务
        /// </summary>
        [JsonMap]
        public OfficialJobStatus OfficialTaskInfo { get; set; }

        /// <summary>
        /// 按钮
        /// </summary>
        [JsonMap]
        public List<CustomComponentModel> Buttons { get; set; } = new List<CustomComponentModel>();

        /// <summary>
        /// 任务的显示信息。
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        [Column(IsIgnore = true)]
        public Dictionary<string, object> Displays
        {
            get
            {
                var dic = new Dictionary<string, object>();

                // 状态
                dic["status"] = Status.ToString();

                // 转为可视化时间
                dic["submitTime"] = SubmitTime?.ToDateTimeString();
                dic["startTime"] = StartTime?.ToDateTimeString();
                dic["finishTime"] = FinishTime?.ToDateTimeString();

                // 行为
                dic["action"] = Action.ToString();

                // discord 实例 ID
                dic["discordInstanceId"] = Properties.ContainsKey("discordInstanceId") ? Properties["discordInstanceId"] : "";

                return dic;
            }
        }

        /// <summary>
        /// 任务的种子。
        /// </summary>
        public string Seed { get; set; }

        /// <summary>
        /// Seed 消息 ID
        /// </summary>
        public string SeedMessageId { get; set; }

        /// <summary>
        /// 绘图任务客户的 IP 地址
        /// </summary>
        public string ClientIp { get; set; }

        /// <summary>
        /// 图片 ID / 图片 hash
        /// </summary>
        public string JobId { get; set; }

        /// <summary>
        /// 是否为 replicate 任务
        /// </summary>
        public bool IsReplicate { get; set; }

        /// <summary>
        /// 人脸源图片
        /// </summary>
        [Column(StringLength = 1024)]
        public string ReplicateSource { get; set; }

        /// <summary>
        /// 目标图片/目标视频
        /// </summary>
        [Column(StringLength = 1024)]
        public string ReplicateTarget { get; set; }

        /// <summary>
        /// 当前绘画的速度模式
        /// 1、优先从路劲获取指定的速度
        /// 2、如果路径没有指定速度，则从执行结果中获取速度模式
        /// 3、变化任务时，默认取父级的速度模式
        /// 4、如果任务成功后，依然没有速度，则默认为 FAST
        /// </summary>
        public GenerationSpeedMode? Mode { get; set; }

        /// <summary>
        /// 客户请求的速度模式。
        /// </summary>
        public GenerationSpeedMode? RequestMode { get; set; }

        /// <summary>
        /// 账号过滤
        /// </summary>
        [JsonMap]
        public AccountFilter AccountFilter { get; set; }

        /// <summary>
        /// 原始内容 - 获取图片的 URL
        /// </summary>
        [Column(StringLength = 1024)]
        public string Url { get; set; }

        /// <summary>
        /// 原始内容 - 获取图片的代理 URL
        /// </summary>
        [Column(StringLength = 1024)]
        public string ProxyUrl { get; set; }

        /// <summary>
        /// 原始内容 - 图片高度
        /// </summary>
        public int? Height { get; set; }

        /// <summary>
        /// 原始内容 - 图片宽度
        /// </summary>
        public int? Width { get; set; }

        /// <summary>
        /// 原始内容 - 图片大小
        /// </summary>
        public long? Size { get; set; }

        /// <summary>
        /// 原始内容 - 内容类型
        /// </summary>
        [Column(StringLength = 200)]
        public string ContentType { get; set; }

        /// <summary>
        /// 原始内容 - 图片宽度
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
        public int? ImageHeight => Height;

        /// <summary>
        /// 原始内容 - 图片高度
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
        public int? ImageWidth => Width;

        ///// <summary>
        ///// 获取当前绘图的速度模式
        ///// </summary>
        ///// <returns></returns>
        //public GenerationSpeedMode? GetMode()
        //{
        //    // 如果自身有速度模式
        //    if (Mode != null)
        //    {
        //        return Mode;
        //    }

        //    // 如果过滤参数中有速度模式，则直接返回
        //    if (AccountFilter != null && AccountFilter.Modes?.Count > 0)
        //    {
        //        return AccountFilter.Modes.FirstOrDefault();
        //    }

        //    if (!string.IsNullOrWhiteSpace(Prompt))
        //    {
        //        // 解析提示词
        //        var prompt = Prompt.ToLower();

        //        // 解析速度模式
        //        if (prompt.Contains("--fast"))
        //        {
        //            return GenerationSpeedMode.FAST;
        //        }
        //        else if (prompt.Contains("--relax"))
        //        {
        //            return GenerationSpeedMode.RELAX;
        //        }
        //        else if (prompt.Contains("--turbo"))
        //        {
        //            return GenerationSpeedMode.TURBO;
        //        }
        //        else
        //        {
        //            return null;
        //        }
        //    }

        //    return null;
        //}

        ///// <summary>
        ///// 获取当前绘图的速度模式字符串表示。
        ///// </summary>
        ///// <returns></returns>
        //public string GetModeString()
        //{
        //    return Mode switch
        //    {
        //        GenerationSpeedMode.FAST => "fast",
        //        GenerationSpeedMode.RELAX => "relax",
        //        GenerationSpeedMode.TURBO => "turbo",
        //        _ => "relax"
        //    };
        //}

        /// <summary>
        /// 视频生成原始图像URL
        /// </summary>
        public string VideoGenOriginImageUrl { get; set; }

        /// <summary>
        /// 视频时长 s
        /// </summary>
        public int? VideoDuration { get; set; }

        /// <summary>
        /// 视频帧数
        /// </summary>
        public int? FrameCount { get; set; }

        /// <summary>
        /// 视频列表
        /// </summary>
        [JsonMap]
        public List<TaskInfoVideoUrl> VideoUrls { get; set; }

        /// <summary>
        /// 耗时
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
        public string UseTime
        {
            get
            {
                if (StartTime.HasValue && FinishTime.HasValue)
                {
                    var duration = FinishTime.Value - StartTime.Value;
                    if (duration < 0)
                    {
                        duration = 0; // 确保不会出现负数
                    }

                    return $"{duration / 1000.0:F2} s";
                }
                return "-";
            }
        }

        /// <summary>
        /// 版本
        /// v 1, v 2, v 3, v 4, v 5, v 5.1, v 5.2, v 6, v 6.1, v 7
        /// niji 4, niji 5, niji 6
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// 存储选项（私人定制）
        /// 请求头 x-storage-options: 1 | 2, 1: 返回官方链接, 2: 返回合作商链接
        /// </summary>
        public EStorageOption? StorageOption { get; set; }

        /// <summary>
        /// 视频分辨率类型
        /// 取值：vid_1.1_i2v_480 | vid_1.1_i2v_720
        /// SD: vid_1.1_i2v_480
        /// HD: vid_1.1_i2v_720
        /// </summary>
        public string VideoType { get; set; }

        /// <summary>
        /// 是否为高清视频
        /// </summary>
        public bool IsHdVideo => VideoType == "vid_1.1_i2v_720";

        // ------------------------------------- 方法 --------------------------------------

        /// <summary>
        /// 启动任务。
        /// </summary>
        public void Start()
        {
            StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Status = TaskStatus.SUBMITTED;
            Progress = "0%";
        }

        /// <summary>
        /// 任务成功。
        /// </summary>
        public void Success()
        {
            try
            {
                // 保存图片
                StorageHelper.DownloadFile(this).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "保存图片失败 {@0}", ImageUrl);
            }

            SuccessUpdate();
        }

        /// <summary>
        /// 异步保存成功（自动设置完成）
        /// </summary>
        /// <returns></returns>
        public async Task SuccessAsync()
        {
            try
            {
                // 保存图片
                await StorageHelper.DownloadFile(this);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "保存图片失败 {@0}", ImageUrl);
            }

            SuccessUpdate();
        }

        /// <summary>
        /// 更新后的操作
        /// </summary>
        private void SuccessUpdate()
        {
            // 调整图片 ACTION
            // 如果是 show 时
            if (Action == TaskAction.SHOW)
            {
                // 根据 buttons 调整
                if (Buttons.Count > 0)
                {
                    // U1
                    if (Buttons.Any(x => x.CustomId?.Contains("MJ::JOB::upsample::1") == true))
                    {
                        Action = TaskAction.IMAGINE;
                    }
                    // 局部重绘说明是放大
                    else if (Buttons.Any(x => x.CustomId?.Contains("MJ::Inpaint::") == true))
                    {
                        Action = TaskAction.UPSCALE;
                    }
                    // MJ::Job::PicReader
                    else if (Buttons.Any(x => x.CustomId?.Contains("MJ::Job::PicReader") == true))
                    {
                        Action = TaskAction.DESCRIBE;
                    }
                }
            }

            // 根据最终提示词更新速度模式
            var finalPrompt = GetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, "");
            if (!string.IsNullOrWhiteSpace(finalPrompt))
            {
                // 解析提示词
                var prompt = finalPrompt.ToLower();

                // 解析速度模式
                if (prompt.Contains("--fast", StringComparison.OrdinalIgnoreCase))
                {
                    Mode = GenerationSpeedMode.FAST;
                }
                else if (prompt.Contains("--relax", StringComparison.OrdinalIgnoreCase))
                {
                    Mode = GenerationSpeedMode.RELAX;
                }
                else if (prompt.Contains("--turbo", StringComparison.OrdinalIgnoreCase))
                {
                    Mode = GenerationSpeedMode.TURBO;
                }

                Version = GetVersion(finalPrompt);
            }

            // 如果没有解析到，则使用默认值
            if (Mode == null)
            {
                Mode = GenerationSpeedMode.FAST;
            }
            if (RequestMode == null)
            {
                RequestMode = GenerationSpeedMode.FAST;
            }

            // 如果开启了保持速度模式，且速度模式不一致时，替换提示词 RequestMode
            var setting = GlobalConfiguration.Setting;
            if (RequestMode != null && Mode != RequestMode && setting.PrivateKeepFinalPromptRequestSpeedMode)
            {
                finalPrompt = finalPrompt.AppendSpeedMode(RequestMode);

                SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, finalPrompt);
            }

            UpdateUserDrawCount(true);

            // 最后才设置完成时间和状态
            FinishTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Status = TaskStatus.SUCCESS;
            Progress = "100%";
        }

        /// <summary>
        /// 任务失败。
        /// </summary>
        /// <param name="reason">失败原因。</param>
        public void Fail(string reason)
        {
            if (reason?.Length > 4000)
            {
                reason = reason.Substring(0, 4000); // 限制失败原因长度
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                if (reason.Contains("Banned prompt detected", StringComparison.OrdinalIgnoreCase)
                    || reason.Contains("Image denied", StringComparison.OrdinalIgnoreCase))
                {
                    // 触发提示提示词封锁
                    var band = GlobalConfiguration.Setting?.BannedLimiting;
                    var cache = GlobalConfiguration.MemoryCache;

                    // 记录累计触发次数
                    if (band?.Enable == true && cache != null)
                    {
                        if (!string.IsNullOrWhiteSpace(UserId))
                        {
                            // user band
                            var bandKey = $"banned:{DateTime.Now.Date:yyyyMMdd}:{UserId}";
                            cache.TryGetValue(bandKey, out int limit);
                            limit++;
                            cache.Set(bandKey, limit, TimeSpan.FromDays(1));
                        }

                        if (true)
                        {
                            // ip band
                            var bandKey = $"banned:{DateTime.Now.Date:yyyyMMdd}:{ClientIp}";
                            cache.TryGetValue(bandKey, out int limit);
                            limit++;
                            cache.Set(bandKey, limit, TimeSpan.FromDays(1));
                        }
                    }
                }
            }

            UpdateUserDrawCount(false);

            FinishTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Status = TaskStatus.FAILURE;
            FailReason = reason;
            Progress = "";
        }

        /// <summary>
        /// 统计用户绘图次数。
        /// </summary>
        public void UpdateUserDrawCount(bool success)
        {
            try
            {
                DrawCounter.Complete(this, success);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "统计绘图次数失败");
            }
        }

        /// <summary>
        /// 放大后的操作按钮
        /// </summary>
        public void OnUpscaleButtons(string id, int index, string prompt)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            // 清除现有按钮
            Buttons.Clear();

            var version = GetVersion(prompt);

            // 高清按钮
            var upscaleButtons = CustomComponentModel.CreateUpscaleButtons(this, id, index, version);
            if (upscaleButtons?.Count > 0)
            {
                Buttons.AddRange(upscaleButtons);
            }

            // 变化按钮
            var varyButtons = CustomComponentModel.CreateVaryButtons(id, index, version);
            if (varyButtons?.Count > 0)
            {
                Buttons.AddRange(varyButtons);
            }

            // 缩放按钮
            var zoomButtons = CustomComponentModel.CreateZoomButtons(id, index, version);
            if (zoomButtons?.Count > 0)
            {
                Buttons.AddRange(zoomButtons);
            }

            // 延展按钮
            var panButtons = CustomComponentModel.CreatePanButtons(id, index, version);
            if (panButtons?.Count > 0)
            {
                Buttons.AddRange(panButtons);
            }

            // 视频操作按钮
            // 开启视频绘图，放大时，增加视频操作
            if (Action == TaskAction.UPSCALE && GlobalConfiguration.Setting.EnableVideo)
            {
                var videoButtons = CustomComponentModel.CreateAnimateButtons(id, index);
                if (videoButtons?.Count > 0)
                {
                    Buttons.AddRange(videoButtons);
                }
            }

            // 重绘按钮
            Buttons.Add(CustomComponentModel.CreateRerollButtons(id));
        }

        /// <summary>
        /// 高清后的操作按钮
        /// </summary>
        public void OnUpscale2xButtons(string id, int index, string prompt)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            // 清除现有按钮
            Buttons.Clear();

            var version = GetVersion(prompt);

            // 变化按钮
            var varyButtons = CustomComponentModel.CreateVaryButtons(id, index, version);
            if (varyButtons?.Count > 0)
            {
                Buttons.AddRange(varyButtons);
            }

            // 缩放按钮
            var zoomButtons = CustomComponentModel.CreateZoomButtons(id, index, version);
            if (zoomButtons?.Count > 0)
            {
                Buttons.AddRange(zoomButtons);
            }

            // 视频按钮
            var videoButtons = CustomComponentModel.CreateAnimateButtons(id, index);
            if (videoButtons?.Count > 0)
            {
                Buttons.AddRange(videoButtons);
            }

            // 重绘按钮
            Buttons.Add(CustomComponentModel.CreateRerollButtons(id));
        }

        /// <summary>
        /// 设置视频放大按钮。
        /// </summary>
        public void SetVideoUpscaleButtons(string id, int videoCount = 4)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            Buttons.Clear();

            for (int i = 1; i <= videoCount; i++)
            {
                Buttons.Add(new CustomComponentModel
                {
                    CustomId = $"MJ::JOB::video_virtual_upscale::{i}::{id}",
                    Label = $"U{i}",
                    Emoji = "",
                    Style = 2,
                    Type = 2
                });
            }

            // 增加重绘按钮
            Buttons.Add(CustomComponentModel.CreateRerollButtons(id));
        }

        /// <summary>
        /// 设置视频扩展动作操作按钮
        /// </summary>
        /// <param name="id"></param>
        public void SetVideoExtendButtons(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            Buttons.Clear();

            Buttons.Add(new CustomComponentModel
            {
                CustomId = $"MJ::JOB::animate_high_extend::1::{id}",
                Label = "Extend (High motion)",
                Emoji = "🎞️",
                Style = 2,
                Type = 2
            });

            Buttons.Add(new CustomComponentModel
            {
                CustomId = $"MJ::JOB::animate_low_extend::1::{id}",
                Label = "Extend (Low motion)",
                Emoji = "🎞️",
                Style = 2,
                Type = 2
            });
        }

        /// <summary>
        /// 设置描述按钮。
        /// </summary>
        /// <param name="result"></param>
        public void SetDescribeButtions(string[] result)
        {
            if (result == null || result.Length <= 0)
                return;

            // emoji 转换
            // https://tw.piliapp.com/emoji/list/
            // https://www.ifreesite.com/unicode/

            var json = """
                        [
                          {
                            "customId": "MJ::Job::PicReader::1",
                            "emoji": "\u0031\uFE0F\u20E3",
                            "label": "",
                            "style": 2,
                            "type": 2
                          },
                          {
                            "customId": "MJ::Job::PicReader::2",
                            "emoji": "\u0032\uFE0F\u20E3",
                            "label": "",
                            "style": 2,
                            "type": 2
                          },
                          {
                            "customId": "MJ::Job::PicReader::3",
                            "emoji": "\u0033\uFE0F\u20E3",
                            "label": "",
                            "style": 2,
                            "type": 2
                          },
                          {
                            "customId": "MJ::Job::PicReader::4",
                            "emoji": "\u0034\uFE0F\u20E3",
                            "label": "",
                            "style": 2,
                            "type": 2
                          },
                          {
                            "customId": "MJ::Picread::Retry",
                            "emoji": "\u1F504",
                            "label": "",
                            "style": 2,
                            "type": 2
                          }
                        ]
                        """;

            try
            {
                // 反序列化 JSON 并添加到按钮列表
                var buttons = System.Text.Json.JsonSerializer.Deserialize<List<CustomComponentModel>>(json, new System.Text.Json.JsonSerializerOptions()
                {
                    // 忽略大小写
                    PropertyNameCaseInsensitive = true,
                });
                if (buttons != null)
                {
                    Buttons.Clear();
                    Buttons.AddRange(buttons);
                }

                // 设置描述结果
                for (int i = 0; i < result.Length; i++)
                {
                    if (i < Buttons.Count)
                    {
                        Buttons[i].Label = result[i];
                    }
                }

                // 移除 label 为空的 button
                Buttons.RemoveAll(c => string.IsNullOrWhiteSpace(c.Label));

                foreach (var item in Buttons)
                {
                    item.Label = "";
                }

                PromptEn = string.Join("\r\n", result);
                SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, string.Join("\r\n", result));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "设置描述按钮失败，JSON 解析错误");
            }
        }

        /// <summary>
        /// 获取版本
        /// </summary>
        /// <param name="prompt"></param>
        /// <returns>v 6.1, niji 6</returns>
        public string GetVersion(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                // 默认返回 v 7
                return "v 7";
            }

            var mat = VersionRegex.Match(prompt);
            if (mat.Success)
            {
                var version = mat.Groups["version"].Value;
                var flag = mat.Groups["flag"].Value;
                if (!string.IsNullOrWhiteSpace(version) && !string.IsNullOrWhiteSpace(flag))
                {
                    return $"{flag} {version}".Trim();
                }
            }

            // niji 默认 niji 6
            if (BotType == EBotType.NIJI_JOURNEY || prompt.Contains("--niji"))
            {
                return "niji 6";
            }

            // 默认返回 v 7
            return "v 7";
        }

        /// <summary>
        /// 计算最大公约数（GCD）
        /// </summary>
        /// <param name="value1"></param>
        /// <param name="value2"></param>
        /// <returns></returns>
        public int GCD(int value1, int value2)
        {
            while (value2 != 0)
            {
                var temp = value2;
                value2 = value1 % value2;
                value1 = temp;
            }
            return value1;
        }

        /// <summary>
        /// 转换 URL 为官方链接或合作商链接。
        /// </summary>
        /// <param name="sourceUrl"></param>
        /// <param name="newUrl"></param>
        /// <returns></returns>
        public string TransformUrl(string sourceUrl, string newUrl = null)
        {
            // 悠船
            if (IsPartner)
            {
                if (!string.IsNullOrWhiteSpace(sourceUrl))
                {
                    if (StorageOption == EStorageOption.Partner)
                    {
                        return sourceUrl;
                    }
                    else if (StorageOption == EStorageOption.Official)
                    {
                        // 转为官方链接
                        var uri = new Uri(sourceUrl);

                        // https://youchuan-imagine.oss-cn-shanghai.aliyuncs.com/16e48f88-e900-4fe3-a9f3-4454e44bf19c_0_2.png
                        // https://cdn.midjourney.com/16e48f88-e900-4fe3-a9f3-4454e44bf19c/0_1.png

                        // 如果路径符合 /{guid}_{i}_{n}.png 的结尾，使用正则名称提取
                        var match = Regex.Match(uri.PathAndQuery, @"^/(?<guid>[a-z0-9\-]+)_(?<i>\d+)_(?<n>\d+)\.png$", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            var guid = match.Groups["guid"].Value;
                            var i = match.Groups["i"].Value;
                            var n = match.Groups["n"].Value;

                            // 返回官方链接
                            return $"https://{MIDJOURNEY_CDN}/{guid}/{i}_{n}.png";
                        }

                        // https://youchuan-imagine.oss-cn-shanghai.aliyuncs.com/e39614f4-a83f-4d39-8402-d807f3a3ca7d_0.mp4
                        // https://cdn.midjourney.com/video/8809c019-f50d-4502-9436-a2716275d546/0.mp4

                        // 如果路径符合 /{guid}_{i}.mp4 的结尾，使用正则名称提取
                        match = Regex.Match(uri.PathAndQuery, @"^/(?<guid>[a-z0-9\-]+)_(?<i>\d+)\.mp4$", RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            var guid = match.Groups["guid"].Value;
                            var i = match.Groups["i"].Value;

                            // 返回官方链接
                            return $"https://{MIDJOURNEY_CDN}/video/{guid}/{i}.mp4";
                        }

                        return $"https://{MIDJOURNEY_CDN}/{uri.PathAndQuery.TrimStart('/')}";
                    }
                }
            }
            // 官方
            else if (IsOfficial)
            {
                if (!string.IsNullOrWhiteSpace(sourceUrl))
                {
                    if (StorageOption == EStorageOption.Official || StorageOption == EStorageOption.Partner)
                    {
                        return sourceUrl;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(newUrl))
                return sourceUrl;

            return newUrl;
        }
    }

    /// <summary>
    /// 每个视频对象对应的操作按钮
    /// </summary>
    public class TaskInfoVideoUrl
    {
        public TaskInfoVideoUrl()
        {
        }

        public TaskInfoVideoUrl(string url)
        {
            Url = url;
        }

        /// <summary>
        /// 视频 URL
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    /// <summary>
    /// 图像URL信息
    /// </summary>
    public class TaskInfoImageUrl
    {
        public TaskInfoImageUrl()
        {
        }

        public TaskInfoImageUrl(string url)
        {
            Url = url;
        }

        /// <summary>
        /// 图像 URL
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; }
    }
}
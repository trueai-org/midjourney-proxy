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
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.Util;
using Serilog;
using System.Net;

namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// 任务类，表示一个任务的基本信息。
    /// </summary>
    [BsonCollection("task")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreExtraElements]
    [Serializable]
    public class TaskInfo : DomainObject
    {
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
        public string Prompt { get; set; }

        /// <summary>
        /// 提示词（英文）。
        /// </summary>
        public string PromptEn { get; set; }

        /// <summary>
        /// 提示词（由 mj 返回的完整提示词）
        /// </summary>
        public string PromptFull { get; set; }

        /// <summary>
        /// 任务描述。
        /// </summary>
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
        public string ImageUrl { get; set; }

        /// <summary>
        /// 缩略图 url
        /// </summary>
        public string ThumbnailUrl { get; set; }

        /// <summary>
        /// 任务进度。
        /// </summary>
        public string Progress { get; set; }

        /// <summary>
        /// 失败原因。
        /// </summary>
        public string FailReason { get; set; }

        /// <summary>
        /// 按钮
        /// </summary>
        public List<CustomComponentModel> Buttons { get; set; } = new List<CustomComponentModel>();

        /// <summary>
        /// 任务的显示信息。
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
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
        public string ReplicateSource { get; set; }

        /// <summary>
        /// 目标图片/目标视频
        /// </summary>
        public string ReplicateTarget { get; set; }

        /// <summary>
        /// 当前绘画客户端指定的速度模式
        /// </summary>
        public GenerationSpeedMode? Mode { get; set; }

        /// <summary>
        /// 账号过滤
        /// </summary>
        public AccountFilter AccountFilter { get; set; }

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
        public void Success(string customCdn, bool downloadToLocal)
        {
            try
            {
                // 如果启用了阿里云 OSS
                if (GlobalConfiguration.Setting?.AliyunOss?.Enable == true && !string.IsNullOrWhiteSpace(ImageUrl))
                {
                    // 本地锁
                    LocalLock.TryLock($"download:{ImageUrl}", TimeSpan.FromSeconds(10), () =>
                    {
                        var opt = GlobalConfiguration.Setting.AliyunOss;
                        customCdn = opt.CustomCdn;

                        // 如果不是以自定义 cdn 加速域名开头
                        if (string.IsNullOrWhiteSpace(customCdn) || !ImageUrl.StartsWith(customCdn))
                        {
                            // 创建保存路径
                            var uri = new Uri(ImageUrl);
                            var localPath = uri.AbsolutePath.TrimStart('/');

                            // 如果路径是 ephemeral-attachments 或 attachments 才处理
                            if (localPath.StartsWith("ephemeral-attachments") || localPath.StartsWith("attachments") || IsReplicate)
                            {
                                var oss = new AliyunOssStorageService();

                                // 换脸放到私有附件中
                                if (IsReplicate)
                                {
                                    localPath = $"pri/{localPath}";
                                }

                                // 替换 url
                                var url = $"{customCdn?.Trim()?.Trim('/')}/{localPath}{uri?.Query}";

                                WebProxy webProxy = null;
                                var proxy = GlobalConfiguration.Setting.Proxy;
                                if (!string.IsNullOrEmpty(proxy?.Host))
                                {
                                    webProxy = new WebProxy(proxy.Host, proxy.Port ?? 80);
                                }
                                var hch = new HttpClientHandler
                                {
                                    UseProxy = webProxy != null,
                                    Proxy = webProxy,
                                };

                                // 下载图片并保存
                                using (HttpClient client = new HttpClient(hch))
                                {
                                    client.Timeout = TimeSpan.FromMinutes(15);

                                    var response = client.GetAsync(ImageUrl).Result;
                                    response.EnsureSuccessStatusCode();
                                    var stream = response.Content.ReadAsStreamAsync().Result;

                                    var mm = MimeKit.MimeTypes.GetMimeType(Path.GetFileName(localPath));
                                    if (string.IsNullOrWhiteSpace(mm))
                                    {
                                        mm = "image/png";
                                    }

                                    oss.SaveAsync(stream, localPath, mm);

                                    // 换脸放到私有附件中
                                    if (IsReplicate)
                                    {
                                        var priUri = oss.GetSignKey(localPath, opt.ExpirationMinutes);
                                        url = $"{customCdn?.Trim()?.Trim('/')}/{priUri.PathAndQuery.TrimStart('/')}";
                                    }
                                }


                                if (Action == TaskAction.SWAP_VIDEO_FACE)
                                {
                                    ImageUrl = url;
                                    ThumbnailUrl = url.ToStyle(opt.VideoSnapshotStyle);
                                }
                                else if (Action == TaskAction.SWAP_FACE)
                                {
                                    // 换脸不格式化
                                    ImageUrl = url;
                                    ThumbnailUrl = url;
                                }
                                else
                                {
                                    ImageUrl = url.ToStyle(opt.ImageStyle);
                                    ThumbnailUrl = url.ToStyle(opt.ThumbnailImageStyle);
                                }
                            }
                        }
                    });
                }
                // https://cdn.discordapp.com/attachments/1265095688782614602/1266300100989161584/03ytbus_LOGO_design_A_warrior_frog_Muscles_like_Popeye_Freehand_06857373-4fd9-403d-a5df-c2f27f9be269.png?ex=66a4a55e&is=66a353de&hm=c597e9d6d128c493df27a4d0ae41204655ab73f7e885878fc1876a8057a7999f&
                // 将图片保存到本地，并替换 url，并且保持原 url和参数
                // 默认保存根目录为 /wwwroot
                // 保存图片
                // 如果处理过了，则不再处理
                else if (downloadToLocal && !string.IsNullOrWhiteSpace(ImageUrl))
                {
                    // 本地锁
                    LocalLock.TryLock($"download:{ImageUrl}", TimeSpan.FromSeconds(10), () =>
                    {
                        // 如果不是以自定义 cdn 加速域名开头
                        if (string.IsNullOrWhiteSpace(customCdn) || !ImageUrl.StartsWith(customCdn))
                        {
                            // 创建保存路径
                            var uri = new Uri(ImageUrl);
                            var localPath = uri.AbsolutePath.TrimStart('/');

                            // 如果路径是 ephemeral-attachments 或 attachments 才处理
                            if (localPath.StartsWith("ephemeral-attachments") || localPath.StartsWith("attachments") || IsReplicate)
                            {
                                // 如果是本地文件，则依然放到 attachments
                                // 换脸放到附件中
                                if (IsReplicate)
                                {
                                    localPath = $"attachments/{localPath}";
                                }

                                var savePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", localPath);
                                var directoryPath = Path.GetDirectoryName(savePath);

                                if (!string.IsNullOrWhiteSpace(directoryPath))
                                {
                                    Directory.CreateDirectory(directoryPath);

                                    WebProxy webProxy = null;
                                    var proxy = GlobalConfiguration.Setting.Proxy;
                                    if (!string.IsNullOrEmpty(proxy?.Host))
                                    {
                                        webProxy = new WebProxy(proxy.Host, proxy.Port ?? 80);
                                    }
                                    var hch = new HttpClientHandler
                                    {
                                        UseProxy = webProxy != null,
                                        Proxy = webProxy
                                    };

                                    // 下载图片并保存
                                    using (HttpClient client = new HttpClient(hch))
                                    {
                                        var response = client.GetAsync(ImageUrl).Result;
                                        response.EnsureSuccessStatusCode();
                                        var imageBytes = response.Content.ReadAsByteArrayAsync().Result;
                                        File.WriteAllBytes(savePath, imageBytes);
                                    }

                                    // 替换 url
                                    ImageUrl = $"{customCdn?.Trim()?.Trim('/')}/{localPath}{uri?.Query}";
                                }
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "保存图片失败 {@0}", ImageUrl);
            }

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
            FinishTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Status = TaskStatus.FAILURE;
            FailReason = reason;
            Progress = "";

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
        }
    }
}
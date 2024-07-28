using LiteDB;
using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.Util;
using Serilog;
using Swashbuckle.AspNetCore.Annotations;

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// 任务类，表示一个任务的基本信息。
    /// </summary>
    [SwaggerSchema("任务")]
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
        /// 账号实例 ID = 账号渠道 ID
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// 消息 ID
        /// 创建消息 ID -> 进度消息 ID -> 完成消息 ID
        /// </summary>
        public List<string> MessageIds { get; set; } = new List<string>();

        /// <summary>
        /// 任务类型。
        /// </summary>
        [SwaggerSchema("任务类型")]
        public TaskAction? Action { get; set; }

        /// <summary>
        /// 任务状态。
        /// </summary>
        [SwaggerSchema("任务状态")]
        public TaskStatus? Status { get; set; }

        /// <summary>
        /// 提示词。
        /// </summary>
        [SwaggerSchema("提示词")]
        public string Prompt { get; set; }

        /// <summary>
        /// 提示词（英文）。
        /// </summary>
        [SwaggerSchema("提示词-英文")]
        public string PromptEn { get; set; }

        /// <summary>
        /// 任务描述。
        /// </summary>
        [SwaggerSchema("任务描述")]
        public string Description { get; set; }

        /// <summary>
        /// 自定义参数。
        /// </summary>
        [SwaggerSchema("自定义参数")]
        public string State { get; set; }

        /// <summary>
        /// 提交时间。
        /// </summary>
        [SwaggerSchema("提交时间")]
        public long? SubmitTime { get; set; }

        /// <summary>
        /// 开始执行时间。
        /// </summary>
        [SwaggerSchema("开始执行时间")]
        public long? StartTime { get; set; }

        /// <summary>
        /// 结束时间。
        /// </summary>
        [SwaggerSchema("结束时间")]
        public long? FinishTime { get; set; }

        /// <summary>
        /// 图片URL。
        /// </summary>
        [SwaggerSchema("图片URL")]
        public string ImageUrl { get; set; }

        /// <summary>
        /// 任务进度。
        /// </summary>
        [SwaggerSchema("任务进度")]
        public string Progress { get; set; }

        /// <summary>
        /// 失败原因。
        /// </summary>
        [SwaggerSchema("失败原因")]
        public string FailReason { get; set; }

        /// <summary>
        /// 按钮
        /// </summary>
        public List<CustomComponentModel> Buttons { get; set; } = new List<CustomComponentModel>();

        /// <summary>
        /// 任务的显示信息。
        /// </summary>
        [BsonIgnore]
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
                // https://cdn.discordapp.com/attachments/1265095688782614602/1266300100989161584/03ytbus_LOGO_design_A_warrior_frog_Muscles_like_Popeye_Freehand_06857373-4fd9-403d-a5df-c2f27f9be269.png?ex=66a4a55e&is=66a353de&hm=c597e9d6d128c493df27a4d0ae41204655ab73f7e885878fc1876a8057a7999f&
                // 将图片保存到本地，并替换 url，并且保持原 url和参数
                // 默认保存根目录为 /wwwroot
                // 保存图片
                // 如果处理过了，则不再处理
                if (downloadToLocal && !string.IsNullOrWhiteSpace(ImageUrl))
                {
                    // 本地锁
                    LocalLock.TryLock(ImageUrl, TimeSpan.FromSeconds(10), () =>
                    {
                        // 如果不是以自定义 cdn 加速域名开头
                        if (string.IsNullOrWhiteSpace(customCdn) || !ImageUrl.StartsWith(customCdn))
                        {
                            // 创建保存路径
                            var uri = new Uri(ImageUrl);
                            var localPath = uri.AbsolutePath.TrimStart('/');

                            // 如果路径是 ephemeral-attachments 或 attachments 才处理
                            if (localPath.StartsWith("ephemeral-attachments") || localPath.StartsWith("attachments"))
                            {
                                var savePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", localPath);
                                var directoryPath = Path.GetDirectoryName(savePath);

                                if (!string.IsNullOrWhiteSpace(directoryPath))
                                {
                                    Directory.CreateDirectory(directoryPath);

                                    // 下载图片并保存
                                    using (HttpClient client = new HttpClient())
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
        }
    }
}
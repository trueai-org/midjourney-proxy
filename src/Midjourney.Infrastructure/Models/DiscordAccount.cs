using LiteDB;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// Discord账号类。
    /// </summary>
    public class DiscordAccount : DomainObject
    {
        public DiscordAccount()
        {
        }

        /// <summary>
        /// 频道ID  = ID
        /// </summary>
        [Display(Name = "频道ID")]
        public string ChannelId { get; set; }

        /// <summary>
        /// 服务器ID
        /// </summary>
        [Display(Name = "服务器ID")]
        public string GuildId { get; set; }

        /// <summary>
        /// Mj 私信频道ID, 用来接收 seed 值
        /// </summary>
        [Display(Name = "私信频道ID")]
        public string PrivateChannelId { get; set; }

        /// <summary>
        /// Niji 私信频道ID, 用来接收 seed 值
        /// </summary>
        public string NijiBotChannelId { get; set; }

        /// <summary>
        /// 用户Token。
        /// </summary>
        [Display(Name = "用户Token")]
        public string UserToken { get; set; }

        /// <summary>
        /// 机器人 Token
        /// </summary>
        [Display(Name = "机器人Token")]
        public string BotToken { get; set; }

        /// <summary>
        /// 用户UserAgent。
        /// </summary>
        [Display(Name = "用户UserAgent")]
        public string UserAgent { get; set; } = Constants.DEFAULT_DISCORD_USER_AGENT;

        /// <summary>
        /// 是否可用。
        /// </summary>
        [Display(Name = "是否可用")]
        public bool Enable { get; set; } = true;

        /// <summary>
        /// 开启 Midjourney 绘图
        /// </summary>
        public bool EnableMj { get; set; } = true;

        /// <summary>
        /// 开启 Niji 绘图
        /// </summary>
        public bool EnableNiji { get; set; } = true;

        /// <summary>
        /// 是否锁定（暂时锁定，可能触发了人机验证）
        /// </summary>
        public bool Lock { get; set; }

        /// <summary>
        /// 禁用原因
        /// </summary>
        public string DisabledReason { get; set; }

        /// <summary>
        /// 真人验证 hash url 创建时间
        /// </summary>
        public DateTime? CfHashCreated { get; set; }

        /// <summary>
        /// 真人验证 hash Url
        /// </summary>
        public string CfHashUrl { get; set; }

        /// <summary>
        /// 真人验证 Url
        /// </summary>
        public string CfUrl { get; set; }

        /// <summary>
        /// 并发数。
        /// </summary>
        [Display(Name = "并发数")]
        public int CoreSize { get; set; } = 3;

        /// <summary>
        /// 任务执行前间隔时间（秒，默认：1.2s）。
        /// </summary>
        public decimal Interval { get; set; } = 1.2m;

        /// <summary>
        /// 任务执行后最小间隔时间（秒，默认：1.2s）
        /// </summary>
        public decimal AfterIntervalMin { get; set; } = 1.2m;

        /// <summary>
        /// 任务执行后最大间隔时间（秒，默认：1.2s）
        /// </summary>
        public decimal AfterIntervalMax { get; set; } = 1.2m;

        /// <summary>
        /// 等待队列长度。
        /// </summary>
        [Display(Name = "等待队列长度")]
        public int QueueSize { get; set; } = 10;

        /// <summary>
        /// 等待最大队列长度
        /// </summary>
        [Display(Name = "等待最大队列长度")]
        public int MaxQueueSize { get; set; } = 100;

        /// <summary>
        /// 任务超时时间（分钟）。
        /// </summary>
        [Display(Name = "任务超时时间（分钟）")]
        public int TimeoutMinutes { get; set; } = 5;

        /// <summary>
        /// 备注
        /// </summary>
        public string Remark { get; set; }

        /// <summary>
        /// 赞助商（富文本）
        /// </summary>
        public string Sponsor { get; set; }

        /// <summary>
        /// 添加时间
        /// </summary>
        public DateTime DateCreated { get; set; } = DateTime.Now;

        /// <summary>
        /// 权重
        /// </summary>
        public int Weight { get; set; }

        /// <summary>
        /// 工作时间（非工作时间段，不接收任何任务）
        /// </summary>
        public string WorkTime { get; set; }

        /// <summary>
        /// 摸鱼时间段（只接收变化任务，不接收新的任务）
        /// </summary>
        public string FishingTime { get; set; }

        /// <summary>
        /// 表示是否接收新的任务
        /// 1、处于工作时间段内
        /// 2、处于非摸鱼时间段内
        /// 3、没有超出最大任务限制
        /// </summary>
        [BsonIgnore]
        public bool IsAcceptNewTask
        {
            get
            {
                if (string.IsNullOrWhiteSpace(WorkTime) && string.IsNullOrWhiteSpace(FishingTime))
                {
                    return true;
                }

                if (DateTime.Now.IsInWorkTime(WorkTime) && !DateTime.Now.IsInFishTime(FishingTime))
                {
                    if (DayDrawLimit <= -1 || DayDrawCount < DayDrawLimit)
                    {
                        return true;
                    }
                }

                // 表示不接收新的任务
                return false;
            }
        }

        /// <summary>
        /// 排序
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// Remix 自动提交
        /// </summary>
        public bool RemixAutoSubmit { get; set; }

        /// <summary>
        /// 指定生成速度模式 --fast, --relax, or --turbo parameter at the end.
        /// </summary>
        [Display(Name = "生成速度模式 fast | relax | turbo")]
        public GenerationSpeedMode? Mode { get; set; }

        /// <summary>
        /// 允许速度模式（如果出现不允许的速度模式，将会自动清除关键词）
        /// </summary>
        public List<GenerationSpeedMode> AllowModes { get; set; } = new List<GenerationSpeedMode>();

        /// <summary>
        /// MJ 组件列表。
        /// </summary>
        public List<Component> Components { get; set; } = new List<Component>();

        /// <summary>
        /// MJ 设置消息 ID
        /// </summary>
        public string SettingsMessageId { get; set; }

        /// <summary>
        /// NIJI 组件列表。
        /// </summary>
        public List<Component> NijiComponents { get; set; } = new List<Component>();

        /// <summary>
        /// NIJI 设置消息 ID
        /// </summary>
        public string NijiSettingsMessageId { get; set; }

        /// <summary>
        /// 开启 Blend 功能
        /// </summary>
        public bool IsBlend { get; set; } = true;

        /// <summary>
        /// 开启 Describe 功能
        /// </summary>
        public bool IsDescribe { get; set; } = true;

        /// <summary>
        /// 日绘图最大次数限制，默认 -1 不限制
        /// </summary>
        public int DayDrawLimit { get; set; } = -1;

        /// <summary>
        /// 当日已绘图次数（每 5 分钟自动刷新）
        /// </summary>
        public int DayDrawCount { get; set; } = 0;

        /// <summary>
        /// 开启垂直领域
        /// </summary>
        public bool IsVerticalDomain { get; set; }

        /// <summary>
        /// 垂直领域 IDS
        /// </summary>
        public List<string> VerticalDomainIds { get; set; } = new List<string>();

        /// <summary>
        /// 子频道列表
        /// </summary>
        public List<string> SubChannels { get; set; } = new List<string>();

        /// <summary>
        /// 执行中的任务数
        /// </summary>
        [BsonIgnore]
        public int RunningCount { get; set; }

        /// <summary>
        /// 队列中的任务数
        /// </summary>
        [BsonIgnore]
        public int QueueCount { get; set; }

        /// <summary>
        /// wss 是否运行中
        /// </summary>
        [BsonIgnore]
        public bool Running { get; set; }

        /// <summary>
        /// Mj 按钮
        /// </summary>
        [BsonIgnore]
        public List<CustomComponentModel> Buttons => Components.Where(c => c.Id != 1).SelectMany(x => x.Components)
            .Select(c =>
            {
                return new CustomComponentModel
                {
                    CustomId = c.CustomId?.ToString() ?? string.Empty,
                    Emoji = c.Emoji?.Name ?? string.Empty,
                    Label = c.Label ?? string.Empty,
                    Style = c.Style ?? 0,
                    Type = (int?)c.Type ?? 0,
                };
            }).Where(c => c != null && !string.IsNullOrWhiteSpace(c.CustomId)).ToList();

        /// <summary>
        /// MJ 是否开启 remix mode
        /// </summary>
        [BsonIgnore]
        public bool MjRemixOn => Buttons.Any(x => x.Label == "Remix mode" && x.Style == 3);

        /// <summary>
        /// Niji 按钮
        /// </summary>
        [BsonIgnore]
        public List<CustomComponentModel> NijiButtons => NijiComponents.SelectMany(x => x.Components)
            .Select(c =>
            {
                return new CustomComponentModel
                {
                    CustomId = c.CustomId?.ToString() ?? string.Empty,
                    Emoji = c.Emoji?.Name ?? string.Empty,
                    Label = c.Label ?? string.Empty,
                    Style = c.Style ?? 0,
                    Type = (int?)c.Type ?? 0,
                };
            }).Where(c => c != null && !string.IsNullOrWhiteSpace(c.CustomId)).ToList();

        /// <summary>
        /// Niji 是否开启 remix mode
        /// </summary>
        [BsonIgnore]
        public bool NijiRemixOn => NijiButtons.Any(x => x.Label == "Remix mode" && x.Style == 3);

        /// <summary>
        /// Mj 下拉框
        /// </summary>
        [BsonIgnore]
        public List<CustomComponentModel> VersionSelector => Components.Where(c => c.Id == 1)
            .FirstOrDefault()?.Components?.FirstOrDefault()?.Options
            .Select(c =>
            {
                return new CustomComponentModel
                {
                    CustomId = c.Value,
                    Emoji = c.Emoji?.Name ?? string.Empty,
                    Label = c.Label ?? string.Empty
                };
            }).Where(c => c != null && !string.IsNullOrWhiteSpace(c.CustomId)).ToList();

        /// <summary>
        /// 默认下拉框值
        /// </summary>
        [BsonIgnore]
        public string Version => Components.Where(c => c.Id == 1)
            .FirstOrDefault()?.Components?.FirstOrDefault()?.Options
            .Where(c => c.Default == true).FirstOrDefault()?.Value;

        /// <summary>
        /// 显示信息。
        /// </summary>
        [BsonIgnore]
        public Dictionary<string, object> Displays
        {
            get
            {
                var dic = new Dictionary<string, object>();

                // Standard (Active monthly, renews next on <t:1722649226>)"
                var plan = Properties.ContainsKey("Subscription") ? Properties["Subscription"].ToString() : "";

                // 正则表达式来捕获 subscribePlan, billedWay 和 timestamp
                var pattern = @"([A-Za-z\s]+) \(([A-Za-z\s]+), renews next on <t:(\d+)\>\)";
                var match = Regex.Match(plan, pattern);
                if (match.Success)
                {
                    string subscribePlan = match.Groups[1].Value;
                    string billedWay = match.Groups[2].Value;
                    string timestamp = match.Groups[3].Value;

                    dic["subscribePlan"] = subscribePlan.Trim();
                    dic["billedWay"] = billedWay.Trim();
                    dic["renewDate"] = DateTimeOffset.FromUnixTimeSeconds(long.Parse(timestamp)).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                }

                dic["mode"] = Properties.ContainsKey("Job Mode") ? Properties["Job Mode"] : "";
                dic["nijiMode"] = Properties.ContainsKey("Niji Job Mode") ? Properties["Niji Job Mode"] : "";

                return dic;
            }
        }

        /// <summary>
        /// 快速时间剩余
        /// </summary>
        public object FastTimeRemaining => Properties.ContainsKey("Fast Time Remaining") ? Properties["Fast Time Remaining"] : "";

        /// <summary>
        /// 慢速用量
        /// </summary>
        public object RelaxedUsage => Properties.ContainsKey("Relaxed Usage") ? Properties["Relaxed Usage"] : "";

        /// <summary>
        /// 加速用量
        /// </summary>
        public object TurboUsage => Properties.ContainsKey("Turbo Usage") ? Properties["Turbo Usage"] : "";

        /// <summary>
        /// 快速用量
        /// </summary>
        public object FastUsage => Properties.ContainsKey("Fast Usage") ? Properties["Fast Usage"] : "";

        /// <summary>
        /// 总用量
        /// </summary>
        public object LifetimeUsage => Properties.ContainsKey("Lifetime Usage") ? Properties["Lifetime Usage"] : "";

        /// <summary>
        /// 获取显示名称。
        /// </summary>
        /// <returns>频道ID。</returns>
        public string GetDisplay()
        {
            return ChannelId;
        }
    }
}
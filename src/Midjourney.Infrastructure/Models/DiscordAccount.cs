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

using FreeSql.DataAnnotations;
using LiteDB;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// Discord账号类。
    /// </summary>
    [BsonCollection("account")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreExtraElements]
    [Serializable]
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
        [Column(StringLength = -1)]
        public string UserToken { get; set; }

        /// <summary>
        /// 机器人 Token
        /// </summary>
        [Display(Name = "机器人Token")]
        [Column(StringLength = -1)]
        public string BotToken { get; set; }

        /// <summary>
        /// 用户UserAgent。
        /// </summary>
        [Display(Name = "用户UserAgent")]
        [Column(StringLength = -1)]
        public string UserAgent { get; set; } = Constants.DEFAULT_DISCORD_USER_AGENT;

        /// <summary>
        /// 是否可用。
        /// </summary>
        public bool? Enable { get; set; }

        /// <summary>
        /// 开启 Midjourney 绘图
        /// </summary>
        public bool? EnableMj { get; set; }

        /// <summary>
        /// 开启 Niji 绘图
        /// </summary>
        public bool? EnableNiji { get; set; }

        /// <summary>
        /// 启用快速模式用完自动切换到慢速模式
        /// </summary>
        public bool? EnableFastToRelax { get; set; }

        /// <summary>
        /// 启用时，当有快速时长时，自动切换到快速模式
        /// </summary>
        public bool? EnableRelaxToFast { get; set; }

        /// <summary>
        /// 表示快速模式是否已经用完了
        /// </summary>
        public bool FastExhausted { get; set; }

        /// <summary>
        /// 是否锁定（暂时锁定，可能触发了人机验证）
        /// </summary>
        public bool Lock { get; set; }

        /// <summary>
        /// 禁用原因
        /// </summary>
        [Column(StringLength = -1)]
        public string DisabledReason { get; set; }

        /// <summary>
        /// 当前频道的永久邀请链接
        /// </summary>
        [Column(StringLength = 2000)]
        public string PermanentInvitationLink { get; set; }

        /// <summary>
        /// 真人验证 hash url 创建时间
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime? CfHashCreated { get; set; }

        /// <summary>
        /// 真人验证 hash Url
        /// </summary>
        [Column(StringLength = -1)]
        public string CfHashUrl { get; set; }

        /// <summary>
        /// 真人验证 Url
        /// </summary>
        [Column(StringLength = -1)]
        public string CfUrl { get; set; }

        /// <summary>
        /// 是否赞助者
        /// </summary>
        public bool IsSponsor { get; set; }

        /// <summary>
        /// 赞助者用户 ID
        /// </summary>
        public string SponsorUserId { get; set; }

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
        [Column(StringLength = -1)]
        public string Remark { get; set; }

        /// <summary>
        /// 赞助商（富文本）
        /// </summary>
        [Column(StringLength = -1)]
        public string Sponsor { get; set; }

        /// <summary>
        /// 添加时间
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime DateCreated { get; set; } = DateTime.Now;

        /// <summary>
        /// mj info 更新时间
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime? InfoUpdated { get; set; }

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
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
        public bool IsAcceptNewTask
        {
            get
            {
                // 如果工作时间段和摸鱼时间段都为空
                if (string.IsNullOrWhiteSpace(WorkTime) && string.IsNullOrWhiteSpace(FishingTime))
                {
                    return IsContinueDrawing;
                }

                // 如果工作时间段内，且不是摸鱼时间段
                if (DateTime.Now.IsInWorkTime(WorkTime) && !DateTime.Now.IsInFishTime(FishingTime))
                {
                    return IsContinueDrawing;
                }

                // 表示不接收新的任务
                return false;
            }
        }

        /// <summary>
        /// 是否达到最大任务限制 - 是否允许继续绘图
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
        public bool IsContinueDrawing
        {
            get
            {
                if (DayDrawLimit <= -1 || DayDrawCount < DayDrawLimit)
                {
                    return true;
                }
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
        [JsonMap]
        public List<GenerationSpeedMode> AllowModes { get; set; } = new List<GenerationSpeedMode>();

        /// <summary>
        /// 自动设置慢速
        /// 启用后，当快速用完时，如果允许生成速度模式是 FAST 或 TURBO，则自动清空原有模式，并设置为 RELAX 模式。
        /// </summary>
        public bool? EnableAutoSetRelax { get; set; }

        /// <summary>
        /// MJ 组件列表。
        /// </summary>
        [JsonMap]
        public List<Component> Components { get; set; } = new List<Component>();

        /// <summary>
        /// MJ 设置消息 ID
        /// </summary>
        public string SettingsMessageId { get; set; }

        /// <summary>
        /// NIJI 组件列表。
        /// </summary>
        [JsonMap]
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
        /// 开启 Shoren 功能
        /// </summary>
        public bool IsShorten { get; set; } = true;

        /// <summary>
        /// 账号（用于自动登录）
        /// </summary>
        public string LoginAccount { get; set; }

        /// <summary>
        /// 密码（用于自动登录）
        /// </summary>
        public string LoginPassword { get; set; }

        /// <summary>
        /// 2FA 密钥（用于自动登录）
        /// </summary>
        public string Login2fa { get; set; }

        /// <summary>
        /// 是否自动登录中（用于自动登录）
        /// </summary>
        public bool IsAutoLogining { get; set; }

        /// <summary>
        /// 尝试登录时间（用于自动登录）
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime? LoginStart { get; set; }

        /// <summary>
        /// 登录结束时间（用于自动登录）
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime? LoginEnd { get; set; }

        /// <summary>
        /// 登录成功/失败消息（用于自动登录）
        /// </summary>
        [Column(StringLength = 2000)]
        public string LoginMessage { get; set; }

        /// <summary>
        /// 日绘图最大次数限制，默认 -1 不限制
        /// </summary>
        public int DayDrawLimit { get; set; } = -1;

        /// <summary>
        /// 当日已绘图次数（每 2 分钟自动刷新）
        /// </summary>
        public int DayDrawCount { get; set; } = 0;

        /// <summary>
        /// 开启垂直领域
        /// </summary>
        public bool IsVerticalDomain { get; set; }

        /// <summary>
        /// 垂直领域 IDS
        /// </summary>
        [JsonMap]
        public List<string> VerticalDomainIds { get; set; } = new List<string>();

        /// <summary>
        /// 子频道列表
        /// </summary>
        [JsonMap]
        public List<string> SubChannels { get; set; } = new List<string>();

        /// <summary>
        /// 子频道 ids 通过 SubChannels 计算得出
        /// key: 频道 id, value: 服务器 id
        /// </summary>
        [JsonMap]
        public Dictionary<string, string> SubChannelValues { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// 执行中的任务数 - 用于前台显示
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
        public int RunningCount { get; set; }

        /// <summary>
        /// 队列中的任务数 - 用于前台显示
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
        public int QueueCount { get; set; }

        /// <summary>
        /// 服务运行中 - 用于前台显示
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
        public bool Running { get; set; }

        /// <summary>
        /// Mj 按钮
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
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
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
        public bool MjRemixOn => Buttons.Any(x => x.Label == "Remix mode" && x.Style == 3);

        /// <summary>
        /// MJ 是否开启 fast mode
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
        public bool MjFastModeOn =>
            Buttons.Any(x => (x.Label == "Fast mode" || x.Label == "Turbo mode") && x.Style == 3);

        /// <summary>
        /// Niji 按钮
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
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
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
        public bool NijiRemixOn => NijiButtons.Any(x => x.Label == "Remix mode" && x.Style == 3);

        /// <summary>
        /// Niji 是否开启 fast mode
        /// </summary>
        public bool NijiFastModeOn =>
            NijiButtons.Any(x => (x.Label == "Fast mode" || x.Label == "Turbo mode") && x.Style == 3);

        /// <summary>
        /// Mj 下拉框
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
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
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
        public string Version => Components.Where(c => c.Id == 1)
            .FirstOrDefault()?.Components?.FirstOrDefault()?.Options
            .Where(c => c.Default == true).FirstOrDefault()?.Value;

        /// <summary>
        /// 显示信息。
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Column(IsIgnore = true)]
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

        /// <summary>
        /// 创建 Discord 账号。
        /// </summary>
        /// <param name="configAccount"></param>
        /// <returns></returns>
        public static DiscordAccount Create(DiscordAccountConfig configAccount)
        {
            if (configAccount.Interval < 1.2m)
            {
                configAccount.Interval = 1.2m;
            }
            if (configAccount.CoreSize > 12)
            {
                configAccount.CoreSize = 12;
            }

            return new DiscordAccount
            {
                Id = Guid.NewGuid().ToString(),
                ChannelId = configAccount.ChannelId,

                UserAgent = string.IsNullOrEmpty(configAccount.UserAgent) ? Constants.DEFAULT_DISCORD_USER_AGENT : configAccount.UserAgent,
                GuildId = configAccount.GuildId,
                UserToken = configAccount.UserToken,
                Enable = configAccount.Enable,
                CoreSize = configAccount.CoreSize,
                QueueSize = configAccount.QueueSize,
                BotToken = configAccount.BotToken,
                TimeoutMinutes = configAccount.TimeoutMinutes,
                PrivateChannelId = configAccount.PrivateChannelId,
                NijiBotChannelId = configAccount.NijiBotChannelId,
                MaxQueueSize = configAccount.MaxQueueSize,
                Mode = configAccount.Mode,
                AllowModes = configAccount.AllowModes,
                Weight = configAccount.Weight,
                Remark = configAccount.Remark,
                RemixAutoSubmit = configAccount.RemixAutoSubmit,
                Sponsor = configAccount.Sponsor,
                IsSponsor = configAccount.IsSponsor,
                Sort = configAccount.Sort,
                Interval = configAccount.Interval,

                AfterIntervalMax = configAccount.AfterIntervalMax,
                AfterIntervalMin = configAccount.AfterIntervalMin,
                WorkTime = configAccount.WorkTime,
                FishingTime = configAccount.FishingTime,
                PermanentInvitationLink = configAccount.PermanentInvitationLink,

                SubChannels = configAccount.SubChannels,
                IsBlend = configAccount.IsBlend,
                VerticalDomainIds = configAccount.VerticalDomainIds,
                IsVerticalDomain = configAccount.IsVerticalDomain,
                IsDescribe = configAccount.IsDescribe,
                IsShorten = configAccount.IsShorten,
                DayDrawLimit = configAccount.DayDrawLimit,
                EnableMj = configAccount.EnableMj,
                EnableNiji = configAccount.EnableNiji,
                EnableFastToRelax = configAccount.EnableFastToRelax,
                EnableRelaxToFast = configAccount.EnableRelaxToFast,
                EnableAutoSetRelax = configAccount.EnableAutoSetRelax,

                LoginAccount = configAccount.LoginAccount,
                LoginPassword = configAccount.LoginPassword,
                Login2fa = configAccount.Login2fa,
            };
        }

        /// <summary>
        /// 初始化子频道
        /// </summary>
        public void InitSubChannels()
        {
            // 启动前校验
            if (SubChannels.Count > 0)
            {
                // https://discord.com/channels/1256526716130693201/1256526716130693204
                // https://discord.com/channels/{guid}/{id}
                // {guid} {id} 都是纯数字

                var dic = new Dictionary<string, string>();
                foreach (var item in SubChannels)
                {
                    if (string.IsNullOrWhiteSpace(item) || !item.Contains("https://discord.com/channels"))
                    {
                        continue;
                    }

                    // {id} 作为 key, {guid} 作为 value
                    var fir = item.Split(',').Where(c => c.Contains("https://discord.com/channels")).FirstOrDefault();
                    if (fir == null)
                    {
                        continue;
                    }

                    var arr = fir.Split('/').Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
                    if (arr.Length < 5)
                    {
                        continue;
                    }

                    var guid = arr[3];
                    var id = arr[4];

                    dic[id] = guid;
                }

                SubChannelValues = dic;
            }
            else
            {
                SubChannels.Clear();
                SubChannelValues.Clear();
            }
        }

        /// <summary>
        /// 赞助账号校验
        /// </summary>
        public void SponsorValidate()
        {
            if (DayDrawLimit > 0 && DayDrawLimit < 10)
            {
                DayDrawLimit = 10;
            }

            if (CoreSize <= 0)
            {
                CoreSize = 1;
            }

            if (QueueSize <= 0)
            {
                QueueSize = 1;
            }

            if (MaxQueueSize <= 0)
            {
                MaxQueueSize = 1;
            }

            if (TimeoutMinutes < 5)
            {
                TimeoutMinutes = 5;
            }

            if (TimeoutMinutes > 30)
            {
                TimeoutMinutes = 30;
            }

            if (Interval > 180)
            {
                Interval = 180;
            }

            if (AfterIntervalMin > 180)
            {
                AfterIntervalMin = 180;
            }

            if (AfterIntervalMax > 180)
            {
                AfterIntervalMax = 180;
            }

            if (EnableMj != true)
            {
                EnableMj = true;
            }

            if (Sponsor?.Length > 1000)
            {
                Sponsor = Sponsor.Substring(0, 1000);
            }
        }
    }
}
﻿using LiteDB;
using Midjourney.Infrastructure.Data;

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// 代理配置属性类.
    /// </summary>
    public class ProxyProperties : DomainObject
    {
        /// <summary>
        /// Discord账号选择规则.
        /// </summary>
        public AccountChooseRule AccountChooseRule { get; set; } = AccountChooseRule.BestWaitIdle;

        /// <summary>
        /// Discord单账号配置.
        /// </summary>
        [BsonIgnore]
        public DiscordAccountConfig Discord { get; set; } = new DiscordAccountConfig();

        /// <summary>
        /// Discord账号池配置.
        /// </summary>
        [BsonIgnore]
        public List<DiscordAccountConfig> Accounts { get; set; } = new List<DiscordAccountConfig>();

        /// <summary>
        /// 代理配置.
        /// </summary>
        public ProxyConfig Proxy { get; set; } = new ProxyConfig();

        /// <summary>
        /// 反代配置.
        /// </summary>
        public NgDiscordConfig NgDiscord { get; set; } = new NgDiscordConfig();

        /// <summary>
        /// 百度翻译配置.
        /// </summary>
        public BaiduTranslateConfig BaiduTranslate { get; set; } = new BaiduTranslateConfig();

        /// <summary>
        /// OpenAI配置.
        /// </summary>
        [BsonIgnore]
        public OpenaiConfig Openai { get; set; } = new OpenaiConfig();

        /// <summary>
        /// 中文prompt翻译方式.
        /// </summary>
        public TranslateWay TranslateWay { get; set; } = TranslateWay.NULL;

        /// <summary>
        /// 任务状态变更回调地址.
        /// </summary>
        public string NotifyHook { get; set; }

        /// <summary>
        /// 通知回调线程池大小.
        /// </summary>
        public int NotifyPoolSize { get; set; } = 10;

        /// <summary>
        /// 邮件发送配置
        /// </summary>
        public SmtpConfig Smtp { get; set; }

        /// <summary>
        /// CF 验证服务器地址
        /// </summary>
        public string CaptchaServer { get; set; }

        /// <summary>
        /// CF 验证通知地址（验证通过后的回调通知，默认就是你的当前域名）
        /// </summary>
        public string CaptchaNotifyHook { get; set; }
    }

    /// <summary>
    /// 邮件发送配置项
    /// </summary>
    public class SmtpConfig
    {
        /// <summary>
        /// SMTP服务器信息
        /// smtp.mxhichina.com
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// SMTP端口，一般为587或465，具体依据你的SMTP服务器而定
        /// </summary>
        public int Port { get; set; } = 465;

        /// <summary>
        /// 根据你的SMTP服务器要求设置
        /// </summary>
        public bool EnableSsl { get; set; } = true;

        /// <summary>
        /// 发件人昵称
        /// system
        /// </summary>
        public string FromName { get; set; }

        /// <summary>
        /// 发件人邮箱地址
        /// system@trueai.org
        /// </summary>
        public string FromEmail { get; set; }

        /// <summary>
        /// 你的邮箱密码或应用专用密码
        /// </summary>
        public string FromPassword { get; set; }

        /// <summary>
        /// 收件人
        /// </summary>
        public string To { get; set; }
    }

    /// <summary>
    /// Discord账号配置.
    /// </summary>
    public class DiscordAccountConfig
    {
        /// <summary>
        /// 服务器ID
        /// </summary>
        public string GuildId { get; set; }

        /// <summary>
        /// 频道ID.
        /// </summary>
        public string ChannelId { get; set; }

        /// <summary>
        /// MJ 私信频道ID, 用来接收 seed 值
        /// </summary>
        public string PrivateChannelId { get; set; }

        /// <summary>
        /// Niji 私信频道ID, 用来接收 seed 值
        /// </summary>
        public string NijiBotChannelId { get; set; }

        /// <summary>
        /// 用户 Token.
        /// </summary>
        public string UserToken { get; set; }

        /// <summary>
        /// 机器人 Token
        /// 
        /// 1. 创建应用
        /// https://discord.com/developers/applications
        /// 
        /// 2. 设置应用权限（确保拥有读取内容权限）
        /// [Bot] 设置 -> 全部开启
        /// 
        /// 3. 添加应用到频道服务器
        /// https://discord.com/oauth2/authorize?client_id=xxx&permissions=8&scope=bot
        /// 
        /// 4. 复制或重置 Bot Token
        /// </summary>
        public string BotToken { get; set; }

        /// <summary>
        /// 用户UserAgent.
        /// </summary>
        public string UserAgent { get; set; } = Constants.DEFAULT_DISCORD_USER_AGENT;

        /// <summary>
        /// 是否可用.
        /// </summary>
        public bool Enable { get; set; }

        /// <summary>
        /// 开启 Midjourney 绘图
        /// </summary>
        public bool EnableMj { get; set; } = true;

        /// <summary>
        /// 开启 Niji 绘图
        /// </summary>
        public bool EnableNiji { get; set; } = true;

        /// <summary>
        /// 并发数.
        /// </summary>
        public int CoreSize { get; set; } = 3;

        /// <summary>
        /// 等待队列长度.
        /// </summary>
        public int QueueSize { get; set; } = 10;

        /// <summary>
        /// 等待最大队列长度
        /// </summary>
        public int MaxQueueSize { get; set; } = 100;

        /// <summary>
        /// 任务超时时间(分钟).
        /// </summary>
        public int TimeoutMinutes { get; set; } = 5;

        /// <summary>
        /// 指定生成速度模式 --fast, --relax, or --turbo parameter at the end.
        /// </summary>
        public GenerationSpeedMode? Mode { get; set; }

        /// <summary>
        /// 允许速度模式（如果出现不允许的速度模式，将会自动清除关键词）
        /// </summary>
        public List<GenerationSpeedMode> AllowModes { get; set; } = new List<GenerationSpeedMode>();

        /// <summary>
        /// 开启 Blend 功能
        /// </summary>
        public bool IsBlend { get; set; } = true;

        /// <summary>
        /// 开启 Describe 功能
        /// </summary>
        public bool IsDescribe { get; set; } = true;

        /// <summary>
        /// 日绘图最大次数限制，默认 0 不限制
        /// </summary>
        public int DayDrawLimit { get; set; } = -1;

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
        /// 备注
        /// </summary>
        public string Remark { get; set; }

        /// <summary>
        /// 赞助商（富文本）
        /// </summary>
        public string Sponsor { get; set; }

        /// <summary>
        /// 排序
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 任务执行间隔时间（秒，默认：1.2s）。
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
        /// 工作时间
        /// </summary>
        public string WorkTime { get; set; }

        /// <summary>
        /// 摸鱼时间段（只接收变化任务，不接收新的任务）
        /// </summary>
        public string FishingTime { get; set; }

        /// <summary>
        /// 当前频道的永久邀请链接
        /// </summary>
        public string PermanentInvitationLink { get; set; }

        /// <summary>
        /// 权重
        /// </summary>
        public int Weight { get; set; }

        /// <summary>
        /// Remix 自动提交
        /// </summary>
        public bool RemixAutoSubmit { get; set; }
    }

    /// <summary>
    /// 百度翻译配置.
    /// </summary>
    public class BaiduTranslateConfig
    {
        /// <summary>
        /// 百度翻译的APP_ID.
        /// </summary>
        public string Appid { get; set; }

        /// <summary>
        /// 百度翻译的密钥.
        /// </summary>
        public string AppSecret { get; set; }
    }

    /// <summary>
    /// OpenAI配置.
    /// </summary>
    public class OpenaiConfig
    {
        /// <summary>
        /// 自定义gpt的api-url.
        /// </summary>
        public string GptApiUrl { get; set; }

        /// <summary>
        /// gpt的api-key.
        /// </summary>
        public string GptApiKey { get; set; }

        /// <summary>
        /// 超时时间.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 使用的模型.
        /// </summary>
        public string Model { get; set; } = "gpt-4o-mini";

        /// <summary>
        /// 返回结果的最大分词数.
        /// </summary>
        public int MaxTokens { get; set; } = 2048;

        /// <summary>
        /// 相似度，取值 0-2.
        /// </summary>
        public double Temperature { get; set; } = 0;
    }

    /// <summary>
    /// 代理配置.
    /// </summary>
    public class ProxyConfig
    {
        /// <summary>
        /// 代理host.
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// 代理端口.
        /// </summary>
        public int? Port { get; set; }
    }

    /// <summary>
    /// 反代配置.
    /// </summary>
    public class NgDiscordConfig
    {
        /// <summary>
        /// https://discord.com 反代.
        /// </summary>
        public string Server { get; set; }

        /// <summary>
        /// https://cdn.discordapp.com 反代.
        /// </summary>
        public string Cdn { get; set; }

        /// <summary>
        /// wss://gateway.discord.gg 反代.
        /// </summary>
        public string Wss { get; set; }

        /// <summary>
        /// wss://gateway-us-east1-b.discord.gg 反代.
        /// </summary>
        public string ResumeWss { get; set; }

        /// <summary>
        /// https://discord-attachments-uploads-prd.storage.googleapis.com 反代.
        /// </summary>
        public string UploadServer { get; set; }

        /// <summary>
        /// 自动下载图片并保存到本地
        /// </summary>
        public bool? SaveToLocal { get; set; }

        /// <summary>
        /// 自定义 CDN 加速地址
        /// </summary>
        public string CustomCdn { get; set; }
    }
}
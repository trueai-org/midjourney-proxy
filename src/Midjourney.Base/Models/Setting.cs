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

using LiteDB;
using Midjourney.Base.Data;
using Midjourney.Base.Options;

namespace Midjourney.Base.Models
{
    /// <summary>
    /// 系统配置
    /// </summary>
    public class Setting : DomainObject
    {
        /// <summary>
        /// Redis 配置项
        /// </summary>
        public string RedisConnectionString { get; set; }

        /// <summary>
        /// 启用悠船内网下载图片
        /// 当程序部署在阿里云上海地域时，如果需要加速下载图片速度，可以开启内网下载功能。
        /// </summary>
        public bool EnableYouChuanInternalDownload { get; set; }

        /// <summary>
        /// 启用悠船失败自动重试
        /// </summary>
        public bool EnableYouChuanRetry { get; set; }

        /// <summary>
        /// 启用悠船重启继续继续任务
        /// </summary>
        public bool EnableYouChuanRestartContinue { get; set; }

        /// <summary>
        /// 私人定制开启的功能列表 - 在保存授权码/激活时生成
        /// </summary>
        public List<string> PrivateFeatures { get; set; } = [];

        /// <summary>
        /// 始终允许 U 放大，不判断账号是否可用 - 私人定制业务
        /// 启用悠船 U 放大时，不验证账号的可用性
        /// </summary>
        public bool PrivateEnableYouChuanAllowU { get; set; } = false;

        /// <summary>
        /// 启用悠船合成图片使用自定义路径 - 私人定制业务
        /// 服务器id/频道id/提示词前面几个单词_uudi.png
        /// </summary>
        public bool PrivateEnableYouChuanMergedCustomize { get; set; } = false;

        /// <summary>
        /// 悠船绘图时将 prompt 提示词中的链接转为悠船官网链接
        /// </summary>
        public bool EnableYouChuanPromptLink { get; set; }

        /// <summary>
        /// 开启视频生成功能
        /// </summary>
        public bool EnableVideo { get; set; } = true;

        /// <summary>
        /// 首页统计显示真实 IP
        /// </summary>
        public bool HomeDisplayRealIP { get; set; } = false;

        /// <summary>
        /// 首页统计显示真实 IP 用户对应的身份
        /// </summary>
        public bool HomeDisplayUserIPState { get; set; } = false;

        /// <summary>
        /// 首页统计默认数量 TOP 10
        /// </summary>
        public int HomeTopCount { get; set; } = 10;

        /// <summary>
        /// 启用更新检查
        /// </summary>
        public bool EnableUpdateCheck { get; set; } = true;

        /// <summary>
        /// 检查升级信息
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public UpgradeInfo UpgradeInfo { get; set; }

        /// <summary>
        /// 网站配置为演示模式
        /// </summary>
        public bool IsDemoMode { get; set; }

        /// <summary>
        /// Consul 服务注册配置
        /// </summary>
        public ConsulOptions ConsulOptions { get; set; } = new();

        /// <summary>
        /// 用户授权码
        /// </summary>
        public string LicenseKey { get; set; }

        /// <summary>
        /// MongoDB 默认连接字符串
        /// </summary>
        public string MongoDefaultConnectionString { get; set; }

        /// <summary>
        /// MongoDB 默认数据库
        /// </summary>
        public string MongoDefaultDatabase { get; set; }

        /// <summary>
        /// （废弃）是否使用
        /// </summary>
        [BsonIgnore]
        public bool IsMongo { get; set; }

        /// <summary>
        /// （废弃）是否启动本地数据自动迁移到 MongoDB
        /// </summary>
        public bool IsMongoAutoMigrate { get; set; }

        /// <summary>
        /// 本地数据库是否自动迁移到其他数据库
        /// </summary>
        public bool IsAutoMigrate { get; set; }

        /// <summary>
        /// 保存最大数据
        /// </summary>
        public int MaxCount { get; set; } = 500000;

        /// <summary>
        /// Discord账号选择规则.
        /// </summary>
        public AccountChooseRule AccountChooseRule { get; set; } = AccountChooseRule.BestWaitIdle;

        ///// <summary>
        ///// Discord单账号配置.
        ///// </summary>
        //[BsonIgnore]
        //public DiscordAccountConfig Discord { get; set; } = new DiscordAccountConfig();

        ///// <summary>
        ///// Discord账号池配置.
        ///// </summary>
        //[BsonIgnore]
        //public List<DiscordAccountConfig> Accounts { get; set; } = new List<DiscordAccountConfig>();

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

        /// <summary>
        /// CF 验证通知回调的密钥，防止篡改
        /// </summary>
        public string CaptchaNotifySecret { get; set; }

        /// <summary>
        /// 图片存储方式
        /// </summary>
        public ImageStorageType ImageStorageType { get; set; } = ImageStorageType.NONE;

        /// <summary>
        /// 阿里云存储配置
        /// </summary>
        public AliyunOssOptions AliyunOss { get; set; } = new AliyunOssOptions();

        /// <summary>
        /// 腾讯云存储配置
        /// </summary>
        public TencentCosOptions TencentCos { get; set; } = new TencentCosOptions();

        /// <summary>
        /// Cloudflare R2 存储配置
        /// </summary>
        public CloudflareR2Options CloudflareR2 { get; set; } = new CloudflareR2Options();

        /// <summary>
        /// S3 兼容存储配置 (支持 MinIO)
        /// </summary>
        public S3StorageOptions S3Storage { get; set; } = new S3StorageOptions();

        /// <summary>
        /// 换脸配置
        /// </summary>
        public ReplicateOptions Replicate { get; set; } = new ReplicateOptions();

        /// <summary>
        /// 本地存储配置
        /// </summary>
        public LocalStorageOptions LocalStorage { get; set; } = new LocalStorageOptions();

        /// <summary>
        /// 全局开启垂直领域
        /// </summary>
        public bool IsVerticalDomain { get; set; }

        /// <summary>
        /// 启用 Swagger
        /// </summary>
        public bool EnableSwagger { get; set; }

        /// <summary>
        /// Banned 限流配置
        /// </summary>
        public BannedLimitingOptions BannedLimiting { get; set; } = new();

        /// <summary>
        /// 限流配置
        /// </summary>
        public IpRateLimitingOptions IpRateLimiting { get; set; } = new();

        /// <summary>
        /// 黑名单限流配置
        /// </summary>
        public IpBlackRateLimitingOptions IpBlackRateLimiting { get; set; } = new();

        /// <summary>
        /// 开启注册
        /// </summary>
        public bool EnableRegister { get; set; }

        /// <summary>
        /// 注册用户默认日绘图限制
        /// </summary>
        public int RegisterUserDefaultDayLimit { get; set; } = -1;

        /// <summary>
        /// 注册用户默认总绘图限制
        /// </summary>
        public int RegisterUserDefaultTotalLimit { get; set; } = -1;

        /// <summary>
        /// 注册用户默认并发数
        /// </summary>
        public int RegisterUserDefaultCoreSize { get; set; } = -1;

        /// <summary>
        /// 注册用户默认队列数
        /// </summary>
        public int RegisterUserDefaultQueueSize { get; set; } = -1;

        /// <summary>
        /// 访客并发数
        /// </summary>
        public int GuestDefaultCoreSize { get; set; } = -1;

        /// <summary>
        /// 访客队列数
        /// </summary>
        public int GuestDefaultQueueSize { get; set; } = -1;

        /// <summary>
        /// 数据库类型
        /// </summary>
        public DatabaseType DatabaseType { get; set; } = DatabaseType.NONE;

        /// <summary>
        /// 数据库连接字符串
        /// </summary>
        public string DatabaseConnectionString { get; set; }

        /// <summary>
        /// 数据库名称
        /// </summary>
        public string DatabaseName { get; set; }

        /// <summary>
        /// 开启访客
        /// </summary>
        public bool EnableGuest { get; set; }

        /// <summary>
        /// 访客默认日绘图限制
        /// </summary>
        public int GuestDefaultDayLimit { get; set; } = -1;

        /// <summary>
        /// 首页公告
        /// </summary>
        public string Notify { get; set; }

        /// <summary>
        /// 启用启动时自动获取私信 ID 功能
        /// </summary>
        public bool EnableAutoGetPrivateId { get; set; }

        /// <summary>
        /// 启用启动时自动验证账号可用性功能
        /// </summary>
        public bool EnableAutoVerifyAccount { get; set; }

        /// <summary>
        /// 启用自动同步信息和设置
        /// </summary>
        public bool EnableAutoSyncInfoSetting { get; set; }

        /// <summary>
        /// 启用 token 自动延期
        /// </summary>
        public bool EnableAutoExtendToken { get; set; }

        /// <summary>
        /// 启用用户自定义上传 Base64
        /// </summary>
        public bool EnableUserCustomUploadBase64 { get; set; } = true;

        /// <summary>
        /// 启用转换官方链接，上传到 discord 服务器
        /// </summary>
        public bool EnableConvertOfficialLink { get; set; } = true;

        /// <summary>
        /// 启用转换云/加速链接/OSS/COS/CDN
        /// </summary>
        [Obsolete("废弃")]
        public bool EnableConvertAliyunLink { get; set; }

        /// <summary>
        /// 保存用户上传的 link 到文件存储（例如：describe）
        /// </summary>
        public bool EnableSaveUserUploadLink { get; set; } = true;

        /// <summary>
        /// 保存用户上传的 base64 到文件存储（例如：垫图、混图等）
        /// </summary>
        public bool EnableSaveUserUploadBase64 { get; set; } = true;

        /// <summary>
        /// 保存生成的图片到文件存储（discord 最终图片）
        /// </summary>
        public bool EnableSaveGeneratedImage { get; set; } = true;

        /// <summary>
        /// 保存过程中间图片到文件存储（discord 进度图片）
        /// </summary>
        public bool EnableSaveIntermediateImage { get; set; } = false;

        /// <summary>
        /// 开启 mj 翻译
        /// </summary>
        public bool EnableMjTranslate { get; set; } = true;

        /// <summary>
        /// 开启 niji 翻译
        /// </summary>
        public bool EnableNijiTranslate { get; set; } = true;

        /// <summary>
        /// 转换 Niji 为 MJ
        /// 启用后将 Niji · journey 任务自动转为 Midjourney 任务，并对任务添加 --niji 后缀（转换后出图效果是一致的）
        /// </summary>
        public bool EnableConvertNijiToMj { get; set; }

        /// <summary>
        /// 转换 --niji 为 Niji Bot
        /// 当 prompt 中包含 --niji 时，将会自动转换为 Niji·journey Bot 任务
        /// </summary>
        public bool EnableConvertNijiToNijiBot { get; set; }

        /// <summary>
        /// 开启自动登录
        /// </summary>
        public bool EnableAutoLogin { get; set; }

        /// <summary>
        /// 开启账号赞助
        /// </summary>
        public bool EnableAccountSponsor { get; set; }

        /// <summary>
        /// 开启悠船绘图功能
        /// </summary>
        public bool EnableYouChuan { get; set; }

        /// <summary>
        /// 开启官方绘图功能
        /// </summary>
        public bool EnableOfficial { get; set; }
    }

    /// <summary>
    /// 表示 Banned 限流配置选项，当处罚 Banned prompt detected 时，将会限制 IP 访问和非白名单用户
    /// </summary>
    public class BannedLimitingOptions
    {
        /// <summary>
        /// 是否启用 Banned 限流
        /// </summary>
        public bool Enable { get; set; }

        /// <summary>
        /// Banned 限流规则，key：当日触发次数，value：封锁时间（分钟）
        /// </summary>
        public Dictionary<int, int> Rules { get; set; } = [];
    }

    /// <summary>
    /// 本地存储配置
    /// </summary>
    public class LocalStorageOptions
    {
        /// <summary>
        /// 加速域名，可用于图片加速和图片审核使用
        /// </summary>
        public string CustomCdn { get; set; }
    }

    /// <summary>
    /// Cloudflare R2 存储配置
    /// </summary>
    public class CloudflareR2Options
    {
        /// <summary>
        /// 
        /// </summary>
        public string AccountId { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string AccessKey { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string SecretKey { get; set; }

        /// <summary>
        /// Bucket
        /// </summary>
        public string Bucket { get; set; }

        /// <summary>
        /// 加速域名，可用于图片加速和图片审核使用
        /// </summary>
        public string CustomCdn { get; set; }

        /// <summary>
        /// 默认图片样式
        /// </summary>
        public string ImageStyle { get; set; }

        /// <summary>
        /// 默认缩略图图片样式
        /// </summary>
        public string ThumbnailImageStyle { get; set; }

        /// <summary>
        /// 视频截帧
        /// https://cloud.tencent.com/document/product/436/55671
        /// </summary>
        public string VideoSnapshotStyle { get; set; }

        ///// <summary>
        ///// Storage class of the object
        ///// en: https://intl.cloud.tencent.com/document/product/436/30925
        ///// zh: https://cloud.tencent.com/document/product/436/33417
        ///// </summary>
        //public string StorageClass { get; set; }

        /// <summary>
        /// 链接默认有效时间
        /// </summary>
        public int ExpiredMinutes { get; set; } = 0;
    }

    /// <summary>
    /// S3 兼容存储配置 (支持 MinIO)
    /// </summary>
    public class S3StorageOptions
    {
        /// <summary>
        /// 服务端点 (例如: http://localhost:9000 或 https://s3.amazonaws.com)
        /// </summary>
        public string Endpoint { get; set; }

        /// <summary>
        /// 访问密钥ID
        /// </summary>
        public string AccessKey { get; set; }

        /// <summary>
        /// 秘密访问密钥
        /// </summary>
        public string SecretKey { get; set; }

        /// <summary>
        /// 存储桶名称
        /// </summary>
        public string Bucket { get; set; }

        /// <summary>
        /// 区域 (可选，默认为 us-east-1)
        /// </summary>
        public string Region { get; set; } = "us-east-1";

        /// <summary>
        /// 是否使用 HTTPS (默认为 true)
        /// </summary>
        public bool UseHttps { get; set; } = true;

        /// <summary>
        /// 是否强制路径样式 (MinIO 通常需要设置为 true)
        /// </summary>
        public bool ForcePathStyle { get; set; } = true;

        /// <summary>
        /// 自定义CDN域名，用于图片加速和图片审核
        /// </summary>
        public string CustomCdn { get; set; }

        /// <summary>
        /// 默认图片样式参数
        /// </summary>
        public string ImageStyle { get; set; }

        /// <summary>
        /// 默认缩略图图片样式参数
        /// </summary>
        public string ThumbnailImageStyle { get; set; }

        /// <summary>
        /// 视频截帧样式参数
        /// </summary>
        public string VideoSnapshotStyle { get; set; }

        /// <summary>
        /// 预签名链接有效时间（分钟，0表示不使用预签名）
        /// </summary>
        public int ExpiredMinutes { get; set; } = 0;

        /// <summary>
        /// 是否启用预签名URL（默认false，使用公共访问）
        /// </summary>
        public bool EnablePresignedUrl { get; set; } = false;
    }

    /// <summary>
    /// 腾讯云存储配置
    /// </summary>
    public class TencentCosOptions
    {
        /// <summary>
        /// Tencent Cloud Account APPID
        /// </summary>
        public string AppId { get; set; }

        /// <summary>
        /// Cloud API Secret Id
        /// </summary>
        public string SecretId { get; set; }

        /// <summary>
        /// Cloud API Secret Key
        /// </summary>
        public string SecretKey { get; set; }

        /// <summary>
        /// Bucket region ap-guangzhou ap-hongkong
        /// en: https://intl.cloud.tencent.com/document/product/436/6224
        /// zh: https://cloud.tencent.com/document/product/436/6224
        /// </summary>
        public string Region { get; set; }

        /// <summary>
        /// Bucket, format: BucketName-APPID
        /// </summary>
        public string Bucket { get; set; }

        /// <summary>
        /// 加速域名，可用于图片加速和图片审核使用
        /// </summary>
        public string CustomCdn { get; set; }

        /// <summary>
        /// 默认图片样式
        /// </summary>
        public string ImageStyle { get; set; }

        /// <summary>
        /// 默认缩略图图片样式
        /// </summary>
        public string ThumbnailImageStyle { get; set; }

        /// <summary>
        /// 视频截帧
        /// https://cloud.tencent.com/document/product/436/55671
        /// </summary>
        public string VideoSnapshotStyle { get; set; }

        ///// <summary>
        ///// Storage class of the object
        ///// en: https://intl.cloud.tencent.com/document/product/436/30925
        ///// zh: https://cloud.tencent.com/document/product/436/33417
        ///// </summary>
        //public string StorageClass { get; set; }

        /// <summary>
        /// 链接默认有效时间
        /// </summary>
        public int ExpiredMinutes { get; set; } = 0;
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
        /// 启用快速模式用完自动切换到慢速模式
        /// </summary>
        public bool EnableFastToRelax { get; set; }

        /// <summary>
        /// 启用时，当有快速时长时，自动切换到快速模式
        /// </summary>
        public bool EnableRelaxToFast { get; set; }

        /// <summary>
        /// 自动设置慢速
        /// 启用后，当快速用完时，如果允许生成速度模式是 FAST 或 TURBO，则自动清空原有模式，并设置为 RELAX 模式。
        /// </summary>
        public bool? EnableAutoSetRelax { get; set; }

        /// <summary>
        /// 并发数.
        /// </summary>
        public int CoreSize { get; set; } = 3;

        /// <summary>
        /// 等待队列长度.
        /// </summary>
        public int QueueSize { get; set; } = 10;

        /// <summary>
        /// 慢速并发数
        /// </summary>
        public int RelaxCoreSize { get; set; } = 3;

        /// <summary>
        /// 慢速等待队列数
        /// </summary>
        public int RelaxQueueSize { get; set; } = 10;

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
        /// 开启 Shoren 功能
        /// </summary>
        public bool IsShorten { get; set; } = true;

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
        /// 是否赞助者
        /// </summary>
        public bool IsSponsor { get; set; }

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
        /// 是否为悠船账号
        /// </summary>
        public bool IsYouChuan { get; set; }

        /// <summary>
        /// 是否为官方账号
        /// </summary>
        public bool IsOfficial { get; set; }
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

        ///// <summary>
        ///// 自动下载图片并保存到本地
        ///// </summary>
        //public bool? SaveToLocal { get; set; }

        ///// <summary>
        ///// 自定义 CDN 加速地址
        ///// </summary>
        //public string CustomCdn { get; set; }
    }

    /// <summary>
    /// 阿里云 OSS 配置
    /// <see cref="https://help.aliyun.com/document_detail/31947.html"/>
    /// </summary>
    public class AliyunOssOptions
    {
        ///// <summary>
        ///// 是否可用
        ///// </summary>
        //public bool Enable { get; set; }

        ///// <summary>
        ///// 启动本地图片自动迁移，待定
        ///// </summary>
        //public bool EnableAutoMigrate { get; set; }

        /// <summary>
        /// 存储空间是您用于存储对象（Object）的容器，所有的对象都必须隶属于某个存储空间。
        /// </summary>
        public string BucketName { get; set; }

        ///// <summary>
        ///// 地域表示 OSS 的数据中心所在物理位置。
        ///// </summary>
        //public string Region { get; set; }

        /// <summary>
        /// AccessKeyId用于标识用户，AccessKeySecret是用户用于加密签名字符串和OSS用来验证签名字符串的密钥，其中AccessKeySecret 必须保密。
        /// </summary>
        public string AccessKeyId { get; set; }

        /// <summary>
        /// AccessKeyId用于标识用户，AccessKeySecret是用户用于加密签名字符串和OSS用来验证签名字符串的密钥，其中AccessKeySecret 必须保密。
        /// </summary>
        public string AccessKeySecret { get; set; }

        /// <summary>
        /// Endpoint 表示OSS对外服务的访问域名。
        /// </summary>
        public string Endpoint { get; set; }

        /// <summary>
        /// 阿里云加速域名，可用于图片加速和图片审核使用
        /// </summary>
        public string CustomCdn { get; set; }

        /// <summary>
        /// 阿里云 OSS 默认图片样式
        /// </summary>
        public string ImageStyle { get; set; }

        /// <summary>
        /// 阿里云 OSS 默认缩略图图片样式 x-oss-process=style/w320
        /// </summary>
        public string ThumbnailImageStyle { get; set; }

        /// <summary>
        /// 阿里云 OSS 视频截帧
        /// x-oss-process=video/snapshot,t_6000,f_jpg,w_400,m_fast
        /// </summary>
        public string VideoSnapshotStyle { get; set; }

        ///// <summary>
        ///// 开启自动迁移本地文件到阿里云支持
        ///// </summary>
        //public bool IsAutoMigrationLocalFile { get; set; }

        /// <summary>
        /// 链接默认有效时间
        /// </summary>
        public int ExpiredMinutes { get; set; } = 0;
    }

    /// <summary>
    /// 基于 replicate 平台进行换脸等业务
    /// https://replicate.com/omniedgeio/face-swap
    /// https://replicate.com/codeplugtech/face-swap
    /// https://github.com/tzktz/face-swap?tab=readme-ov-file
    ///
    /// 其他参考：
    /// https://huggingface.co/spaces/tonyassi/video-face-swap
    /// https://huggingface.co/spaces/felixrosberg/face-swap
    /// https://felixrosberg-face-swap.hf.space/
    ///
    /// Picsi.Ai
    /// https://www.picsi.ai/faceswap
    /// </summary>
    public class ReplicateOptions
    {
        /// <summary>
        /// REPLICATE_API_TOKEN
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// 启用换脸
        /// </summary>
        public bool EnableFaceSwap { get; set; }

        /// <summary>
        /// 换脸版本
        /// 默认（$0.002/次）：https://replicate.com/codeplugtech/face-swap -> 278a81e7ebb22db98bcba54de985d22cc1abeead2754eb1f2af717247be69b34
        /// 快速（$0.019/次）：https://replicate.com/omniedgeio/face-swap -> d28faa318942bf3f1cbed9714def03594f99b3c69b2eb279c39fc60993cee9ac
        /// </summary>
        public string FaceSwapVersion { get; set; } = "278a81e7ebb22db98bcba54de985d22cc1abeead2754eb1f2af717247be69b34";

        /// <summary>
        /// 换脸并发数
        /// </summary>
        public int FaceSwapCoreSize { get; set; } = 3;

        /// <summary>
        /// 换脸等待队列长度。
        /// </summary>
        public int FaceSwapQueueSize { get; set; } = 10;

        /// <summary>
        /// 换脸任务超时
        /// </summary>
        public int FaceSwapTimeoutMinutes { get; set; } = 10;

        /// <summary>
        /// 启用视频换脸
        /// </summary>
        public bool EnableVideoFaceSwap { get; set; }

        /// <summary>
        /// 视频换脸模型版本
        /// https://replicate.com/xrunda/hello
        /// </summary>
        public string VideoFaceSwapVersion { get; set; } = "104b4a39315349db50880757bc8c1c996c5309e3aa11286b0a3c84dab81fd440";

        /// <summary>
        /// 视频换脸并发数
        /// </summary>
        public int VideoFaceSwapCoreSize { get; set; } = 3;

        /// <summary>
        /// 视频换脸等待队列长度。
        /// </summary>
        public int VideoFaceSwapQueueSize { get; set; } = 10;

        /// <summary>
        /// 视频换脸任务超时
        /// </summary>
        public int VideoFaceSwapTimeoutMinutes { get; set; } = 30;

        /// <summary>
        /// 最大文件大小限制
        /// </summary>
        public long MaxFileSize { get; set; } = 10 * 1024 * 1024;

        /// <summary>
        /// 回调通知
        /// </summary>
        public string Webhook { get; set; }

        /// <summary>
        /// 回调通知事件过滤
        /// start：预测开始时立即
        /// output：每次预测都会产生一个输出（请注意，预测可以产生多个输出）
        /// logs：每次日志输出都是由预测生成的
        /// completed：当预测达到终止状态（成功/取消/失败）时
        /// </summary>
        public string[] WebhookEventsFilter { get; set; } = [];
    }
}
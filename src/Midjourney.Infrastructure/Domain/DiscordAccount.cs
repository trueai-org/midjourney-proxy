using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace Midjourney.Infrastructure.Domain
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
        /// 服务器ID。
        /// </summary>
        [Required]
        [Display(Name = "服务器ID")]
        public string GuildId { get; set; }

        /// <summary>
        /// 频道ID。
        /// </summary>
        [Required]
        [Display(Name = "频道ID")]
        public string ChannelId { get; set; }

        /// <summary>
        /// 私信频道ID, 用来接收 seed 值
        /// </summary>
        [Display(Name = "私信频道ID")]
        public string PrivateChannelId { get; set; }

        /// <summary>
        /// 用户Token。
        /// </summary>
        [Required]
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
        /// 并发数。
        /// </summary>
        [Display(Name = "并发数")]
        public int CoreSize { get; set; } = 3;

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
        /// 获取显示名称。
        /// </summary>
        /// <returns>频道ID。</returns>
        public string GetDisplay()
        {
            return ChannelId;
        }
    }
}
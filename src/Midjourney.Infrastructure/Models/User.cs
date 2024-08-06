namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// 用户
    /// </summary>
    public class User : DomainObject
    {
        public User()
        {

        }

        /// <summary>
        /// 用户昵称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 邮箱
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// 手机号
        /// </summary>
        public string Phone { get; set; }

        /// <summary>
        /// 头像
        /// </summary>
        public string Avatar { get; set; }

        /// <summary>
        /// 角色 ADMIN | USER
        /// </summary>
        public EUserRole? Role { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public EUserStatus? Status { get; set; }

        /// <summary>
        /// 用户令牌
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// 最后登录 ip
        /// </summary>
        public string LastLoginIp { get; set; }

        /// <summary>
        /// 最后登录时间
        /// </summary>
        public DateTime? LastLoginTime { get; set; }

        /// <summary>
        /// 最后登录时间格式化
        /// </summary>
        public string LastLoginTimeFormat => LastLoginTime?.ToString("yyyy-MM-dd HH:mm");

        /// <summary>
        /// 注册 ip
        /// </summary>
        public string RegisterIp { get; set; }

        /// <summary>
        /// 注册时间
        /// </summary>
        public DateTime RegisterTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 注册时间格式化
        /// </summary>
        public string RegisterTimeFormat => RegisterTime.ToString("yyyy-MM-dd HH:mm");

        /// <summary>
        /// 日绘图最大次数限制，默认 0 不限制
        /// </summary>
        public int DayDrawLimit { get; set; }

        /// <summary>
        /// 白名单用户（加入白名单不受限流控制）
        /// </summary>
        public bool IsWhite { get; set; } = false;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
}
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using System.Text.RegularExpressions;

namespace Midjourney.API.Controllers
{
    /// <summary>
    /// 用于注册用户的控制器
    /// </summary>
    [ApiController]
    [Route("mj/register")]
    [AllowAnonymous]
    public class RegisterController : ControllerBase
    {
        private readonly IMemoryCache _memoryCache;
        private readonly string _ip;

        public RegisterController(IMemoryCache memoryCache, IHttpContextAccessor httpContextAccessor)
        {
            _memoryCache = memoryCache;
            _ip = httpContextAccessor.HttpContext.Request.GetIP();
        }

        /// <summary>
        /// 注册用户
        /// </summary>
        /// <param name="registerDto"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost()]
        public Result Register([FromBody] RegisterDto registerDto)
        {
            if (registerDto == null || string.IsNullOrWhiteSpace(registerDto.Email))
            {
                throw new LogicException("参数错误");
            }

            // 验证长度
            if (registerDto.Email.Length < 5 || registerDto.Email.Length > 50)
            {
                throw new LogicException("邮箱长度错误");
            }

            var mail = registerDto.Email.Trim();

            // 验证 email 格式
            var isMatch = Regex.IsMatch(mail, @"^[\w-]+(\.[\w-]+)*@[\w-]+(\.[\w-]+)+$");
            if (!isMatch)
            {
                throw new LogicException("邮箱格式错误");
            }

            // 判断是否开放注册
            // 如果没有配置邮件服务，则不允许注册
            if (GlobalConfiguration.Setting.EnableRegister != true
                || string.IsNullOrWhiteSpace(GlobalConfiguration.Setting?.Smtp?.FromPassword))
            {
                throw new LogicException("注册已关闭");
            }

            // 每个IP每天只能注册一个账号
            var key = $"register:{_ip}";
            if (_memoryCache.TryGetValue(key, out _))
            {
                throw new LogicException("注册太频繁");
            }

            // 验证用户是否存在
            var user = DbHelper.UserStore.Single(u => u.Email == mail);
            if (user != null)
            {
                throw new LogicException("用户已存在");
            }

            user = new User
            {
                Id = Guid.NewGuid().ToString("N"),
                Role = EUserRole.USER,
                Status = EUserStatus.NORMAL,
                DayDrawLimit = GlobalConfiguration.Setting.RegisterUserDefaultDayLimit,
                Email = mail,
                RegisterIp = _ip,
                RegisterTime = DateTime.Now,
                Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                Name = mail.Split('@').FirstOrDefault()
            };
            DbHelper.UserStore.Add(user);

            // 发送邮件
            EmailJob.Instance.EmailSend(GlobalConfiguration.Setting.Smtp,
                $"Midjourney Proxy 注册通知", $"您的登录密码为：{user.Token}");

            // 设置缓存
            _memoryCache.Set(key, true, TimeSpan.FromDays(1));

            return Result.Ok();
        }
    }
}
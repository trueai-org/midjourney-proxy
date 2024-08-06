using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Midjourney.Infrastructure.Dto;

namespace Midjourney.API.Controllers
{
    /// <summary>
    /// 用于获取首页等信息的控制器
    /// </summary>
    [ApiController]
    [Route("mj/home")]
    [AllowAnonymous]
    public class HomeController : ControllerBase
    {
        private readonly IMemoryCache _memoryCache;
        private readonly string _ip;

        public HomeController(IMemoryCache memoryCache, IHttpContextAccessor httpContextAccessor)
        {
            _memoryCache = memoryCache;
            _ip = httpContextAccessor.HttpContext.Request.GetIP();
        }

        /// <summary>
        /// 首页
        /// </summary>
        /// <returns></returns>
        [HttpGet()]
        public Result<HomeDto> Info()
        {
            var dto = new HomeDto
            {
                IsRegister = GlobalConfiguration.Setting.EnableRegister == true
                && !string.IsNullOrWhiteSpace(GlobalConfiguration.Setting?.Smtp?.FromPassword),
                IsGuest = GlobalConfiguration.Setting.EnableGuest == true,
            };

            return Result.Ok(dto);
        }
    }
}
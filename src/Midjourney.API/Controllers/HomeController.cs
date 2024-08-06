using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Midjourney.Infrastructure.Data;
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
        public HomeController(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        /// <summary>
        /// 首页
        /// </summary>
        /// <returns></returns>
        [HttpGet()]
        public Result<HomeDto> Info()
        {
            var data = _memoryCache.GetOrCreate("HOME", c =>
            {
                c.SetAbsoluteExpiration(TimeSpan.FromMinutes(10));

                var dto = new HomeDto
                {
                    IsRegister = GlobalConfiguration.Setting.EnableRegister == true
                    && !string.IsNullOrWhiteSpace(GlobalConfiguration.Setting?.Smtp?.FromPassword),
                    IsGuest = GlobalConfiguration.Setting.EnableGuest == true,
                    IsDemoMode = GlobalConfiguration.IsDemoMode == true
                };

                var now = new DateTimeOffset(DateTime.UtcNow.Date).ToUnixTimeMilliseconds();
                var yesterday = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-1)).ToUnixTimeMilliseconds();

                dto.TodayDraw = (int)DbHelper.TaskStore.Count(x => x.SubmitTime >= now);
                dto.YesterdayDraw = (int)DbHelper.TaskStore.Count(x => x.SubmitTime >= yesterday && x.SubmitTime < now);
                dto.TotalDraw = DbHelper.TaskStore.GetCollection().Query().Count();

                // 今日绘图客户端 top 5
                var top5 = DbHelper.TaskStore.GetCollection().Query()
                    .Where(x => x.SubmitTime >= now)
                    .ToList()
                    .GroupBy(c => c.ClientIp)
                    .Select(c => new
                    {
                        ip = c.Key,
                        count = c.Count()
                    })
                    .OrderByDescending(c => c.count)
                    .Take(5)
                    .ToDictionary(c => c.ip, c => c.count);

                dto.Tops = top5;

                return dto;
            });

            return Result.Ok(data);
        }
    }
}
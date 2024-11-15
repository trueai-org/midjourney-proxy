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
            var now = DateTime.Now.ToString("yyyyMMdd");
            var data = _memoryCache.GetOrCreate($"{now}_home", c =>
            {
                c.SetAbsoluteExpiration(TimeSpan.FromMinutes(10));

                var dto = new HomeDto
                {
                    IsRegister = GlobalConfiguration.Setting.EnableRegister == true
                    && !string.IsNullOrWhiteSpace(GlobalConfiguration.Setting?.Smtp?.FromPassword),
                    IsGuest = GlobalConfiguration.Setting.EnableGuest == true,
                    IsDemoMode = GlobalConfiguration.IsDemoMode == true,
                    Version = GlobalConfiguration.Version,
                    Notify = GlobalConfiguration.Setting.Notify
                };

                var now = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();
                var yesterday = new DateTimeOffset(DateTime.Now.Date.AddDays(-1)).ToUnixTimeMilliseconds();

                dto.TodayDraw = (int)DbHelper.Instance.TaskStore.Count(x => x.SubmitTime >= now);
                dto.YesterdayDraw = (int)DbHelper.Instance.TaskStore.Count(x => x.SubmitTime >= yesterday && x.SubmitTime < now);
                dto.TotalDraw = (int)DbHelper.Instance.TaskStore.Count(x => true);

                // 今日绘图客户端 top 10
                var top10 = DbHelper.Instance.TaskStore.Where(x => x.SubmitTime >= now)
                    .ToList()
                    .GroupBy(c => string.Join(".", c.ClientIp?.Split('.')?.Take(2) ?? []) + ".x.x")
                    .Select(c => new
                    {
                        ip = c.Key ?? "null",
                        count = c.Count()
                    })
                    .OrderByDescending(c => c.count)
                    .Take(10)
                    .ToDictionary(c => c.ip, c => c.count);

                dto.Tops = top10;

                return dto;
            });

            return Result.Ok(data);
        }
    }
}
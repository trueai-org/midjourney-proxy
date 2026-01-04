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
using MongoDB.Driver;

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
        private readonly IFreeSql _freeSql;

        public HomeController(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
            _freeSql = FreeSqlHelper.FreeSql;
        }

        /// <summary>
        /// 首页
        /// </summary>
        /// <returns></returns>
        [HttpGet()]
        public async Task<Result<HomeDto>> Info()
        {
            var now = DateTime.Now.ToString("yyyyMMdd");
            var homeKey = $"{now}_home";

            var data = await _memoryCache.GetOrCreate(homeKey, async c =>
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

                dto.TodayDraw = _freeSql.Count<TaskInfo>(x => x.SubmitTime >= now);
                dto.YesterdayDraw = _freeSql.Count<TaskInfo>(x => x.SubmitTime >= yesterday && x.SubmitTime < now);
                dto.TotalDraw = _freeSql.Count<TaskInfo>();

                // 今日绘图客户端 top 10
                var setting = GlobalConfiguration.Setting;

                var top = GlobalConfiguration.Setting.HomeTopCount;
                if (top <= 0)
                {
                    top = 10; // 默认取前10
                }
                if (top > 100)
                {
                    top = 100; // 最多取前100
                }

                // 如果显示 ip 对应的身份
                if (setting.HomeDisplayUserIPState)
                {
                    var todayIps = _freeSql.Select<TaskInfo>().Where(c => c.SubmitTime >= now)
                    .GroupBy(c => new { c.ClientIp, c.State })
                    .ToList(c => new
                    {
                        c.Key.ClientIp,
                        c.Key.State,
                        Count = c.Count(),
                    });
                    var tops = todayIps
                    .GroupBy(c =>
                    {
                        if (setting.HomeDisplayRealIP)
                        {
                            return c.ClientIp ?? "null";
                        }

                        // 如果不显示真实IP，则只显示前两段IP地址
                        // 只显示前两段IP地址
                        return string.Join(".", c.ClientIp?.Split('.')?.Take(2) ?? []) + ".x.x";
                    })
                    .Select(c =>
                    {
                        var item = todayIps.FirstOrDefault(u => u.ClientIp == c.Key && !string.IsNullOrWhiteSpace(u.State));

                        return new
                        {
                            ip = (c.Key ?? "null") + " - " + item?.State,
                            count = c.Sum(x => x.Count)
                        };
                    })
                    .OrderByDescending(c => c.count)
                    .Take(top)
                    .ToDictionary(c => c.ip, c => c.count);

                    dto.Tops = tops;
                }
                else
                {
                    var todayIps = _freeSql.Select<TaskInfo>().Where(c => c.SubmitTime >= now)
                    .GroupBy(c => c.ClientIp)
                    .ToList(c => new
                    {
                        ClientIp = c.Key,
                        Count = c.Count(),
                    });

                    var tops = todayIps
                    .GroupBy(c =>
                    {
                        if (setting.HomeDisplayRealIP)
                        {
                            return c.ClientIp ?? "null";
                        }

                        // 如果不显示真实IP，则只显示前两段IP地址
                        // 只显示前两段IP地址
                        return string.Join(".", c.ClientIp?.Split('.')?.Take(2) ?? []) + ".x.x";
                    })
                    .Select(c =>
                    {
                        return new
                        {
                            ip = c.Key ?? "null",
                            count = c.Sum(x => x.Count)
                        };
                    })
                    .OrderByDescending(c => c.count)
                    .Take(top)
                    .ToDictionary(c => c.ip, c => c.count);

                    dto.Tops = tops;
                }

                var localIp = await PrivateNetworkHelper.GetAliyunPrivateIpAsync();
                if (!string.IsNullOrWhiteSpace(localIp))
                {
                    dto.PrivateIp = localIp;
                }

                // 今日成功任务速度操作统计
                var todaySuccessGroups = _freeSql.Select<TaskInfo>()
                .Where(c => c.SubmitTime >= now && c.Status == TaskStatus.SUCCESS)
                .GroupBy(c => new { c.Mode, c.Action })
                .ToList(c => new { c.Key, Count = c.Count() })
                .ToDictionary(c => c.Key, c => c.Count);
                var todaySuccessCounter = new Dictionary<GenerationSpeedMode, Dictionary<TaskAction, int>>();
                foreach (var kvp in todaySuccessGroups)
                {
                    var mode = kvp.Key.Mode ?? GenerationSpeedMode.FAST;
                    if (!todaySuccessCounter.ContainsKey(mode))
                    {
                        todaySuccessCounter[mode] = [];
                    }
                    todaySuccessCounter[mode][kvp.Key.Action ?? TaskAction.IMAGINE] = kvp.Value;
                }
                dto.TodayCounter = todaySuccessCounter.OrderBy(c => c.Key).ToDictionary(c => c.Key, c => c.Value.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value));

                // 今日取消任务操作统计
                var todayCancelGroups = _freeSql.Select<TaskInfo>()
                .Where(c => c.SubmitTime >= now && c.Status == TaskStatus.CANCEL && c.Action != null)
                .GroupBy(c => c.Action)
                .ToList(c => new { Action = c.Key.Value, Count = c.Count() })
                .ToDictionary(c => c.Action, c => c.Count);

                // 今日失败任务操作统计
                var todayFailGroups = _freeSql.Select<TaskInfo>()
                .Where(c => c.SubmitTime >= now && c.Status == TaskStatus.FAILURE && c.Action != null)
                .GroupBy(c => c.Action)
                .ToList(c => new { Action = c.Key.Value, Count = c.Count() })
                .ToDictionary(c => c.Action, c => c.Count);

                dto.TodayCancelCounter = todayCancelGroups;
                dto.TodayFailCounter = todayFailGroups;

                return dto;
            });
            data.SystemInfo = SystemInfo.GetCurrentSystemInfo();

            return Result.Ok(data);
        }
    }
}
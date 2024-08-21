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
using Microsoft.Extensions.Caching.Memory;
using System.Net;

namespace Midjourney.API
{
    /// <summary>
    /// 限流中间件，用于限制特定 IP 地址的请求频率。
    /// </summary>
    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMemoryCache _cache;

        public RateLimitingMiddleware(RequestDelegate next, IMemoryCache cache)
        {
            _next = next;
            _cache = cache;
        }

        /// <summary>
        /// 中间件执行逻辑，处理请求限流。
        /// </summary>
        /// <param name="context">HTTP 上下文。</param>
        /// <param name="workContext"></param>
        /// <returns>异步任务。</returns>
        public async Task InvokeAsync(HttpContext context, WorkContext workContext)
        {
            var ipRateOpt = GlobalConfiguration.Setting?.IpRateLimiting;
            var ipBlackRateOpt = GlobalConfiguration.Setting?.IpBlackRateLimiting;

            if (ipRateOpt?.Enable != true && ipBlackRateOpt?.Enable != true)
            {
                await _next(context);
                return;
            }

            // 检查是否有 AllowAnonymous 特性
            // 匿名 API 不限流
            var endpoint = context.GetEndpoint();
            var allowAnonymous = endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null;
            if (allowAnonymous)
            {
                await _next(context);
                return;
            }

            // 白名单用户不限流
            // 管理员不限流
            var user = workContext.GetUser();
            if (user != null && (user.IsWhite || user.Role == EUserRole.ADMIN))
            {
                await _next(context);
                return;
            }

            var clientIp = context.Request.GetIP();

            // 转为 /32
            clientIp += "/32";

            var ipAddress = IPNetwork2.Parse(clientIp);
            var requestPath = context.Request.Path.ToString();

            // IP/IP 段限流
            if (ipRateOpt?.Enable == true)
            {
                // 检查是否在白名单中
                if (ipRateOpt.WhitelistNetworks.Any(c => c.Contains(ipAddress)))
                {
                    await _next(context);
                    return;
                }

                // 检查是否在黑名单中
                if (ipRateOpt.BlacklistNetworks.Any(c => c.Contains(ipAddress)))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    return;
                }

                // 检查普通限流规则
                if (!CheckRateLimits("rate_", ipAddress, requestPath,
                    ipRateOpt.IpRules,
                    ipRateOpt.Ip24Rules,
                    ipRateOpt.Ip16Rules))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                    return;
                }
            }

            // IP/IP 段黑名单限流
            if (ipBlackRateOpt.Enable)
            {
                // 检查是否在白名单中
                if (ipBlackRateOpt.WhitelistNetworks.Any(c => c.Contains(ipAddress)))
                {
                    await _next(context);
                    return;
                }

                // 检查是否在黑名单中
                if (_cache.TryGetValue($"black_rate_{ipAddress.Value}", out _)
                    || ipBlackRateOpt.BlacklistNetworks.Any(c => c.Contains(ipAddress)))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    return;
                }

                // 检查黑名单限流规则
                if (!CheckRateLimits("black_rate_", ipAddress, requestPath,
                    ipBlackRateOpt.IpRules,
                    ipBlackRateOpt.Ip24Rules,
                    ipBlackRateOpt.Ip16Rules))
                {
                    _cache.Set($"black_rate_{ipAddress.Value}", 1, TimeSpan.FromMinutes(ipBlackRateOpt.BlockTime));

                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    return;
                }
            }

            await _next(context);
        }

        /// <summary>
        /// 检查指定 IP 地址的请求是否符合限流规则。
        /// </summary>
        /// <param name="keyPrefix">key 前缀</param>
        /// <param name="ipAddress">请求的 IP 地址。</param>
        /// <param name="requestPath">请求路径。</param>
        /// <param name="ipRules">IP 限流规则。</param>
        /// <param name="ip24Rules">IP /24 段</param>
        /// <param name="ip16Rules">IP /16 段</param>
        /// <returns>是否符合限流规则。</returns>
        private bool CheckRateLimits(
            string keyPrefix,
            IPNetwork2 ipAddress,
            string requestPath,
            Dictionary<string, Dictionary<int, int>> ipRules,
            Dictionary<string, Dictionary<int, int>> ip24Rules,
            Dictionary<string, Dictionary<int, int>> ip16Rules)
        {
            // 检查 IP 规则
            foreach (var rule in ipRules)
            {
                if (MatchesPath(requestPath, rule.Key))
                {
                    if (!ApplyRateLimits(ipAddress, $"{keyPrefix}{rule.Key}", rule.Value))
                    {
                        return false;
                    }
                }
            }

            // 检查 IP 段规则 0.0.0.0/24
            // 将当前 ip 转为 ip 段 192.168.1.3/32 -> 192.168.1.0/24
            var ip24 = IPNetwork2.Parse($"{ipAddress.Network}/24");
            foreach (var rule in ip24Rules)
            {
                if (MatchesPath(requestPath, rule.Key))
                {
                    if (!ApplyRateLimits(ip24, $"{keyPrefix}{rule.Key}", rule.Value))
                    {
                        return false;
                    }
                }
            }

            // 检查 IP 段规则 0.0.0.0/16
            // 将当前 ip 转为 ip 段 192.168.1.3/32 -> 192.168.0.0/16
            var ip16 = IPNetwork2.Parse($"{ipAddress.Network}/16");
            foreach (var rule in ip16Rules)
            {
                if (MatchesPath(requestPath, rule.Key))
                {
                    if (!ApplyRateLimits(ip16, $"{keyPrefix}{rule.Key}", rule.Value))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 应用限流规则。
        /// </summary>
        /// <param name="ipAddress">请求的 IP 地址。</param>
        /// <param name="requestPathKey">请求路径规则：*/mj/*</param>
        /// <param name="rateLimits">限流规则</param>
        /// <returns>是否符合限流规则。</returns>
        private bool ApplyRateLimits(IPNetwork2 ipAddress, string requestPathKey, Dictionary<int, int> rateLimits)
        {
            var now = DateTime.UtcNow;

            foreach (var limit in rateLimits)
            {
                var timeWindowSeconds = limit.Key;
                var unit = now.Ticks / TimeSpan.TicksPerSecond / timeWindowSeconds;

                var cacheKey = $"RateLimiter:{ipAddress}:{requestPathKey}:{unit}";

                if (_cache.TryGetValue(cacheKey, out int count))
                {
                    if (count >= limit.Value)
                    {
                        return false;
                    }
                }

                _cache.Set(cacheKey, count + 1, TimeSpan.FromSeconds(timeWindowSeconds));
            }
            return true;
        }

        /// <summary>
        /// 检查请求路径是否匹配特定模式，支持 * 开头、中间或结尾的通配符。
        /// </summary>
        /// <param name="requestPath">请求路径。</param>
        /// <param name="pattern">匹配模式。</param>
        /// <returns>是否匹配。</returns>
        private bool MatchesPath(string requestPath, string pattern)
        {
            if (pattern.StartsWith("*") && pattern.EndsWith("*"))
            {
                return requestPath.Contains(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase);
            }
            else if (pattern.StartsWith("*"))
            {
                return requestPath.EndsWith(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase);
            }
            else if (pattern.EndsWith("*"))
            {
                return requestPath.StartsWith(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase);
            }
            return requestPath.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
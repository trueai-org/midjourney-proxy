using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Midjourney.Infrastructure.Options;
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
        private readonly IpRateLimitingOptions _ipRateOptions;
        private readonly IpBlackRateLimitingOptions _ipBlackRateOptions;

        public RateLimitingMiddleware(RequestDelegate next,
            IMemoryCache cache,
            IOptionsMonitor<IpRateLimitingOptions> ipRateoptions,
            IOptionsMonitor<IpBlackRateLimitingOptions> ipBlackRateoptions)
        {
            _next = next;
            _cache = cache;
            _ipRateOptions = ipRateoptions.CurrentValue;
            _ipBlackRateOptions = ipBlackRateoptions.CurrentValue;
        }

        /// <summary>
        /// 中间件执行逻辑，处理请求限流。
        /// </summary>
        /// <param name="context">HTTP 上下文。</param>
        /// <returns>异步任务。</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (_ipRateOptions?.Enable != true && _ipBlackRateOptions?.Enable != true)
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
            if (_ipRateOptions?.Enable == true)
            {
                // 检查是否在白名单中
                if (_ipRateOptions.WhitelistNetworks.Any(c => c.Contains(ipAddress)))
                {
                    await _next(context);
                    return;
                }

                // 检查是否在黑名单中
                if (_ipRateOptions.BlacklistNetworks.Any(c => c.Contains(ipAddress)))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    return;
                }

                // 检查普通限流规则
                if (!CheckRateLimits(ipAddress, requestPath, _ipRateOptions.IpRules, _ipRateOptions.IpRangeRules))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                    return;
                }
            }

            // IP/IP 段黑名单限流
            if (_ipBlackRateOptions.Enable)
            {
                // 检查是否在白名单中
                if (_ipBlackRateOptions.WhitelistNetworks.Any(c => c.Contains(ipAddress)))
                {
                    await _next(context);
                    return;
                }

                // 检查是否在黑名单中
                if (_cache.TryGetValue($"BLACK_RATE_{ipAddress.Value}", out _) || _ipBlackRateOptions.BlacklistNetworks.Any(c => c.Contains(ipAddress)))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    return;
                }

                // 检查黑名单限流规则
                if (!CheckRateLimits(ipAddress, requestPath, _ipBlackRateOptions.IpRules, _ipBlackRateOptions.IpRangeRules))
                {
                    _cache.Set($"BLACK_RATE_{ipAddress.Value}", 1, TimeSpan.FromMinutes(_ipBlackRateOptions.BlockTime));

                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                    return;
                }
            }

            await _next(context);
        }

        /// <summary>
        /// 检查指定 IP 地址的请求是否符合限流规则。
        /// </summary>
        /// <param name="ipAddress">请求的 IP 地址。</param>
        /// <param name="requestPath">请求路径。</param>
        /// <param name="ipRules">IP 限流规则。</param>
        /// <param name="ipRangeRules">IP 段限流规则。</param>
        /// <returns>是否符合限流规则。</returns>
        private bool CheckRateLimits(IPNetwork2 ipAddress, string requestPath, Dictionary<string, Dictionary<int, int>> ipRules, Dictionary<string, Dictionary<int, int>> ipRangeRules)
        {
            // 检查 IP 规则
            foreach (var rule in ipRules)
            {
                if (MatchesPath(requestPath, rule.Key))
                {
                    if (!ApplyRateLimits(ipAddress, rule.Key, rule.Value))
                    {
                        return false;
                    }
                }
            }

            // 检查 IP 段规则
            // 将当前 ip 转为 ip 段 192.168.1.3/32 -> 192.168.1.0/24
            var ipRange = IPNetwork2.Parse($"{ipAddress.Network}/24");
            foreach (var rule in ipRangeRules)
            {
                if (MatchesPath(requestPath, rule.Key))
                {
                    if (!ApplyRateLimits(ipRange, rule.Key, rule.Value))
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
            foreach (var limit in rateLimits)
            {
                var cacheKey = $"RateLimiter:{ipAddress}:{requestPathKey}:{limit.Key}";
                var limitTime = TimeSpan.FromSeconds(limit.Key);
                if (_cache.TryGetValue(cacheKey, out int count))
                {
                    if (count >= limit.Value)
                    {
                        return false;
                    }
                }
                _cache.Set(cacheKey, count + 1, limitTime);
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
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
using Microsoft.Extensions.Caching.Memory;

namespace Midjourney.API
{
    /// <summary>
    /// 工作上下文
    /// </summary>
    public class WorkContext
    {
        private readonly IMemoryCache _memberCache;
        private readonly string _ip;

        /// <summary>
        /// 请求携带的认证令牌，按请求头优先级取第一个非空值
        /// </summary>
        private readonly string _token;

        public WorkContext(IHttpContextAccessor contextAccessor, IMemoryCache memoryCache)
        {
            _memberCache = memoryCache;

            var request = contextAccessor?.HttpContext?.Request;
            if (request != null)
            {
                _ip = request.GetIP();

                // 兼容 OpenAI、Midjourney、Claude、Azure 等客户端的认证头。
                // 按优先级取第一个非空值；头名不区分大小写，Bearer 前缀会自动去掉。
                _token = GetFirstToken(request, "Authorization")
                    ?? GetFirstToken(request, "mj-api-secret")
                    ?? GetFirstToken(request, "x-api-key")
                    ?? GetFirstToken(request, "api-key");
            }
        }

        /// <summary>
        /// 读取指定请求头的第一个非空令牌值
        /// </summary>
        /// <param name="request">当前请求</param>
        /// <param name="headerName">请求头名称</param>
        /// <returns>去除 Bearer 前缀后的令牌；没有非空值时返回 null</returns>
        private static string GetFirstToken(HttpRequest request, string headerName)
        {
            if (!request.Headers.TryGetValue(headerName, out var values))
            {
                return null;
            }

            foreach (var value in values)
            {
                var token = NormalizeToken(value);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }
            }

            return null;
        }

        /// <summary>
        /// 去除令牌首尾空白和 Bearer 前缀
        /// </summary>
        /// <param name="value">请求头原始值</param>
        /// <returns>规范化后的令牌；原始值为空时返回 null</returns>
        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            value = value.Trim();

            const string bearerPrefix = "Bearer ";
            if (value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[bearerPrefix.Length..].Trim();
            }

            return value;
        }

        public string GetToken()
        {
            return _token;
        }

        public string GetIp()
        {
            return _ip;
        }

        public User GetUser()
        {
            if (!string.IsNullOrWhiteSpace(_token))
            {
                var user = _memberCache.GetOrCreate($"USER_{_token}", c =>
                {
                    c.SetAbsoluteExpiration(TimeSpan.FromMinutes(30));
                    c.SetSlidingExpiration(TimeSpan.FromMinutes(5));

                    return FreeSqlHelper.FreeSql.Single<User>(c => c.Token == _token);
                });

                // 无效 token, 立即清空缓存
                if (user == null)
                {
                    _memberCache.Remove($"USER_{_token}");
                }

                return user;
            }

            return null;
        }

        /// <summary>
        /// 清除用户缓存
        /// </summary>
        /// <param name="tokens"></param>
        public void ClearUserCache(params string[] tokens)
        {
            if (!string.IsNullOrWhiteSpace(_token))
            {
                _memberCache.Remove($"USER_{_token}");
            }

            if (tokens != null)
            {
                foreach (var token in tokens)
                {
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        _memberCache.Remove($"USER_{token}");
                    }
                }
            }
        }
    }
}
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
using Midjourney.Infrastructure.Data;

namespace Midjourney.API
{
    /// <summary>
    /// 工作上下文
    /// </summary>
    public class WorkContext
    {
        private readonly IMemoryCache _memberCache;
        private readonly string _ip;
        private readonly string _token;

        public WorkContext(IHttpContextAccessor contextAccessor, IMemoryCache memoryCache)
        {
            _memberCache = memoryCache;

            var request = contextAccessor?.HttpContext?.Request;
            if (request != null)
            {
                _ip = request.GetIP();

                var hasAuthHeader = request.Headers.TryGetValue("Authorization", out var authHeader);
                var hasApiSecretHeader = request.Headers.TryGetValue("Mj-Api-Secret", out var apiSecretHeader);
                _token = hasAuthHeader ? authHeader.ToString() : apiSecretHeader.ToString();
            }
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

                    return DbHelper.Instance.UserStore.Single(c => c.Token == _token);
                });

                return user;
            }

            return null;
        }
    }
}
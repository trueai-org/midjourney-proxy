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

                    return DbHelper.UserStore.Single(c => c.Token == _token);
                });

                return user;
            }

            return null;
        }
    }
}
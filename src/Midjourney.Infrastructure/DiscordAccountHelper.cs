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
using Midjourney.Infrastructure.Handle;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using Org.BouncyCastle.Cms;
using System.Net;
using System.Reflection;
using System.Text.Json;

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// Discord账号辅助类，用于创建和管理Discord实例。
    /// </summary>
    public class DiscordAccountHelper
    {
        private readonly DiscordHelper _discordHelper;
        private readonly ProxyProperties _properties;

        private readonly ITaskStoreService _taskStoreService;
        private readonly INotifyService _notifyService;

        private readonly IEnumerable<BotMessageHandler> _botMessageHandlers;
        private readonly IEnumerable<UserMessageHandler> _userMessageHandlers;
        private readonly Dictionary<string, string> _paramsMap;
        private readonly IMemoryCache _memoryCache;
        private readonly ITaskService _taskService;

        public DiscordAccountHelper(
            DiscordHelper discordHelper,
            ITaskStoreService taskStoreService,
            IEnumerable<BotMessageHandler> messageHandlers,
            INotifyService notifyService,
            IEnumerable<UserMessageHandler> userMessageHandlers,
            IMemoryCache memoryCache,
            ITaskService taskService)
        {
            _properties = GlobalConfiguration.Setting;
            _discordHelper = discordHelper;
            _taskStoreService = taskStoreService;
            _notifyService = notifyService;
            _botMessageHandlers = messageHandlers;
            _userMessageHandlers = userMessageHandlers;
            _memoryCache = memoryCache;

            var paramsMap = new Dictionary<string, string>();
            var assembly = Assembly.GetExecutingAssembly();
            var assemblyName = assembly.GetName().Name;
            var resourceNames = assembly.GetManifestResourceNames()
                .Where(name => name.EndsWith(".json") && name.Contains("Resources.ApiParams"))
                .ToList();

            foreach (var resourceName in resourceNames)
            {
                var fileName = Path.GetFileNameWithoutExtension(resourceName);
                using var stream = assembly.GetManifestResourceStream(resourceName);
                using var reader = new StreamReader(stream);
                var paramsContent = reader.ReadToEnd();

                var fileKey = fileName.TrimPrefix(assemblyName + ".Resources.ApiParams.").TrimSuffix(".json");

                paramsMap[fileKey] = paramsContent;
            }

            _paramsMap = paramsMap;
            _taskService = taskService;
        }

        /// <summary>
        /// 创建Discord实例。
        /// </summary>
        /// <param name="account">Discord账号信息。</param>
        /// <returns>Discord实例。</returns>
        /// <exception cref="ArgumentException">当guildId, channelId或userToken为空时抛出。</exception>
        public async Task<DiscordInstance> CreateDiscordInstance(DiscordAccount account)
        {
            if (string.IsNullOrWhiteSpace(account.GuildId) || string.IsNullOrWhiteSpace(account.ChannelId) || string.IsNullOrWhiteSpace(account.UserToken))
            {
                throw new ArgumentException("guildId, channelId, userToken must not be blank");
            }

            if (string.IsNullOrWhiteSpace(account.UserAgent))
            {
                account.UserAgent = Constants.DEFAULT_DISCORD_USER_AGENT;
            }

            // Bot 消息监听器
            WebProxy webProxy = null;
            if (!string.IsNullOrEmpty(_properties.Proxy?.Host))
            {
                webProxy = new WebProxy(_properties.Proxy.Host, _properties.Proxy.Port ?? 80);
            }

            var discordInstance = new DiscordInstance(
                _memoryCache,
                account,
                _taskStoreService,
                _notifyService,
                _discordHelper,
                _paramsMap,
                webProxy,
                _taskService);

            if (account.Enable == true)
            {
                // bot 消息监听
                var messageListener = new BotMessageListener(_discordHelper, webProxy);
                messageListener.Init(discordInstance, _botMessageHandlers, _userMessageHandlers);
                await messageListener.StartAsync();

                // 用户 WebSocket 连接
                var webSocket = new WebSocketManager(
                    _discordHelper,
                    messageListener,
                    webProxy,
                    discordInstance,
                    _memoryCache);
                await webSocket.StartAsync();

                // 跟踪 wss 连接
                discordInstance.BotMessageListener = messageListener;
                discordInstance.WebSocketManager = webSocket;
            }

            return discordInstance;
        }

        /// <summary>
        /// 验证账号是否可用
        /// </summary>
        /// <param name="account"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<bool> ValidateAccount(DiscordAccount account)
        {
            if (string.IsNullOrWhiteSpace(account.UserAgent))
            {
                account.UserAgent = Constants.DEFAULT_DISCORD_USER_AGENT;
            }

            WebProxy webProxy = null;
            if (!string.IsNullOrEmpty(_properties.Proxy?.Host))
            {
                webProxy = new WebProxy(_properties.Proxy.Host, _properties.Proxy.Port ?? 80);
            }

            var hch = new HttpClientHandler
            {
                UseProxy = webProxy != null,
                Proxy = webProxy
            };

            var client = new HttpClient(hch)
            {
                Timeout = TimeSpan.FromMinutes(10),
            };

            var request = new HttpRequestMessage(HttpMethod.Get, DiscordHelper.DISCORD_VAL_URL);
            request.Headers.Add("Authorization", account.UserToken);
            request.Headers.Add("User-Agent", account.UserAgent);

            var response = await client.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return true;
            }

            //{
            //    "message": "执行此操作需要先验证您的账号。",
            //    "code": 40002
            //}

            var data = JsonDocument.Parse(json).RootElement;
            if (data.TryGetProperty("message", out var message))
            {
                throw new Exception(message.GetString() ?? "账号验证异常");
            }

            return false;
        }

        /// <summary>
        /// 获取私信 ID
        /// </summary>
        /// <param name="account"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<string> GetBotPrivateId(DiscordAccount account, EBotType botType)
        {
            if (string.IsNullOrWhiteSpace(account.UserAgent))
            {
                account.UserAgent = Constants.DEFAULT_DISCORD_USER_AGENT;
            }

            WebProxy webProxy = null;
            if (!string.IsNullOrEmpty(_properties.Proxy?.Host))
            {
                webProxy = new WebProxy(_properties.Proxy.Host, _properties.Proxy.Port ?? 80);
            }

            var hch = new HttpClientHandler
            {
                UseProxy = webProxy != null,
                Proxy = webProxy
            };

            var client = new HttpClient(hch)
            {
                Timeout = TimeSpan.FromMinutes(10),
            };

            var request = new HttpRequestMessage(HttpMethod.Post, DiscordHelper.ME_CHANNELS_URL);
            request.Headers.Add("Authorization", account.UserToken);
            request.Headers.Add("User-Agent", account.UserAgent);

            var obj = new
            {
                recipients = new string[] { botType == EBotType.MID_JOURNEY ? Constants.MJ_APPLICATION_ID : Constants.NIJI_APPLICATION_ID }
            };
            var objStr = JsonSerializer.Serialize(obj);
            var content = new StringContent(objStr, null, "application/json");
            request.Content = content;

            var response = await client.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var data = JsonDocument.Parse(json).RootElement;
                if (data.TryGetProperty("id", out var id))
                {
                    return id.GetString();
                }
            }

            throw new Exception($"获取私信 ID 失败 {response?.StatusCode}, {response?.Content}");
        }
    }
}
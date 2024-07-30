using Microsoft.Extensions.Options;
using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.Handle;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using System.Net;
using System.Reflection;

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
        private readonly IEnumerable<BotMessageHandler> _botMessageHandlers;
        private readonly IEnumerable<UserMessageHandler> _userMessageHandlers;
        private readonly Dictionary<string, string> _paramsMap;
        private readonly INotifyService _notifyService;

        public DiscordAccountHelper(
            DiscordHelper discordHelper,
            IOptionsMonitor<ProxyProperties> options,
            ITaskStoreService taskStoreService,
            IEnumerable<BotMessageHandler> messageHandlers,
            INotifyService notifyService,
            IEnumerable<UserMessageHandler> userMessageHandlers)
        {
            _discordHelper = discordHelper;
            _properties = options.CurrentValue;
            _taskStoreService = taskStoreService;
            _notifyService = notifyService;
            _botMessageHandlers = messageHandlers;
            _userMessageHandlers = userMessageHandlers;

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
        }

        /// <summary>
        /// 创建Discord实例。
        /// </summary>
        /// <param name="account">Discord账号信息。</param>
        /// <returns>Discord实例。</returns>
        /// <exception cref="ArgumentException">当guildId, channelId或userToken为空时抛出。</exception>
        public async Task<IDiscordInstance> CreateDiscordInstance(DiscordAccount account)
        {
            if (string.IsNullOrWhiteSpace(account.GuildId) || string.IsNullOrWhiteSpace(account.ChannelId) || string.IsNullOrWhiteSpace(account.UserToken))
            {
                throw new ArgumentException("guildId, channelId, userToken must not be blank");
            }

            if (string.IsNullOrWhiteSpace(account.UserAgent))
            {
                account.UserAgent = Constants.DEFAULT_DISCORD_USER_AGENT;
            }

            var discordInstance = new DiscordInstance(account,
                _taskStoreService,
                _notifyService,
                _discordHelper,
                _paramsMap);

            if (account.Enable)
            {
                // Bot 消息监听器
                WebProxy webProxy = null;
                if (!string.IsNullOrEmpty(_properties.Proxy?.Host))
                {
                    webProxy = new WebProxy(_properties.Proxy.Host, _properties.Proxy.Port ?? 80);
                }
                var messageListener = new BotMessageListener(_discordHelper, _properties, webProxy);

                // 用户 WebSocket 连接
                var webSocket = new WebSocketManager(
                    _discordHelper,
                    messageListener,
                    webProxy,
                    discordInstance);

                await webSocket.StartAsync();

                messageListener.Init(discordInstance, _botMessageHandlers, _userMessageHandlers);

                await messageListener.StartAsync();

                // 跟踪 wss 连接
                discordInstance.BotMessageListener = messageListener;
                discordInstance.WebSocketManager = webSocket;
            }

            return discordInstance;
        }
    }
}
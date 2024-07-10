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
        private readonly HttpClient _httpClient;
        private readonly ITaskStoreService _taskStoreService;
        private readonly IEnumerable<MessageHandler> _messageHandlers;
        private readonly Dictionary<string, string> _paramsMap;
        private readonly INotifyService _notifyService;

        private BotMessageListener _messageListener;


        /// <summary>
        /// 初始化 DiscordAccountHelper 类的新实例。
        /// </summary>
        /// <param name="discordHelper">Discord辅助类实例。</param>
        /// <param name="options">ProxyProperties 配置选项。</param>
        /// <param name="httpClient">HttpClient 实例。</param>
        /// <param name="taskStoreService">任务存储服务实例。</param>
        /// <param name="notifyService">通知服务实例。</param>
        /// <param name="messageHandlers">消息处理程序列表。</param>
        /// <param name="paramsMap">参数映射。</param>
        public DiscordAccountHelper(
            DiscordHelper discordHelper,
            IOptionsMonitor<ProxyProperties> options,
            HttpClient httpClient,
            ITaskStoreService taskStoreService,
            IEnumerable<MessageHandler> messageHandlers,
            INotifyService notifyService)
        {
            _discordHelper = discordHelper;
            _properties = options.CurrentValue;
            _httpClient = httpClient;
            _taskStoreService = taskStoreService;
            _notifyService = notifyService;
            _messageHandlers = messageHandlers;

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
            if (string.IsNullOrWhiteSpace(account.GuildId) ||
                string.IsNullOrWhiteSpace(account.ChannelId) ||
                string.IsNullOrWhiteSpace(account.UserToken))
            {
                throw new ArgumentException("guildId, channelId, userToken must not be blank");
            }

            if (string.IsNullOrWhiteSpace(account.UserAgent))
            {
                account.UserAgent = Constants.DEFAULT_DISCORD_USER_AGENT;
            }

            var discordService = new DiscordServiceImpl(account, _httpClient, _discordHelper, _paramsMap);
            var discordInstance = new DiscordInstanceImpl(account, discordService, _taskStoreService, _notifyService);

            // Bot 消息监听器
            WebProxy webProxy = null;
            if (!string.IsNullOrEmpty(_properties.Proxy?.Host))
            {
                webProxy = new WebProxy(_properties.Proxy.Host, _properties.Proxy.Port ?? 80);
            }
            _messageListener = new BotMessageListener(account.BotToken, account, webProxy);

            // 用户 WebSocket 连接
            var webSocket = new WebSocketStarter(account, _discordHelper, _messageListener,
                webProxy, discordService);

            await webSocket.StartAsync();

            _messageListener.Init(discordInstance, _messageHandlers);
            await _messageListener.StartAsync();

            return discordInstance;
        }
    }
}
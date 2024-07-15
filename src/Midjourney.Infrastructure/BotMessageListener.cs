using Discord;
using Discord.Commands;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using Discord.WebSocket;
using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.Handle;
using Midjourney.Infrastructure.LoadBalancer;
using Serilog;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// 机器人消息监听器。
    /// </summary>
    public class BotMessageListener : IDisposable
    {
        private readonly WebProxy _webProxy;
        private readonly DiscordAccount _discordAccount;
        private readonly DiscordHelper _discordHelper;
        private readonly ILogger _logger = Log.Logger;

        private DiscordInstanceImpl _discordInstance;
        private IEnumerable<MessageHandler> _messageHandlers;

        public BotMessageListener(
            DiscordAccount discordAccount,
            DiscordHelper discordHelper,
            WebProxy webProxy = null)
        {
            _discordAccount = discordAccount;
            _webProxy = webProxy;
            _discordHelper = discordHelper;
        }

        public void Init(DiscordInstanceImpl instance, IEnumerable<MessageHandler> messageHandlers)
        {
            _discordInstance = instance;
            _messageHandlers = messageHandlers;
        }

        public async Task StartAsync()
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                //// How much logging do you want to see?
                LogLevel = LogSeverity.Info,

                // If you or another service needs to do anything with messages
                // (eg. checking Reactions, checking the content of edited/deleted messages),
                // you must set the MessageCacheSize. You may adjust the number as needed.
                //MessageCacheSize = 50,

                RestClientProvider = DefaultRestClientProvider.Create(true),
                WebSocketProvider = DefaultWebSocketProvider.Create(_webProxy),

                // 读取消息权限 GatewayIntents.MessageContent
                // GatewayIntents.AllUnprivileged & ~(GatewayIntents.GuildScheduledEvents | GatewayIntents.GuildInvites) | GatewayIntents.MessageContent
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
            });

            _commands = new CommandService(new CommandServiceConfig
            {
                // Again, log level:
                LogLevel = LogSeverity.Info,

                // There's a few more properties you can set,
                // for example, case-insensitive commands.
                CaseSensitiveCommands = false,
            });

            // Subscribe the logging handler to both the client and the CommandService.
            _client.Log += LogAction;
            _commands.Log += LogAction;

            await _client.LoginAsync(TokenType.Bot, _discordAccount.BotToken);
            await _client.StartAsync();

            // Centralize the logic for commands into a separate method.
            // Subscribe a handler to see if a message invokes a command.
            _client.MessageReceived += HandleCommandAsync;
            _client.MessageUpdated += MessageUpdatedAsync;
        }

        private DiscordSocketClient _client;

        // Keep the CommandService and DI container around for use with commands.
        // These two types require you install the Discord.Net.Commands package.
        private CommandService _commands;

        // Example of a logging handler. This can be re-used by addons
        // that ask for a Func<LogMessage, Task>.
        private Task LogAction(LogMessage message)
        {
            switch (message.Severity)
            {
                case LogSeverity.Critical:
                case LogSeverity.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;

                case LogSeverity.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;

                case LogSeverity.Info:
                    Console.ForegroundColor = ConsoleColor.White;
                    break;

                case LogSeverity.Verbose:
                case LogSeverity.Debug:
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    break;
            }

            Log.Information($"{DateTime.Now,-19} [{message.Severity,8}] {message.Source}: {message.Message} {message.Exception}");

            return Task.CompletedTask;
        }

        /// <summary>
        /// 处理接收到的消息
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        private async Task HandleCommandAsync(SocketMessage arg)
        {
            try
            {
                var msg = arg as SocketUserMessage;
                if (msg == null)
                    return;

                _logger.Debug($"BOT Received, {msg.Type}, id: {msg.Id}, rid: {msg.Reference?.MessageId.Value}, {msg.Content}");

                if (!string.IsNullOrWhiteSpace(msg.Content)
                    && msg.Author.IsBot)
                {
                    foreach (var handler in _messageHandlers.OrderBy(h => h.Order()))
                    {
                        handler.Handle(_discordInstance, MessageType.CREATE, msg);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "处理 bot 消息异常");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 处理更新消息
        /// </summary>
        /// <param name="before"></param>
        /// <param name="after"></param>
        /// <param name="channel"></param>
        /// <returns></returns>
        private async Task MessageUpdatedAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
        {
            try
            {
                var msg = after as IUserMessage;
                if (msg == null)
                    return;

                _logger.Debug($"BOT Updated, {msg.Type}, id: {msg.Id}, rid: {msg.Reference?.MessageId.Value}, {msg.Content}");

                if (!string.IsNullOrWhiteSpace(msg.Content)
                    && msg.Content.Contains("%")
                    && msg.Author.IsBot)
                {
                    foreach (var handler in _messageHandlers.OrderBy(h => h.Order()))
                    {
                        handler.Handle(_discordInstance, MessageType.UPDATE, after);
                    }
                }
                else if (msg.InteractionMetadata is ApplicationCommandInteractionMetadata metadata && metadata.Name == "describe")
                {
                    var handler = _messageHandlers.FirstOrDefault(x => x.GetType() == typeof(DescribeSuccessHandler));
                    handler?.Handle(_discordInstance, MessageType.CREATE, after);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "处理 bot 更新消息异常");
            }
        }


        /// <summary>
        /// 处理接收到用户 ws 消息
        /// </summary>
        /// <param name="raw"></param>
        public void OnMessage(JsonElement raw)
        {
            if (!raw.TryGetProperty("t", out JsonElement messageTypeElement))
            {
                return;
            }

            var messageType = MessageTypeExtensions.Of(messageTypeElement.GetString());
            if (messageType == null || messageType == MessageType.DELETE)
            {
                return;
            }

            if (!raw.TryGetProperty("d", out JsonElement data))
            {
                return;
            }

            // 内容
            var contentStr = string.Empty;
            if (data.TryGetProperty("content", out JsonElement content))
            {
                contentStr = content.GetString();
            }

            // 私信频道
            var isPrivareChannel = false;
            if (data.TryGetProperty("channel_id", out JsonElement channelIdElement))
            {
                if (channelIdElement.GetString() == _discordAccount.PrivateChannelId
                    || channelIdElement.GetString() == _discordAccount.NijiBotChannelId)
                {
                    isPrivareChannel = true;
                }

                if (channelIdElement.GetString() == _discordAccount.ChannelId)
                {
                    isPrivareChannel = false;
                }

                // 都不相同
                // 如果有渠道 id，但不是当前渠道 id，则忽略
                if (channelIdElement.GetString() != _discordAccount.ChannelId
                    && channelIdElement.GetString() != _discordAccount.PrivateChannelId
                    && channelIdElement.GetString() != _discordAccount.NijiBotChannelId)
                {
                    return;
                }
            }


            if (isPrivareChannel)
            {
                // 私信频道
                if (messageType == MessageType.CREATE && data.TryGetProperty("id", out JsonElement subIdElement))
                {
                    var id = subIdElement.GetString();

                    // 定义正则表达式模式
                    // "**girl**\n**Job ID**: 6243686b-7ab1-4174-a9fe-527cca66a829\n**seed** 1259687673"
                    var pattern = @"\*\*Job ID\*\*:\s*(?<jobId>[a-fA-F0-9-]{36})\s*\*\*seed\*\*\s*(?<seed>\d+)";

                    // 创建正则表达式对象
                    var regex = new Regex(pattern);

                    // 尝试匹配输入字符串
                    var match = regex.Match(contentStr);

                    if (match.Success)
                    {
                        // 提取 Job ID 和 seed
                        var jobId = match.Groups["jobId"].Value;
                        var seed = match.Groups["seed"].Value;

                        if (!string.IsNullOrWhiteSpace(jobId) && !string.IsNullOrWhiteSpace(seed))
                        {
                            var task = _discordInstance.FindRunningTask(c => c.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_HASH, default) == jobId).FirstOrDefault();
                            if (task != null)
                            {
                                if (!task.MessageIds.Contains(id))
                                {
                                    task.MessageIds.Add(id);
                                }

                                task.Seed = seed;
                            }
                        }
                    }
                    else
                    {
                        // 获取附件对象 attachments 中的第一个对象的 url 属性
                        if (data.TryGetProperty("attachments", out JsonElement attachments) && attachments.ValueKind == JsonValueKind.Array)
                        {
                            var item = attachments.EnumerateArray().FirstOrDefault();
                            if (item.TryGetProperty("url", out JsonElement url))
                            {
                                var imgUrl = url.GetString();
                                if (!string.IsNullOrWhiteSpace(imgUrl))
                                {
                                    var hash = _discordHelper.GetMessageHash(imgUrl);
                                    if (!string.IsNullOrWhiteSpace(hash))
                                    {
                                        var task = _discordInstance.FindRunningTask(c => c.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_HASH, default) == hash).FirstOrDefault();
                                        if (task != null)
                                        {
                                            if (!task.MessageIds.Contains(id))
                                            {
                                                task.MessageIds.Add(id);
                                            }
                                            task.SeedMessageId = id;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                return;
            }


            // 作者
            var authorName = string.Empty;
            if (data.TryGetProperty("author", out JsonElement author)
                && author.TryGetProperty("username", out JsonElement username))
            {
                authorName = username.GetString();
            }

            // 应用 ID 即机器人 ID
            var applicationId = string.Empty;
            if (data.TryGetProperty("application_id", out JsonElement application))
            {
                applicationId = application.GetString();
            }

            // 交互元数据 id
            var metaId = string.Empty;
            var metaName = string.Empty;
            if (data.TryGetProperty("interaction_metadata", out JsonElement meta) && meta.TryGetProperty("id", out var m))
            {
                metaId = m.GetString();

                metaName = meta.TryGetProperty("name", out var n) ? n.GetString() : string.Empty;
            }

            // 任务 id
            // 任务 nonce
            if (data.TryGetProperty("id", out JsonElement idElement))
            {
                var id = idElement.GetString();
                _logger.Debug($"用户消息, {messageType}, {_discordAccount.GetDisplay()} - {authorName}: {contentStr}, id: {id}, mid: {metaId}");

                // 判断账号是否用量已经用完

                var isEm = data.TryGetProperty("embeds", out var em);

                if (messageType == MessageType.CREATE && isEm)
                {
                    // em 是一个 JSON 数组
                    if (em.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement item in em.EnumerateArray())
                        {
                            if (item.TryGetProperty("title", out var emtitle))
                            {
                                if (emtitle.GetString() == "Credits exhausted")
                                {
                                    // 你的处理逻辑
                                    _logger.Warning($"账号 {_discordAccount.GetDisplay()} 用量已经用完");
                                    _discordAccount.Enable = false;

                                    var task = _discordInstance.FindRunningTask(c => c.MessageId == id).FirstOrDefault();
                                    if (task == null && !string.IsNullOrWhiteSpace(metaId))
                                    {
                                        task = _discordInstance.FindRunningTask(c => c.InteractionMetadataId == metaId).FirstOrDefault();
                                    }

                                    if (task != null)
                                    {
                                        task.Fail("账号用量已经用完");
                                    }

                                    return;
                                }
                                else if (emtitle.GetString() == "Invalid parameter")
                                {
                                    if (data.TryGetProperty("nonce", out JsonElement noneEle))
                                    {
                                        var nonce = noneEle.GetString();
                                        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(nonce))
                                        {
                                            // 设置 none 对应的任务 id
                                            var task = _discordInstance.GetRunningTaskByNonce(nonce);
                                            if (task != null)
                                            {
                                                if (messageType == MessageType.CREATE)
                                                {
                                                    task.MessageId = id;
                                                    task.Description = item.GetProperty("description").GetString();

                                                    if (!task.MessageIds.Contains(id))
                                                    {
                                                        task.MessageIds.Add(id);
                                                    }

                                                    task.Fail($"无效参数");
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else if (messageType == MessageType.UPDATE && isEm)
                {
                    if (metaName == "info")
                    {
                        // info 指令
                        if (em.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement item in em.EnumerateArray())
                            {
                                if (item.TryGetProperty("title", out var emtitle) && emtitle.GetString().Contains("Your info"))
                                {
                                    if (item.TryGetProperty("description", out var description))
                                    {
                                        var dic = ParseDiscordData(description.GetString());
                                        foreach (var d in dic)
                                        {
                                            if (d.Key == "Job Mode")
                                            {
                                                if (applicationId == Constants.NIJI_APPLICATION_ID)
                                                {
                                                    _discordAccount.SetProperty($"Niji {d.Key}", d.Value);
                                                }
                                                else if (applicationId == Constants.MJ_APPLICATION_ID)
                                                {
                                                    _discordAccount.SetProperty(d.Key, d.Value);
                                                }
                                            }
                                            else
                                            {
                                                _discordAccount.SetProperty(d.Key, d.Value);
                                            }
                                        }

                                        var db = DbHelper.AccountStore;
                                        db.Update(_discordAccount);
                                    }
                                }
                            }
                        }

                        return;
                    }
                    else if (metaName == "settings" && data.TryGetProperty("components", out var components))
                    {
                        // settings 指令
                        var eventData = data.Deserialize<EventData>();
                        if (eventData != null && eventData.InteractionMetadata?.Name == "settings" && eventData.Components?.Count > 0)
                        {
                            if (applicationId == Constants.NIJI_APPLICATION_ID)
                            {
                                _discordAccount.NijiComponents = eventData.Components;
                                _discordAccount.NijiSettingsMessageId = id;

                                DbHelper.AccountStore.Update(_discordAccount);
                            }
                            else if (applicationId == Constants.MJ_APPLICATION_ID)
                            {
                                _discordAccount.Components = eventData.Components;
                                _discordAccount.SettingsMessageId = id;

                                DbHelper.AccountStore.Update(_discordAccount);
                            }
                        }

                        return;
                    }
                }

                if (data.TryGetProperty("nonce", out JsonElement noneElement))
                {
                    var nonce = noneElement.GetString();

                    _logger.Debug($"用户消息, {messageType}, id: {id}, nonce: {nonce}");

                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(nonce))
                    {
                        // 设置 none 对应的任务 id
                        var task = _discordInstance.GetRunningTaskByNonce(nonce);
                        if (task != null)
                        {
                            if (isPrivareChannel)
                            {
                                // 私信频道

                            }
                            else
                            {
                                // 绘画频道

                                // MJ 交互成功后
                                if (messageType == MessageType.INTERACTION_SUCCESS)
                                {
                                    task.InteractionMetadataId = id;
                                }
                                // MJ 局部重绘完成后
                                else if (messageType == MessageType.INTERACTION_IFRAME_MODAL_CREATE
                                    && data.TryGetProperty("custom_id", out var custom_id))
                                {
                                    task.SetProperty(Constants.TASK_PROPERTY_IFRAME_MODAL_CREATE_CUSTOM_ID, custom_id.GetString());
                                    task.MessageId = id;

                                    if (!task.MessageIds.Contains(id))
                                    {
                                        task.MessageIds.Add(id);
                                    }
                                }
                                else
                                {
                                    task.MessageId = id;

                                    if (!task.MessageIds.Contains(id))
                                    {
                                        task.MessageIds.Add(id);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            //Thread.Sleep(50);

            //foreach (var messageHandler in _messageHandlers.OrderBy(h => h.Order()))
            //{
            //    if (data.TryGetProperty(Constants.MJ_MESSAGE_HANDLED, out JsonElement handled) && handled.GetBoolean())
            //    {
            //        return;
            //    }

            //    //messageHandler.Handle(_discordInstance, messageType.Value, data);
            //}
        }

        private static Dictionary<string, string> ParseDiscordData(string input)
        {
            var data = new Dictionary<string, string>();

            foreach (var line in input.Split('\n'))
            {
                var parts = line.Split(new[] { ':' }, 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Replace("**", "").Trim();
                    var value = parts[1].Trim();
                    data[key] = value;
                }
            }

            return data;
        }

        public void Dispose()
        {
            // Unsubscribe from events
            if (_client != null)
            {
                _client.Log -= LogAction;
                _client.MessageReceived -= HandleCommandAsync;
                _client.MessageUpdated -= MessageUpdatedAsync;

                // Dispose the Discord client
                _client.Dispose();
            }

            // Dispose the command service
            if (_commands != null)
            {
                _commands.Log -= LogAction;
            }
        }
    }
}
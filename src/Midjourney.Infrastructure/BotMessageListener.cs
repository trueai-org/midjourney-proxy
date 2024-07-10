using Discord;
using Discord.Commands;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using Discord.WebSocket;
using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.Handle;
using Midjourney.Infrastructure.LoadBalancer;
using Serilog;
using System.Net;
using System.Text.Json;

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// 机器人消息监听器。
    /// </summary>
    public class BotMessageListener
    {
        private readonly string _token;
        private readonly WebProxy _webProxy;
        private readonly DiscordAccount _discordAccount;
        private readonly ILogger _logger = Log.Logger;

        private DiscordInstanceImpl _discordInstance;
        private IEnumerable<MessageHandler> _messageHandlers;

        public BotMessageListener(string token, DiscordAccount discordAccount, WebProxy webProxy = null)
        {
            _token = token;
            _discordAccount = discordAccount;
            _webProxy = webProxy;
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

            await _client.LoginAsync(TokenType.Bot, _token);
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

        //private async Task HandleInteractionAsync(SocketInteraction arg)
        //{
        //    try
        //    {
        //        var interaction = arg as SocketMessageComponent;
        //        if (interaction == null)
        //            return;

        //        _logger.Debug($"Interaction received, id: {interaction.Data.CustomId}, nonce: {interaction.Id}");

        //        // 处理自定义指令
        //        if (interaction.Data.CustomId.StartsWith("MJ::BOOKMARK::"))
        //        {
        //            // 自定义处理逻辑
        //            //await interaction.RespondAsync($"Bookmark received: {interaction.Data.CustomId}");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error(ex, "处理交互事件异常");
        //    }
        //}

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

                _logger.Debug($"BOT Received, id: {msg.Id}, rid: {msg.Reference?.MessageId.Value}, {msg.Content}");

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

                _logger.Debug($"BOT Updated, id: {msg.Id}, rid: {msg.Reference?.MessageId.Value}, {msg.Content}");

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
        /// <param name="msg"></param>
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

            // 如果有渠道 id，但不是当前渠道 id，则忽略
            if (data.TryGetProperty("channel_id", out JsonElement channelIdElement) && channelIdElement.GetString() != _discordAccount.ChannelId)
            {
                return;
            }

            // 作者
            var authorName = string.Empty;
            if (data.TryGetProperty("author", out JsonElement author) && author.TryGetProperty("username", out JsonElement username))
            {
                authorName = username.GetString();
            }

            // 内容
            var contentStr = string.Empty;
            if (data.TryGetProperty("content", out JsonElement content))
            {
                contentStr = content.GetString();
            }

            // 交互元数据 id
            var metaId = string.Empty;
            if (data.TryGetProperty("interaction_metadata", out JsonElement meta) && meta.TryGetProperty("id", out var m))
            {
                metaId = m.GetString();
            }

            // 任务 id
            // 任务 nonce
            if (data.TryGetProperty("id", out JsonElement idElement))
            {
                var id = idElement.GetString();
                _logger.Debug($"用户消息, {messageType}, {_discordAccount.GetDisplay()} - {authorName}: {contentStr}, id: {id}, mid: {metaId}");

                // 判断账号是否用量已经用完
                if (messageType == MessageType.CREATE && data.TryGetProperty("embeds", out var em))
                {
                    // em 是一个 JSON 数组
                    if (em.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement item in em.EnumerateArray())
                        {
                            if (item.TryGetProperty("title", out var emtitle) && emtitle.GetString() == "Credits exhausted")
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
                        }
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
    }
}
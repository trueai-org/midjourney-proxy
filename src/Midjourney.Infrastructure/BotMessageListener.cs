using Discord;
using Discord.Commands;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using Discord.WebSocket;
using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.Handle;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using Serilog;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

using EventData = Midjourney.Infrastructure.Dto.EventData;

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
        private IEnumerable<BotMessageHandler> _botMessageHandlers;
        private IEnumerable<UserMessageHandler> _userMessageHandlers;

        public BotMessageListener(
            DiscordAccount discordAccount,
            DiscordHelper discordHelper,
            WebProxy webProxy = null)
        {
            _discordAccount = discordAccount;
            _webProxy = webProxy;
            _discordHelper = discordHelper;
        }

        public void Init(
            DiscordInstanceImpl instance,
            IEnumerable<BotMessageHandler> botMessageHandlers,
            IEnumerable<UserMessageHandler> userMessageHandlers)
        {
            _discordInstance = instance;
            _botMessageHandlers = botMessageHandlers;
            _userMessageHandlers = userMessageHandlers;
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
                GatewayIntents = GatewayIntents.AllUnprivileged & ~(GatewayIntents.GuildScheduledEvents | GatewayIntents.GuildInvites) | GatewayIntents.MessageContent
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

                _logger.Information($"BOT Received, {msg.Type}, id: {msg.Id}, rid: {msg.Reference?.MessageId.Value}, mid: {msg?.InteractionMetadata?.Id}, {msg.Content}");

                if (!string.IsNullOrWhiteSpace(msg.Content) && msg.Author.IsBot)
                {
                    foreach (var handler in _botMessageHandlers.OrderBy(h => h.Order()))
                    {
                        // 消息加锁处理
                        LocalLock.TryLock($"lock_{msg.Id}", TimeSpan.FromSeconds(10), () =>
                        {
                            handler.Handle(_discordInstance, MessageType.CREATE, msg);
                        });
                    }
                }
                // describe 重新提交
                // MJ::Picread::Retry
                else if (msg.Embeds.Count > 0 && msg.Author.IsBot && msg.Components.Count > 0 && msg.Components.First().Components.Any(x => x.CustomId.Contains("PicReader")))
                {
                    var em = msg.Embeds.FirstOrDefault();
                    if (em != null && !string.IsNullOrWhiteSpace(em.Description))
                    {
                        var handler = _botMessageHandlers.FirstOrDefault(x => x.GetType() == typeof(BotDescribeSuccessHandler));
                        handler?.Handle(_discordInstance, MessageType.CREATE, msg);
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

                _logger.Information($"BOT Updated, {msg.Type}, id: {msg.Id}, rid: {msg.Reference?.MessageId.Value}, {msg.Content}");

                if (!string.IsNullOrWhiteSpace(msg.Content)
                    && msg.Content.Contains("%")
                    && msg.Author.IsBot)
                {
                    foreach (var handler in _botMessageHandlers.OrderBy(h => h.Order()))
                    {
                        handler.Handle(_discordInstance, MessageType.UPDATE, after);
                    }
                }
                else if (msg.InteractionMetadata is ApplicationCommandInteractionMetadata metadata && metadata.Name == "describe")
                {
                    var handler = _botMessageHandlers.FirstOrDefault(x => x.GetType() == typeof(BotDescribeSuccessHandler));
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
            try
            {
                _logger.Debug("用户收到消息 {@0}", raw.ToString());

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

                // 作者
                var authorName = string.Empty;
                var authId = string.Empty;
                if (data.TryGetProperty("author", out JsonElement author)
                    && author.TryGetProperty("username", out JsonElement username)
                    && author.TryGetProperty("id", out JsonElement uid))
                {
                    authorName = username.GetString();
                    authId = uid.GetString();
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

                // 处理 remix 开关
                if (metaName == "prefer remix" && !string.IsNullOrWhiteSpace(contentStr))
                {
                    // MJ
                    if (authId == Constants.MJ_APPLICATION_ID)
                    {
                        if (contentStr.StartsWith("Remix mode turned off"))
                        {
                            foreach (var item in _discordAccount.Components)
                            {
                                foreach (var sub in item.Components)
                                {
                                    if (sub.Label == "Remix mode")
                                    {
                                        sub.Style = 2;
                                    }
                                }
                            }
                        }
                        else if (contentStr.StartsWith("Remix mode turned on"))
                        {
                            foreach (var item in _discordAccount.Components)
                            {
                                foreach (var sub in item.Components)
                                {
                                    if (sub.Label == "Remix mode")
                                    {
                                        sub.Style = 3;
                                    }
                                }
                            }
                        }
                    }
                    // NIJI
                    else if (authId == Constants.NIJI_APPLICATION_ID)
                    {
                        if (contentStr.StartsWith("Remix mode turned off"))
                        {
                            foreach (var item in _discordAccount.NijiComponents)
                            {
                                foreach (var sub in item.Components)
                                {
                                    if (sub.Label == "Remix mode")
                                    {
                                        sub.Style = 2;
                                    }
                                }
                            }
                        }
                        else if (contentStr.StartsWith("Remix mode turned on"))
                        {
                            foreach (var item in _discordAccount.NijiComponents)
                            {
                                foreach (var sub in item.Components)
                                {
                                    if (sub.Label == "Remix mode")
                                    {
                                        sub.Style = 3;
                                    }
                                }
                            }
                        }
                    }

                    DbHelper.AccountStore.Save(_discordAccount);

                    return;
                }
                // 同步 settings 和 remix
                else if (metaName == "settings")
                {
                    // settings 指令
                    var eventData = data.Deserialize<EventData>();
                    if (eventData != null && eventData.InteractionMetadata?.Name == "settings" && eventData.Components?.Count > 0)
                    {
                        if (applicationId == Constants.NIJI_APPLICATION_ID)
                        {
                            _discordAccount.NijiComponents = eventData.Components;
                            DbHelper.AccountStore.Update(_discordAccount);
                        }
                        else if (applicationId == Constants.MJ_APPLICATION_ID)
                        {
                            _discordAccount.Components = eventData.Components;
                            DbHelper.AccountStore.Update(_discordAccount);
                        }
                    }
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
                            // seed 消息处理
                            if (data.TryGetProperty("attachments", out JsonElement attachments) && attachments.ValueKind == JsonValueKind.Array)
                            {
                                if (attachments.EnumerateArray().Count() > 0)
                                {
                                    var item = attachments.EnumerateArray().First();

                                    if (item.ValueKind != JsonValueKind.Null
                                        && item.TryGetProperty("url", out JsonElement url)
                                        && url.ValueKind != JsonValueKind.Null)
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
                    }

                    return;
                }

                // 任务 id
                // 任务 nonce
                if (data.TryGetProperty("id", out JsonElement idElement))
                {
                    var id = idElement.GetString();

                    _logger.Information($"用户消息, {messageType}, {_discordAccount.GetDisplay()} - id: {id}, mid: {metaId}, {authorName}, content: {contentStr}");

                    var isEm = data.TryGetProperty("embeds", out var em);
                    if (messageType == MessageType.CREATE && isEm)
                    {
                        // em 是一个 JSON 数组
                        if (em.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement item in em.EnumerateArray())
                            {
                                if (item.TryGetProperty("title", out var emTitle))
                                {
                                    // 判断账号是否用量已经用完
                                    var title = emTitle.GetString();

                                    // 16711680 error, 65280 success, 16776960 warning
                                    var color = item.TryGetProperty("color", out var colorEle) ? colorEle.GetInt32() : 0;

                                    // 无效参数、违规的提示词、无效提示词
                                    var errorTitles = new[] {
                                        "Invalid prompt", // 无效提示词
                                        "Invalid parameter", // 无效参数
                                        "Banned prompt detected", // 违规提示词
                                        "Invalid link", // 无效链接
                                        "Request cancelled due to output filters"
                                    };

                                    // 跳过的 title
                                    var continueTitles = new[] {
                                        "Job queued", // 执行中的队列已满
                                        "Credits exhausted", // 余额不足
                                        "Action needed to continue",
                                        "Pending mod message", // 警告
                                        "Blocked", // 警告
                                        "Plan Cancelled", // 取消计划
                                        "Subscription required" // 订阅过期
                                    };

                                    if (!continueTitles.Contains(title) && (errorTitles.Contains(title) || color == 16711680 || title.Contains("Invalid")))
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
                                                        task.Description = $"{title}, {item.GetProperty("description").GetString()}";

                                                        if (!task.MessageIds.Contains(id))
                                                        {
                                                            task.MessageIds.Add(id);
                                                        }

                                                        task.Fail(title);
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // 如果没有获取到 none
                                            _logger.Error("未知错误 {@0}, {@1}", _discordAccount.ChannelId, data.ToString());


                                            // 如果 meta 是 show
                                            // 说明是 show 任务出错了
                                            if (metaName == "show")
                                            {
                                                var desc = item.GetProperty("description").GetString();
                                                if (!string.IsNullOrWhiteSpace(desc))
                                                {
                                                    // 设置 none 对应的任务 id
                                                    var task = _discordInstance.GetRunningTasks().Where(c => c.Action == TaskAction.SHOW && desc.Contains(c.JobId)).FirstOrDefault();
                                                    if (task != null)
                                                    {
                                                        if (messageType == MessageType.CREATE)
                                                        {
                                                            task.MessageId = id;
                                                            task.Description = $"{title}, {item.GetProperty("description").GetString()}";

                                                            if (!task.MessageIds.Contains(id))
                                                            {
                                                                task.MessageIds.Add(id);
                                                            }

                                                            task.Fail(title);
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                    }
                                    // fast 用量已经使用完了
                                    // TODO 可以改为慢速模式
                                    else if (title == "Credits exhausted")
                                    {
                                        // 你的处理逻辑
                                        _logger.Warning($"账号 {_discordAccount.GetDisplay()} 用量已经用完, 自动禁用账号");

                                        var task = _discordInstance.FindRunningTask(c => c.MessageId == id).FirstOrDefault();
                                        if (task == null && !string.IsNullOrWhiteSpace(metaId))
                                        {
                                            task = _discordInstance.FindRunningTask(c => c.InteractionMetadataId == metaId).FirstOrDefault();
                                        }

                                        if (task != null)
                                        {
                                            task.Fail("账号用量已经用完");
                                        }

                                        // 5s 后禁用账号
                                        Task.Run(() =>
                                        {
                                            try
                                            {
                                                Thread.Sleep(5 * 1000);

                                                // 保存
                                                _discordAccount.Enable = false;
                                                _discordAccount.DisabledReason = "账号用量已经用完";

                                                DbHelper.AccountStore.Save(_discordAccount);

                                                _discordInstance?.Dispose();
                                            }
                                            catch (Exception ex)
                                            {
                                                Log.Error(ex, "账号用量已经用完, 禁用账号异常 {@0}", _discordAccount.ChannelId);
                                            }
                                        });

                                        return;
                                    }
                                    // 临时禁止/订阅取消/订阅过期
                                    else if (title == "Pending mod message"
                                        || title == "Blocked"
                                        || title == "Plan Cancelled"
                                        || title == "Subscription required")
                                    {
                                        // 你的处理逻辑
                                        _logger.Warning($"账号 {_discordAccount.GetDisplay()} {title}, 自动禁用账号");

                                        var task = _discordInstance.FindRunningTask(c => c.MessageId == id).FirstOrDefault();
                                        if (task == null && !string.IsNullOrWhiteSpace(metaId))
                                        {
                                            task = _discordInstance.FindRunningTask(c => c.InteractionMetadataId == metaId).FirstOrDefault();
                                        }

                                        if (task != null)
                                        {
                                            task.Fail(title);
                                        }

                                        // 5s 后禁用账号
                                        Task.Run(() =>
                                        {
                                            try
                                            {
                                                Thread.Sleep(5 * 1000);

                                                // 保存
                                                _discordAccount.Enable = false;
                                                _discordAccount.DisabledReason = title;

                                                DbHelper.AccountStore.Save(_discordAccount);

                                                _discordInstance?.Dispose();
                                            }
                                            catch (Exception ex)
                                            {
                                                Log.Error(ex, "{@0}, 禁用账号异常 {@1}", title, _discordAccount.ChannelId);
                                            }
                                        });

                                        return;
                                    }
                                    // 执行中的任务已满（一般超过 3 个时）
                                    else if (title == "Job queued")
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
                                                        task.Description = $"{emTitle.GetString()}, {item.GetProperty("description").GetString()}";

                                                        if (!task.MessageIds.Contains(id))
                                                        {
                                                            task.MessageIds.Add(id);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    // 未知消息
                                    else
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
                                                        task.Description = $"{title}, {item.GetProperty("description").GetString()}";

                                                        if (!task.MessageIds.Contains(id))
                                                        {
                                                            task.MessageIds.Add(id);
                                                        }

                                                        _logger.Warning($"未知消息: {title}, {item.GetProperty("description").GetString()}, {_discordAccount.ChannelId}");
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

                // 如果消息类型是 CREATE
                // 则再次处理消息确认事件，确保消息的高可用
                if (messageType == MessageType.CREATE)
                {
                    Thread.Sleep(50);

                    var eventData = data.Deserialize<EventData>();
                    if (eventData != null && eventData.ChannelId == _discordAccount.ChannelId)
                    {
                        foreach (var messageHandler in _userMessageHandlers.OrderBy(h => h.Order()))
                        {
                            // 处理过了
                            if (eventData.GetProperty<bool?>(Constants.MJ_MESSAGE_HANDLED, default) == true)
                            {
                                return;
                            }

                            // 消息加锁处理
                            LocalLock.TryLock($"lock_{eventData.Id}", TimeSpan.FromSeconds(10), () =>
                            {
                                messageHandler.Handle(_discordInstance, messageType.Value, eventData);
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "处理用户消息异常 {@0}", raw.ToString());
            }
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
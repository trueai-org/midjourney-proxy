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

using Discord;
using Discord.Commands;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using Discord.WebSocket;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.Handle;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using RestSharp;
using Serilog;
using System.Diagnostics.Metrics;
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
        private readonly ILogger _logger = Log.Logger;

        private readonly WebProxy _webProxy;
        private readonly DiscordHelper _discordHelper;
        private readonly ProxyProperties _properties;

        private DiscordInstance _discordInstance;
        private IEnumerable<BotMessageHandler> _botMessageHandlers;
        private IEnumerable<UserMessageHandler> _userMessageHandlers;

        public BotMessageListener(DiscordHelper discordHelper, WebProxy webProxy = null)
        {
            _properties = GlobalConfiguration.Setting;
            _webProxy = webProxy;
            _discordHelper = discordHelper;
        }

        public void Init(
            DiscordInstance instance,
            IEnumerable<BotMessageHandler> botMessageHandlers,
            IEnumerable<UserMessageHandler> userMessageHandlers)
        {
            _discordInstance = instance;
            _botMessageHandlers = botMessageHandlers;
            _userMessageHandlers = userMessageHandlers;
        }

        private DiscordAccount Account => _discordInstance?.Account;

        public async Task StartAsync()
        {
            // 机器人 TOKEN 可选
            if (string.IsNullOrWhiteSpace(Account.BotToken))
            {
                return;
            }

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                //// How much logging do you want to see?
                LogLevel = LogSeverity.Info,

                // If you or another service needs to do anything with messages
                // (eg. checking Reactions, checking the content of edited/deleted messages),
                // you must set the MessageCacheSize. You may adjust the number as needed.
                //MessageCacheSize = 50,

                RestClientProvider = _webProxy != null ? CustomRestClientProvider.Create(_webProxy, true)
                : DefaultRestClientProvider.Create(true),
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

            await _client.LoginAsync(TokenType.Bot, Account.BotToken);
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
                else if (msg.Embeds.Count > 0 && msg.Author.IsBot && msg.Components.Count > 0
                    && msg.Components.First().Components.Any(x => x.CustomId?.Contains("PicReader") == true))
                {
                    // 消息加锁处理
                    LocalLock.TryLock($"lock_{msg.Id}", TimeSpan.FromSeconds(10), () =>
                    {
                        var em = msg.Embeds.FirstOrDefault();
                        if (em != null && !string.IsNullOrWhiteSpace(em.Description))
                        {
                            var handler = _botMessageHandlers.FirstOrDefault(x => x.GetType() == typeof(BotDescribeSuccessHandler));
                            handler?.Handle(_discordInstance, MessageType.CREATE, msg);
                        }
                    });
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

                // 触发 CF 真人验证
                if (messageType == MessageType.INTERACTION_IFRAME_MODAL_CREATE)
                {
                    if (data.TryGetProperty("title", out var t))
                    {
                        if (t.GetString() == "Action required to continue")
                        {
                            _logger.Warning("CF 验证 {@0}, {@1}", Account.ChannelId, raw.ToString());

                            // 全局锁定中
                            // 等待人工处理或者自动处理
                            // 重试最多 3 次，最多处理 5 分钟
                            LocalLock.TryLock($"cf_{Account.ChannelId}", TimeSpan.FromSeconds(10), () =>
                            {
                                try
                                {
                                    var custom_id = data.TryGetProperty("custom_id", out var c) ? c.GetString() : string.Empty;
                                    var application_id = data.TryGetProperty("application", out var a) && a.TryGetProperty("id", out var id) ? id.GetString() : string.Empty;
                                    if (!string.IsNullOrWhiteSpace(custom_id) && !string.IsNullOrWhiteSpace(application_id))
                                    {
                                        Account.Lock = true;

                                        // MJ::iframe::U3NmeM-lDTrmTCN_QY5n4DXvjrQRPGOZrQiLa-fT9y3siLA2AGjhj37IjzCqCtVzthUhGBj4KKqNSntQ
                                        var hash = custom_id.Split("::").LastOrDefault();
                                        var hashUrl = $"https://{application_id}.discordsays.com/captcha/api/c/{hash}/ack?hash=1";

                                        // 验证中，处于锁定模式
                                        Account.DisabledReason = "CF 自动验证中...";
                                        Account.CfHashUrl = hashUrl;
                                        Account.CfHashCreated = DateTime.Now;

                                        DbHelper.Instance.AccountStore.Update(Account);
                                        _discordInstance.ClearAccountCache(Account.Id);

                                        try
                                        {
                                            // 通知验证服务器
                                            if (!string.IsNullOrWhiteSpace(_properties.CaptchaNotifyHook) && !string.IsNullOrWhiteSpace(_properties.CaptchaServer))
                                            {
                                                // 使用 restsharp 通知，最多 3 次
                                                var notifyCount = 0;
                                                do
                                                {
                                                    if (notifyCount > 3)
                                                    {
                                                        break;
                                                    }

                                                    notifyCount++;
                                                    var notifyUrl = $"{_properties.CaptchaServer.Trim().TrimEnd('/')}/cf/verify";
                                                    var client = new RestClient();
                                                    var request = new RestRequest(notifyUrl, Method.Post);
                                                    request.AddHeader("Content-Type", "application/json");
                                                    var body = new CaptchaVerfyRequest
                                                    {
                                                        Url = hashUrl,
                                                        State = Account.ChannelId,
                                                        NotifyHook = _properties.CaptchaNotifyHook,
                                                        Secret = _properties.CaptchaNotifySecret
                                                    };
                                                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(body);
                                                    request.AddJsonBody(json);
                                                    var response = client.Execute(request);
                                                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                                                    {
                                                        // 已通知自动验证服务器
                                                        _logger.Information("CF 验证，已通知服务器 {@0}, {@1}", Account.ChannelId, hashUrl);

                                                        break;
                                                    }

                                                    Thread.Sleep(1000);
                                                } while (true);

                                                // 发送邮件
                                                EmailJob.Instance.EmailSend(_properties.Smtp, $"CF自动真人验证-{Account.ChannelId}", hashUrl);
                                            }
                                            else
                                            {
                                                // 发送 hashUrl GET 请求, 返回 {"hash":"OOUxejO94EQNxsCODRVPbg","token":"dXDm-gSb4Zlsx-PCkNVyhQ"}
                                                // 通过 hash 和 token 拼接验证 CF 验证 URL

                                                WebProxy webProxy = null;
                                                var proxy = GlobalConfiguration.Setting.Proxy;
                                                if (!string.IsNullOrEmpty(proxy?.Host))
                                                {
                                                    webProxy = new WebProxy(proxy.Host, proxy.Port ?? 80);
                                                }
                                                var hch = new HttpClientHandler
                                                {
                                                    UseProxy = webProxy != null,
                                                    Proxy = webProxy
                                                };

                                                var httpClient = new HttpClient(hch);
                                                var response = httpClient.GetAsync(hashUrl).Result;
                                                var con = response.Content.ReadAsStringAsync().Result;
                                                if (!string.IsNullOrWhiteSpace(con))
                                                {
                                                    // 解析
                                                    var json = JsonSerializer.Deserialize<JsonElement>(con);
                                                    if (json.TryGetProperty("hash", out var h) && json.TryGetProperty("token", out var to))
                                                    {
                                                        var hashStr = h.GetString();
                                                        var token = to.GetString();

                                                        if (!string.IsNullOrWhiteSpace(hashStr) && !string.IsNullOrWhiteSpace(token))
                                                        {
                                                            // 发送验证 URL
                                                            // 通过 hash 和 token 拼接验证 CF 验证 URL
                                                            // https://editor.midjourney.com/captcha/challenge/index.html?hash=OOUxejO94EQNxsCODRVPbg&token=dXDm-gSb4Zlsx-PCkNVyhQ

                                                            var url = $"https://editor.midjourney.com/captcha/challenge/index.html?hash={hashStr}&token={token}";

                                                            _logger.Information($"{Account.ChannelId}, CF 真人验证 URL: {url}");

                                                            Account.CfUrl = url;

                                                            // 发送邮件
                                                            EmailJob.Instance.EmailSend(_properties.Smtp, $"CF手动真人验证-{Account.ChannelId}", url);
                                                        }
                                                    }
                                                }

                                                Account.DisabledReason = "CF 人工验证...";

                                                DbHelper.Instance.AccountStore.Update(Account);
                                                _discordInstance.ClearAccountCache(Account.Id);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.Error(ex, "CF 真人验证处理失败 {@0}", Account.ChannelId);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.Error(ex, "CF 真人验证处理异常 {@0}", Account.ChannelId);
                                }
                            });

                            return;
                        }
                    }
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
                if (data.TryGetProperty("interaction_metadata", out JsonElement meta) && meta.TryGetProperty("id", out var mid))
                {
                    metaId = mid.GetString();

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
                            foreach (var item in Account.Components)
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
                            foreach (var item in Account.Components)
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
                            foreach (var item in Account.NijiComponents)
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
                            foreach (var item in Account.NijiComponents)
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

                    DbHelper.Instance.AccountStore.Update("Components,NijiComponents", Account);
                    _discordInstance.ClearAccountCache(Account.Id);

                    return;
                }
                // 同步 settings 和 remix
                else if (metaName == "settings")
                {
                    // settings 指令
                    var eventDataMsg = data.Deserialize<EventData>();
                    if (eventDataMsg != null && eventDataMsg.InteractionMetadata?.Name == "settings" && eventDataMsg.Components?.Count > 0)
                    {
                        if (applicationId == Constants.NIJI_APPLICATION_ID)
                        {
                            Account.NijiComponents = eventDataMsg.Components;
                            DbHelper.Instance.AccountStore.Update("NijiComponents", Account);
                            _discordInstance.ClearAccountCache(Account.Id);
                        }
                        else if (applicationId == Constants.MJ_APPLICATION_ID)
                        {
                            Account.Components = eventDataMsg.Components;
                            DbHelper.Instance.AccountStore.Update("Components", Account);
                            _discordInstance.ClearAccountCache(Account.Id);
                        }
                    }
                }
                // 切换 fast 和 relax
                else if (metaName == "fast" || metaName == "relax" || metaName == "turbo")
                {
                    // MJ
                    // Done! Your jobs now do not consume fast-hours, but might take a little longer. You can always switch back with /fast
                    if (metaName == "fast" && contentStr.StartsWith("Done!"))
                    {
                        foreach (var item in Account.Components)
                        {
                            foreach (var sub in item.Components)
                            {
                                if (sub.Label == "Fast mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Relax mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Turbo mode")
                                {
                                    sub.Style = 3;
                                }
                            }
                        }
                        foreach (var item in Account.NijiComponents)
                        {
                            foreach (var sub in item.Components)
                            {
                                if (sub.Label == "Fast mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Relax mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Turbo mode")
                                {
                                    sub.Style = 3;
                                }
                            }
                        }
                    }
                    else if (metaName == "turbo" && contentStr.StartsWith("Done!"))
                    {
                        foreach (var item in Account.Components)
                        {
                            foreach (var sub in item.Components)
                            {
                                if (sub.Label == "Fast mode")
                                {
                                    sub.Style = 3;
                                }
                                else if (sub.Label == "Relax mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Turbo mode")
                                {
                                    sub.Style = 2;
                                }
                            }
                        }
                        foreach (var item in Account.NijiComponents)
                        {
                            foreach (var sub in item.Components)
                            {
                                if (sub.Label == "Fast mode")
                                {
                                    sub.Style = 3;
                                }
                                else if (sub.Label == "Relax mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Turbo mode")
                                {
                                    sub.Style = 2;
                                }
                            }
                        }
                    }
                    else if (metaName == "relax" && contentStr.StartsWith("Done!"))
                    {
                        foreach (var item in Account.Components)
                        {
                            foreach (var sub in item.Components)
                            {
                                if (sub.Label == "Fast mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Relax mode")
                                {
                                    sub.Style = 3;
                                }
                                else if (sub.Label == "Turbo mode")
                                {
                                    sub.Style = 2;
                                }
                            }
                        }
                        foreach (var item in Account.NijiComponents)
                        {
                            foreach (var sub in item.Components)
                            {
                                if (sub.Label == "Fast mode")
                                {
                                    sub.Style = 2;
                                }
                                else if (sub.Label == "Relax mode")
                                {
                                    sub.Style = 3;
                                }
                                else if (sub.Label == "Turbo mode")
                                {
                                    sub.Style = 2;
                                }
                            }
                        }
                    }

                    DbHelper.Instance.AccountStore.Update("Components,NijiComponents", Account);
                    _discordInstance.ClearAccountCache(Account.Id);

                    return;
                }

                // 私信频道
                var isPrivareChannel = false;
                if (data.TryGetProperty("channel_id", out JsonElement channelIdElement))
                {
                    var cid = channelIdElement.GetString();
                    if (cid == Account.PrivateChannelId || cid == Account.NijiBotChannelId)
                    {
                        isPrivareChannel = true;
                    }

                    if (channelIdElement.GetString() == Account.ChannelId)
                    {
                        isPrivareChannel = false;
                    }

                    // 都不相同
                    // 如果有渠道 id，但不是当前渠道 id，则忽略
                    if (cid != Account.ChannelId
                        && cid != Account.PrivateChannelId
                        && cid != Account.NijiBotChannelId)
                    {
                        // 如果也不是子频道 id, 则忽略
                        if (!Account.SubChannelValues.ContainsKey(cid))
                        {
                            return;
                        }
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

                    _logger.Information($"用户消息, {messageType}, {Account.GetDisplay()} - id: {id}, mid: {metaId}, {authorName}, content: {contentStr}");

                    var isEm = data.TryGetProperty("embeds", out var em);
                    if ((messageType == MessageType.CREATE || messageType == MessageType.UPDATE) && isEm)
                    {
                        if (metaName == "info" && messageType == MessageType.UPDATE)
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
                                                        Account.SetProperty($"Niji {d.Key}", d.Value);
                                                    }
                                                    else if (applicationId == Constants.MJ_APPLICATION_ID)
                                                    {
                                                        Account.SetProperty(d.Key, d.Value);
                                                    }
                                                }
                                                else
                                                {
                                                    Account.SetProperty(d.Key, d.Value);
                                                }
                                            }

                                            var db = DbHelper.Instance.AccountStore;
                                            Account.InfoUpdated = DateTime.Now;

                                            db.Update("InfoUpdated,Properties", Account);
                                            _discordInstance?.ClearAccountCache(Account.Id);
                                        }
                                    }
                                }
                            }

                            return;
                        }
                        else if (metaName == "settings" && data.TryGetProperty("components", out var components))
                        {
                            // settings 指令
                            var eventDataMsg = data.Deserialize<EventData>();
                            if (eventDataMsg != null && eventDataMsg.InteractionMetadata?.Name == "settings" && eventDataMsg.Components?.Count > 0)
                            {
                                if (applicationId == Constants.NIJI_APPLICATION_ID)
                                {
                                    Account.NijiComponents = eventDataMsg.Components;
                                    Account.NijiSettingsMessageId = id;

                                    DbHelper.Instance.AccountStore.Update("NijiComponents,NijiSettingsMessageId", Account);
                                    _discordInstance?.ClearAccountCache(Account.Id);
                                }
                                else if (applicationId == Constants.MJ_APPLICATION_ID)
                                {
                                    Account.Components = eventDataMsg.Components;
                                    Account.SettingsMessageId = id;

                                    DbHelper.Instance.AccountStore.Update("Components,SettingsMessageId", Account);
                                    _discordInstance?.ClearAccountCache(Account.Id);
                                }
                            }

                            return;
                        }

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

                                    // 描述
                                    var desc = item.GetProperty("description").GetString();

                                    _logger.Information($"用户 embeds 消息, {messageType}, {Account.GetDisplay()} - id: {id}, mid: {metaId}, {authorName}, embeds: {title}, {color}, {desc}");

                                    // 无效参数、违规的提示词、无效提示词
                                    var errorTitles = new[] {
                                        "Invalid prompt", // 无效提示词
                                        "Invalid parameter", // 无效参数
                                        "Banned prompt detected", // 违规提示词
                                        "Invalid link", // 无效链接
                                        "Request cancelled due to output filters",
                                        "Queue full", // 执行中的队列已满
                                    };

                                    // 跳过的 title
                                    var continueTitles = new[] { "Action needed to continue" };

                                    // fast 用量已经使用完了
                                    if (title == "Credits exhausted")
                                    {
                                        // 你的处理逻辑
                                        _logger.Information($"账号 {Account.GetDisplay()} 用量已经用完");

                                        var task = _discordInstance.FindRunningTask(c => c.MessageId == id).FirstOrDefault();
                                        if (task == null && !string.IsNullOrWhiteSpace(metaId))
                                        {
                                            task = _discordInstance.FindRunningTask(c => c.InteractionMetadataId == metaId).FirstOrDefault();
                                        }

                                        if (task != null)
                                        {
                                            task.Fail("账号用量已经用完");
                                        }


                                        // 标记快速模式已经用完了
                                        Account.FastExhausted = true;

                                        // 自动设置慢速，如果快速用完
                                        if (Account.FastExhausted == true && Account.EnableAutoSetRelax == true)
                                        {
                                            Account.AllowModes = new List<GenerationSpeedMode>() { GenerationSpeedMode.RELAX };

                                            if (Account.CoreSize > 3)
                                            {
                                                Account.CoreSize = 3;
                                            }
                                        }

                                        DbHelper.Instance.AccountStore.Update("AllowModes,FastExhausted,CoreSize", Account);
                                        _discordInstance?.ClearAccountCache(Account.Id);

                                        // 如果开启自动切换慢速模式
                                        if (Account.EnableFastToRelax == true)
                                        {
                                            // 切换到慢速模式
                                            // 加锁切换到慢速模式
                                            // 执行切换慢速命令
                                            // 如果当前不是慢速，则切换慢速，加锁切换
                                            if (Account.MjFastModeOn || Account.NijiFastModeOn)
                                            {
                                                _ = AsyncLocalLock.TryLockAsync($"relax:{Account.ChannelId}", TimeSpan.FromSeconds(5), async () =>
                                                {
                                                    try
                                                    {
                                                        Thread.Sleep(2500);
                                                        await _discordInstance?.RelaxAsync(SnowFlake.NextId(), EBotType.MID_JOURNEY);

                                                        Thread.Sleep(2500);
                                                        await _discordInstance?.RelaxAsync(SnowFlake.NextId(), EBotType.NIJI_JOURNEY);
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        _logger.Error(ex, "切换慢速异常 {@0}", Account.ChannelId);
                                                    }
                                                });
                                            }
                                        }
                                        else
                                        {
                                            // 你的处理逻辑
                                            _logger.Warning($"账号 {Account.GetDisplay()} 用量已经用完, 自动禁用账号");

                                            // 5s 后禁用账号
                                            Task.Run(() =>
                                            {
                                                try
                                                {
                                                    Thread.Sleep(5 * 1000);

                                                    // 保存
                                                    Account.Enable = false;
                                                    Account.DisabledReason = "账号用量已经用完";

                                                    DbHelper.Instance.AccountStore.Update(Account);
                                                    _discordInstance?.ClearAccountCache(Account.Id);
                                                    _discordInstance?.Dispose();


                                                    // 发送邮件
                                                    EmailJob.Instance.EmailSend(_properties.Smtp, $"MJ账号禁用通知-{Account.ChannelId}",
                                                        $"{Account.ChannelId}, {Account.DisabledReason}");
                                                }
                                                catch (Exception ex)
                                                {
                                                    Log.Error(ex, "账号用量已经用完, 禁用账号异常 {@0}", Account.ChannelId);
                                                }
                                            });
                                        }

                                        return;
                                    }
                                    // 临时禁止/订阅取消/订阅过期/订阅暂停
                                    else if (title == "Pending mod message"
                                        || title == "Blocked"
                                        || title == "Plan Cancelled"
                                        || title == "Subscription required"
                                        || title == "Subscription paused")
                                    {
                                        // 你的处理逻辑
                                        _logger.Warning($"账号 {Account.GetDisplay()} {title}, 自动禁用账号");

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
                                                Account.Enable = false;
                                                Account.DisabledReason = $"{title}, {desc}";

                                                DbHelper.Instance.AccountStore.Update(Account);

                                                _discordInstance?.ClearAccountCache(Account.Id);
                                                _discordInstance?.Dispose();

                                                // 发送邮件
                                                EmailJob.Instance.EmailSend(_properties.Smtp, $"MJ账号禁用通知-{Account.ChannelId}",
                                                    $"{Account.ChannelId}, {Account.DisabledReason}");
                                            }
                                            catch (Exception ex)
                                            {
                                                Log.Error(ex, "{@0}, 禁用账号异常 {@1}", title, Account.ChannelId);
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
                                                        // 不需要赋值
                                                        //task.MessageId = id;

                                                        task.Description = $"{title}, {desc}";

                                                        if (!task.MessageIds.Contains(id))
                                                        {
                                                            task.MessageIds.Add(id);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    // 暂时跳过的业务处理
                                    else if (continueTitles.Contains(title))
                                    {
                                        _logger.Warning("跳过 embeds {@0}, {@1}", Account.ChannelId, data.ToString());
                                    }
                                    // 其他错误消息
                                    else if (errorTitles.Contains(title)
                                        || color == 16711680
                                        || title.Contains("Invalid")
                                        || title.Contains("error")
                                        || title.Contains("denied"))
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
                                                    // 需要用户同意 Tos
                                                    if (title.Contains("Tos not accepted"))
                                                    {
                                                        try
                                                        {
                                                            var tosData = data.Deserialize<EventData>();
                                                            var customId = tosData?.Components?.SelectMany(x => x.Components)
                                                                .Where(x => x.Label == "Accept ToS")
                                                                .FirstOrDefault()?.CustomId;

                                                            if (!string.IsNullOrWhiteSpace(customId))
                                                            {
                                                                var nonce2 = SnowFlake.NextId();
                                                                var tosRes = _discordInstance.ActionAsync(id, customId, tosData.Flags, nonce2, task)
                                                                    .ConfigureAwait(false).GetAwaiter().GetResult();

                                                                if (tosRes?.Code == ReturnCode.SUCCESS)
                                                                {
                                                                    _logger.Information("处理 Tos 成功 {@0}", Account.ChannelId);
                                                                    return;
                                                                }
                                                                else
                                                                {
                                                                    _logger.Information("处理 Tos 失败 {@0}, {@1}", Account.ChannelId, tosRes);
                                                                }
                                                            }
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            _logger.Error(ex, "处理 Tos 异常 {@0}", Account.ChannelId);
                                                        }
                                                    }

                                                    var error = $"{title}, {desc}";

                                                    task.MessageId = id;
                                                    task.Description = error;

                                                    if (!task.MessageIds.Contains(id))
                                                    {
                                                        task.MessageIds.Add(id);
                                                    }

                                                    task.Fail(error);
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // 如果 meta 是 show
                                            // 说明是 show 任务出错了
                                            if (metaName == "show" && !string.IsNullOrWhiteSpace(desc))
                                            {
                                                // 设置 none 对应的任务 id
                                                var task = _discordInstance.GetRunningTasks().Where(c => c.Action == TaskAction.SHOW && desc.Contains(c.JobId)).FirstOrDefault();
                                                if (task != null)
                                                {
                                                    if (messageType == MessageType.CREATE)
                                                    {
                                                        var error = $"{title}, {desc}";

                                                        task.MessageId = id;
                                                        task.Description = error;

                                                        if (!task.MessageIds.Contains(id))
                                                        {
                                                            task.MessageIds.Add(id);
                                                        }

                                                        task.Fail(error);
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                // 没有获取到 none 尝试使用 mid 获取 task
                                                var task = _discordInstance.GetRunningTasks()
                                                    .Where(c => c.MessageId == metaId || c.MessageIds.Contains(metaId) || c.InteractionMetadataId == metaId)
                                                    .FirstOrDefault();
                                                if (task != null)
                                                {
                                                    var error = $"{title}, {desc}";
                                                    task.Fail(error);
                                                }
                                                else
                                                {
                                                    // 如果没有获取到 none
                                                    _logger.Error("未知 embeds 错误 {@0}, {@1}", Account.ChannelId, data.ToString());
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
                                                        task.Description = $"{title}, {desc}";

                                                        if (!task.MessageIds.Contains(id))
                                                        {
                                                            task.MessageIds.Add(id);
                                                        }

                                                        _logger.Warning($"未知消息: {title}, {desc}, {Account.ChannelId}");
                                                    }
                                                }
                                            }
                                        }
                                    }
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
                            if (task != null && task.Status != TaskStatus.SUCCESS && task.Status != TaskStatus.FAILURE)
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

                                        //task.MessageId = id;

                                        if (!task.MessageIds.Contains(id))
                                        {
                                            task.MessageIds.Add(id);
                                        }
                                    }
                                    else
                                    {
                                        //task.MessageId = id;

                                        if (!task.MessageIds.Contains(id))
                                        {
                                            task.MessageIds.Add(id);
                                        }
                                    }

                                    // 只有 CREATE 才会设置消息 id
                                    if (messageType == MessageType.CREATE)
                                    {
                                        task.MessageId = id;

                                        // 设置 prompt 完整词
                                        if (!string.IsNullOrWhiteSpace(contentStr) && contentStr.Contains("(Waiting to start)"))
                                        {
                                            if (string.IsNullOrWhiteSpace(task.PromptFull))
                                            {
                                                task.PromptFull = ConvertUtils.GetFullPrompt(contentStr);
                                            }
                                        }
                                    }

                                    // 如果任务是 remix 自动提交任务
                                    if (task.RemixAutoSubmit
                                        && task.RemixModaling == true
                                        && messageType == MessageType.INTERACTION_SUCCESS)
                                    {
                                        task.RemixModalMessageId = id;
                                    }
                                }
                            }
                        }
                    }
                }

                var eventData = data.Deserialize<EventData>();

                // 如果消息类型是 CREATE
                // 则再次处理消息确认事件，确保消息的高可用
                if (messageType == MessageType.CREATE)
                {
                    Thread.Sleep(50);

                    if (eventData != null &&
                        (eventData.ChannelId == Account.ChannelId || Account.SubChannelValues.ContainsKey(eventData.ChannelId)))
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
                // describe 重新提交
                // MJ::Picread::Retry
                else if (eventData.Embeds.Count > 0 && eventData.Author?.Bot == true && eventData.Components.Count > 0
                    && eventData.Components.First().Components.Any(x => x.CustomId?.Contains("PicReader") == true))
                {
                    // 消息加锁处理
                    LocalLock.TryLock($"lock_{eventData.Id}", TimeSpan.FromSeconds(10), () =>
                    {
                        var em = eventData.Embeds.FirstOrDefault();
                        if (em != null && !string.IsNullOrWhiteSpace(em.Description))
                        {
                            var handler = _userMessageHandlers.FirstOrDefault(x => x.GetType() == typeof(UserDescribeSuccessHandler));
                            handler?.Handle(_discordInstance, MessageType.CREATE, eventData);
                        }
                    });
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(eventData.Content)
                          && eventData.Content.Contains("%")
                          && eventData.Author?.Bot == true)
                    {
                        // 消息加锁处理
                        LocalLock.TryLock($"lock_{eventData.Id}", TimeSpan.FromSeconds(10), () =>
                        {
                            var handler = _userMessageHandlers.FirstOrDefault(x => x.GetType() == typeof(UserStartAndProgressHandler));
                            handler?.Handle(_discordInstance, MessageType.UPDATE, eventData);
                        });
                    }
                    else if (eventData.InteractionMetadata?.Name == "describe")
                    {
                        // 消息加锁处理
                        LocalLock.TryLock($"lock_{eventData.Id}", TimeSpan.FromSeconds(10), () =>
                        {
                            var handler = _userMessageHandlers.FirstOrDefault(x => x.GetType() == typeof(UserDescribeSuccessHandler));
                            handler?.Handle(_discordInstance, MessageType.CREATE, eventData);
                        });
                    }
                    else if (eventData.InteractionMetadata?.Name == "shorten"
                        // shorten show details -> PromptAnalyzerExtended
                        || eventData.Embeds?.FirstOrDefault()?.Footer?.Text.Contains("Click on a button to imagine one of the shortened prompts") == true)
                    {
                        // 消息加锁处理
                        LocalLock.TryLock($"lock_{eventData.Id}", TimeSpan.FromSeconds(10), () =>
                        {
                            var handler = _userMessageHandlers.FirstOrDefault(x => x.GetType() == typeof(UserShortenSuccessHandler));
                            handler?.Handle(_discordInstance, MessageType.CREATE, eventData);
                        });
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
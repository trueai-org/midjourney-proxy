using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using Serilog;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using UAParser;

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// 处理与 Discord WebSocket 连接的类，并提供启动和消息监听功能
    /// https://discord.com/developers/docs/topics/gateway-events
    /// </summary>
    public class WebSocketManager : IDisposable
    {
        /// <summary>
        /// 新连接最大重拾次数
        /// </summary>
        private const int CONNECT_RETRY_LIMIT = 5;

        /// <summary>
        /// 重连错误码
        /// </summary>
        public const int CLOSE_CODE_RECONNECT = 2001;

        /// <summary>
        /// 异常错误码（创建新的连接）
        /// </summary>
        public const int CLOSE_CODE_EXCEPTION = 1011;

        private readonly ILogger _logger;
        private readonly DiscordHelper _discordHelper;
        private readonly DiscordAccount _account;
        private readonly BotMessageListener _botListener;
        private readonly WebProxy _webProxy;
        private readonly DiscordServiceImpl _discordService;
        private readonly DiscordInstanceImpl _discordInstance;

        /// <summary>
        /// 压缩的消息
        /// </summary>
        private MemoryStream _compressed;

        /// <summary>
        /// 解压缩器
        /// </summary>
        private DeflateStream _decompressor;

        /// <summary>
        /// wss
        /// </summary>
        public ClientWebSocket WebSocket { get; private set; }

        /// <summary>
        /// wss 心跳进程
        /// </summary>
        private Task _heartbeatTask;

        /// <summary>
        /// wss 最后一次收到消息的时间
        /// </summary>
        private long _lastMessageTime;

        /// <summary>
        /// wss 是否收到心跳通知
        /// </summary>
        private bool _heartbeatAck = true;

        /// <summary>
        /// wss 心跳间隔
        /// </summary>
        private long _heartbeatInterval = 41250;

        /// <summary>
        /// wss 客户端收到的最后一个会话 ID
        /// </summary>
        private string _sessionId;

        /// <summary>
        /// wss 客户端收到的最后一个序列号
        /// </summary>
        private int? _sequence;

        /// <summary>
        /// wss 网关恢复 url
        /// </summary>
        private string _resumeGatewayUrl;

        /// <summary>
        /// wss 接受消息 token
        /// </summary>
        private CancellationTokenSource _receiveTokenSource;

        /// <summary>
        /// wss 接收消息进程
        /// </summary>
        private Task _receiveTask;

        /// <summary>
        /// wss 心跳队列
        /// </summary>
        private readonly ConcurrentQueue<long> _heartbeatTimes = new ConcurrentQueue<long>();

        /// <summary>
        /// wss 延迟
        /// </summary>
        public int Latency { get; private set; }

        /// <summary>
        /// wss 是否运行中
        /// </summary>
        public bool Running { get; private set; }

        public WebSocketManager(
            DiscordAccount account,
            DiscordHelper discordHelper,
            BotMessageListener userMessageListener,
            WebProxy webProxy,
            DiscordServiceImpl discordService,
            DiscordInstanceImpl discordInstanceImpl)
        {
            _account = account;
            _botListener = userMessageListener;
            _discordHelper = discordHelper;
            _webProxy = webProxy;
            _discordService = discordService;
            _logger = Log.Logger;
            _discordInstance = discordInstanceImpl;
        }

        /// <summary>
        /// 异步启动 WebSocket 连接
        /// </summary>
        /// <param name="reconnect"></param>
        /// <returns></returns>
        public async Task StartAsync(bool reconnect = false)
        {
            try
            {
                WebSocket = new ClientWebSocket();

                if (_webProxy != null)
                {
                    WebSocket.Options.Proxy = _webProxy;
                }

                WebSocket.Options.SetRequestHeader("User-Agent", _account.UserAgent);
                WebSocket.Options.SetRequestHeader("Accept-Encoding", "gzip, deflate, br");
                WebSocket.Options.SetRequestHeader("Accept-Language", "zh-CN,zh;q=0.9");
                WebSocket.Options.SetRequestHeader("Cache-Control", "no-cache");
                WebSocket.Options.SetRequestHeader("Pragma", "no-cache");
                WebSocket.Options.SetRequestHeader("Sec-WebSocket-Extensions", "permessage-deflate; client_max_window_bits");

                var gatewayUrl = GetGatewayServer(reconnect ? _resumeGatewayUrl : null) + "/?encoding=json&v=9&compress=zlib-stream";
                await WebSocket.ConnectAsync(new Uri(gatewayUrl), CancellationToken.None);

                // 延时 1s
                await Task.Delay(1000);

                // 重新连接
                if (reconnect && !string.IsNullOrWhiteSpace(_sessionId) && _sequence.HasValue)
                {
                    await ResumeSessionAsync();
                }
                else
                {
                    await SendIdentifyMessageAsync();
                }

                _receiveTokenSource?.Cancel();
                _receiveTask?.Wait();
                _receiveTokenSource = new CancellationTokenSource();

                _receiveTask = ReceiveMessagesAsync(_receiveTokenSource.Token);

                _logger.Information("用户 WebSocket 连接已建立 {@0}", _account.ChannelId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "用户 WebSocket 连接异常 {@0}", _account.ChannelId);

                HandleFailure(CLOSE_CODE_EXCEPTION, "用户 WebSocket 连接异常");
            }
        }

        /// <summary>
        /// 获取网关
        /// </summary>
        /// <param name="resumeGatewayUrl"></param>
        /// <returns></returns>
        private string GetGatewayServer(string resumeGatewayUrl = null)
        {
            return !string.IsNullOrWhiteSpace(resumeGatewayUrl) ? resumeGatewayUrl : _discordHelper.GetWss();
        }

        /// <summary>
        /// 发送授权连接
        /// </summary>
        /// <returns></returns>
        private async Task SendIdentifyMessageAsync()
        {
            var authData = CreateAuthData();
            var identifyMessage = new { op = 2, d = authData };
            await SendMessageAsync(identifyMessage);

            _logger.Information("用户已发送 IDENTIFY 消息 {@0}", _account.ChannelId);
        }

        /// <summary>
        /// 重新恢复连接
        /// </summary>
        /// <returns></returns>
        private async Task ResumeSessionAsync()
        {
            var resumeMessage = new
            {
                op = 6, // RESUME 操作码
                d = new
                {
                    token = _account.UserToken,
                    session_id = _sessionId,
                    seq = _sequence,
                }
            };

            await SendMessageAsync(resumeMessage);

            _logger.Information("用户已发送 RESUME 消息 {@0}", _account.ChannelId);
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private async Task SendMessageAsync(object message)
        {
            if (WebSocket.State != WebSocketState.Open)
            {
                _logger.Warning("用户 WebSocket 已关闭，无法发送消息 {@0}", _account.ChannelId);
                return;
            }

            var messageJson = JsonSerializer.Serialize(message);
            var messageBytes = Encoding.UTF8.GetBytes(messageJson);
            await WebSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        /// <summary>
        /// 接收消息
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            while (WebSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                var buffer = new byte[1024 * 4];

                using (var ms = new MemoryStream())
                {
                    try
                    {
                        do
                        {
                            result = await WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                            ms.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage && !cancellationToken.IsCancellationRequested);

                        ms.Seek(0, SeekOrigin.Begin);
                        if (result.MessageType == WebSocketMessageType.Binary)
                        {
                            buffer = ms.ToArray();
                            await HandleBinaryMessageAsync(buffer);
                        }
                        else if (result.MessageType == WebSocketMessageType.Text)
                        {
                            var message = Encoding.UTF8.GetString(ms.ToArray());
                            HandleMessage(message);
                        }
                        else if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken);
                            _logger.Warning("用户 WebSocket 连接已关闭。");
                            HandleFailure((int)result.CloseStatus, result.CloseStatusDescription);
                        }
                        else
                        {
                            _logger.Warning("用户收到未知消息");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "用户 接收 ws 消息时发生异常");

                        HandleFailure(CLOSE_CODE_EXCEPTION, "用户 接收消息时发生异常");
                    }
                }
            }
        }

        /// <summary>
        /// 处理 byte 类型的消息
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        private async Task HandleBinaryMessageAsync(byte[] buffer)
        {
            using (var decompressed = new MemoryStream())
            {
                if (_compressed == null)
                    _compressed = new MemoryStream();
                if (_decompressor == null)
                    _decompressor = new DeflateStream(_compressed, CompressionMode.Decompress);

                if (buffer[0] == 0x78)
                {
                    _compressed.Write(buffer, 2, buffer.Length - 2);
                    _compressed.SetLength(buffer.Length - 2);
                }
                else
                {
                    _compressed.Write(buffer, 0, buffer.Length);
                    _compressed.SetLength(buffer.Length);
                }

                _compressed.Position = 0;
                await _decompressor.CopyToAsync(decompressed);
                _compressed.Position = 0;
                decompressed.Position = 0;

                using (var reader = new StreamReader(decompressed, Encoding.UTF8))
                {
                    var messageContent = await reader.ReadToEndAsync();
                    HandleMessage(messageContent);
                }
            }
        }

        /// <summary>
        /// 处理消息
        /// </summary>
        /// <param name="message"></param>
        private void HandleMessage(string message)
        {
            try
            {
                var data = JsonDocument.Parse(message).RootElement;
                var opCode = data.GetProperty("op").GetInt32();
                var seq = data.TryGetProperty("s", out var seqElement) && seqElement.ValueKind == JsonValueKind.Number ? (int?)seqElement.GetInt32() : null;
                var type = data.TryGetProperty("t", out var typeElement) ? typeElement.GetString() : null;

                ProcessMessageAsync((GatewayOpCode)opCode, seq, type, data).Wait();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "处理接收到的 WebSocket 消息失败 {@0}", _account.ChannelId);
            }
        }

        /// <summary>
        /// 执行心跳
        /// </summary>
        /// <param name="intervalMillis"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        private async Task RunHeartbeatAsync(int intervalMillis, CancellationToken cancelToken)
        {
            int delayInterval = (int)(intervalMillis * 0.9);

            try
            {
                _logger.Information("Heartbeat Started {@0}", _account.ChannelId);

                while (!cancelToken.IsCancellationRequested)
                {
                    int now = Environment.TickCount;

                    if (_heartbeatTimes.Count != 0 && (now - _lastMessageTime) > intervalMillis)
                    {
                        if (WebSocket.State == WebSocketState.Open)
                        {
                            HandleFailure(CLOSE_CODE_RECONNECT, "服务器未响应上次的心跳，将进行重连");
                            return;
                        }
                    }

                    _heartbeatTimes.Enqueue(now);
                    try
                    {
                        await SendHeartbeatAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Heartbeat Errored {@0}", _account.ChannelId);
                    }

                    int delay = Math.Max(0, delayInterval - Latency);
                    await Task.Delay(delay, cancelToken).ConfigureAwait(false);
                }

                _logger.Information("Heartbeat Stopped {@0}", _account.ChannelId);
            }
            catch (OperationCanceledException)
            {
                _logger.Information("Heartbeat Stopped {@0}", _account.ChannelId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Heartbeat Errored {@0}", _account.ChannelId);
            }
        }

        /// <summary>
        /// 发送心跳
        /// </summary>
        /// <returns></returns>
        private async Task SendHeartbeatAsync()
        {
            if (!_heartbeatAck)
            {
                _logger.Warning("用户未收到心跳 ACK，正在重新连接... {@0}", _account.ChannelId);
                await ReconnectAsync();
                return;
            }

            var heartbeatMessage = new { op = 1, d = _sequence };

            await SendMessageAsync(heartbeatMessage);
            _logger.Information("用户已发送 HEARTBEAT 消息 {@0}", _account.ChannelId);

            _heartbeatAck = false;
        }

        private async Task ReconnectAsync()
        {
            if (_heartbeatTask != null && _heartbeatTask.IsCompleted)
            {
                _heartbeatTask.Dispose();
                _heartbeatTask = null;
            }
            _receiveTokenSource?.Cancel();

            if (WebSocket.State != WebSocketState.Closed)
            {
                await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "用户未收到心跳 ACK", CancellationToken.None);
            }
            await StartAsync(true);
        }

        private async Task ProcessMessageAsync(GatewayOpCode opCode, int? seq, string type, JsonElement payload)
        {
            if (seq != null)
            {
                _sequence = seq.Value;
            }

            _lastMessageTime = Environment.TickCount;

            try
            {
                switch (opCode)
                {
                    case GatewayOpCode.Hello:
                        {
                            _logger.Information("Received Hello {@0}", _account.ChannelId);
                            _heartbeatInterval = payload.GetProperty("d").GetProperty("heartbeat_interval").GetInt64();
                            _heartbeatAck = true;
                            _heartbeatTask = RunHeartbeatAsync((int)_heartbeatInterval, _receiveTokenSource.Token);
                        }
                        break;

                    case GatewayOpCode.Heartbeat:
                        {
                            _logger.Information("Received Heartbeat {@0}", _account.ChannelId);

                            // 立即发送心跳
                            var heartbeatMessage = new { op = 1, d = _sequence };
                            await SendMessageAsync(heartbeatMessage);

                            _logger.Information("Received Heartbeat 消息已发送 {@0}", _account.ChannelId);
                        }
                        break;

                    case GatewayOpCode.HeartbeatAck:
                        {
                            _logger.Information("Received HeartbeatAck {@0}", _account.ChannelId);

                            if (_heartbeatTimes.TryDequeue(out long time))
                            {
                                Latency = (int)(Environment.TickCount - time);

                                _heartbeatAck = true;
                            }
                        }
                        break;

                    case GatewayOpCode.InvalidSession:
                        {
                            _logger.Warning("Received InvalidSession {@0}", _account.ChannelId);
                            _logger.Warning("Failed to resume previous session {@0}", _account.ChannelId);

                            _sessionId = null;
                            _sequence = null;
                            _resumeGatewayUrl = null;

                            await SendIdentifyMessageAsync();
                        }
                        break;

                    case GatewayOpCode.Reconnect:
                        {
                            _logger.Warning("Received Reconnect {@0}", _account.ChannelId);

                            HandleFailure(CLOSE_CODE_RECONNECT, "收到重连请求，将自动重连");
                        }
                        break;

                    case GatewayOpCode.Dispatch:
                        {
                            _logger.Information("Received Dispatch {@0}", _account.ChannelId);
                            HandleDispatch(payload);
                        }
                        break;

                    default:
                        _logger.Warning("Unknown OpCode ({@0}) {@1}", opCode, _account.ChannelId);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling {opCode}{(type != null ? $" ({type})" : "")}, {_account.ChannelId}");
            }
        }

        /// <summary>
        /// 收到消息
        /// </summary>
        /// <param name="data"></param>
        private void HandleDispatch(JsonElement data)
        {
            if (data.TryGetProperty("t", out var t) && t.GetString() == "READY")
            {
                _sessionId = data.GetProperty("d").GetProperty("session_id").GetString();
                _resumeGatewayUrl = data.GetProperty("d").GetProperty("resume_gateway_url").GetString() + "/?encoding=json&v=9&compress=zlib-stream";

                OnSocketSuccess();
            }
            else if (data.TryGetProperty("t", out var resumed) && resumed.GetString() == "RESUMED")
            {
                OnSocketSuccess();
            }
            else
            {
                _botListener.OnMessage(data);
            }
        }

        /// <summary>
        /// 创建授权信息
        /// </summary>
        /// <returns></returns>
        private JsonElement CreateAuthData()
        {
            var uaParser = Parser.GetDefault();
            var agent = uaParser.Parse(_account.UserAgent);
            var connectionProperties = new
            {
                browser = agent.UA.Family,
                browser_user_agent = _account.UserAgent,
                browser_version = agent.UA.Major + "." + agent.UA.Minor,
                client_build_number = 222963,
                client_event_source = (string)null,
                device = agent.Device.Model,
                os = agent.OS.Family,
                referer = "https://www.midjourney.com",
                referring_domain = "www.midjourney.com",
                release_channel = "stable",
                system_locale = "zh-CN"
            };

            var presence = new
            {
                activities = Array.Empty<object>(),
                afk = false,
                since = 0,
                status = "online"
            };

            var clientState = new
            {
                api_code_version = 0,
                guild_versions = new { },
                highest_last_message_id = "0",
                private_channels_version = "0",
                read_state_version = 0,
                user_guild_settings_version = -1,
                user_settings_version = -1
            };

            var authData = new
            {
                capabilities = 16381,
                client_state = clientState,
                compress = false,
                presence = presence,
                properties = connectionProperties,
                token = _account.UserToken
            };

            return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(authData));
        }

        /// <summary>
        /// 处理错误
        /// </summary>
        /// <param name="code"></param>
        /// <param name="reason"></param>
        private void HandleFailure(int code, string reason)
        {
            _logger.Error("用户 WebSocket 连接失败, 代码 {0}: {1}, {2}", code, reason, _account.ChannelId);

            CloseSocketSessionWhenIsOpen();

            if (!Running)
            {
                NotifyWss(code, reason);
            }

            Running = false;

            if (code >= 4000)
            {
                _logger.Warning("用户无法重新连接！账号自动禁用。由 {0}({1}) 关闭。", code, reason);

                DisableAccount();
            }
            else if (code == 2001)
            {
                _logger.Warning("用户由 {0}({1}) 关闭。尝试重新连接...", code, reason);

                TryReconnect();
            }
            else
            {
                _logger.Warning("用户由 {0}({1}) 关闭。尝试新连接...", code, reason);

                TryNewConnect();
            }
        }

        /// <summary>
        /// 重新连接
        /// </summary>
        private void TryReconnect()
        {
            try
            {
                StartAsync(true).Wait();
            }
            catch (Exception e)
            {
                if (e is TimeoutException)
                {
                    CloseSocketSessionWhenIsOpen();
                }

                _logger.Warning(e, "用户重新连接失败 {@0}，尝试新连接", _account.ChannelId);

                Thread.Sleep(1000);

                TryNewConnect();
            }
        }

        /// <summary>
        /// 新的连接
        /// </summary>
        private void TryNewConnect()
        {
            for (int i = 1; i <= CONNECT_RETRY_LIMIT; i++)
            {
                try
                {
                    StartAsync(false).Wait();
                    return;
                }
                catch (Exception e)
                {
                    if (e is TimeoutException)
                    {
                        CloseSocketSessionWhenIsOpen();
                    }

                    _logger.Warning(e, "用户新连接失败, 第 {@0} 次, {@1}", i, _account.ChannelId);

                    Thread.Sleep(5000);
                }
            }

            if (WebSocket == null || WebSocket.State != WebSocketState.Open)
            {
                _logger.Error("由于无法重新连接，自动禁用账号");

                DisableAccount();
            }
        }

        /// <summary>
        /// 停止并禁用账号
        /// </summary>
        private void DisableAccount()
        {
            // 保存
            _account.Enable = false;

            DbHelper.AccountStore.Save(_account);

            _discordInstance?.Dispose();
        }

        /// <summary>
        /// 如果打开了，则关闭 wss
        /// </summary>
        private void CloseSocketSessionWhenIsOpen()
        {
            try
            {
                if (WebSocket != null && WebSocket.State == WebSocketState.Open)
                {
                    WebSocket.Abort();
                    WebSocket.Dispose();
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 通知错误或成功
        /// </summary>
        /// <param name="code"></param>
        /// <param name="reason"></param>
        private void NotifyWss(int code, string reason)
        {
            _account.DisabledReason = reason;

            // 保存
            DbHelper.AccountStore.Save(_account);
        }

        /// <summary>
        /// 释放
        /// </summary>
        public void Dispose()
        {
            CloseSocketSessionWhenIsOpen();
            WebSocket?.Dispose();
            _botListener?.Dispose();
        }

        /// <summary>
        /// 连接成功
        /// </summary>
        private void OnSocketSuccess()
        {
            Running = true;
            _discordService.DefaultSessionId = _sessionId;

            NotifyWss(ReturnCode.SUCCESS, "");
        }
    }

    internal enum GatewayOpCode : byte
    {
        Dispatch = 0,
        Heartbeat = 1,
        Identify = 2,
        PresenceUpdate = 3,
        VoiceStateUpdate = 4,
        VoiceServerPing = 5,
        Resume = 6,
        Reconnect = 7,
        RequestGuildMembers = 8,
        InvalidSession = 9,
        Hello = 10,
        HeartbeatAck = 11,
        GuildSync = 12
    }
}
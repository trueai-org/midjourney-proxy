using Discord;
using Discord.Net.WebSockets;
using Discord.WebSocket;
using Midjourney.Infrastructure.Domain;
using Newtonsoft.Json;
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
    /// 处理与 Discord WebSocket 连接的类
    /// </summary>
    public class WebSocketHandler
    {
        private int _lastSeq;
        private Task _heartbeatTask, _guildDownloadTask;
        private int _unavailableGuildCount;
        private long _lastGuildAvailableTime, _lastMessageTime;

        public const int CloseCodeReconnect = 2001;
        public const int CloseCodeInvalidate = 1009;
        public const int CloseCodeException = 1011;

        private readonly DiscordHelper _discordHelper;
        private readonly DiscordAccount _account;
        private readonly BotMessageListener _userMessageListener;

        private readonly ILogger _logger;

        private ClientWebSocket _webSocket;
        private Timer _heartbeatTimer;
        private bool _heartbeatAck = true;
        private long _heartbeatInterval = 41250;
        private string _sessionId;
        private object _sequence;
        private string _resumeGatewayUrl;

        // 存储 zlib 解压流
        private MemoryStream _compressed;
        private DeflateStream _decompressor;

        public delegate void SuccessCallback(string sessionId, object sequence, string resumeGatewayUrl);
        public delegate void FailureCallback(int code, string reason);

        private readonly SuccessCallback _successCallback;
        private readonly FailureCallback _failureCallback;

        private Task _receiveTask;
        private CancellationTokenSource _cancellationTokenSource;

        private readonly WebProxy _webProxy;
        protected readonly SemaphoreSlim _stateLock;

        public ConnectionState ConnectionState { get; private set; }
        public int Latency { get; protected set; }

        private readonly ConcurrentQueue<long> _heartbeatTimes = new ConcurrentQueue<long>();

        public WebSocketHandler(DiscordAccount account,
            DiscordHelper discordHelper,
            BotMessageListener userMessageListener,
            WebProxy webProxy,
            SuccessCallback successCallback,
            FailureCallback failureCallback)
        {
            _account = account;
            _userMessageListener = userMessageListener;
            _discordHelper = discordHelper;
            _webProxy = webProxy;
            _successCallback = successCallback;
            _failureCallback = failureCallback;
            _logger = Log.Logger;

            _stateLock = new SemaphoreSlim(1, 1);
        }

        IWebSocketClient WebSocketClient { get; set; }

        /// <summary>
        /// 异步启动 WebSocket 连接
        /// </summary>
        /// <param name="reconnect">是否重连</param>
        public async Task StartAsync(
            bool reconnect = false,
            string sessionId = null,
            object seq = null,
            string resumeGatewayUrl = null)
        {
            _webSocket = new ClientWebSocket();

            if (_webProxy != null)
            {
                _webSocket.Options.Proxy = _webProxy;
            }

            _webSocket.Options.SetRequestHeader("User-Agent", _account.UserAgent);
            _webSocket.Options.SetRequestHeader("Accept-Encoding", "gzip, deflate, br");
            _webSocket.Options.SetRequestHeader("Accept-Language", "zh-CN,zh;q=0.9");
            _webSocket.Options.SetRequestHeader("Cache-Control", "no-cache");
            _webSocket.Options.SetRequestHeader("Pragma", "no-cache");
            _webSocket.Options.SetRequestHeader("Sec-WebSocket-Extensions", "permessage-deflate; client_max_window_bits");

            try
            {
                string gatewayUrl;
                if (reconnect)
                {
                    gatewayUrl = GetGatewayServer(resumeGatewayUrl) + "/?encoding=json&v=9&compress=zlib-stream";
                }
                else
                {
                    gatewayUrl = GetGatewayServer() + "/?encoding=json&v=9&compress=zlib-stream";
                }

                await _webSocket.ConnectAsync(new Uri(gatewayUrl), CancellationToken.None);

                // 延时 1s
                await Task.Delay(1000);

                _logger.Information("用户 WebSocket 连接已建立。");

                if (reconnect)
                {
                    await ResumeSessionAsync(sessionId, seq);
                }
                else
                {
                    await SendIdentifyMessageAsync();
                }

                if (_receiveTask != null)
                {
                    _cancellationTokenSource.Cancel();
                    await _receiveTask;
                }

                _cancellationTokenSource = new CancellationTokenSource();
                _receiveTask = ReceiveMessagesAsync(_cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "用户 WebSocket 连接错误。");

                HandleFailure(CloseCodeException, "用户 WebSocket 连接错误");
            }
        }

        public ClientWebSocket ClientWebSocket => _webSocket;

        private string GetGatewayServer(string resumeGatewayUrl = null)
        {
            if (!string.IsNullOrWhiteSpace(resumeGatewayUrl))
            {
                return string.IsNullOrWhiteSpace(_resumeGatewayUrl) ? resumeGatewayUrl : _resumeGatewayUrl;
            }
            return _discordHelper.GetWss();
        }

        /// <summary>
        /// 发送识别消息以进行身份验证
        /// </summary>
        private async Task SendIdentifyMessageAsync()
        {
            var authData = CreateAuthData();
            var identifyMessage = new { op = 2, d = authData };

            await SendMessageAsync(identifyMessage);
            _logger.Information("用户 已发送 IDENTIFY 消息。");
        }

        /// <summary>
        /// 恢复之前的 WebSocket 会话
        /// </summary>
        private async Task ResumeSessionAsync(string sessionId = null, object seq = null)
        {
            var resumeMessage = new
            {
                op = 6, // RESUME 操作码
                d = new
                {
                    token = _account.UserToken,
                    session_id = sessionId ?? _sessionId,
                    seq = seq ?? _sequence,
                }
            };

            await SendMessageAsync(resumeMessage);

            _logger.Information("用户 已发送 RESUME 消息。");
        }

        /// <summary>
        /// 发送消息到 WebSocket
        /// </summary>
        /// <param name="message">要发送的消息对象</param>
        private async Task SendMessageAsync(object message)
        {
            if (_webSocket.State != WebSocketState.Open)
            {
                _logger.Warning("用户 WebSocket 已关闭，无法发送消息 {@0}", _account.ChannelId);
                return;
            }

            var messageJson = System.Text.Json.JsonSerializer.Serialize(message);
            var messageBytes = Encoding.UTF8.GetBytes(messageJson);
            await _webSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                var buffer = new byte[1024 * 4];

                using (var ms = new MemoryStream())
                {
                    try
                    {
                        do
                        {
                            result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                            ms.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage && !cancellationToken.IsCancellationRequested);

                        ms.Seek(0, SeekOrigin.Begin);
                        if (result.MessageType == WebSocketMessageType.Binary)
                        {
                            buffer = ms.ToArray();

                            using (var decompressed = new MemoryStream())
                            {
                                if (_compressed == null)
                                    _compressed = new MemoryStream();
                                if (_decompressor == null)
                                    _decompressor = new DeflateStream(_compressed, CompressionMode.Decompress);

                                if (buffer[0] == 0x78)
                                {
                                    // 去掉 zlib 头部
                                    _compressed.Write(buffer, 2, (int)ms.Length - 2);
                                    _compressed.SetLength(ms.Length - 2);
                                }
                                else
                                {
                                    _compressed.Write(buffer, 0, (int)ms.Length);
                                    _compressed.SetLength(ms.Length);
                                }

                                _compressed.Position = 0;
                                _decompressor.CopyTo(decompressed);
                                _compressed.Position = 0;
                                decompressed.Position = 0;

                                string messageContent;
                                using (var reader = new StreamReader(decompressed, Encoding.UTF8))
                                {
                                    messageContent = await reader.ReadToEndAsync();
                                }

                                HandleMessage(messageContent);
                            }
                        }
                        else if (result.MessageType == WebSocketMessageType.Text)
                        {
                            var message = Encoding.UTF8.GetString(ms.ToArray());
                            HandleMessage(message);
                        }
                        else if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken);

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
                        HandleFailure(CloseCodeException, "用户 接收消息时发生异常");
                    }
                }
            }
        }

        public async Task DisconnectAsync(Exception ex = null)
        {
            await _stateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await DisconnectInternalAsync(ex).ConfigureAwait(false);
            }
            finally { _stateLock.Release(); }
        }

        internal async Task DisconnectInternalAsync(Exception ex = null)
        {
            if (ConnectionState == ConnectionState.Disconnected)
                return;
            ConnectionState = ConnectionState.Disconnecting;

            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "用户关闭连接", CancellationToken.None);
            }
            catch { }

            ConnectionState = ConnectionState.Disconnected;
        }

        /// <summary>
        /// 处理接收到的 WebSocket 消息
        /// </summary>
        /// <param name="message">接收到的消息内容</param>
        private void HandleMessage(string message)
        {
            try
            {
                var data = JsonDocument.Parse(message).RootElement;
                var opCode = data.GetProperty("op").GetInt32();
                var seq = data.TryGetProperty("s", out var seqElement) && seqElement.ValueKind == JsonValueKind.Number ? (int?)seqElement.GetInt32() : null;
                var type = data.TryGetProperty("t", out var typeElement) ? typeElement.GetString() : null;

                //var payload = data.TryGetProperty("d", out var payloadElement) ? payloadElement : default;

                ProcessMessageAsync((GatewayOpCode)opCode, seq, type, data).Wait();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "处理接收到的 WebSocket 消息失败 {@0}", _account.ChannelId);
            }
        }

        /// <summary>
        /// 处理 Hello 消息，启动心跳定时器
        /// </summary>
        /// <param name="data">消息数据</param>
        private void HandleHello(JsonElement data)
        {
            _heartbeatInterval = data.GetProperty("d").GetProperty("heartbeat_interval").GetInt64();
            _heartbeatAck = true;

            _heartbeatTask = RunHeartbeatAsync((int)_heartbeatInterval, _cancellationTokenSource.Token);
        }

        /// <summary>
        /// 运行心跳任务
        /// </summary>
        /// <param name="intervalMillis">心跳间隔时间</param>
        /// <param name="cancelToken">取消令牌</param>
        private async Task RunHeartbeatAsync(int intervalMillis, CancellationToken cancelToken)
        {
            int delayInterval = (int)(intervalMillis * 0.9);

            try
            {
                _logger.Information("Heartbeat Started {@0}", _account.ChannelId);
                while (!cancelToken.IsCancellationRequested)
                {
                    int now = Environment.TickCount;

                    // 检查服务器是否响应了上次的心跳，或我们仍在接收消息（长时间加载？）
                    if (_heartbeatTimes.Count != 0 && (now - _lastMessageTime) > intervalMillis)
                    {
                        if (ConnectionState == ConnectionState.Connected && (_guildDownloadTask?.IsCompleted ?? true))
                        {
                            HandleFailure(CloseCodeReconnect, "服务器未响应上次的心跳");
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
        /// 发送心跳消息
        /// </summary>
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

        /// <summary>
        /// 重新连接 WebSocket
        /// </summary>
        private async Task ReconnectAsync()
        {
            _heartbeatTask?.Dispose();
            if (_webSocket.State != WebSocketState.Closed)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "用户未收到心跳 ACK", CancellationToken.None);
            }
            await StartAsync(true);
        }

        /// <summary>
        /// 处理消息
        /// </summary>
        private async Task ProcessMessageAsync(GatewayOpCode opCode, int? seq, string type, JsonElement payload)
        {
            if (seq != null)
            { 
                _lastSeq = seq.Value;
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
                            _heartbeatTask = RunHeartbeatAsync((int)_heartbeatInterval, _cancellationTokenSource.Token);
                        }
                        break;
                    case GatewayOpCode.Heartbeat:
                        {
                            _logger.Information("Received Heartbeat {@0}", _account.ChannelId);

                            await SendHeartbeatAsync();
                        }
                        break;
                    case GatewayOpCode.HeartbeatAck:
                        {
                            _logger.Information("Received HeartbeatAck {@0}", _account.ChannelId);

                            if (_heartbeatTimes.TryDequeue(out long time))
                            {
                                int latency = (int)(Environment.TickCount - time);
                                int before = Latency;
                                Latency = latency;

                                _heartbeatAck = true;

                                // Notify latency update
                                // await TimedInvokeAsync(_latencyUpdatedEvent, nameof(LatencyUpdated), before, latency).ConfigureAwait(false);
                            }
                        }
                        break;
                    case GatewayOpCode.InvalidSession:
                        {
                            _logger.Warning("Received InvalidSession {@0}", _account.ChannelId);
                            _logger.Warning("Failed to resume previous session {@0}", _account.ChannelId);

                            _sessionId = null;
                            _lastSeq = 0;
                            _sequence = 0;
                            _resumeGatewayUrl = null;

                            await SendIdentifyMessageAsync();
                        }
                        break;
                    case GatewayOpCode.Reconnect:
                        {
                            _logger.Warning("Received Reconnect {@0}", _account.ChannelId);
                            HandleFailure(CloseCodeReconnect, "Server requested a reconnect");
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
        /// 执行恢复或识别
        /// </summary>
        private void DoResumeOrIdentify()
        {
            if (string.IsNullOrWhiteSpace(_sessionId))
            {
                SendIdentifyMessageAsync().Wait();
            }
            else
            {
                ResumeSessionAsync().Wait();
            }
        }

        /// <summary>
        /// 处理 Dispatch 消息
        /// </summary>
        /// <param name="data">消息数据</param>
        private void HandleDispatch(JsonElement data)
        {
            //_sequence = data.TryGetProperty("s", out var seq) ? (object)seq.GetInt64() : null;

            if (data.TryGetProperty("t", out var t) && t.GetString() == "READY")
            {
                _sessionId = data.GetProperty("d").GetProperty("session_id").GetString();
                _resumeGatewayUrl = data.GetProperty("d").GetProperty("resume_gateway_url").GetString() + "/?encoding=json&v=9&compress=zlib-stream";

                OnSuccess();
            }
            else if (data.TryGetProperty("t", out var resumed) && resumed.GetString() == "RESUMED")
            {
                OnSuccess();
            }
            else
            {
                _userMessageListener.OnMessage(data);
            }
        }

        /// <summary>
        /// 创建身份验证数据
        /// </summary>
        /// <returns>身份验证数据的 JsonElement</returns>
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

            return System.Text.Json.JsonSerializer.Deserialize<JsonElement>(System.Text.Json.JsonSerializer.Serialize(authData));
        }

        /// <summary>
        /// 处理 WebSocket 连接失败
        /// </summary>
        /// <param name="code">失败代码</param>
        /// <param name="reason">失败原因</param>
        private void HandleFailure(int code, string reason)
        {
            _logger.Error("用户 WebSocket 连接失败，代码 {0}：{1}", code, reason);
            _failureCallback?.Invoke(code, reason);
        }

        /// <summary>
        /// 处理 WebSocket 连接成功
        /// </summary>
        private void OnSuccess()
        {
            _successCallback?.Invoke(_sessionId, _sequence, _resumeGatewayUrl);
        }
    }

    internal enum GatewayOpCode : byte
    {
        /// <summary> C←S - Used to send most events. </summary>
        Dispatch = 0,
        /// <summary> C↔S - Used to keep the connection alive and measure latency. </summary>
        Heartbeat = 1,
        /// <summary> C→S - Used to associate a connection with a token and specify configuration. </summary>
        Identify = 2,
        /// <summary> C→S - Used to update client's status and current game id. </summary>
        PresenceUpdate = 3,
        /// <summary> C→S - Used to join a particular voice channel. </summary>
        VoiceStateUpdate = 4,
        /// <summary> C→S - Used to ensure the guild's voice server is alive. </summary>
        VoiceServerPing = 5,
        /// <summary> C→S - Used to resume a connection after a redirect occurs. </summary>
        Resume = 6,
        /// <summary> C←S - Used to notify a client that they must reconnect to another gateway. </summary>
        Reconnect = 7,
        /// <summary> C→S - Used to request members that were withheld by large_threshold </summary>
        RequestGuildMembers = 8,
        /// <summary> C←S - Used to notify the client that their session has expired and cannot be resumed. </summary>
        InvalidSession = 9,
        /// <summary> C←S - Used to provide information to the client immediately on connection. </summary>
        Hello = 10,
        /// <summary> C←S - Used to reply to a client's heartbeat. </summary>
        HeartbeatAck = 11,
        /// <summary> C→S - Used to request presence updates from particular guilds. </summary>
        GuildSync = 12
    }
}

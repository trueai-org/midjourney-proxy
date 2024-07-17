using Discord;
using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.Util;
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
    /// </summary>
    public class WebSocketManager : IDisposable
    {
        private const int CONNECT_RETRY_LIMIT = 5;
        private Task _heartbeatTask;
        private long _lastMessageTime;

        public const int CloseCodeReconnect = 2001;
        public const int CloseCodeInvalidate = 1009;
        public const int CloseCodeException = 1011;

        private readonly DiscordHelper _discordHelper;
        private readonly DiscordAccount _account;
        private readonly BotMessageListener _userMessageListener;

        private readonly ILogger _logger;
        private readonly WebProxy _webProxy;
        private readonly DiscordServiceImpl _discordService;
        private readonly DiscordInstanceImpl _discordInstanceImpl;

        private ClientWebSocket _webSocketSession = null;
        private bool _heartbeatAck = true;
        private long _heartbeatInterval = 41250;
        private string _sessionId;
        private object _sequence;
        private string _resumeGatewayUrl;
        private bool _running = false;

        private MemoryStream _compressed;
        private DeflateStream _decompressor;

        private Task _receiveTask;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly ConcurrentQueue<long> _heartbeatTimes = new ConcurrentQueue<long>();

        public ConnectionState ConnectionState { get; private set; }
        public int Latency { get; protected set; }

        public WebSocketManager(DiscordAccount account,
            DiscordHelper discordHelper,
            BotMessageListener userMessageListener,
            WebProxy webProxy,
            DiscordServiceImpl discordService,
            DiscordInstanceImpl discordInstanceImpl)
        {
            _account = account;
            _userMessageListener = userMessageListener;
            _discordHelper = discordHelper;
            _webProxy = webProxy;
            _discordService = discordService;
            _logger = Log.Logger;
            _discordInstanceImpl = discordInstanceImpl;
        }

        /// <summary>
        /// 异步启动 WebSocket 连接
        /// </summary>
        public async Task StartAsync()
        {
            await StartAsync(false);
        }

        private async Task StartAsync(bool reconnect)
        {
            try
            {
                _webSocketSession = new ClientWebSocket();

                if (_webProxy != null)
                {
                    _webSocketSession.Options.Proxy = _webProxy;
                }

                _webSocketSession.Options.SetRequestHeader("User-Agent", _account.UserAgent);
                _webSocketSession.Options.SetRequestHeader("Accept-Encoding", "gzip, deflate, br");
                _webSocketSession.Options.SetRequestHeader("Accept-Language", "zh-CN,zh;q=0.9");
                _webSocketSession.Options.SetRequestHeader("Cache-Control", "no-cache");
                _webSocketSession.Options.SetRequestHeader("Pragma", "no-cache");
                _webSocketSession.Options.SetRequestHeader("Sec-WebSocket-Extensions", "permessage-deflate; client_max_window_bits");

                var gatewayUrl = GetGatewayServer(reconnect ? _resumeGatewayUrl : null) + "/?encoding=json&v=9&compress=zlib-stream";
                await _webSocketSession.ConnectAsync(new Uri(gatewayUrl), CancellationToken.None);

                // 延时 1s
                await Task.Delay(1000);

                _logger.Information("用户 WebSocket 连接已建立。");

                if (reconnect && !string.IsNullOrWhiteSpace(_sessionId))
                {
                    await ResumeSessionAsync(_sessionId, _sequence);
                }
                else
                {
                    await SendIdentifyMessageAsync();
                }

                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();
                _receiveTask = ReceiveMessagesAsync(_cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "用户 WebSocket 连接错误。");
                HandleFailure(CloseCodeException, "用户 WebSocket 连接错误");
            }
        }

        private string GetGatewayServer(string resumeGatewayUrl = null)
        {
            return !string.IsNullOrWhiteSpace(resumeGatewayUrl) ? resumeGatewayUrl : _discordHelper.GetWss();
        }

        private async Task SendIdentifyMessageAsync()
        {
            var authData = CreateAuthData();
            var identifyMessage = new { op = 2, d = authData };
            await SendMessageAsync(identifyMessage);
            _logger.Information("用户 已发送 IDENTIFY 消息。");
        }

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

        private async Task SendMessageAsync(object message)
        {
            if (_webSocketSession.State != WebSocketState.Open)
            {
                _logger.Warning("用户 WebSocket 已关闭，无法发送消息 {@0}", _account.ChannelId);
                return;
            }

            var messageJson = JsonSerializer.Serialize(message);
            var messageBytes = Encoding.UTF8.GetBytes(messageJson);
            await _webSocketSession.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            while (_webSocketSession.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                var buffer = new byte[1024 * 4];

                using (var ms = new MemoryStream())
                {
                    try
                    {
                        do
                        {
                            result = await _webSocketSession.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
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
                            await _webSocketSession.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken);
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
                        if (ConnectionState == ConnectionState.Connected)
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
            _cancellationTokenSource?.Cancel();

            if (_webSocketSession.State != WebSocketState.Closed)
            {
                await _webSocketSession.CloseAsync(WebSocketCloseStatus.NormalClosure, "用户未收到心跳 ACK", CancellationToken.None);
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
                                Latency = latency;
                                _heartbeatAck = true;
                            }
                        }
                        break;

                    case GatewayOpCode.InvalidSession:
                        {
                            _logger.Warning("Received InvalidSession {@0}", _account.ChannelId);
                            _logger.Warning("Failed to resume previous session {@0}", _account.ChannelId);

                            _sessionId = null;
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
                _userMessageListener.OnMessage(data);
            }
        }

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

        private void HandleFailure(int code, string reason)
        {
            _logger.Error("用户 WebSocket 连接失败, 代码 {0}: {1}, {2}", code, reason, _account.ChannelId);

            CloseSocketSessionWhenIsOpen();

            if (!_running)
            {
                NotifyWssLock(code, reason);
            }

            _running = false;

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

        private void TryReconnect()
        {
            try
            {
                TryStartAsync(true).Wait();
            }
            catch (Exception e)
            {
                if (e is TimeoutException)
                {
                    CloseSocketSessionWhenIsOpen();
                }
                _logger.Warning("用户重新连接失败: {0}，尝试新连接...", e.Message);

                Thread.Sleep(1000);

                TryNewConnect();
            }
        }

        private void TryNewConnect()
        {
            for (int i = 1; i <= CONNECT_RETRY_LIMIT; i++)
            {
                try
                {
                    TryStartAsync(false).Wait();
                    return;
                }
                catch (Exception e)
                {
                    if (e is TimeoutException)
                    {
                        CloseSocketSessionWhenIsOpen();
                    }

                    _logger.Warning("用户新连接失败 ({0}): {1}", i, e.Message);

                    Thread.Sleep(5000);
                }
            }

            if (_webSocketSession == null || _webSocketSession.State != WebSocketState.Open)
            {
                _logger.Error("由于无法重新连接，自动禁用账号");
                DisableAccount();
            }
        }

        public async Task TryStartAsync(bool reconnect)
        {
            await StartAsync(reconnect);

            var lockObject = await AsyncLockUtils.WaitForLockAsync($"wss:{_account.Id}", TimeSpan.FromSeconds(20));
            if (lockObject != null)
            {
                _logger.Information("{0} 成功。", reconnect ? "重新连接" : "新连接");
                return;
            }

            throw new Exception("获取锁超时");
        }

        private void NotifyWssLock(int code, string reason)
        {
            _account.DisabledReason = reason;

            // 保存
            DbHelper.AccountStore.Save(_account);
        }

        private void DisableAccount()
        {
            // 保存
            _account.Enable = false;

            DbHelper.AccountStore.Save(_account);

            _discordInstanceImpl?.Dispose();
        }

        private void CloseSocketSessionWhenIsOpen()
        {
            try
            {
                if (_webSocketSession != null && _webSocketSession.State == WebSocketState.Open)
                {
                    _webSocketSession.Abort();
                    _webSocketSession.Dispose();
                }
            }
            catch (Exception)
            {
                // do nothing
            }
        }

        public void Dispose()
        {
            CloseSocketSessionWhenIsOpen();
            _webSocketSession?.Dispose();
            _userMessageListener?.Dispose();
        }

        private void OnSocketSuccess()
        {
            _running = true;
            _discordService.DefaultSessionId = _sessionId;

            NotifyWssLock(ReturnCode.SUCCESS, "");
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
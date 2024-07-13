using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.Services;
using Serilog;
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

        /// <summary>
        /// 初始化 WebSocketHandler 实例
        /// </summary>
        /// <param name="account">Discord 账户信息</param>
        /// <param name="discordHelper">Discord 帮助类</param>
        /// <param name="userMessageListener">用户消息监听器</param>
        /// <param name="successCallback">成功回调</param>
        /// <param name="failureCallback">失败回调</param>
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
        }

        /// <summary>
        /// 异步启动 WebSocket 连接
        /// </summary>
        /// <param name="reconnect">是否重连</param>
        public async Task StartAsync(bool reconnect = false,
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

                // 不需要发送 IDENTIFY 消息，因为建立后会尝试发送
                //else
                //{
                //    await SendIdentifyMessageAsync();
                //}

                // 异步接收消息
                //await ReceiveMessagesAsync();

                // 如果有任务了，则清除
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
            var messageJson = JsonSerializer.Serialize(message);
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
                            _logger.Information("用户 WebSocket 连接已关闭。");
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

        /// <summary>
        /// 处理接收到的 WebSocket 消息
        /// </summary>
        /// <param name="message">接收到的消息内容</param>
        private void HandleMessage(string message)
        {
            //_logger.Debug("用户收到消息: {0}", message);

            var data = JsonDocument.Parse(message).RootElement;
            var opCode = data.GetProperty("op").GetInt32();
            switch (opCode)
            {
                case 10: // Hello
                    _logger.Information("用户收到 Hello 消息");
                    HandleHello(data);
                    DoResumeOrIdentify();
                    break;

                case 11: // Heartbeat ACK
                    _logger.Information("用户收到 Heartbeat ACK");
                    _heartbeatAck = true;
                    break;

                case 0: // Dispatch
                    _logger.Information("用户收到 Dispatch 消息");
                    HandleDispatch(data);
                    break;

                case 9: // Invalid Session
                    _logger.Information("用户收到 Invalid Session 消息");
                    HandleFailure(CloseCodeInvalidate, "会话无效");
                    break;

                case 7: // Reconnect
                    _logger.Information("用户收到 Reconnect 消息");
                    HandleFailure(CloseCodeReconnect, "服务器请求重连");
                    break;

                default:
                    _logger.Information("用户收到未知操作码: {0}", opCode);
                    break;
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
            _heartbeatTimer = new Timer(SendHeartbeat, null, _heartbeatInterval, _heartbeatInterval);
        }

        /// <summary>
        /// 发送心跳消息
        /// </summary>
        /// <param name="state">状态对象</param>
        private async void SendHeartbeat(object state)
        {
            try
            {
                if (!_heartbeatAck)
                {
                    _logger.Warning("用户未收到心跳 ACK，正在重新连接...");
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "用户 未收到心跳 ACK", CancellationToken.None);
                    _heartbeatTimer.Dispose();
                    await StartAsync(true);
                    return;
                }

                var heartbeatMessage = new { op = 1, d = _sequence };
                await SendMessageAsync(heartbeatMessage);
                _logger.Information("用户已发送 HEARTBEAT 消息。");

                _heartbeatAck = false;
            }
            catch (Exception ex)
            {
                // The WebSocket is in an invalid state ('Closed')
                if (_webSocket.State == WebSocketState.Closed)
                {
                    _logger.Warning("用户 WebSocket 已关闭，无法发送心跳。");

                    // 关闭定时器
                    _heartbeatTimer.Dispose();
                }

                _logger.Error(ex, "发送心跳异常");
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
            _sequence = data.TryGetProperty("s", out var seq) ? (object)seq.GetInt64() : null;
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

            return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(authData));
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
}
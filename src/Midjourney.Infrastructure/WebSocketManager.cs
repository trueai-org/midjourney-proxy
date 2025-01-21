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

using Microsoft.Extensions.Caching.Memory;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Util;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
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
        private readonly BotMessageListener _botListener;
        private readonly WebProxy _webProxy;
        private readonly DiscordInstance _discordInstance;

        /// <summary>
        /// 表示是否已释放资源
        /// </summary>
        private bool _isDispose = false;

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
        /// wss 接收消息和心跳 token
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
        private int Latency { get; set; }

        /// <summary>
        /// wss 是否运行中
        /// </summary>
        public bool Running { get; private set; }

        /// <summary>
        /// 消息队列
        /// </summary>
        private readonly ConcurrentQueue<JsonElement> _messageQueue = new ConcurrentQueue<JsonElement>();

        private readonly Task _messageQueueTask;

        private readonly IMemoryCache _memoryCache;

        public WebSocketManager(
            DiscordHelper discordHelper,
            BotMessageListener userMessageListener,
            WebProxy webProxy,
            DiscordInstance discordInstance,
            IMemoryCache memoryCache)
        {
            _botListener = userMessageListener;
            _discordHelper = discordHelper;
            _webProxy = webProxy;
            _discordInstance = discordInstance;
            _memoryCache = memoryCache;

            _logger = Log.Logger;

            _messageQueueTask = new Task(MessageQueueDoWork, TaskCreationOptions.LongRunning);
            _messageQueueTask.Start();
        }

        private DiscordAccount Account => _discordInstance?.Account;

        /// <summary>
        /// 异步启动 WebSocket 连接
        /// </summary>
        /// <param name="reconnect"></param>
        /// <returns></returns>
        public async Task<bool> StartAsync(bool reconnect = false)
        {
            try
            {
                // 如果资源已释放则，不再处理
                // 或者账号已禁用
                if (_isDispose || Account?.Enable != true)
                {
                    _logger.Warning("用户已禁用或资源已释放 {@0},{@1}", Account.ChannelId, _isDispose);
                    return false;
                }

                var isLock = await AsyncLocalLock.TryLockAsync($"contact_{Account.Id}", TimeSpan.FromMinutes(1), async () =>
                {
                    // 关闭现有连接并取消相关任务
                    CloseSocket(reconnect);

                    // 重置 token
                    _receiveTokenSource = new CancellationTokenSource();

                    WebSocket = new ClientWebSocket();

                    if (_webProxy != null)
                    {
                        WebSocket.Options.Proxy = _webProxy;
                    }

                    WebSocket.Options.SetRequestHeader("User-Agent", Account.UserAgent);
                    WebSocket.Options.SetRequestHeader("Accept-Encoding", "gzip, deflate, br");
                    WebSocket.Options.SetRequestHeader("Accept-Language", "zh-CN,zh;q=0.9");
                    WebSocket.Options.SetRequestHeader("Cache-Control", "no-cache");
                    WebSocket.Options.SetRequestHeader("Pragma", "no-cache");
                    WebSocket.Options.SetRequestHeader("Sec-WebSocket-Extensions", "permessage-deflate; client_max_window_bits");

                    // 获取网关地址
                    var gatewayUrl = GetGatewayServer(reconnect ? _resumeGatewayUrl : null) + "/?encoding=json&v=9&compress=zlib-stream";

                    // 重新连接
                    if (reconnect && !string.IsNullOrWhiteSpace(_sessionId) && _sequence.HasValue)
                    {
                        // 恢复
                        await WebSocket.ConnectAsync(new Uri(gatewayUrl), CancellationToken.None);

                        // 尝试恢复会话
                        await ResumeSessionAsync();
                    }
                    else
                    {
                        await WebSocket.ConnectAsync(new Uri(gatewayUrl), CancellationToken.None);

                        // 新连接，发送身份验证消息
                        await SendIdentifyMessageAsync();
                    }

                    _receiveTask = ReceiveMessagesAsync(_receiveTokenSource.Token);

                    _logger.Information("用户 WebSocket 连接已建立 {@0}", Account.ChannelId);

                });

                if (!isLock)
                {
                    _logger.Information($"取消处理, 未获取到锁, 重连: {reconnect}, {Account.ChannelId}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "用户 WebSocket 连接异常 {@0}", Account.ChannelId);

                HandleFailure(CLOSE_CODE_EXCEPTION, "用户 WebSocket 连接异常");
            }

            return false;
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
        /// 执行恢复或识别
        /// </summary>
        private async Task DoResumeOrIdentify()
        {
            if (!string.IsNullOrWhiteSpace(_sessionId) && _sequence.HasValue)
            {
                await ResumeSessionAsync();
            }
            else
            {
                await SendIdentifyMessageAsync();
            }
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

            _logger.Information("用户已发送 IDENTIFY 消息 {@0}", Account.ChannelId);
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
                    token = Account.UserToken,
                    session_id = _sessionId,
                    seq = _sequence,
                }
            };

            await SendMessageAsync(resumeMessage);

            _logger.Information("用户已发送 RESUME 消息 {@0}", Account.ChannelId);
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
                _logger.Warning("用户 WebSocket 已关闭，无法发送消息 {@0}", Account.ChannelId);
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
            try
            {
                if (WebSocket == null)
                {
                    return;
                }

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
                                //result = await WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                                //ms.Write(buffer, 0, result.Count);

                                // 使用Task.WhenAny等待ReceiveAsync或取消任务
                                var receiveTask = WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                                var completedTask = await Task.WhenAny(receiveTask, Task.Delay(-1, cancellationToken));

                                if (completedTask == receiveTask)
                                {
                                    result = receiveTask.Result;
                                    ms.Write(buffer, 0, result.Count);
                                }
                                else
                                {
                                    // 任务已取消
                                    _logger.Information("接收消息任务已取消 {@0}", Account.ChannelId);
                                    return;
                                }

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
                                _logger.Warning("用户 WebSocket 连接已关闭 {@0}", Account.ChannelId);

                                await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cancellationToken);
                                HandleFailure((int)result.CloseStatus, result.CloseStatusDescription);
                            }
                            else
                            {
                                _logger.Warning("用户收到未知消息 {@0}", Account.ChannelId);
                            }
                        }
                        catch (Exception ex)
                        {
                            // 不重连
                            //HandleFailure(CLOSE_CODE_EXCEPTION, "用户 接收消息时发生异常");

                            _logger.Error(ex, "用户接收 ws 消息时发生异常 {@0}", Account.ChannelId);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 任务被取消
                _logger.Information("接收消息任务被取消 {@0}", Account.ChannelId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "接收消息处理异常 {@0}", Account.ChannelId);
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
            // 不再等待消息处理完毕，直接返回
            _ = Task.Run(async () =>
            {
                try
                {
                    var data = JsonDocument.Parse(message).RootElement;
                    var opCode = data.GetProperty("op").GetInt32();
                    var seq = data.TryGetProperty("s", out var seqElement) && seqElement.ValueKind == JsonValueKind.Number ? (int?)seqElement.GetInt32() : null;
                    var type = data.TryGetProperty("t", out var typeElement) ? typeElement.GetString() : null;

                    await ProcessMessageAsync((GatewayOpCode)opCode, seq, type, data);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "处理接收到的 WebSocket 消息失败 {@0}", Account.ChannelId);
                }
            });
        }

        /// <summary>
        /// 执行心跳
        /// </summary>
        /// <param name="intervalMillis"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        private async Task RunHeartbeatAsync(int intervalMillis, CancellationToken cancelToken)
        {
            // 生成 0.9 到 1.0 之间的随机数
            var r = new Random();
            var v = 1 - r.NextDouble() / 10;
            var delayInterval = (int)(intervalMillis * v);

            //int delayInterval = (int)(intervalMillis * 0.9);

            try
            {
                _logger.Information("Heartbeat Started {@0}", Account.ChannelId);

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
                        _logger.Warning(ex, "Heartbeat Errored {@0}", Account.ChannelId);
                    }

                    int delay = Math.Max(0, delayInterval - Latency);
                    await Task.Delay(delay, cancelToken).ConfigureAwait(false);
                }

                _logger.Information("Heartbeat Stopped {@0}", Account.ChannelId);
            }
            catch (OperationCanceledException)
            {
                _logger.Information("Heartbeat Canceled {@0}", Account.ChannelId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Heartbeat Errored {@0}", Account.ChannelId);
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
                _logger.Warning("用户未收到心跳 ACK，正在尝试重连... {@0}", Account.ChannelId);
                TryReconnect();
                return;
            }

            var heartbeatMessage = new { op = 1, d = _sequence };

            await SendMessageAsync(heartbeatMessage);
            _logger.Information("用户已发送 HEARTBEAT 消息 {@0}", Account.ChannelId);

            _heartbeatAck = false;
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
                            _logger.Information("Received Hello {@0}", Account.ChannelId);
                            _heartbeatInterval = payload.GetProperty("d").GetProperty("heartbeat_interval").GetInt64();

                            // 尝试释放之前的心跳任务
                            if (_heartbeatTask != null && !_heartbeatTask.IsCompleted)
                            {
                                try
                                {
                                    _receiveTokenSource?.Cancel();

                                    await _heartbeatTask;
                                }
                                catch (Exception ex)
                                {
                                    _logger.Error(ex, "心跳任务取消失败");
                                }

                                _heartbeatTask = null;
                            }

                            //// 先发送身份验证消息
                            //await DoResumeOrIdentify();

                            // 再处理心跳
                            _heartbeatAck = true;
                            _heartbeatTimes.Clear();
                            Latency = 0;
                            _heartbeatTask = RunHeartbeatAsync((int)_heartbeatInterval, _receiveTokenSource.Token);
                        }
                        break;

                    case GatewayOpCode.Heartbeat:
                        {
                            _logger.Information("Received Heartbeat {@0}", Account.ChannelId);

                            // 立即发送心跳
                            var heartbeatMessage = new { op = 1, d = _sequence };
                            await SendMessageAsync(heartbeatMessage);

                            _logger.Information("Received Heartbeat 消息已发送 {@0}", Account.ChannelId);
                        }
                        break;

                    case GatewayOpCode.HeartbeatAck:
                        {
                            _logger.Information("Received HeartbeatAck {@0}", Account.ChannelId);

                            if (_heartbeatTimes.TryDequeue(out long time))
                            {
                                Latency = (int)(Environment.TickCount - time);
                                _heartbeatAck = true;
                            }
                        }
                        break;

                    case GatewayOpCode.InvalidSession:
                        {
                            _logger.Warning("Received InvalidSession {@0}", Account.ChannelId);
                            _logger.Warning("Failed to resume previous session {@0}", Account.ChannelId);

                            _sessionId = null;
                            _sequence = null;
                            _resumeGatewayUrl = null;

                            HandleFailure(CLOSE_CODE_EXCEPTION, "无效授权，创建新的连接");
                        }
                        break;

                    case GatewayOpCode.Reconnect:
                        {
                            _logger.Warning("Received Reconnect {@0}", Account.ChannelId);

                            HandleFailure(CLOSE_CODE_RECONNECT, "收到重连请求，将自动重连");
                        }
                        break;

                    case GatewayOpCode.Resume:
                        {
                            _logger.Information("Resume {@0}", Account.ChannelId);

                            OnSocketSuccess();
                        }
                        break;

                    case GatewayOpCode.Dispatch:
                        {
                            _logger.Information("Received Dispatch {@0}, {@1}", type, Account.ChannelId);
                            HandleDispatch(payload);
                        }
                        break;

                    default:
                        _logger.Warning("Unknown OpCode ({@0}) {@1}", opCode, Account.ChannelId);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling {opCode}{(type != null ? $" ({type})" : "")}, {Account.ChannelId}");
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
                _messageQueue.Enqueue(data);
            }
        }

        private void MessageQueueDoWork()
        {
            while (true)
            {
                while (_messageQueue.TryDequeue(out var message))
                {
                    try
                    {
                        _botListener.OnMessage(message);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "处理消息队列时发生异常 {@0}", Account.ChannelId);
                    }
                }

                Thread.Sleep(10);
            }
        }


        /// <summary>
        /// 创建授权信息
        /// </summary>
        /// <returns></returns>
        private JsonElement CreateAuthData()
        {
            var uaParser = Parser.GetDefault();
            var agent = uaParser.Parse(Account.UserAgent);
            var connectionProperties = new
            {
                browser = agent.UA.Family,
                browser_user_agent = Account.UserAgent,
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
                token = Account.UserToken
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
            _logger.Error("用户 WebSocket 连接失败, 代码 {0}: {1}, {2}", code, reason, Account.ChannelId);

            if (!Running)
            {
                NotifyWss(code, reason);
            }

            Running = false;

            if (code >= 4000)
            {
                _logger.Warning("用户无法重新连接， 由 {0}({1}) 关闭 {2}, 尝试新连接... ", code, reason, Account.ChannelId);
                TryNewConnect();
            }
            else if (code == 2001)
            {
                _logger.Warning("用户由 {0}({1}) 关闭, 尝试重新连接... {2}", code, reason, Account.ChannelId);
                TryReconnect();
            }
            else
            {
                _logger.Warning("用户由 {0}({1}) 关闭, 尝试新连接... {2}", code, reason, Account.ChannelId);
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
                if (_isDispose)
                {
                    return;
                }

                var success = StartAsync(true).ConfigureAwait(false).GetAwaiter().GetResult();
                if (!success)
                {
                    _logger.Warning("用户重新连接失败 {@0}，尝试新连接", Account.ChannelId);

                    Thread.Sleep(1000);
                    TryNewConnect();
                }
            }
            catch (Exception e)
            {
                _logger.Warning(e, "用户重新连接异常 {@0}，尝试新连接", Account.ChannelId);

                Thread.Sleep(1000);
                TryNewConnect();
            }
        }

        /// <summary>
        /// 新的连接
        /// </summary>
        private void TryNewConnect()
        {
            if (_isDispose)
            {
                return;
            }

            var isLock = LocalLock.TryLock("TryNewConnect", TimeSpan.FromSeconds(3), () =>
            {
                for (int i = 1; i <= CONNECT_RETRY_LIMIT; i++)
                {
                    try
                    {
                        // 如果 5 分钟内失败次数超过限制，则禁用账号
                        var ncKey = $"TryNewConnect_{Account.ChannelId}";
                        _memoryCache.TryGetValue(ncKey, out int count);
                        if (count > CONNECT_RETRY_LIMIT)
                        {
                            _logger.Warning("新的连接失败次数超过限制，禁用账号");
                            DisableAccount("新的连接失败次数超过限制，禁用账号");
                            return;
                        }
                        _memoryCache.Set(ncKey, count + 1, TimeSpan.FromMinutes(5));

                        var success = StartAsync(false).ConfigureAwait(false).GetAwaiter().GetResult();
                        if (success)
                        {
                            return;
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.Warning(e, "用户新连接失败, 第 {@0} 次, {@1}", i, Account.ChannelId);

                        Thread.Sleep(5000);
                    }
                }

                if (WebSocket == null || WebSocket.State != WebSocketState.Open)
                {
                    _logger.Error("由于无法重新连接，自动禁用账号");

                    DisableAccount("由于无法重新连接，自动禁用账号");
                }
            });

            if (!isLock)
            {
                _logger.Warning("新的连接作业正在执行中，禁止重复执行");
            }
        }

        /// <summary>
        /// 停止并禁用账号
        /// </summary>
        public void DisableAccount(string msg)
        {
            try
            {
                // 保存
                Account.Enable = false;
                Account.DisabledReason = msg;

                DbHelper.Instance.AccountStore.Update(Account);

                _discordInstance?.ClearAccountCache(Account.Id);
                _discordInstance?.Dispose();

                // 尝试自动登录
                var sw = new Stopwatch();
                var setting = GlobalConfiguration.Setting;
                var info = new StringBuilder(); 
                var account = Account;
                if (setting.EnableAutoLogin)
                {
                    sw.Stop();
                    info.AppendLine($"{account.Id}尝试自动登录...");
                    sw.Restart();

                    try
                    {
                        // 开始尝试自动登录
                        var suc = DiscordAccountHelper.AutoLogin(account, true);

                        if (suc)
                        {
                            sw.Stop();
                            info.AppendLine($"{account.Id}自动登录请求成功...");
                            sw.Restart();
                        }
                        else
                        {
                            sw.Stop();
                            info.AppendLine($"{account.Id}自动登录请求失败...");
                            sw.Restart();
                        }
                    }
                    catch (Exception exa)
                    {
                        _logger.Error(exa, "Account({@0}) auto login fail, disabled: {@1}", account.ChannelId, exa.Message);

                        sw.Stop();
                        info.AppendLine($"{account.Id}自动登录请求异常...");
                        sw.Restart();
                    }
                }

            }
            catch (Exception ex)
            {
                _logger.Error(ex, "禁用账号失败 {@0}", Account.ChannelId);
            }
            finally
            {
                // 邮件通知
                var smtp = GlobalConfiguration.Setting?.Smtp;
                EmailJob.Instance.EmailSend(smtp, $"MJ账号禁用通知-{Account.ChannelId}",
                    $"{Account.ChannelId}, {Account.DisabledReason}");
            }
        }

        /// <summary>
        /// 写 info 消息
        /// </summary>
        /// <param name="msg"></param>
        private void LogInfo(string msg)
        {
            _logger.Information(msg + ", {@ChannelId}", Account.ChannelId);
        }

        /// <summary>
        /// 如果打开了，则关闭 wss
        /// </summary>
        private void CloseSocket(bool reconnect = false)
        {
            try
            {
                try
                {
                    if (_receiveTokenSource != null)
                    {
                        LogInfo("强制取消消息 token");
                        _receiveTokenSource?.Cancel();
                        _receiveTokenSource?.Dispose();
                    }
                }
                catch
                {
                }

                try
                {
                    if (_receiveTask != null)
                    {
                        LogInfo("强制释放消息 task");
                        _receiveTask?.Wait(1000);
                        _receiveTask?.Dispose();
                    }
                }
                catch
                {
                }

                try
                {
                    if (_heartbeatTask != null)
                    {
                        LogInfo("强制释放心跳 task");
                        _heartbeatTask?.Wait(1000);
                        _heartbeatTask?.Dispose();
                    }
                }
                catch
                {
                }

                try
                {
                    // 强制关闭
                    if (WebSocket != null && WebSocket.State != WebSocketState.Closed)
                    {
                        LogInfo("强制关闭 wss close");

                        if (reconnect)
                        {
                            // 重连使用 4000 断开
                            var status = (WebSocketCloseStatus)4000;
                            var closeTask = Task.Run(() => WebSocket.CloseOutputAsync(status, "", new CancellationToken()));
                            if (!closeTask.Wait(5000))
                            {
                                _logger.Warning("WebSocket 关闭操作超时 {@0}", Account.ChannelId);

                                // 如果关闭操作超时，则强制中止连接
                                WebSocket?.Abort();
                            }
                        }
                        else
                        {
                            var closeTask = Task.Run(() => WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "强制关闭", CancellationToken.None));
                            if (!closeTask.Wait(5000))
                            {
                                _logger.Warning("WebSocket 关闭操作超时 {@0}", Account.ChannelId);

                                // 如果关闭操作超时，则强制中止连接
                                WebSocket?.Abort();
                            }
                        }
                    }
                }
                catch
                {
                }

                // 强制关闭
                try
                {
                    if (WebSocket != null && (WebSocket.State == WebSocketState.Open || WebSocket.State == WebSocketState.CloseReceived))
                    {
                        LogInfo("强制关闭 wss open");

                        WebSocket.Abort();
                        WebSocket.Dispose();
                    }
                }
                catch
                {
                }
            }
            catch
            {
                // do
            }
            finally
            {
                WebSocket = null;
                _receiveTokenSource = null;
                _receiveTask = null;
                _heartbeatTask = null;

                LogInfo("WebSocket 资源已释放");
            }

            // 暂时不需要延迟
            //// 延迟以确保所有资源正确释放
            //Thread.Sleep(1000);
        }

        /// <summary>
        /// 通知错误或成功
        /// </summary>
        /// <param name="code"></param>
        /// <param name="reason"></param>
        private void NotifyWss(int code, string reason)
        {
            if (!Account.Lock)
            {
                Account.DisabledReason = reason;
            }

            // 保存
            DbHelper.Instance.AccountStore.Update("Enable,DisabledReason", Account);
            _discordInstance?.ClearAccountCache(Account.Id);
        }

        /// <summary>
        /// 释放
        /// </summary>
        public void Dispose()
        {
            try
            {
                _isDispose = true;

                CloseSocket();

                _messageQueue?.Clear();
                _messageQueueTask?.Dispose();
            }
            catch
            {
            }

            try
            {
                WebSocket?.Dispose();
                _botListener?.Dispose();

                // 锁不需要释放
                //_stateLock?.Dispose();
            }
            catch
            {

            }
        }

        /// <summary>
        /// 连接成功
        /// </summary>
        private void OnSocketSuccess()
        {
            Running = true;
            _discordInstance.DefaultSessionId = _sessionId;

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
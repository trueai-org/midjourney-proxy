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

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CSRedis;
using RestSharp;
using Serilog;
using UAParser;

namespace Midjourney.Services
{
    /// <summary>
    /// 处理与 Discord WebSocket 连接的类，并提供启动和消息监听功能
    /// https://discord.com/developers/docs/topics/gateway-events
    /// </summary>
    public class DiscordWebSocketService : IDisposable
    {
        /// <summary>
        /// WS 初始化连接锁 key
        /// </summary>
        private static string InitConnectLockKey(DiscordAccount account) => $"DiscordAccountInitConnectLock:{account.ChannelId}";

        /// <summary>
        /// WS 判断是否初始化连接 - 如果存在说明正在连接/连接中
        /// </summary>
        public static bool IsInitConnect(DiscordAccount account) => RedisHelper.Instance.ExistsLock(InitConnectLockKey(account));

        /// <summary>
        /// 创建 WebSocketManager 并启动连接
        /// </summary>
        /// <param name="discordInstance"></param>
        /// <returns></returns>
        public static async Task CreateAndStartAsync(DiscordService discordInstance)
        {
            if (discordInstance == null || discordInstance.Account == null || discordInstance.Account.Enable == false)
            {
                return;
            }

            if (IsInitConnect(discordInstance.Account))
            {
                return;
            }

            var webSocketManager = new DiscordWebSocketService(discordInstance);
            var started = await webSocketManager.StartAsync();
            if (!started)
            {
                webSocketManager?.Dispose();
                discordInstance.Wss = null;
            }
        }

        /// <summary>
        /// WS 初始化连接中 key
        /// </summary>
        private static string InitConnectingKey(DiscordAccount account) => $"DiscordAccountInitConnecting:{account.ChannelId}";

        /// <summary>
        /// WS 判断是否初始化连接中
        /// </summary>
        public static bool IsInitConnecting(DiscordAccount account) => RedisHelper.Exists(InitConnectingKey(account));

        private readonly IFreeSql _freeSql = FreeSqlHelper.FreeSql;

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

        private readonly ILogger _logger = Log.Logger;

        private readonly WebProxy _webProxy;
        private readonly DiscordService _discordInstance;

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

        public DiscordWebSocketService(DiscordService discordInstance)
        {
            var setting = GlobalConfiguration.Setting;

            WebProxy webProxy = null;
            if (webProxy == null && !string.IsNullOrEmpty(setting.Proxy?.Host))
            {
                webProxy = new WebProxy(setting.Proxy.Host, setting.Proxy.Port ?? 80);
            }

            _webProxy = webProxy;
            _discordInstance = discordInstance;

            _messageQueueTask = new Task(async () => { await MessageQueueDoWork(); }, TaskCreationOptions.LongRunning);
            _messageQueueTask.Start();

            // 跟踪连接实例
            discordInstance.Wss = this;
        }

        private DiscordAccount Account => _discordInstance?.Account;

        /// <summary>
        /// WS 初始化连接锁 key
        /// </summary>
        private CSRedisClientLock _initConnectLock;

        /// <summary>
        /// WS 设置初始化连接中
        /// </summary>
        public void SetInitConnecting()
        {
            RedisHelper.Set(InitConnectingKey(Account), DateTime.Now, TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// WS 移除初始化连接中
        /// </summary>
        public void RemoveInitConnecting()
        {
            RedisHelper.Del(InitConnectingKey(Account));
        }

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

                // 关闭现有连接并取消相关任务
                CloseSocket(reconnect);

                // 尝试获取
                _initConnectLock = RedisHelper.Instance.TryLock(InitConnectLockKey(Account), 30, true);
                if (_initConnectLock == null)
                {
                    _logger.Debug("用户连接处理中，跳过此次连接请求 {@0}", Account.ChannelId);
                    return false;
                }

                SetInitConnecting();

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

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "用户 WebSocket 连接异常 {@0}", Account.ChannelId);

                await HandleFailure(CLOSE_CODE_EXCEPTION, "用户 WebSocket 连接异常");
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
            return !string.IsNullOrWhiteSpace(resumeGatewayUrl) ? resumeGatewayUrl : DiscordHelper.GetWss();
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
                                await HandleFailure((int)result.CloseStatus, result.CloseStatusDescription);
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
                            await HandleFailure(CLOSE_CODE_RECONNECT, "服务器未响应上次的心跳，将进行重连");
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
                await TryReconnect();
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

                            await HandleFailure(CLOSE_CODE_EXCEPTION, "无效授权，创建新的连接");
                        }
                        break;

                    case GatewayOpCode.Reconnect:
                        {
                            _logger.Warning("Received Reconnect {@0}", Account.ChannelId);

                            await HandleFailure(CLOSE_CODE_RECONNECT, "收到重连请求，将自动重连");
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

        private async Task MessageQueueDoWork()
        {
            while (!_isDispose)
            {
                while (!_isDispose && _messageQueue.TryDequeue(out var message))
                {
                    try
                    {
                        await OnMessage(message);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "处理消息队列时发生异常 {@0}, {@1}", Account.ChannelId, message);

                        await Task.Delay(100);
                    }
                }

                await Task.Delay(10);
            }
        }

        /// <summary>
        /// 处理接收到用户 ws 消息
        /// </summary>
        /// <param name="raw"></param>
        public async Task OnMessage(JsonElement raw)
        {
            try
            {
                var setting = GlobalConfiguration.Setting;

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
                                        _freeSql.Update(Account);
                                        Account.ClearCache();

                                        try
                                        {
                                            // 通知验证服务器
                                            if (!string.IsNullOrWhiteSpace(setting.CaptchaNotifyHook) && !string.IsNullOrWhiteSpace(setting.CaptchaServer))
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
                                                    var notifyUrl = $"{setting.CaptchaServer.Trim().TrimEnd('/')}/cf/verify";
                                                    var client = new RestClient();
                                                    var request = new RestRequest(notifyUrl, Method.Post);
                                                    request.AddHeader("Content-Type", "application/json");
                                                    var body = new CaptchaVerfyRequest
                                                    {
                                                        Url = hashUrl,
                                                        State = Account.ChannelId,
                                                        NotifyHook = setting.CaptchaNotifyHook,
                                                        Secret = setting.CaptchaNotifySecret
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

                                                Task.Run(async () =>
                                                {
                                                    try
                                                    {
                                                        await EmailHelper.Instance.EmailSend(setting.Smtp, $"CF自动真人验证-{Account.ChannelId}", hashUrl);
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        _logger.Error(ex, "邮件发送失败");
                                                    }
                                                });
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

                                                            Task.Run(async () =>
                                                            {
                                                                try
                                                                {
                                                                    await EmailHelper.Instance.EmailSend(setting.Smtp, $"CF手动真人验证-{Account.ChannelId}", url);
                                                                }
                                                                catch (Exception ex)
                                                                {
                                                                    _logger.Error(ex, "邮件发送失败");
                                                                }
                                                            });
                                                        }
                                                    }
                                                }

                                                Account.DisabledReason = "CF 人工验证...";
                                                _freeSql.Update(Account);
                                                Account.ClearCache();
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

                    _freeSql.Update("Components,NijiComponents", Account);
                    Account.ClearCache();

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
                            _freeSql.Update("NijiComponents", Account);
                            Account.ClearCache();
                        }
                        else if (applicationId == Constants.MJ_APPLICATION_ID)
                        {
                            Account.Components = eventDataMsg.Components;
                            _freeSql.Update("Components", Account);
                            Account.ClearCache();
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

                    _freeSql.Update("Components,NijiComponents", Account);
                    Account.ClearCache();

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
                                            var hash = DiscordHelper.GetMessageHash(imgUrl);
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
                                            try
                                            {
                                                var acc = Account;
                                                var dic = ParseDiscordData(description.GetString());

                                                foreach (var d in dic)
                                                {
                                                    if (d.Key == "Job Mode")
                                                    {
                                                        if (applicationId == Constants.NIJI_APPLICATION_ID)
                                                        {
                                                            acc.SetProperty($"Niji {d.Key}", d.Value);
                                                        }
                                                        else if (applicationId == Constants.MJ_APPLICATION_ID)
                                                        {
                                                            acc.SetProperty(d.Key, d.Value);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        acc.SetProperty(d.Key, d.Value);
                                                    }
                                                }

                                                acc.InfoUpdated = DateTime.Now;

                                                // 快速时长校验
                                                // 如果 fastTime <= 0.2，则标记为快速用完
                                                var fastTime = acc.FastTimeRemaining?.ToString()?.Split('/')?.FirstOrDefault()?.Trim();

                                                // 0.2h = 12 分钟 = 12 次
                                                var ftime = 0.0;
                                                if (!string.IsNullOrWhiteSpace(fastTime) && double.TryParse(fastTime, out ftime) && ftime <= 0.2)
                                                {
                                                    acc.FastExhausted = true;
                                                }

                                                // 自动设置慢速，如果快速用完
                                                if (acc.FastExhausted == true && acc.EnableAutoSetRelax == true && acc.Mode != GenerationSpeedMode.RELAX)
                                                {
                                                    acc.AllowModes = [GenerationSpeedMode.RELAX];
                                                    acc.CoreSize = 3;
                                                }

                                                // 计算快速可用次数
                                                var fastAvailableCount = (int)Math.Ceiling(ftime * 60);
                                                CounterHelper.SetFastTaskAvailableCount(acc.ChannelId, fastAvailableCount);

                                                _freeSql.Update<DiscordAccount>()
                                                    .Set(c => c.InfoUpdated, acc.InfoUpdated)
                                                    .Set(c => c.Properties, acc.Properties)
                                                    .Set(c => c.FastExhausted, acc.FastExhausted)
                                                    .Set(c => c.AllowModes, acc.AllowModes)
                                                    .Set(c => c.CoreSize, acc.CoreSize)
                                                    .Where(c => c.Id == acc.Id)
                                                    .ExecuteAffrows();
                                                acc.ClearCache();

                                                Log.Information("Discord 同步信息完成，ChannelId={@0}, 预估快速剩余次数={@1}",
                                                    acc.ChannelId, fastAvailableCount);

                                                // 切换慢速模式
                                                if (acc.FastExhausted)
                                                {
                                                    DiscordSetRelax();
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Log.Error(ex, "解析 info 消息异常 {@0}", description.GetString());
                                            }
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

                                    _freeSql.Update("NijiComponents,NijiSettingsMessageId", Account);
                                    Account.ClearCache();
                                }
                                else if (applicationId == Constants.MJ_APPLICATION_ID)
                                {
                                    Account.Components = eventDataMsg.Components;
                                    Account.SettingsMessageId = id;

                                    _freeSql.Update("Components,SettingsMessageId", Account);
                                    Account.ClearCache();
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
                                            task.Fail("任务失败，请重试");
                                        }

                                        // 标记快速模式已经用完了
                                        Account.MarkFastExhausted();
                                        DiscordSetRelax();

                                        // 如果开启自动切换慢速模式
                                        if (Account.EnableAutoSetRelax == true)
                                        {
                                        }
                                        else
                                        {
                                            // 你的处理逻辑
                                            _logger.Warning($"账号 {Account.GetDisplay()} 用量已经用完, 自动禁用账号");

                                            // 5s 后禁用账号
                                            _ = Task.Run(() =>
                                             {
                                                 try
                                                 {
                                                     Thread.Sleep(5 * 1000);

                                                     // 保存
                                                     Account.Enable = false;
                                                     Account.DisabledReason = "账号用量已经用完";

                                                     _freeSql.Update<DiscordAccount>()
                                                     .Set(c => c.Enable, Account.Enable)
                                                     .Set(c => c.DisabledReason, Account.DisabledReason)
                                                     .Where(c => c.Id == Account.Id)
                                                     .ExecuteAffrows();

                                                     Account.ClearCache();

                                                     _discordInstance?.Dispose();

                                                     _ = EmailHelper.Instance.EmailSend(setting.Smtp,
                                                                    $"MJ账号禁用通知-{Account.ChannelId}",
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
                                        _ = Task.Run(() =>
                                        {
                                            try
                                            {
                                                Thread.Sleep(5 * 1000);

                                                // 保存
                                                Account.Enable = false;
                                                Account.DisabledReason = $"{title}, {desc}";
                                                _freeSql.Update(Account);
                                                Account.ClearCache();

                                                _discordInstance?.Dispose();

                                                Task.Run(async () =>
                                                {
                                                    try
                                                    {
                                                        await EmailHelper.Instance.EmailSend(setting.Smtp,
                                                            $"MJ账号禁用通知-{Account.ChannelId}",
                                                            $"{Account.ChannelId}, {Account.DisabledReason}");
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        _logger.Error(ex, "邮件发送失败");
                                                    }
                                                });
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
                                                var task = _discordInstance.GetRunningTasks().Where(c => c.MessageId == metaId
                                                || c.MessageIds.Contains(metaId) || c.InteractionMetadataId == metaId).FirstOrDefault();
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
                                                task.PromptFull = MjMessageParser.GetFullPrompt(contentStr);
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

                // 加分布式锁，防止重复处理消息
                using var redisLock = RedisHelper.Instance.Lock($"DiscordMessageHandle:{eventData.Id}", 5);
                if (redisLock == null)
                {
                    return;
                }

                // 如果消息类型是 CREATE
                // 则再次处理消息确认事件，确保消息的高可用
                if (messageType == MessageType.CREATE)
                {
                    await Task.Delay(10);

                    if (eventData != null && (eventData.ChannelId == Account.ChannelId || Account.SubChannelValues.ContainsKey(eventData.ChannelId)))
                    {
                        await HandleMessage(_discordInstance, messageType.Value, eventData);
                    }
                }
                // describe 重新提交
                // MJ::Picread::Retry
                else if (eventData.Embeds.Count > 0 && eventData.Author?.Bot == true && eventData.Components.Count > 0
                    && eventData.Components.First().Components.Any(x => x.CustomId?.Contains("PicReader") == true))
                {
                    var em = eventData.Embeds.FirstOrDefault();
                    if (em != null && !string.IsNullOrWhiteSpace(em.Description))
                    {
                        await HandleDescribe(_discordInstance, MessageType.CREATE, eventData);
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(eventData.Content) && eventData.Content.Contains("%") && eventData.Author?.Bot == true)
                    {
                        await HandleMessage(_discordInstance, messageType.Value, eventData);
                    }
                    else if (eventData.InteractionMetadata?.Name == "describe")
                    {
                        await HandleDescribe(_discordInstance, MessageType.CREATE, eventData);
                    }
                    else if (eventData.InteractionMetadata?.Name == "shorten"
                        || eventData.Embeds?.FirstOrDefault()?.Footer?.Text.Contains("Click on a button to imagine one of the shortened prompts") == true)
                    {
                        await HandleShorten(_discordInstance, MessageType.CREATE, eventData);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "处理 wss 消息异常 {@0}", raw.ToString());
            }
        }

        /// <summary>
        /// Discord 账号，调用接口切换慢速（1小时最多执行1次）
        /// </summary>
        public void DiscordSetRelax()
        {
            if (Account.EnableAutoSetRelax == true && Account.FastExhausted)
            {
                // 切换到慢速模式
                // 加锁切换到慢速模式
                // 执行切换慢速命令
                // 如果当前不是慢速，则切换慢速，加锁切换
                Task.Run(async () =>
                {
                    using var isLock = RedisHelper.Lock($"DiscordSetRelax:{Account.ChannelId}", 10);
                    if (isLock != null)
                    {
                        try
                        {
                            // 计数器，1 小时最多切换 1 次
                            var key = $"DiscordSetRelaxCount:{Account.ChannelId}";
                            var count = RedisHelper.IncrBy(key, 1);
                            if (count > 1)
                            {
                                _logger.Warning("切换慢速跳过，1小时内已切换过 {@0}", Account.ChannelId);
                                return;
                            }

                            RedisHelper.Expire(key, TimeSpan.FromHours(1));

                            // 切换到慢速模式
                            // 加锁切换到慢速模式
                            // 执行切换慢速命令
                            // 如果当前不是慢速，则切换慢速，加锁切换
                            if (Account.MjFastModeOn || Account.NijiFastModeOn)
                            {
                                await Task.Delay(2500);
                                await _discordInstance?.RelaxAsync(SnowFlake.NextId(), EBotType.MID_JOURNEY);

                                await Task.Delay(2500);
                                await _discordInstance?.RelaxAsync(SnowFlake.NextId(), EBotType.NIJI_JOURNEY);

                                _logger.Information("切换慢速成功 {@0}", Account.ChannelId);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "切换慢速异常 {@0}", Account.ChannelId);
                        }
                    }
                });
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
        private async Task HandleFailure(int code, string reason)
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
                await TryNewConnect();
            }
            else if (code == 2001)
            {
                _logger.Warning("用户由 {0}({1}) 关闭, 尝试重新连接... {2}", code, reason, Account.ChannelId);
                await TryReconnect();
            }
            else
            {
                _logger.Warning("用户由 {0}({1}) 关闭, 尝试新连接... {2}", code, reason, Account.ChannelId);
                await TryNewConnect();
            }
        }

        /// <summary>
        /// 重新连接
        /// </summary>
        private async Task TryReconnect()
        {
            try
            {
                if (_isDispose)
                {
                    return;
                }

                var success = await StartAsync(true);
                if (!success)
                {
                    _logger.Warning("用户重新连接失败 {@0}，尝试新连接", Account.ChannelId);

                    await Task.Delay(1000);

                    await TryNewConnect();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "用户重新连接异常 {@0}，尝试新连接", Account.ChannelId);

                await Task.Delay(1000);

                await TryNewConnect();
            }
        }

        /// <summary>
        /// 新的连接
        /// </summary>
        private async Task TryNewConnect()
        {
            if (_isDispose)
            {
                return;
            }

            await using var lockObj = await AdaptiveLock.LockAsync("TryNewConnect", 3);
            if (lockObj.IsAcquired == false)
            {
                _logger.Warning("新的连接作业正在执行中，禁止重复执行");
                return;
            }

            for (int i = 1; i <= CONNECT_RETRY_LIMIT; i++)
            {
                try
                {
                    // 如果 5 分钟内失败次数超过限制，则禁用账号
                    var ncKey = $"DiscordTryNewConnectCount:{Account.ChannelId}";
                    var count = AdaptiveCache.AddOrUpdate(ncKey, 1, (k, v) => ++v, TimeSpan.FromMinutes(5));
                    if (count > CONNECT_RETRY_LIMIT)
                    {
                        _logger.Warning("新的连接失败次数超过限制，禁用账号");

                        DisableAccount("新的连接失败次数超过限制，禁用账号");

                        return;
                    }

                    var success = await StartAsync();
                    if (success)
                    {
                        return;
                    }
                }
                catch (Exception e)
                {
                    _logger.Warning(e, "用户新连接失败, 第 {@0} 次, {@1}", i, Account.ChannelId);

                    await Task.Delay(5000);
                }
            }

            if (WebSocket == null || WebSocket.State != WebSocketState.Open)
            {
                _logger.Error("由于无法重新连接，自动禁用账号");

                DisableAccount("由于无法重新连接，自动禁用账号");
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
                FreeSqlHelper.FreeSql.Update(Account);
                Account.ClearCache();

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
                        var suc = DiscordAccountService.AutoLogin(account, true);

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

                Task.Run(async () =>
                {
                    try
                    {
                        await EmailHelper.Instance.EmailSend(smtp,
                            $"MJ账号禁用通知-{Account.ChannelId}",
                            $"{Account.ChannelId}, {Account.DisabledReason}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "邮件发送失败");
                    }
                });
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

                _initConnectLock?.Dispose();

                LogInfo("WebSocket 资源已释放");
            }
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
            FreeSqlHelper.FreeSql.Update("Enable,DisabledReason", Account);
            Account.ClearCache();
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
            RemoveInitConnecting();

            _discordInstance.DefaultSessionId = _sessionId;

            NotifyWss(ReturnCode.SUCCESS, "");
        }

        protected string GetMessageId(EventData message)
        {
            return message.Id;
        }

        protected string GetMessageContent(EventData message)
        {
            return message.Content;
        }

        protected string GetFullPrompt(EventData message)
        {
            return MjMessageParser.GetFullPrompt(message.Content);
        }

        protected EBotType? GetBotType(EventData message)
        {
            var botId = message.Author?.Id;
            EBotType? botType = null;
            if (botId == Constants.NIJI_APPLICATION_ID)
            {
                botType = EBotType.NIJI_JOURNEY;
            }
            else if (botId == Constants.MJ_APPLICATION_ID)
            {
                botType = EBotType.MID_JOURNEY;
            }

            return botType;
        }

        protected bool HasImage(EventData message)
        {
            return message?.Attachments?.Count > 0;
        }

        protected string GetImageUrl(EventData message)
        {
            if (message?.Attachments?.Count > 0)
            {
                return ReplaceCdnUrl(message.Attachments.FirstOrDefault()?.Url);
            }

            return default;
        }

        protected string ReplaceCdnUrl(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return imageUrl;

            string cdn = DiscordHelper.GetCdn();
            if (imageUrl.StartsWith(cdn))
                return imageUrl;

            return imageUrl.Replace(DiscordHelper.DISCORD_CDN_URL, cdn);
        }

        protected async Task FinishTask(TaskInfo task, EventData message, MessageParseResult messageParseResult)
        {
            // 设置图片信息
            var image = message.Attachments?.FirstOrDefault();
            if (task != null && image != null)
            {
                task.Width = image.Width;
                task.Height = image.Height;
                task.Url = image.Url;
                task.ProxyUrl = image.ProxyUrl;
                task.Size = image.Size;
                task.ContentType = image.ContentType;
            }

            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, message.Id);
            task.SetProperty(Constants.TASK_PROPERTY_FLAGS, Convert.ToInt32(message.Flags));

            var messageHash = task.GetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, string.Empty);
            if (string.IsNullOrWhiteSpace(messageHash))
            {
                messageHash = DiscordHelper.GetMessageHash(task.ImageUrl);

                task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
                task.JobId = messageHash;
            }

            task.Buttons = message.Components.SelectMany(x => x.Components)
                .Select(btn =>
                {
                    return new CustomComponentModel
                    {
                        CustomId = btn.CustomId ?? string.Empty,
                        Emoji = btn.Emoji?.Name ?? string.Empty,
                        Label = btn.Label ?? string.Empty,
                        Style = (int?)btn.Style ?? 0,
                        Type = (int?)btn.Type ?? 0,
                    };
                }).Where(c => c != null && !string.IsNullOrWhiteSpace(c.CustomId)).ToList();

            if (string.IsNullOrWhiteSpace(task.Description))
            {
                task.Description = "Submit success";
            }

            if (string.IsNullOrWhiteSpace(task.FailReason))
            {
                task.FailReason = "";
            }

            if (string.IsNullOrWhiteSpace(task.State))
            {
                task.State = "";
            }

            // 通过速度模式匹配，计算最终速度 fast/relaxed/turbo
            switch (messageParseResult?.Mode)
            {
                case "fast":
                    task.Mode = GenerationSpeedMode.FAST;
                    break;

                case "relaxed":
                    task.Mode = GenerationSpeedMode.RELAX;
                    break;

                case "turbo":
                    task.Mode = GenerationSpeedMode.TURBO;
                    break;

                default:
                    break;
            }

            await task.SuccessAsync();
        }

        /// <summary>
        /// 处理图生文完成消息
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="messageType"></param>
        /// <param name="message"></param>
        public async Task HandleDescribe(DiscordService instance, MessageType messageType, EventData message)
        {
            if (instance == null || message == null)
            {
                return;
            }

            // 跳过 Waiting to start 消息
            if (!string.IsNullOrWhiteSpace(message.Content) && message.Content.Contains("(Waiting to start)"))
            {
                return;
            }

            if (messageType == MessageType.CREATE && message.Author.Bot == true && message.Author.Username.Contains("journey Bot", StringComparison.OrdinalIgnoreCase))
            {
                // 图生文完成
                if (message.Embeds.Count > 0 && !string.IsNullOrWhiteSpace(message.Embeds.FirstOrDefault()?.Image?.Url))
                {
                    var msgId = GetMessageId(message);

                    var task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.MessageId == msgId).FirstOrDefault();
                    if (task == null && !string.IsNullOrWhiteSpace(message.InteractionMetadata?.Id))
                    {
                        task = instance.FindRunningTask(c =>
                        (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.InteractionMetadataId == message.InteractionMetadata.Id).FirstOrDefault();
                    }

                    if (task == null)
                    {
                        return;
                    }

                    var imageUrl = message.Embeds.First().Image?.Url;
                    var messageHash = DiscordHelper.GetMessageHash(imageUrl);

                    var finalPrompt = message.Embeds.First().Description;

                    task.PromptEn = finalPrompt;
                    task.MessageId = msgId;

                    if (!task.MessageIds.Contains(msgId))
                        task.MessageIds.Add(msgId);

                    task.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
                    task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, finalPrompt);
                    task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);

                    task.ImageUrl = imageUrl;
                    task.JobId = messageHash;

                    await FinishTask(task, message, null);

                    task.Awake();
                }
            }
        }

        /// <summary>
        /// 处理 shorten 完成消息
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="messageType"></param>
        /// <param name="message"></param>
        public async Task HandleShorten(DiscordService instance, MessageType messageType, EventData message)
        {
            if (instance == null || message == null)
            {
                return;
            }

            if (message.InteractionMetadata?.Name != "shorten"
                && message.Embeds?.FirstOrDefault()?.Footer?.Text.Contains("Click on a button to imagine one of the shortened prompts") != true)
            {
                return;
            }

            // 跳过 Waiting to start 消息
            if (!string.IsNullOrWhiteSpace(message.Content) && message.Content.Contains("(Waiting to start)"))
            {
                return;
            }

            if (messageType == MessageType.CREATE
                && message.Author.Bot == true
                && message.Author.Username.Contains("journey Bot", StringComparison.OrdinalIgnoreCase))
            {
                // 分析 prompt 完成
                if (message.Embeds.Count > 0)
                {
                    var msgId = GetMessageId(message);

                    var task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.MessageId == msgId).FirstOrDefault();

                    if (task == null && !string.IsNullOrWhiteSpace(message.InteractionMetadata?.Id))
                    {
                        task = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) &&
                        c.InteractionMetadataId == message.InteractionMetadata.Id).FirstOrDefault();
                    }

                    if (task == null)
                    {
                        return;
                    }

                    var desc = message.Embeds.First().Description;

                    task.Description = desc;
                    task.MessageId = msgId;

                    if (!task.MessageIds.Contains(msgId))
                        task.MessageIds.Add(msgId);

                    task.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
                    task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, desc);

                    await FinishTask(task, message, null);
                    task.Awake();
                }
            }
        }

        /// <summary>
        /// 处理进度/完成消息
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="messageType"></param>
        /// <param name="message"></param>
        public async Task HandleMessage(DiscordService instance, MessageType messageType, EventData message)
        {
            if (messageType != MessageType.UPDATE && messageType != MessageType.CREATE)
            {
                return;
            }

            if (instance == null || message == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(message.Content) && message.Content.Contains("(Waiting to start)"))
            {
                return;
            }

            var content = GetMessageContent(message);
            if (!string.IsNullOrWhiteSpace(content)
                && content != "Displaying..."
                && content != "Animating...")
            {
                var parseResult = MjMessageParser.Parse(content);
                if (parseResult == null)
                {
                    Log.Error("解析消息内容失败: {@0}", content);
                }
                else
                {
                    // 判断消息 action
                    switch (parseResult.Action)
                    {
                        case TaskAction.IMAGINE:
                            {
                                await FindAndFinishImageTask(instance, message, parseResult, messageType);
                            }
                            break;

                        case TaskAction.UPSCALE:
                            {
                                await FindAndFinishUTask(instance, message, parseResult, messageType);
                            }
                            break;

                        case TaskAction.VARIATION:
                            {
                                await FindAndFinishVaryTask(instance, message, parseResult, messageType);
                            }
                            break;

                        case TaskAction.PAN:
                            {
                                await FindAndFinishPanTask(instance, message, parseResult, messageType);
                            }
                            break;

                        case TaskAction.ZOOM:
                            {
                                await FindAndFinishZoomTask(instance, message, parseResult, messageType);
                            }
                            break;

                        case TaskAction.VIDEO:
                            {
                                await FindAndFinishVideoTask(instance, message, parseResult, messageType);
                            }
                            break;

                        case TaskAction.UPSCALE_HD:
                            {
                                await FindAndFinishUpscaleHDTask(instance, message, parseResult, messageType);
                            }
                            break;

                        case TaskAction.EDIT:
                        case TaskAction.RETEXTURE:
                        case TaskAction.PIC_READER:
                        case TaskAction.REROLL:
                        case TaskAction.DESCRIBE:
                        case TaskAction.BLEND:
                        case TaskAction.ACTION:
                        case TaskAction.OUTPAINT:
                        case TaskAction.INPAINT:
                        case TaskAction.SHOW:
                        case TaskAction.SHORTEN:
                        case TaskAction.SWAP_FACE:
                        case TaskAction.SWAP_VIDEO_FACE:
                        case TaskAction.VIDEO_EXTEND:
                        default:
                            {
                                Log.Error("不支持的消息类型: {@0}, {@1}", parseResult, content);
                            }
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// 查找并完成放大任务
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="finalPrompt"></param>
        /// <param name="index">1 | 2 | 3 | 4</param>
        /// <param name="message"></param>
        private async Task FindAndFinishUTask(DiscordService instance, EventData message, MessageParseResult messageParseResult, MessageType messageType)
        {
            if (messageType != MessageType.CREATE)
            {
                return;
            }

            var index = messageParseResult.ImageIndex;
            if (index == null || index <= 0 || index > 4)
            {
                Log.Error("跳过无效的放大索引: {@0}, {@1}", messageParseResult, message);
                return;
            }

            var msgId = GetMessageId(message);
            var fullPrompt = GetFullPrompt(message);
            var imageUrl = GetImageUrl(message);
            var messageHash = DiscordHelper.GetMessageHash(imageUrl);

            if (string.IsNullOrWhiteSpace(msgId)
                || string.IsNullOrWhiteSpace(fullPrompt)
                || string.IsNullOrWhiteSpace(imageUrl)
                || string.IsNullOrWhiteSpace(messageHash))
            {
                Log.Warning("跳过无效的放大完成消息: MessageId={@0}, FullPrompt={@1}, ImageUrl={@2}, MessageHash={@3}",
                    msgId, fullPrompt, imageUrl, messageHash);
                return;
            }

            var list = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED)
            && c.Action == TaskAction.UPSCALE
            && c.GetProperty(Constants.TASK_PROPERTY_ACTION_INDEX, 0) == index).ToList();

            // 1. 优先通过 message id 精确匹配
            var task = list.Where(c => c.MessageId == msgId).FirstOrDefault();

            // 2. 其次通过 interaction meta id 匹配
            if (task == null && message.InteractionMetadata?.Id != null)
            {
                task = list.Where(c => c.InteractionMetadataId == message.InteractionMetadata.Id).FirstOrDefault();

                if (task != null)
                {
                    Log.Information("通过 InteractionMetadataId 找到放大任务: TaskId={@0}, MessageId={@1}, InteractionMetadataId={@2}",
                        task.Id, msgId, message.InteractionMetadata.Id);
                }
            }

            // 3. 通过提示词查找任务
            if (task == null)
            {
                var parseResult = MjPromptParser.Parse(fullPrompt);
                if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.CleanPrompt))
                {
                    Log.Warning("跳过无效的提示词放大完成消息: FullPrompt={@0}", fullPrompt);
                    return;
                }
                // 注意则使用 MJ 最终返回的 PromptFull 进行匹配
                var filterList = list.Where(c => !string.IsNullOrWhiteSpace(c.PromptFull) && parseResult.CleanPrompt.Equals(MjPromptParser.Parse(c.PromptFull)?.CleanPrompt, StringComparison.OrdinalIgnoreCase)).ToList();
                if (filterList.Count == 1)
                {
                    task = filterList.FirstOrDefault();
                }
                else if (filterList.Count > 1)
                {
                    // 根据提交时间取 1 个并记录警告日志
                    task = filterList.OrderBy(c => c.StartTime).FirstOrDefault();
                    Log.Warning("通过提示词找到多个放大任务，取最早提交的一个: Count={@0}, TaskId={@1}, FullPrompt={@2}", filterList.Count, task.Id, fullPrompt);
                }
            }

            if (task == null || task.IsCompleted)
            {
                return;
            }

            // 完善提示词
            if (task != null && string.IsNullOrWhiteSpace(task.PromptFull))
            {
                task.PromptFull = fullPrompt;
            }

            task.MessageId = msgId;

            if (!task.MessageIds.Contains(msgId))
                task.MessageIds.Add(msgId);

            task.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
            task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, fullPrompt);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_CONTENT, message.Content);

            task.ImageUrl = imageUrl;
            task.JobId = messageHash;

            // 普通放大任务，直接完成
            await FinishTask(task, message, messageParseResult);
            task.Awake();
        }

        /// <summary>
        /// 查找并处理/完成高清任务
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="action"></param>
        /// <param name="finalPrompt"></param>
        /// <param name="message"></param>
        protected async Task FindAndFinishUpscaleHDTask(DiscordService instance, EventData message, MessageParseResult messageParseResult, MessageType messageType)
        {
            var msgId = GetMessageId(message);
            var fullPrompt = GetFullPrompt(message);

            if (string.IsNullOrWhiteSpace(msgId) || string.IsNullOrWhiteSpace(fullPrompt))
            {
                Log.Warning("跳过无效的高清消息: MessageId={@0}, FullPrompt={@1}", msgId, fullPrompt);
                return;
            }

            var list = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.Action == TaskAction.UPSCALE_HD).ToList();

            // 1. 优先通过 message id 精确匹配
            var task = list.Where(c => c.MessageId == msgId).FirstOrDefault();

            // 2. 其次通过 interaction meta id 匹配
            if (task == null && message.InteractionMetadata?.Id != null)
            {
                task = list.Where(c => c.InteractionMetadataId == message.InteractionMetadata.Id).FirstOrDefault();

                if (task != null)
                {
                    Log.Information("通过 InteractionMetadataId 找到高清任务: TaskId={@0}, MessageId={@1}, InteractionMetadataId={@2}",
                        task.Id, msgId, message.InteractionMetadata.Id);
                }
            }

            // 3. 通过提示词查找任务
            if (task == null)
            {
                var parseResult = MjPromptParser.Parse(fullPrompt);
                if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.CleanPrompt))
                {
                    Log.Warning("跳过无效的提示词高清消息: FullPrompt={@0}", fullPrompt);
                    return;
                }

                // 注意则使用 MJ 最终返回的 PromptFull 进行匹配
                var filterList = list.Where(c => !string.IsNullOrWhiteSpace(c.PromptFull) && parseResult.CleanPrompt.Equals(MjPromptParser.Parse(c.PromptFull)?.CleanPrompt, StringComparison.OrdinalIgnoreCase)).ToList();
                if (filterList.Count == 1)
                {
                    task = filterList.FirstOrDefault();
                }
                else if (filterList.Count > 1)
                {
                    // 根据提交时间取 1 个并记录警告日志
                    task = filterList.OrderBy(c => c.StartTime).FirstOrDefault();

                    Log.Warning("通过提示词找到多个高清任务，取最早提交的一个: Count={@0}, TaskId={@1}, FullPrompt={@2}", filterList.Count, task.Id, fullPrompt);
                }
            }

            if (task == null || task.IsCompleted)
            {
                return;
            }

            var imageUrl = GetImageUrl(message);

            if (!task.MessageIds.Contains(msgId))
                task.MessageIds.Add(msgId);

            message.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
            task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, fullPrompt);

            // 高清放大中
            if (messageType == MessageType.UPDATE)
            {
                if (!string.IsNullOrWhiteSpace(messageParseResult.Progress))
                {
                    task.Status = TaskStatus.IN_PROGRESS;
                    task.Progress = messageParseResult.Progress;

                    // 如果启用保存过程图片
                    if (GlobalConfiguration.Setting.EnableSaveIntermediateImage && !string.IsNullOrWhiteSpace(imageUrl))
                    {
                        //var ff = new MjImageFetchHelper();
                        var ffUrl = await MjImageFetchHelper.FetchFileToStorageAsync(imageUrl);
                        if (!string.IsNullOrWhiteSpace(ffUrl))
                        {
                            imageUrl = ffUrl;
                        }

                        // 必须确保任务仍是 IN_PROGRESS 状态
                        if (task.Status == TaskStatus.IN_PROGRESS)
                        {
                            task.ImageUrl = imageUrl;
                            task.Awake();
                        }
                    }
                    else
                    {
                        task.ImageUrl = imageUrl;
                        task.Awake();
                    }
                }
                return;
            }

            // https://www.midjourney.com/jobs/3e8ebcc8-cb3a-472b-a63b-8ef558dc9f48
            var url = message.Components.SelectMany(x => x.Components).Where(y => y.Url?.StartsWith("https://www.midjourney.com/jobs/") == true).FirstOrDefault()?.Url;
            var messageHash = "";
            if (!string.IsNullOrWhiteSpace(url))
            {
                messageHash = url.Substring(url.LastIndexOf('/') + 1);
            }
            if (string.IsNullOrWhiteSpace(messageHash))
            {
                Log.Error("跳过无效的高清完成消息，无法获取 message hash: MessageId={@0}, Url={@1}", msgId, url);
                return;
            }

            task.ImageUrl = imageUrl;
            task.MessageId = msgId;
            task.JobId = messageHash;

            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_CONTENT, message.Content);

            await FinishTask(task, message, messageParseResult);
            task.Awake();
        }

        /// <summary>
        /// 查找并处理/完成平移任务
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="message"></param>
        /// <param name="messageParseResult"></param>
        /// <returns></returns>
        protected async Task FindAndFinishPanTask(DiscordService instance, EventData message, MessageParseResult messageParseResult, MessageType messageType)
        {
            var msgId = GetMessageId(message);
            var fullPrompt = GetFullPrompt(message);

            if (string.IsNullOrWhiteSpace(msgId) || string.IsNullOrWhiteSpace(fullPrompt))
            {
                Log.Warning("跳过无效的平移消息: MessageId={@0}, FullPrompt={@1}", msgId, fullPrompt);
                return;
            }

            var list = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.Action == messageParseResult.Action).ToList();

            // 1. 优先通过 message id 精确匹配
            var task = list.Where(c => c.MessageId == msgId).FirstOrDefault();

            // 2. 其次通过 interaction meta id 匹配
            if (task == null && message.InteractionMetadata?.Id != null)
            {
                task = list.Where(c => c.InteractionMetadataId == message.InteractionMetadata.Id).FirstOrDefault();

                if (task != null)
                {
                    Log.Information("通过 InteractionMetadataId 找到平移任务: TaskId={@0}, MessageId={@1}, InteractionMetadataId={@2}",
                        task.Id, msgId, message.InteractionMetadata.Id);
                }
            }

            var messageHash = DiscordHelper.GetMessageHash(GetImageUrl(message));

            // 非放大任务，如果通过 imageUrl 获取到 hash
            // 则对当前任务赋值
            if (task != null)
            {
                if (messageType == MessageType.UPDATE && !string.IsNullOrWhiteSpace(messageHash))
                {
                    task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
                    task.JobId = messageHash;

                    if (!string.IsNullOrWhiteSpace(fullPrompt) && string.IsNullOrWhiteSpace(task.PromptFull))
                    {
                        task.PromptFull = fullPrompt;
                    }
                }
            }

            // 通过 jobId 匹配完成任务
            if (task == null)
            {
                if (messageType == MessageType.CREATE && !string.IsNullOrWhiteSpace(messageHash))
                {
                    task = list.Where(c => c.JobId == messageHash).FirstOrDefault();
                }
            }

            // 3. 通过提示词查找任务
            if (task == null)
            {
                var parseResult = MjPromptParser.Parse(fullPrompt);
                if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.CleanPrompt))
                {
                    Log.Warning("跳过无效的提示词平移消息: FullPrompt={@0}", fullPrompt);
                    return;
                }

                var filterList = list.Where(c => !string.IsNullOrWhiteSpace(c.PromptFull) && parseResult.CleanPrompt.Equals(MjPromptParser.Parse(c.PromptFull)?.CleanPrompt, StringComparison.OrdinalIgnoreCase)).ToList();
                if (filterList.Count == 1)
                {
                    task = filterList.FirstOrDefault();
                }
                else if (filterList.Count > 1)
                {
                    // 根据提交时间取 1 个并记录警告日志
                    task = filterList.OrderBy(c => c.StartTime).FirstOrDefault();

                    Log.Warning("通过提示词找到多个平移任务，取最早提交的一个: Count={@0}, TaskId={@1}, FullPrompt={@2}", filterList.Count, task.Id, fullPrompt);
                }
            }

            // 4. 替换  url 为 <link> 后再次通过提示词查找任务
            if (task == null)
            {
                var parseResult = MjPromptParser.Parse(fullPrompt);
                if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.CleanPromptNormalized))
                {
                    Log.Warning("跳过无效的替换 URL 后的提示词平移消息: FullPrompt={@0}", fullPrompt);
                    return;
                }
                var filterList = list.Where(c => !string.IsNullOrWhiteSpace(c.PromptEn) && parseResult.CleanPromptNormalized.Equals(MjPromptParser.Parse(c.PromptEn)?.CleanPromptNormalized, StringComparison.OrdinalIgnoreCase)).ToList();
                if (filterList.Count == 1)
                {
                    task = filterList.FirstOrDefault();
                }
                else if (filterList.Count > 1)
                {
                    // 根据提交时间取 1 个并记录警告日志
                    task = filterList.OrderBy(c => c.StartTime).FirstOrDefault();
                    Log.Warning("通过替换 URL 后的提示词找到多个平移任务，取最早提交的一个: Count={@0}, TaskId={@1}, FullPrompt={@2}", filterList.Count, task.Id, fullPrompt);
                }
            }

            if (task == null || task.IsCompleted)
            {
                return;
            }

            var imageUrl = GetImageUrl(message);

            if (!task.MessageIds.Contains(msgId))
                task.MessageIds.Add(msgId);

            message.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
            task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, fullPrompt);

            // 进度中
            if (messageType == MessageType.UPDATE)
            {
                if (!string.IsNullOrWhiteSpace(messageParseResult.Progress))
                {
                    task.Status = TaskStatus.IN_PROGRESS;
                    task.Progress = messageParseResult.Progress;

                    // 如果启用保存过程图片
                    if (GlobalConfiguration.Setting.EnableSaveIntermediateImage && !string.IsNullOrWhiteSpace(imageUrl))
                    {
                        //var ff = new MjImageFetchHelper();
                        var ffUrl = await MjImageFetchHelper.FetchFileToStorageAsync(imageUrl);
                        if (!string.IsNullOrWhiteSpace(ffUrl))
                        {
                            imageUrl = ffUrl;
                        }

                        // 必须确保任务仍是 IN_PROGRESS 状态
                        if (task.Status == TaskStatus.IN_PROGRESS)
                        {
                            task.ImageUrl = imageUrl;
                            task.Awake();
                        }
                    }
                    else
                    {
                        task.ImageUrl = imageUrl;
                        task.Awake();
                    }
                }

                return;
            }

            // https://www.midjourney.com/jobs/3e8ebcc8-cb3a-472b-a63b-8ef558dc9f48
            var url = message.Components.SelectMany(x => x.Components).Where(y => y.Url?.StartsWith("https://www.midjourney.com/jobs/") == true).FirstOrDefault()?.Url;
            if (!string.IsNullOrWhiteSpace(url))
            {
                messageHash = url.Substring(url.LastIndexOf('/') + 1);
            }
            if (string.IsNullOrWhiteSpace(messageHash))
            {
                Log.Error("跳过无效的平移完成消息，无法获取 message hash: MessageId={@0}, Url={@1}", msgId, url);
                return;
            }

            task.ImageUrl = imageUrl;
            task.MessageId = msgId;
            task.JobId = messageHash;

            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_CONTENT, message.Content);

            await FinishTask(task, message, messageParseResult);
            task.Awake();
        }

        /// <summary>
        /// 查找并处理/完成变焦任务
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="message"></param>
        /// <param name="messageParseResult"></param>
        /// <returns></returns>
        protected async Task FindAndFinishZoomTask(DiscordService instance, EventData message, MessageParseResult messageParseResult, MessageType messageType)
        {
            var msgId = GetMessageId(message);
            var fullPrompt = GetFullPrompt(message);

            if (string.IsNullOrWhiteSpace(msgId) || string.IsNullOrWhiteSpace(fullPrompt))
            {
                Log.Warning("跳过无效的变焦消息: MessageId={@0}, FullPrompt={@1}", msgId, fullPrompt);
                return;
            }

            var list = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.Action == messageParseResult.Action).ToList();

            // 1. 优先通过 message id 精确匹配
            var task = list.Where(c => c.MessageId == msgId).FirstOrDefault();

            // 2. 其次通过 interaction meta id 匹配
            if (task == null && message.InteractionMetadata?.Id != null)
            {
                task = list.Where(c => c.InteractionMetadataId == message.InteractionMetadata.Id).FirstOrDefault();

                if (task != null)
                {
                    Log.Information("通过 InteractionMetadataId 找到变焦任务: TaskId={@0}, MessageId={@1}, InteractionMetadataId={@2}",
                        task.Id, msgId, message.InteractionMetadata.Id);
                }
            }


            var messageHash = DiscordHelper.GetMessageHash(GetImageUrl(message));

            // 非放大任务，如果通过 imageUrl 获取到 hash
            // 则对当前任务赋值
            if (task != null)
            {
                if (messageType == MessageType.UPDATE && !string.IsNullOrWhiteSpace(messageHash))
                {
                    task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
                    task.JobId = messageHash;

                    if (!string.IsNullOrWhiteSpace(fullPrompt) && string.IsNullOrWhiteSpace(task.PromptFull))
                    {
                        task.PromptFull = fullPrompt;
                    }
                }
            }

            // 通过 jobId 匹配完成任务
            if (task == null)
            {
                if (messageType == MessageType.CREATE && !string.IsNullOrWhiteSpace(messageHash))
                {
                    task = list.Where(c => c.JobId == messageHash).FirstOrDefault();
                }
            }

            // 3. 通过提示词查找任务
            if (task == null)
            {
                var parseResult = MjPromptParser.Parse(fullPrompt);
                if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.CleanPrompt))
                {
                    Log.Warning("跳过无效的提示词变焦消息: FullPrompt={@0}", fullPrompt);
                    return;
                }

                var filterList = list.Where(c => !string.IsNullOrWhiteSpace(c.PromptFull) && parseResult.CleanPrompt.Equals(MjPromptParser.Parse(c.PromptFull)?.CleanPrompt, StringComparison.OrdinalIgnoreCase)).ToList();
                if (filterList.Count == 1)
                {
                    task = filterList.FirstOrDefault();
                }
                else if (filterList.Count > 1)
                {
                    // 根据提交时间取 1 个并记录警告日志
                    task = filterList.OrderBy(c => c.StartTime).FirstOrDefault();

                    Log.Warning("通过提示词找到多个变焦任务，取最早提交的一个: Count={@0}, TaskId={@1}, FullPrompt={@2}", filterList.Count, task.Id, fullPrompt);
                }
            }

            // 4. 替换  url 为 <link> 后再次通过提示词查找任务
            if (task == null)
            {
                var parseResult = MjPromptParser.Parse(fullPrompt);
                if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.CleanPromptNormalized))
                {
                    Log.Warning("跳过无效的替换 URL 后的提示词变焦消息: FullPrompt={@0}", fullPrompt);
                    return;
                }
                var filterList = list.Where(c => !string.IsNullOrWhiteSpace(c.PromptEn) && parseResult.CleanPromptNormalized.Equals(MjPromptParser.Parse(c.PromptEn)?.CleanPromptNormalized, StringComparison.OrdinalIgnoreCase)).ToList();
                if (filterList.Count == 1)
                {
                    task = filterList.FirstOrDefault();
                }
                else if (filterList.Count > 1)
                {
                    // 根据提交时间取 1 个并记录警告日志
                    task = filterList.OrderBy(c => c.StartTime).FirstOrDefault();
                    Log.Warning("通过替换 URL 后的提示词找到多个变焦任务，取最早提交的一个: Count={@0}, TaskId={@1}, FullPrompt={@2}", filterList.Count, task.Id, fullPrompt);
                }
            }

            if (task == null || task.IsCompleted)
            {
                return;
            }

            var imageUrl = GetImageUrl(message);

            if (!task.MessageIds.Contains(msgId))
                task.MessageIds.Add(msgId);

            message.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
            task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, fullPrompt);

            // 进度中
            if (messageType == MessageType.UPDATE)
            {
                if (!string.IsNullOrWhiteSpace(messageParseResult.Progress))
                {
                    task.Status = TaskStatus.IN_PROGRESS;
                    task.Progress = messageParseResult.Progress;

                    // 如果启用保存过程图片
                    if (GlobalConfiguration.Setting.EnableSaveIntermediateImage && !string.IsNullOrWhiteSpace(imageUrl))
                    {
                        //var ff = new MjImageFetchHelper();
                        var ffUrl = await MjImageFetchHelper.FetchFileToStorageAsync(imageUrl);
                        if (!string.IsNullOrWhiteSpace(ffUrl))
                        {
                            imageUrl = ffUrl;
                        }

                        // 必须确保任务仍是 IN_PROGRESS 状态
                        if (task.Status == TaskStatus.IN_PROGRESS)
                        {
                            task.ImageUrl = imageUrl;
                            task.Awake();
                        }
                    }
                    else
                    {
                        task.ImageUrl = imageUrl;
                        task.Awake();
                    }
                }

                return;
            }

            // https://www.midjourney.com/jobs/3e8ebcc8-cb3a-472b-a63b-8ef558dc9f48
            var url = message.Components.SelectMany(x => x.Components).Where(y => y.Url?.StartsWith("https://www.midjourney.com/jobs/") == true).FirstOrDefault()?.Url;
            if (!string.IsNullOrWhiteSpace(url))
            {
                messageHash = url.Substring(url.LastIndexOf('/') + 1);
            }
            if (string.IsNullOrWhiteSpace(messageHash))
            {
                Log.Error("跳过无效的变焦完成消息，无法获取 message hash: MessageId={@0}, Url={@1}", msgId, url);
                return;
            }

            task.ImageUrl = imageUrl;
            task.MessageId = msgId;
            task.JobId = messageHash;

            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_CONTENT, message.Content);

            await FinishTask(task, message, messageParseResult);
            task.Awake();
        }

        /// <summary>
        /// 查找并处理/完成变化任务
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="message"></param>
        /// <param name="messageParseResult"></param>
        /// <returns></returns>
        protected async Task FindAndFinishVaryTask(DiscordService instance, EventData message, MessageParseResult messageParseResult, MessageType messageType)
        {
            var msgId = GetMessageId(message);
            var fullPrompt = GetFullPrompt(message);

            if (string.IsNullOrWhiteSpace(msgId) || string.IsNullOrWhiteSpace(fullPrompt))
            {
                Log.Warning("跳过无效的变化消息: MessageId={@0}, FullPrompt={@1}", msgId, fullPrompt);
                return;
            }

            var list = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED) && c.Action == messageParseResult.Action).ToList();

            // 1. 优先通过 message id 精确匹配
            var task = list.Where(c => c.MessageId == msgId).FirstOrDefault();

            // 2. 其次通过 interaction meta id 匹配
            if (task == null && message.InteractionMetadata?.Id != null)
            {
                task = list.Where(c => c.InteractionMetadataId == message.InteractionMetadata.Id).FirstOrDefault();

                if (task != null)
                {
                    Log.Information("通过 InteractionMetadataId 找到变化任务: TaskId={@0}, MessageId={@1}, InteractionMetadataId={@2}",
                        task.Id, msgId, message.InteractionMetadata.Id);
                }
            }

            var messageHash = DiscordHelper.GetMessageHash(GetImageUrl(message));

            // 非放大任务，如果通过 imageUrl 获取到 hash
            // 则对当前任务赋值
            if (task != null)
            {
                if (messageType == MessageType.UPDATE && !string.IsNullOrWhiteSpace(messageHash))
                {
                    task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
                    task.JobId = messageHash;

                    if (!string.IsNullOrWhiteSpace(fullPrompt) && string.IsNullOrWhiteSpace(task.PromptFull))
                    {
                        task.PromptFull = fullPrompt;
                    }
                }
            }

            // 通过 jobId 匹配完成任务
            if (task == null)
            {
                if (messageType == MessageType.CREATE && !string.IsNullOrWhiteSpace(messageHash))
                {
                    task = list.Where(c => c.JobId == messageHash).FirstOrDefault();
                }
            }

            // 3. 通过提示词查找任务
            if (task == null)
            {
                var parseResult = MjPromptParser.Parse(fullPrompt);
                if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.CleanPrompt))
                {
                    Log.Warning("跳过无效的提示词变化消息: FullPrompt={@0}", fullPrompt);
                    return;
                }

                var filterList = list.Where(c => !string.IsNullOrWhiteSpace(c.PromptFull) && parseResult.CleanPrompt.Equals(MjPromptParser.Parse(c.PromptFull)?.CleanPrompt, StringComparison.OrdinalIgnoreCase)).ToList();
                if (filterList.Count == 1)
                {
                    task = filterList.FirstOrDefault();
                }
                else if (filterList.Count > 1)
                {
                    // 根据提交时间取 1 个并记录警告日志
                    task = filterList.OrderBy(c => c.StartTime).FirstOrDefault();

                    Log.Warning("通过提示词找到多个变化任务，取最早提交的一个: Count={@0}, TaskId={@1}, FullPrompt={@2}", filterList.Count, task.Id, fullPrompt);
                }
            }

            // 4. 替换  url 为 <link> 后再次通过提示词查找任务
            if (task == null)
            {
                var parseResult = MjPromptParser.Parse(fullPrompt);
                if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.CleanPromptNormalized))
                {
                    Log.Warning("跳过无效的替换 URL 后的提示词变化消息: FullPrompt={@0}", fullPrompt);
                    return;
                }
                var filterList = list.Where(c => !string.IsNullOrWhiteSpace(c.PromptEn) && parseResult.CleanPromptNormalized.Equals(MjPromptParser.Parse(c.PromptEn)?.CleanPromptNormalized, StringComparison.OrdinalIgnoreCase)).ToList();
                if (filterList.Count == 1)
                {
                    task = filterList.FirstOrDefault();
                }
                else if (filterList.Count > 1)
                {
                    // 根据提交时间取 1 个并记录警告日志
                    task = filterList.OrderBy(c => c.StartTime).FirstOrDefault();
                    Log.Warning("通过替换 URL 后的提示词找到多个变化任务，取最早提交的一个: Count={@0}, TaskId={@1}, FullPrompt={@2}", filterList.Count, task.Id, fullPrompt);
                }
            }

            if (task == null || task.IsCompleted)
            {
                return;
            }
            var imageUrl = GetImageUrl(message);

            if (!task.MessageIds.Contains(msgId))
                task.MessageIds.Add(msgId);

            message.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
            task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, fullPrompt);

            // 进度中
            if (messageType == MessageType.UPDATE)
            {
                if (!string.IsNullOrWhiteSpace(messageParseResult.Progress))
                {
                    task.Status = TaskStatus.IN_PROGRESS;
                    task.Progress = messageParseResult.Progress;

                    // 如果启用保存过程图片
                    if (GlobalConfiguration.Setting.EnableSaveIntermediateImage && !string.IsNullOrWhiteSpace(imageUrl))
                    {
                        //var ff = new MjImageFetchHelper();
                        var ffUrl = await MjImageFetchHelper.FetchFileToStorageAsync(imageUrl);
                        if (!string.IsNullOrWhiteSpace(ffUrl))
                        {
                            imageUrl = ffUrl;
                        }

                        // 必须确保任务仍是 IN_PROGRESS 状态
                        if (task.Status == TaskStatus.IN_PROGRESS)
                        {
                            task.ImageUrl = imageUrl;
                            task.Awake();
                        }
                    }
                    else
                    {
                        task.ImageUrl = imageUrl;
                        task.Awake();
                    }
                }

                return;
            }

            // https://www.midjourney.com/jobs/3e8ebcc8-cb3a-472b-a63b-8ef558dc9f48
            var url = message.Components.SelectMany(x => x.Components).Where(y => y.Url?.StartsWith("https://www.midjourney.com/jobs/") == true).FirstOrDefault()?.Url;
            if (!string.IsNullOrWhiteSpace(url))
            {
                messageHash = url.Substring(url.LastIndexOf('/') + 1);
            }
            if (string.IsNullOrWhiteSpace(messageHash))
            {
                Log.Error("跳过无效的变化完成消息，无法获取 message hash: MessageId={@0}, Url={@1}", msgId, url);
                return;
            }

            task.ImageUrl = imageUrl;
            task.MessageId = msgId;
            task.JobId = messageHash;

            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_CONTENT, message.Content);

            await FinishTask(task, message, messageParseResult);
            task.Awake();
        }

        /// <summary>
        /// 查找并放大/处理/完成视频任务
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="message"></param>
        /// <param name="messageParseResult"></param>
        /// <returns></returns>
        protected async Task FindAndFinishVideoTask(DiscordService instance, EventData message, MessageParseResult messageParseResult, MessageType messageType)
        {
            var msgId = GetMessageId(message);
            var fullPrompt = GetFullPrompt(message);

            if (string.IsNullOrWhiteSpace(msgId) || string.IsNullOrWhiteSpace(fullPrompt))
            {
                Log.Warning("跳过无效的视频消息: MessageId={@0}, FullPrompt={@1}", msgId, fullPrompt);
                return;
            }

            // 视频放大任务：MJ::JOB::animate_high_extend::4::2a284b14-2894-4d52-99b5-0d31b9ee0c1b
            var isUpscale = message.Components.SelectMany(c => c.Components).Any(x => x.CustomId?.Contains("animate", StringComparison.OrdinalIgnoreCase) == true);

            //// MJ::JOB::video_virtual_upscale::1::708fed1c-1358-41ce-8d3c-8ba20097b9b6
            //var isExtend = message.Components.SelectMany(c => c.Components).Any(x => x.CustomId?.Contains("video_virtual_upscale", StringComparison.OrdinalIgnoreCase) == true);

            var list = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED))
                .WhereIf(isUpscale, c => c.Action == TaskAction.UPSCALE)
                .WhereIf(!isUpscale, c => c.Action == TaskAction.VIDEO)

                //// 如果当前任务是视频扩展、且完成时
                //// MJ::JOB::animate_low_extend::1::89c5a507-2391-498f-9d42-4721794cdecb
                //.WhereIf(messageType == MessageType.CREATE && messageParseResult.Action == TaskAction.VIDEO && isExtend == true,
                //c => c.GetProperty(Constants.TASK_PROPERTY_CUSTOM_ID, "")?.Contains("extend") == true)
                //.WhereIf(messageType == MessageType.CREATE && messageParseResult.Action == TaskAction.VIDEO && isExtend == false,
                //c => c.GetProperty(Constants.TASK_PROPERTY_CUSTOM_ID, "")?.Contains("extend") != true)

                .ToList();

            // 1. 优先通过 message id 精确匹配
            var task = list.Where(c => c.MessageId == msgId).FirstOrDefault();

            // 2. 其次通过 interaction meta id 匹配
            if (task == null && message.InteractionMetadata?.Id != null)
            {
                task = list.Where(c => c.InteractionMetadataId == message.InteractionMetadata.Id).FirstOrDefault();

                if (task != null)
                {
                    Log.Information("通过 InteractionMetadataId 找到视频任务: TaskId={@0}, MessageId={@1}, InteractionMetadataId={@2}",
                        task.Id, msgId, message.InteractionMetadata.Id);
                }
            }

            var messageHash = DiscordHelper.GetMessageHash(GetImageUrl(message));

            if (!isUpscale)
            {
                // 非放大任务，如果通过 imageUrl 获取到 hash
                // 则对当前任务赋值
                if (task != null)
                {
                    if (messageType == MessageType.UPDATE && !string.IsNullOrWhiteSpace(messageHash))
                    {
                        task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
                        task.JobId = messageHash;

                        if (!string.IsNullOrWhiteSpace(fullPrompt) && string.IsNullOrWhiteSpace(task.PromptFull))
                        {
                            task.PromptFull = fullPrompt;
                        }
                    }
                }

                // 通过 jobId 匹配完成任务
                if (task == null)
                {
                    if (messageType == MessageType.CREATE && !string.IsNullOrWhiteSpace(messageHash))
                    {
                        task = list.Where(c => c.JobId == messageHash).FirstOrDefault();
                    }
                }
            }

            // 3. 通过 seed + 干净的提示词 匹配任务
            if (task == null && fullPrompt.Contains("--seed", StringComparison.OrdinalIgnoreCase))
            {
                var parseResult = MjPromptParser.Parse(fullPrompt);
                if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.CleanPrompt))
                {
                    Log.Warning("跳过无效的提示词种子视频消息: FullPrompt={@0}", fullPrompt);
                    return;
                }

                var seed = parseResult.GetSeed()?.ToString();
                var filterList = list
                    .Where(c => !string.IsNullOrWhiteSpace(c.PromptFull) && parseResult.CleanPrompt.Equals(MjPromptParser.Parse(c.PromptFull)?.CleanPrompt, StringComparison.OrdinalIgnoreCase))
                    .WhereIf(!string.IsNullOrWhiteSpace(seed), c => c.Seed == seed)
                    .ToList();
                if (filterList.Count == 1)
                {
                    task = filterList.FirstOrDefault();
                }
                else if (filterList.Count > 1)
                {
                    // 根据提交时间取 1 个并记录警告日志
                    task = filterList.OrderBy(c => c.StartTime).FirstOrDefault();
                    Log.Warning("通过替换 URL 后的提示词找到多个种子视频任务，取最早提交的一个: Count={@0}, TaskId={@1}, FullPrompt={@2}", filterList.Count, task.Id, fullPrompt);
                }
            }

            // 4. 通过完整提示词查找任务
            if (task == null)
            {
                var parseResult = MjPromptParser.Parse(fullPrompt);
                if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.CleanPrompt))
                {
                    Log.Warning("跳过无效的提示词视频消息: FullPrompt={@0}", fullPrompt);
                    return;
                }

                var filterList = list.Where(c => !string.IsNullOrWhiteSpace(c.PromptEn) && parseResult.CleanPrompt.Equals(MjPromptParser.Parse(c.PromptEn)?.CleanPrompt, StringComparison.OrdinalIgnoreCase)).ToList();
                if (filterList.Count == 1)
                {
                    task = filterList.FirstOrDefault();
                }
                else if (filterList.Count > 1)
                {
                    // 根据提交时间取 1 个并记录警告日志
                    task = filterList.OrderBy(c => c.StartTime).FirstOrDefault();

                    Log.Warning("通过提示词找到多个视频任务，取最早提交的一个: Count={@0}, TaskId={@1}, FullPrompt={@2}", filterList.Count, task.Id, fullPrompt);
                }
            }

            // 5. 替换  url 为 <link> 后再次通过提示词查找任务
            if (task == null)
            {
                var parseResult = MjPromptParser.Parse(fullPrompt);
                if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.CleanPromptNormalized))
                {
                    Log.Warning("跳过无效的替换 URL 后的提示词视频消息: FullPrompt={@0}", fullPrompt);
                    return;
                }
                var filterList = list.Where(c => !string.IsNullOrWhiteSpace(c.PromptEn) && parseResult.CleanPromptNormalized.Equals(MjPromptParser.Parse(c.PromptEn)?.CleanPromptNormalized, StringComparison.OrdinalIgnoreCase)).ToList();
                if (filterList.Count == 1)
                {
                    task = filterList.FirstOrDefault();
                }
                else if (filterList.Count > 1)
                {
                    // 根据提交时间取 1 个并记录警告日志
                    task = filterList.OrderBy(c => c.StartTime).FirstOrDefault();
                    Log.Warning("通过替换 URL 后的提示词找到多个视频任务，取最早提交的一个: Count={@0}, TaskId={@1}, FullPrompt={@2}", filterList.Count, task.Id, fullPrompt);
                }
            }

            if (task == null || task.IsCompleted)
            {
                return;
            }

            var imageUrl = GetImageUrl(message);

            if (!task.MessageIds.Contains(msgId))
                task.MessageIds.Add(msgId);

            message.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
            task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, fullPrompt);

            // 进度中
            if (messageType == MessageType.UPDATE)
            {
                if (!string.IsNullOrWhiteSpace(messageParseResult.Progress))
                {
                    task.Status = TaskStatus.IN_PROGRESS;
                    task.Progress = messageParseResult.Progress;

                    // 如果启用保存过程图片
                    if (GlobalConfiguration.Setting.EnableSaveIntermediateImage && !string.IsNullOrWhiteSpace(imageUrl))
                    {
                        //var ff = new MjImageFetchHelper();
                        var ffUrl = await MjImageFetchHelper.FetchFileToStorageAsync(imageUrl);
                        if (!string.IsNullOrWhiteSpace(ffUrl))
                        {
                            imageUrl = ffUrl;
                        }

                        // 必须确保任务仍是 IN_PROGRESS 状态
                        if (task.Status == TaskStatus.IN_PROGRESS)
                        {
                            task.ImageUrl = imageUrl;
                            task.Awake();
                        }
                    }
                    else
                    {
                        task.ImageUrl = imageUrl;
                        task.Awake();
                    }
                }

                return;
            }

            if (isUpscale)
            {
                var customId = message.Components.SelectMany(c => c.Components)
                    .Where(x => x.CustomId.Contains("extend", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault()?.CustomId;
                if (!string.IsNullOrWhiteSpace(customId))
                {
                    messageHash = customId.Split("::").LastOrDefault();
                }

                // 视频 API 拓展任务类型
                // 检查是否是视频扩展的第一步（放大）
                var isApiVideoExtend = !string.IsNullOrWhiteSpace(task.GetProperty<string>(Constants.TASK_PROPERTY_VIDEO_EXTEND_TARGET_TASK_ID, default));
                if (isApiVideoExtend)
                {
                    // 视频扩展任务，不要完成，继续触发扩展操作
                    task.Status = TaskStatus.IN_PROGRESS;
                    task.Description = "/video extend";
                    task.Progress = "0%";
                    task.MessageId = msgId;
                    task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, message.Id);
                    task.SetProperty(Constants.TASK_PROPERTY_FLAGS, Convert.ToInt32(message.Flags));

                    Log.Information("视频 api 任务完成，准备自动触发扩展操作: TaskId={TaskId}", task.Id);

                    var buttons = message.Components.SelectMany(x => x.Components)
                        .Select(btn =>
                        {
                            return new CustomComponentModel
                            {
                                CustomId = btn.CustomId ?? string.Empty,
                                Emoji = btn.Emoji?.Name ?? string.Empty,
                                Label = btn.Label ?? string.Empty,
                                Style = (int?)btn.Style ?? 0,
                                Type = (int?)btn.Type ?? 0,
                            };
                        }).Where(c => c != null && !string.IsNullOrWhiteSpace(c.CustomId)).ToList();

                    // 触发第二步（扩展）
                    await CheckAndTriggerVideoExtend(instance, task, messageHash, buttons);

                    return;
                }
            }
            else
            {
                // https://www.midjourney.com/jobs/3e8ebcc8-cb3a-472b-a63b-8ef558dc9f48
                var url = message.Components.SelectMany(x => x.Components).Where(y => y.Url?.StartsWith("https://www.midjourney.com/jobs/") == true).FirstOrDefault()?.Url;
                if (!string.IsNullOrWhiteSpace(url))
                {
                    messageHash = url.Substring(url.LastIndexOf('/') + 1);
                }
                if (string.IsNullOrWhiteSpace(messageHash))
                {
                    Log.Error("跳过无效的视频完成消息，无法获取 message hash: MessageId={@0}, Url={@1}", msgId, url);
                    return;
                }
            }

            task.ImageUrl = imageUrl;
            task.MessageId = msgId;
            task.JobId = messageHash;

            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_CONTENT, message.Content);

            await FinishTask(task, message, messageParseResult);
            task.Awake();
        }

        /// <summary>
        /// 查找并处理/完成想象/混图任务
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="message"></param>
        /// <param name="messageParseResult"></param>
        /// <returns></returns>
        protected async Task FindAndFinishImageTask(DiscordService instance, EventData message, MessageParseResult messageParseResult, MessageType messageType)
        {
            var msgId = GetMessageId(message);
            var fullPrompt = GetFullPrompt(message);

            if (string.IsNullOrWhiteSpace(msgId) || string.IsNullOrWhiteSpace(fullPrompt))
            {
                Log.Warning("跳过无效的想象/混图消息: MessageId={@0}, FullPrompt={@1}", msgId, fullPrompt);
                return;
            }

            var list = instance.FindRunningTask(c => (c.Status == TaskStatus.IN_PROGRESS || c.Status == TaskStatus.SUBMITTED)
            && (c.Action == TaskAction.IMAGINE || c.Action == TaskAction.BLEND)).ToList();

            // 1. 优先通过 message id 精确匹配
            var task = list.Where(c => c.MessageId == msgId).FirstOrDefault();

            // 2. 其次通过 interaction meta id 匹配
            if (task == null && message.InteractionMetadata?.Id != null)
            {
                task = list.Where(c => c.InteractionMetadataId == message.InteractionMetadata.Id).FirstOrDefault();

                if (task != null)
                {
                    Log.Information("通过 InteractionMetadataId 找到想象/混图任务: TaskId={@0}, MessageId={@1}, InteractionMetadataId={@2}",
                        task.Id, msgId, message.InteractionMetadata.Id);
                }
            }

            var messageHash = DiscordHelper.GetMessageHash(GetImageUrl(message));

            // 非放大任务，如果通过 imageUrl 获取到 hash
            // 则对当前任务赋值
            if (task != null)
            {
                if (messageType == MessageType.UPDATE && !string.IsNullOrWhiteSpace(messageHash))
                {
                    task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
                    task.JobId = messageHash;

                    if (!string.IsNullOrWhiteSpace(fullPrompt) && string.IsNullOrWhiteSpace(task.PromptFull))
                    {
                        task.PromptFull = fullPrompt;
                    }
                }
            }

            // 通过 jobId 匹配完成任务
            if (task == null)
            {
                if (messageType == MessageType.CREATE && !string.IsNullOrWhiteSpace(messageHash))
                {
                    task = list.Where(c => c.JobId == messageHash).FirstOrDefault();
                }
            }

            // 3. 通过 seed + 干净的提示词 匹配任务
            if (task == null && fullPrompt.Contains("--seed", StringComparison.OrdinalIgnoreCase))
            {
                var parseResult = MjPromptParser.Parse(fullPrompt);
                if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.CleanPrompt))
                {
                    Log.Warning("跳过无效的提示词种子想象/混图消息: FullPrompt={@0}", fullPrompt);
                    return;
                }

                var seed = parseResult.GetSeed()?.ToString();
                var filterList = list
                    .Where(c => !string.IsNullOrWhiteSpace(c.PromptFull) && parseResult.CleanPrompt.Equals(MjPromptParser.Parse(c.PromptFull)?.CleanPrompt, StringComparison.OrdinalIgnoreCase))
                    .WhereIf(!string.IsNullOrWhiteSpace(seed), c => c.Seed == seed)
                    .ToList();
                if (filterList.Count == 1)
                {
                    task = filterList.FirstOrDefault();
                }
                else if (filterList.Count > 1)
                {
                    // 根据提交时间取 1 个并记录警告日志
                    task = filterList.OrderBy(c => c.StartTime).FirstOrDefault();
                    Log.Warning("通过替换 URL 后的提示词找到多个种子想象/混图任务，取最早提交的一个: Count={@0}, TaskId={@1}, FullPrompt={@2}", filterList.Count, task.Id, fullPrompt);
                }
            }

            // 4. 通过完整提示词查找任务
            if (task == null)
            {
                var parseResult = MjPromptParser.Parse(fullPrompt);
                if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.CleanPrompt))
                {
                    Log.Warning("跳过无效的提示词想象/混图消息: FullPrompt={@0}", fullPrompt);
                    return;
                }

                var filterList = list.Where(c => !string.IsNullOrWhiteSpace(c.PromptEn) && parseResult.CleanPrompt.Equals(MjPromptParser.Parse(c.PromptEn)?.CleanPrompt, StringComparison.OrdinalIgnoreCase)).ToList();
                if (filterList.Count == 1)
                {
                    task = filterList.FirstOrDefault();
                }
                else if (filterList.Count > 1)
                {
                    // 根据提交时间取 1 个并记录警告日志
                    task = filterList.OrderBy(c => c.StartTime).FirstOrDefault();

                    Log.Warning("通过提示词找到多个想象/混图任务，取最早提交的一个: Count={@0}, TaskId={@1}, FullPrompt={@2}", filterList.Count, task.Id, fullPrompt);
                }
            }

            // 5. 替换  url 为 <link> 后再次通过提示词查找任务
            if (task == null)
            {
                var parseResult = MjPromptParser.Parse(fullPrompt);
                if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.CleanPromptNormalized))
                {
                    Log.Warning("跳过无效的替换 URL 后的提示词想象/混图消息: FullPrompt={@0}", fullPrompt);
                    return;
                }
                var filterList = list.Where(c => !string.IsNullOrWhiteSpace(c.PromptEn) && parseResult.CleanPromptNormalized.Equals(MjPromptParser.Parse(c.PromptEn)?.CleanPromptNormalized, StringComparison.OrdinalIgnoreCase)).ToList();
                if (filterList.Count == 1)
                {
                    task = filterList.FirstOrDefault();
                }
                else if (filterList.Count > 1)
                {
                    // 根据提交时间取 1 个并记录警告日志
                    task = filterList.OrderBy(c => c.StartTime).FirstOrDefault();
                    Log.Warning("通过替换 URL 后的提示词找到多个想象/混图任务，取最早提交的一个: Count={@0}, TaskId={@1}, FullPrompt={@2}", filterList.Count, task.Id, fullPrompt);
                }
            }

            if (task == null || task.IsCompleted)
            {
                return;
            }

            var imageUrl = GetImageUrl(message);

            if (!task.MessageIds.Contains(msgId))
                task.MessageIds.Add(msgId);

            message.SetProperty(Constants.MJ_MESSAGE_HANDLED, true);
            task.SetProperty(Constants.TASK_PROPERTY_FINAL_PROMPT, fullPrompt);

            // 进度中
            if (messageType == MessageType.UPDATE)
            {
                if (!string.IsNullOrWhiteSpace(messageParseResult.Progress))
                {
                    task.Status = TaskStatus.IN_PROGRESS;
                    task.Progress = messageParseResult.Progress;

                    // 如果启用保存过程图片
                    if (GlobalConfiguration.Setting.EnableSaveIntermediateImage && !string.IsNullOrWhiteSpace(imageUrl))
                    {
                        //var ff = new MjImageFetchHelper();
                        var ffUrl = await MjImageFetchHelper.FetchFileToStorageAsync(imageUrl);
                        if (!string.IsNullOrWhiteSpace(ffUrl))
                        {
                            imageUrl = ffUrl;
                        }

                        // 必须确保任务仍是 IN_PROGRESS 状态
                        if (task.Status == TaskStatus.IN_PROGRESS)
                        {
                            task.ImageUrl = imageUrl;
                            task.Awake();
                        }
                    }
                    else
                    {
                        task.ImageUrl = imageUrl;
                        task.Awake();
                    }
                }

                return;
            }

            // https://www.midjourney.com/jobs/3e8ebcc8-cb3a-472b-a63b-8ef558dc9f48
            var url = message.Components.SelectMany(x => x.Components).Where(y => y.Url?.StartsWith("https://www.midjourney.com/jobs/") == true).FirstOrDefault()?.Url;
            if (!string.IsNullOrWhiteSpace(url))
            {
                messageHash = url.Substring(url.LastIndexOf('/') + 1);
            }
            if (string.IsNullOrWhiteSpace(messageHash))
            {
                Log.Error("跳过无效的想象/混图完成消息，无法获取 message hash: MessageId={@0}, Url={@1}", msgId, url);
                return;
            }

            task.ImageUrl = imageUrl;
            task.MessageId = msgId;
            task.JobId = messageHash;

            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_HASH, messageHash);
            task.SetProperty(Constants.TASK_PROPERTY_MESSAGE_CONTENT, message.Content);

            await FinishTask(task, message, messageParseResult);

            task.Awake();
        }

        /// <summary>
        /// 检查并触发视频扩展操作
        /// </summary>
        protected async Task CheckAndTriggerVideoExtend(DiscordService instance, TaskInfo upscaleTask, string messageHash, List<CustomComponentModel> buttons)
        {
            try
            {
                // 检查任务是否有视频扩展标记
                var videoExtendTargetTaskId = upscaleTask.GetProperty<string>(Constants.TASK_PROPERTY_VIDEO_EXTEND_TARGET_TASK_ID, default);
                if (string.IsNullOrWhiteSpace(videoExtendTargetTaskId))
                {
                    return;
                }

                // 获取扩展相关参数
                var extendPrompt = upscaleTask.GetProperty<string>(Constants.TASK_PROPERTY_VIDEO_EXTEND_PROMPT, default);
                var extendMotion = upscaleTask.GetProperty<string>(Constants.TASK_PROPERTY_VIDEO_EXTEND_MOTION, default);
                var extendIndex = upscaleTask.GetProperty<int>(Constants.TASK_PROPERTY_VIDEO_EXTEND_INDEX, 1);

                if (string.IsNullOrWhiteSpace(extendMotion))
                {
                    extendMotion = "high";
                }

                // 关键改进：从 Buttons 中查找正确的 extend customId，而不是自己构建
                // 因为 upscale 后的 JobId 可能不是正确的 hash 值
                var extendButton = buttons?.FirstOrDefault(x => x.CustomId?.Contains($"animate_{extendMotion}_extend") == true);
                if (extendButton == null || string.IsNullOrWhiteSpace(extendButton.CustomId))
                {
                    Log.Warning("找不到 extend 按钮: UpscaleTaskId={@0}, Motion={@1}, Buttons={@2}",
                        upscaleTask.Id, extendMotion, buttons);

                    // 标记任务失败
                    upscaleTask.Status = TaskStatus.FAILURE;
                    upscaleTask.FailReason = $"找不到 extend 按钮 (motion: {extendMotion})";
                    _freeSql.Update(upscaleTask);
                    upscaleTask.Awake();
                    return;
                }

                var extendCustomId = extendButton.CustomId;

                // 必须异步触发扩展操作，不能阻止当前消息处理
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 等待 1.5 ~ 秒，确保消息已完全处理
                        await Task.Delay(Random.Shared.Next(1500, 5000));

                        // 创建一个新的 nonce 用于 extend 操作
                        var extendNonce = SnowFlake.NextId();

                        // 更新当前任务（upscaleTask 就是用户看到的任务）
                        upscaleTask.Nonce = extendNonce;
                        upscaleTask.Status = TaskStatus.SUBMITTED;
                        upscaleTask.Action = TaskAction.VIDEO;
                        upscaleTask.Description = "/video extend";
                        upscaleTask.Progress = "0%";
                        upscaleTask.PromptEn = extendPrompt;
                        upscaleTask.RemixAutoSubmit = instance.Account.RemixAutoSubmit && (instance.Account.MjRemixOn || instance.Account.NijiRemixOn);

                        // 必须开启 REMIX 模式才支持 API 视频拓展


                        upscaleTask.SetProperty(Constants.TASK_PROPERTY_CUSTOM_ID, extendCustomId);
                        upscaleTask.SetProperty(Constants.TASK_PROPERTY_NONCE, extendNonce);
                        upscaleTask.SetProperty(Constants.TASK_PROPERTY_MESSAGE_ID, upscaleTask.MessageId);
                        upscaleTask.SetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_PROMPT, extendPrompt);

                        // 清除 video extend 标记，避免任务完成时再次触发
                        upscaleTask.SetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_TARGET_TASK_ID, null);
                        upscaleTask.SetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_MOTION, null);
                        upscaleTask.SetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_INDEX, null);

                        // 弹窗确认
                        var task = upscaleTask;
                        task.RemixModaling = true;

                        var res = await instance.ActionAsync(upscaleTask.MessageId, extendCustomId, upscaleTask.GetProperty<int>(Constants.TASK_PROPERTY_FLAGS, 0),
                            extendNonce, task);
                        if (res?.Code != ReturnCode.SUCCESS)
                        {
                            task.Fail(res?.Description ?? "未知错误");
                            return;
                        }

                        // 等待获取 messageId 和交互消息 id
                        // 等待最大超时 5min
                        var sw = new Stopwatch();
                        sw.Start();
                        do
                        {
                            // 等待 2.5s
                            await Task.Delay(Random.Shared.Next(2500, 5000));
                            task = instance.GetRunningTask(task.Id);

                            if (string.IsNullOrWhiteSpace(task.RemixModalMessageId) || string.IsNullOrWhiteSpace(task.InteractionMetadataId))
                            {
                                if (sw.ElapsedMilliseconds > 300000)
                                {
                                    task.Fail("超时，未找到消息");
                                    return;
                                }
                            }
                        } while (string.IsNullOrWhiteSpace(task.RemixModalMessageId) || string.IsNullOrWhiteSpace(task.InteractionMetadataId));

                        // 等待 1.2s
                        await Task.Delay(Random.Shared.Next(1200, 2500));

                        task.RemixModaling = false;

                        var modal = "MJ::AnimateModal::prompt";
                        var customId = extendCustomId;
                        var parts = customId.Split("::");
                        var low = "low";
                        if (!customId.Contains("low"))
                        {
                            low = "high";
                        }

                        var convertedString = $"MJ::AnimateModal::{parts[4]}::{parts[3]}::{low}::1";
                        customId = convertedString;
                        task.SetProperty(Constants.TASK_PROPERTY_REMIX_CUSTOM_ID, customId);
                        task.SetProperty(Constants.TASK_PROPERTY_REMIX_MODAL, modal);

                        extendNonce = SnowFlake.NextId();
                        task.Nonce = extendNonce;
                        task.SetProperty(Constants.TASK_PROPERTY_NONCE, extendNonce);

                        var result = await instance.RemixAsync(task, task.Action.Value, task.RemixModalMessageId, modal,
                                          customId, task.PromptEn, extendNonce, task.RealBotType ?? task.BotType);
                        if (result?.Code != ReturnCode.SUCCESS)
                        {
                            task.Fail(result?.Description ?? "未知错误");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "执行视频扩展操作时发生异常: UpscaleTaskId={UpscaleTaskId}", upscaleTask.Id);
                    }
                });

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "检查视频扩展时发生异常: UpscaleTaskId={UpscaleTaskId}", upscaleTask.Id);
            }
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
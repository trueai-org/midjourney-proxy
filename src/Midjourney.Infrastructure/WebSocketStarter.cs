using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.Util;
using Serilog;
using System.Net;
using System.Net.WebSockets;

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// wss 启动器
    /// </summary>
    public class WebSocketStarter : IDisposable
    {
        private const int CONNECT_RETRY_LIMIT = 5;

        private readonly DiscordAccount _account;
        private readonly BotMessageListener _userMessageListener;
        private readonly ILogger _logger;
        private readonly WebProxy _webProxy;
        private readonly DiscordHelper _discordHelper;

        private bool _running = false;
        private bool _sessionClosing = false;

        private ClientWebSocket _webSocketSession = null;
        private ResumeData _resumeData = null;
        private DiscordServiceImpl _discordService;

        public WebSocketStarter(
            DiscordAccount account,
            DiscordHelper discordHelper,
            BotMessageListener userMessageListener,
            WebProxy webProxy,
            DiscordServiceImpl discordService)
        {
            _account = account;
            _userMessageListener = userMessageListener;
            _discordHelper = discordHelper;
            _logger = Log.Logger;
            _webProxy = webProxy;
            _discordService = discordService;
        }

        public async Task StartAsync()
        {
            await StartAsync(false);
        }

        private async Task StartAsync(bool reconnect)
        {
            _sessionClosing = false;

            try
            {
                var handler = new WebSocketHandler(_account, _discordHelper, _userMessageListener,
                    _webProxy,
                    OnSocketSuccess,
                    OnSocketFailure);

                _webSocketSession = handler.ClientWebSocket;

                await handler.StartAsync(reconnect,
                    _resumeData?.SessionId, _resumeData?.Sequence, _resumeData?.ResumeGatewayUrl);
            }
            catch (Exception e)
            {
                _logger.Error(e, "WebSocket 连接错误");

                OnSocketFailure(WebSocketHandler.CloseCodeException, e.Message);
            }
        }

        private void OnSocketSuccess(string sessionId, object sequence, string resumeGatewayUrl)
        {
            _resumeData = new ResumeData(sessionId, sequence, resumeGatewayUrl);
            _running = true;
            _discordService.DefaultSessionId = sessionId;

            NotifyWssLock(ReturnCode.SUCCESS, "");
        }

        private void OnSocketFailure(int code, string reason)
        {
            if (_sessionClosing)
            {
                _sessionClosing = false;
                DisableAccount();
                return;
            }

            CloseSocketSessionWhenIsOpen();

            if (!_running)
            {
                NotifyWssLock(code, reason);
            }

            _running = false;

            if (code >= 4000)
            {
                _logger.Warning("用户无法重新连接！帐户已禁用。由 {0}({1}) 关闭。", code, reason);
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

            _logger.Error("帐户已禁用");

            DisableAccount();
        }

        public async Task TryStartAsync(bool reconnect)
        {
            await StartAsync(reconnect);
            var lockObject = await AsyncLockUtils.WaitForLockAsync($"wss:{_account.Id}", TimeSpan.FromSeconds(20));
            if (lockObject != null)
            {
                _logger.Debug("{0} 成功。", reconnect ? "重新连接" : "新连接");
                return;
            }

            throw new Exception("获取锁超时");
        }

        private void NotifyWssLock(int code, string reason)
        {
            var lockObject = AsyncLockUtils.GetLock($"wss:{_account.Id}");
            if (lockObject != null)
            {

            }

            _account.DisabledReason = reason;

            // 保存
            DbHelper.AccountStore.Save(_account);
        }

        private void DisableAccount()
        {
            if (_account.Enable == false)
            {
                return;
            }

            _account.Enable = false;

            // 保存
            DbHelper.AccountStore.Save(_account);
        }

        private void CloseSocketSessionWhenIsOpen()
        {
            try
            {
                if (_webSocketSession != null && _webSocketSession.State == WebSocketState.Open)
                {
                    _sessionClosing = true;
                    _webSocketSession.Abort();
                    _webSocketSession.Dispose();
                }
            }
            catch (Exception)
            {
                // do nothing
            }
        }

        public class ResumeData
        {
            public string SessionId { get; }
            public object Sequence { get; }
            public string ResumeGatewayUrl { get; }

            public ResumeData(string sessionId, object sequence, string resumeGatewayUrl)
            {
                SessionId = sessionId;
                Sequence = sequence;
                ResumeGatewayUrl = resumeGatewayUrl;
            }
        }

        public void Dispose()
        {
            // Close the WebSocket session if it is open
            CloseSocketSessionWhenIsOpen();

            // Dispose the WebSocket session
            _webSocketSession?.Dispose();

            // Dispose the user message listener
            _userMessageListener?.Dispose();
        }
    }
}
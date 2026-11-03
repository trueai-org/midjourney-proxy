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

using System.Text.Json;
using System.Text.Json.Serialization;
using CSRedis;
using FreeSql;
using Microsoft.Extensions.Hosting;
using Midjourney.Base.Utils;
using Serilog;

namespace Midjourney.API
{
    /// <summary>
    /// 上游订阅同步服务。
    ///
    /// 业务说明：
    /// 1. 按 <see cref="UpstreamSubscriptionConfig"/> 配置的频率，从多个上游代理拉取账号列表；
    /// 2. 以 LoginAccount（优先）或 ChannelId 作为判重键，已存在的账号只同步受管字段，不覆盖本地配置；
    /// 3. 新账号仅在存在可用凭证时插入，避免把上游脱敏返回的 **** 落库；
    /// 4. 依赖 Redis 分布式锁保证多节点部署时同一时间只有一个节点执行同步；
    /// 5. 同步产生新增或更新后发布 <see cref="ENotificationType.UpstreamAccountSynced"/> 通知，让各节点重新加载账号。
    /// </summary>
    public class UpstreamSubscriptionSyncService : BackgroundService
    {
        /// <summary>
        /// 上游订阅同步的分布式锁 key
        /// </summary>
        private const string SyncLockKey = "MJ_UPSTREAM_SUBSCRIPTION_SYNC_LOCK";

        /// <summary>
        /// 分布式锁 TTL（秒），CSRedis 锁会自动续期
        /// </summary>
        private const int SyncLockTtlSeconds = 300;

        /// <summary>
        /// 调度循环间隔（秒）
        /// </summary>
        private const int LoopIntervalSeconds = 30;

        /// <summary>
        /// 默认同步频率（分钟）
        /// </summary>
        private const int DefaultSyncIntervalMinutes = 5;

        /// <summary>
        /// 同步频率下限（分钟）
        /// </summary>
        private const int MinSyncIntervalMinutes = 1;

        /// <summary>
        /// 同步频率上限（分钟）
        /// </summary>
        private const int MaxSyncIntervalMinutes = 60;

        /// <summary>
        /// 单个上游响应体的最大长度，超过视为失败，避免异常响应耗尽内存
        /// </summary>
        private const long MaxResponseBytes = 20L * 1024 * 1024;

        /// <summary>
        /// 单次拉取上游的超时时间
        /// </summary>
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 反序列化上游账号列表的 JSON 选项：上游字段为 camelCase，枚举按字符串返回
        /// </summary>
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly ILogger _logger = Log.Logger;

        /// <summary>
        /// 下一次允许执行同步的时间，成功或失败都会推进，避免失败后每 30 秒重试
        /// </summary>
        private DateTime _nextRunAt = DateTime.MinValue;

        /// <summary>
        /// 当前实例持有的同步领导锁；只有持锁节点会执行同步
        /// </summary>
        private CSRedisClientLock _leaderLock;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="httpClientFactory">HTTP 客户端工厂，用于复用 HttpClient 实例</param>
        /// <param name="applicationLifetime">应用生命周期，用于等待 ApplicationStarted</param>
        public UpstreamSubscriptionSyncService(IHttpClientFactory httpClientFactory, IHostApplicationLifetime applicationLifetime)
        {
            _httpClientFactory = httpClientFactory;
            _applicationLifetime = applicationLifetime;
        }

        /// <summary>
        /// 后台调度循环：等待应用启动后，每 30 秒检查一次是否到达同步时间
        /// </summary>
        /// <param name="stoppingToken">停止令牌</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.Information("上游订阅同步服务已启动，等待应用启动完成");

            try
            {
                // 等待宿主完全启动，避免与账号初始化等后台任务同时抢占数据库
                await WaitForApplicationStartedAsync(stoppingToken);

                _logger.Information("应用已启动，上游订阅同步调度开始");

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await TrySyncOnceAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        // 后台服务不能因为单轮异常退出
                        _logger.Error(ex, "上游订阅同步调度异常");
                    }

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(LoopIntervalSeconds), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // 宿主正常停止
            }
            finally
            {
                // 停止时释放领导锁，让其它节点可以立即接管
                ReleaseLeadership();

                _logger.Information("上游订阅同步服务已停止");
            }
        }

        /// <summary>
        /// 等待宿主触发 ApplicationStarted
        /// </summary>
        /// <param name="stoppingToken">停止令牌</param>
        private async Task WaitForApplicationStartedAsync(CancellationToken stoppingToken)
        {
            if (_applicationLifetime.ApplicationStarted.IsCancellationRequested)
            {
                return;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var startedRegistration = _applicationLifetime.ApplicationStarted.Register(() => tcs.TrySetResult(true));
            using var stoppingRegistration = stoppingToken.Register(() => tcs.TrySetCanceled(stoppingToken));

            await tcs.Task;
        }

        /// <summary>
        /// 执行一轮同步检查：解析配置、判断是否到点、抢分布式锁并同步
        /// </summary>
        /// <param name="stoppingToken">停止令牌</param>
        private async Task TrySyncOnceAsync(CancellationToken stoppingToken)
        {
            var setting = GlobalConfiguration.Setting;
            var config = setting?.UpstreamSubscription;

            if (config == null || config.Enable != true || config.Upstreams == null || config.Upstreams.Count == 0)
            {
                // 配置关闭时主动释放领导权，让其它已启用节点可以立即接管
                ReleaseLeadership();

                // 未启用时只记录 Debug，避免每 30 秒刷屏
                _logger.Debug("上游订阅同步未启用或未配置上游，跳过本轮调度");
                return;
            }

            var interval = NormalizeSyncInterval(config.SyncIntervalMinutes ?? 0);
            if (DateTime.Now < _nextRunAt)
            {
                return;
            }

            if (setting.IsValidRedis != true)
            {
                // 没有 Redis 时不能退化为本地锁，否则多节点会各自重复同步
                ReleaseLeadership();

                _logger.Warning("Redis 不可用，跳过上游订阅同步；该功能依赖 Redis 分布式锁保证单实例执行");
                _nextRunAt = DateTime.Now.AddMinutes(interval);
                return;
            }

            // 同步属于领导任务：只有拿到领导锁的节点执行，避免多节点按各自周期重复拉取上游
            if (!EnsureLeadership())
            {
                return;
            }

            try
            {
                await SyncUpstreamsAsync(config, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "上游订阅同步执行失败");
            }
            finally
            {
                // 无论成功失败都推进下一次执行时间，避免失败后每 30 秒重试
                _nextRunAt = DateTime.Now.AddMinutes(interval);
            }
        }

        /// <summary>
        /// 获取或续期同步领导锁。
        /// 只有持锁节点会执行同步，避免多节点各自按自己的周期重复拉取上游。
        /// </summary>
        /// <returns>当前节点是否持有领导锁</returns>
        private bool EnsureLeadership()
        {
            try
            {
                if (_leaderLock != null)
                {
                    // 校验锁仍属于当前实例，避免 Redis 重启或锁过期后被其他节点接管
                    if (_leaderLock.Refresh(SyncLockTtlSeconds * 1000))
                    {
                        return true;
                    }

                    ReleaseLeadership();
                }

                // 使用 TryLock 而不是 Lock：Lock 在锁被占用时会持续自旋重试，形成 Redis 命令风暴
                _leaderLock = RedisHelper.Instance.TryLock(SyncLockKey, SyncLockTtlSeconds, true);
                return _leaderLock != null;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "获取或续期上游订阅同步领导锁失败");
                ReleaseLeadership();
                return false;
            }
        }

        /// <summary>
        /// 释放同步领导锁
        /// </summary>
        private void ReleaseLeadership()
        {
            var leaderLock = _leaderLock;
            _leaderLock = null;

            try
            {
                leaderLock?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "释放上游订阅同步领导锁失败，等待自动过期");
            }
        }

        /// <summary>
        /// 同步所有启用的上游
        /// </summary>
        /// <param name="config">上游订阅配置</param>
        /// <param name="stoppingToken">停止令牌</param>
        private async Task SyncUpstreamsAsync(UpstreamSubscriptionConfig config, CancellationToken stoppingToken)
        {
            // 默认速度配置每轮解析一次，非法值只提示一次
            var defaultMode = ResolveDefaultMode(config.DefaultMode);
            var defaultAllowModes = ResolveDefaultAllowModes(config.DefaultAllowModes);

            var freeSql = FreeSqlHelper.FreeSql;

            // 一次性读取本地账号并建立判重索引，避免在循环中反复查询数据库
            var localAccounts = freeSql.Select<DiscordAccount>().ToList();
            var accountIndex = BuildAccountIndex(localAccounts);

            var hasChanged = false;

            foreach (var upstream in config.Upstreams)
            {
                if (upstream == null || upstream.Enable != true)
                {
                    continue;
                }

                var upstreamName = string.IsNullOrWhiteSpace(upstream.Name) ? "(未命名)" : upstream.Name.Trim();

                if (!TryNormalizeUpstreamUrl(upstream.Url, out var upstreamUrl))
                {
                    _logger.Warning("上游订阅项地址非法，已跳过: Name={Name}, Url={Url}", upstreamName, upstream.Url);
                    LogUpstreamSummary(upstreamName, upstream.Url, 0, 0, 0, 0, 0, 1);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(upstream.Token))
                {
                    _logger.Warning("上游订阅项未配置 Token，已跳过: Name={Name}, Url={Url}", upstreamName, upstreamUrl);
                    LogUpstreamSummary(upstreamName, upstreamUrl, 0, 0, 0, 0, 0, 1);
                    continue;
                }

                var pulledCount = 0;
                var addedCount = 0;
                var updatedCount = 0;
                var unchangedCount = 0;
                var skippedCount = 0;
                var failedCount = 0;

                try
                {
                    var upstreamAccounts = await FetchUpstreamAccountsAsync(upstreamUrl, upstream.Token, upstreamName, stoppingToken);
                    pulledCount = upstreamAccounts.Count;

                    foreach (var upstreamAccount in upstreamAccounts)
                    {
                        try
                        {
                            var result = ApplyUpstreamAccount(upstreamAccount, freeSql, accountIndex, defaultMode, defaultAllowModes, upstreamName);
                            switch (result)
                            {
                                case UpstreamAccountSyncResult.Added:
                                    addedCount++;
                                    hasChanged = true;
                                    break;

                                case UpstreamAccountSyncResult.Updated:
                                    updatedCount++;
                                    hasChanged = true;
                                    break;

                                case UpstreamAccountSyncResult.Unchanged:
                                    unchangedCount++;
                                    break;

                                default:
                                    skippedCount++;
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            // 单个账号处理失败不影响同批其它账号
                            failedCount++;
                            _logger.Error(ex, "同步上游账号失败: Name={Name}, ChannelId={ChannelId}", upstreamName, upstreamAccount?.ChannelId);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // 单个上游失败不影响其它上游；错误日志只记录名称与地址，绝不记录 Token
                    failedCount++;
                    _logger.Error(ex, "拉取上游账号失败: Name={Name}, Url={Url}", upstreamName, upstreamUrl);
                }

                LogUpstreamSummary(upstreamName, upstreamUrl, pulledCount, addedCount, updatedCount, unchangedCount, skippedCount, failedCount);
            }

            if (hasChanged)
            {
                PublishSyncedNotification();
            }
        }

        /// <summary>
        /// 输出单个上游的同步汇总日志，日志中不包含 Token
        /// </summary>
        /// <param name="upstreamName">上游名称</param>
        /// <param name="upstreamUrl">上游地址</param>
        /// <param name="pulled">拉取数</param>
        /// <param name="added">新增数</param>
        /// <param name="updated">更新数</param>
        /// <param name="unchanged">未变数</param>
        /// <param name="skipped">跳过数</param>
        /// <param name="failed">失败数</param>
        private void LogUpstreamSummary(string upstreamName, string upstreamUrl, int pulled, int added, int updated, int unchanged, int skipped, int failed)
        {
            _logger.Information(
                "上游订阅同步汇总: Name={Name}, Url={Url}, 拉取={Pulled}, 新增={Added}, 更新={Updated}, 未变={Unchanged}, 跳过={Skipped}, 失败={Failed}",
                upstreamName, upstreamUrl, pulled, added, updated, unchanged, skipped, failed);
        }

        /// <summary>
        /// 处理单个上游账号：本地不存在则新增，存在则只同步受管字段
        /// </summary>
        /// <param name="upstreamAccount">上游返回的账号</param>
        /// <param name="freeSql">FreeSql 实例</param>
        /// <param name="accountIndex">本地账号判重索引</param>
        /// <param name="defaultMode">默认固定速度模式</param>
        /// <param name="defaultAllowModes">默认允许速度集合</param>
        /// <param name="upstreamName">上游名称，仅用于日志</param>
        /// <returns>本次处理结果</returns>
        private UpstreamAccountSyncResult ApplyUpstreamAccount(
            DiscordAccount upstreamAccount,
            IFreeSql freeSql,
            Dictionary<string, DiscordAccount> accountIndex,
            GenerationSpeedMode? defaultMode,
            List<GenerationSpeedMode> defaultAllowModes,
            string upstreamName)
        {
            var loginKey = NormalizeLoginAccount(upstreamAccount.LoginAccount);
            var channelKey = NormalizeChannelId(upstreamAccount.ChannelId);

            if (string.IsNullOrEmpty(loginKey) && string.IsNullOrEmpty(channelKey))
            {
                _logger.Warning("上游账号缺少 LoginAccount 和 ChannelId，已跳过: Name={Name}", upstreamName);
                return UpstreamAccountSyncResult.Skipped;
            }

            // 登录账号优先；登录账号匹配不到时再用频道 ID 兜底，避免同一账号因上游补全登录账号而被重复新增
            DiscordAccount localAccount = null;
            if (!string.IsNullOrEmpty(loginKey))
            {
                accountIndex.TryGetValue(loginKey, out localAccount);
            }
            if (localAccount == null && !string.IsNullOrEmpty(channelKey))
            {
                accountIndex.TryGetValue(channelKey, out localAccount);
            }

            if (localAccount == null)
            {
                // 本地不存在：没有任何可用凭证时不新增，避免把脱敏占位值写进数据库
                // 登录密码不参与同步，因此这里只判断 UserToken / Cookie
                if (!IsUsableCredential(upstreamAccount.UserToken) && !IsUsableCredential(upstreamAccount.Cookie))
                {
                    _logger.Warning("上游账号无可用凭证，已跳过新增: Name={Name}, ChannelId={ChannelId}", upstreamName, upstreamAccount.ChannelId);
                    return UpstreamAccountSyncResult.Skipped;
                }

                var config = BuildAccountConfig(upstreamAccount, defaultMode, defaultAllowModes);
                var newAccount = DiscordAccount.Create(config);
                freeSql.Add(newAccount);

                // 同轮内立即加入索引，避免上游返回重复账号时重复插入
                AddAccountKey(accountIndex, NormalizeLoginAccount(newAccount.LoginAccount), newAccount);
                AddAccountKey(accountIndex, NormalizeChannelId(newAccount.ChannelId), newAccount);

                return UpstreamAccountSyncResult.Added;
            }

            // 本地存在：只比较/更新受管字段，绝不覆盖并发、队列、备注、登录密码等本地配置
            var tokenChanged = IsUsableCredential(upstreamAccount.UserToken)
                && !string.Equals(localAccount.UserToken, upstreamAccount.UserToken, StringComparison.Ordinal);
            var cookieChanged = IsUsableCredential(upstreamAccount.Cookie)
                && !string.Equals(localAccount.Cookie, upstreamAccount.Cookie, StringComparison.Ordinal);
            var enableChanged = localAccount.Enable != true;
            var modeChanged = localAccount.Mode != defaultMode;

            // AllowModes 忽略顺序和重复项后比较，避免上游顺序变化导致无意义的更新
            var localAllowModes = new HashSet<GenerationSpeedMode>(localAccount.AllowModes ?? new List<GenerationSpeedMode>());
            var allowModesChanged = !localAllowModes.SetEquals(defaultAllowModes);

            if (!tokenChanged && !cookieChanged && !enableChanged && !modeChanged && !allowModesChanged)
            {
                return UpstreamAccountSyncResult.Unchanged;
            }

            var newUserToken = tokenChanged ? upstreamAccount.UserToken : localAccount.UserToken;
            var newCookie = cookieChanged ? upstreamAccount.Cookie : localAccount.Cookie;
            var newAllowModes = new List<GenerationSpeedMode>(defaultAllowModes);

            // 使用强类型 Set，一次写入全部受管字段；登录密码不参与同步
            freeSql.Update<DiscordAccount>()
                .Set(c => c.UserToken, newUserToken)
                .Set(c => c.Cookie, newCookie)
                .Set(c => c.Enable, true)
                .Set(c => c.Mode, defaultMode)
                .Set(c => c.AllowModes, newAllowModes)
                .Where(c => c.Id == localAccount.Id)
                .ExecuteAffrows();

            // 同步内存对象，保证同轮内后续上游账号复用该索引时判断一致
            localAccount.UserToken = newUserToken;
            localAccount.Cookie = newCookie;
            localAccount.Enable = true;
            localAccount.Mode = defaultMode;
            localAccount.AllowModes = newAllowModes;

            // 通知所有节点重新加载账号缓存（例如刷新 token）
            localAccount.ClearCache();

            return UpstreamAccountSyncResult.Updated;
        }

        /// <summary>
        /// 按上游账号构造新增账号所需的配置，凭证为脱敏占位值时写入 null
        /// </summary>
        /// <param name="upstreamAccount">上游返回的账号</param>
        /// <param name="defaultMode">默认固定速度模式</param>
        /// <param name="defaultAllowModes">默认允许速度集合</param>
        /// <returns>账号创建配置</returns>
        private static DiscordAccountConfig BuildAccountConfig(
            DiscordAccount upstreamAccount,
            GenerationSpeedMode? defaultMode,
            List<GenerationSpeedMode> defaultAllowModes)
        {
            return new DiscordAccountConfig
            {
                IsDraft = upstreamAccount.IsDraft,
                ChannelId = upstreamAccount.ChannelId,
                GuildId = upstreamAccount.GuildId,
                PrivateChannelId = upstreamAccount.PrivateChannelId,
                NijiBotChannelId = upstreamAccount.NijiBotChannelId,

                // 上游可能脱敏返回 ****，这里只写入可用凭证；登录密码不参与同步
                UserToken = IsUsableCredential(upstreamAccount.UserToken) ? upstreamAccount.UserToken : null,
                Cookie = IsUsableCredential(upstreamAccount.Cookie) ? upstreamAccount.Cookie : null,

                UserAgent = upstreamAccount.UserAgent,
                Enable = true,
                EnableMj = upstreamAccount.EnableMj ?? true,
                EnableNiji = upstreamAccount.EnableNiji ?? true,
                EnableAutoSetRelax = upstreamAccount.EnableAutoSetRelax,
                CoreSize = upstreamAccount.CoreSize,
                QueueSize = upstreamAccount.QueueSize,
                RelaxCoreSize = upstreamAccount.RelaxCoreSize,
                RelaxQueueSize = upstreamAccount.RelaxQueueSize,
                TimeoutMinutes = upstreamAccount.TimeoutMinutes,
                IsBlend = upstreamAccount.IsBlend,
                IsDescribe = upstreamAccount.IsDescribe,
                IsShorten = upstreamAccount.IsShorten,
                DayDrawLimit = upstreamAccount.DayDrawLimit,
                DayRelaxDrawLimit = upstreamAccount.DayRelaxDrawLimit,
                IsVerticalDomain = upstreamAccount.IsVerticalDomain,
                VerticalDomainIds = upstreamAccount.VerticalDomainIds ?? new List<string>(),
                SubChannels = upstreamAccount.SubChannels ?? new List<string>(),
                Remark = upstreamAccount.Remark,
                Sponsor = upstreamAccount.Sponsor,
                IsSponsor = upstreamAccount.IsSponsor,
                Sort = upstreamAccount.Sort,
                Interval = upstreamAccount.Interval,
                AfterIntervalMin = upstreamAccount.AfterIntervalMin,
                AfterIntervalMax = upstreamAccount.AfterIntervalMax,
                WorkTime = upstreamAccount.WorkTime,
                FishingTime = upstreamAccount.FishingTime,
                PermanentInvitationLink = upstreamAccount.PermanentInvitationLink,
                Weight = upstreamAccount.Weight,
                RemixAutoSubmit = upstreamAccount.RemixAutoSubmit,
                LoginAccount = upstreamAccount.LoginAccount,
                Login2fa = upstreamAccount.Login2fa,
                IsYouChuan = upstreamAccount.IsYouChuan,
                YouChuanEnablePreferRelax = upstreamAccount.YouChuanEnablePreferRelax,
                IsOfficial = upstreamAccount.IsOfficial,
                OfficialEnablePersonalize = upstreamAccount.OfficialEnablePersonalize,
                IsHdVideo = upstreamAccount.IsHdVideo,
                IsRelaxVideo = upstreamAccount.IsRelaxVideo,

                // 默认速度由订阅配置统一决定，覆盖上游值
                Mode = defaultMode,
                AllowModes = new List<GenerationSpeedMode>(defaultAllowModes),
            };
        }

        /// <summary>
        /// 拉取一个上游的账号列表
        /// </summary>
        /// <param name="url">上游账号列表地址</param>
        /// <param name="token">上游管理员 Token，作为 mj-api-secret 请求头发送</param>
        /// <param name="upstreamName">上游名称，仅用于日志</param>
        /// <param name="stoppingToken">停止令牌</param>
        /// <returns>上游账号列表；空响应或解析失败返回空列表</returns>
        /// <exception cref="InvalidOperationException">上游返回非 2xx 或响应体超过大小限制时抛出</exception>
        private async Task<List<DiscordAccount>> FetchUpstreamAccountsAsync(
            string url,
            string token,
            string upstreamName,
            CancellationToken stoppingToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(RequestTimeout);

            var client = _httpClientFactory.CreateClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("mj-api-secret", token);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"上游返回非成功状态码: HTTP {(int)response.StatusCode}");
            }

            if (response.Content.Headers.ContentLength > MaxResponseBytes)
            {
                throw new InvalidOperationException($"上游响应超过 {MaxResponseBytes} 字节限制");
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            if (json.Length > MaxResponseBytes)
            {
                throw new InvalidOperationException($"上游响应超过 {MaxResponseBytes} 字节限制");
            }

            try
            {
                var accounts = JsonSerializer.Deserialize<List<DiscordAccount>>(json, JsonOptions);
                if (accounts == null || accounts.Count == 0)
                {
                    _logger.Warning("上游返回账号列表为空: Name={Name}, Url={Url}", upstreamName, url);
                    return [];
                }

                return accounts;
            }
            catch (Exception ex) when (ex is JsonException || ex is NotSupportedException)
            {
                _logger.Warning(ex, "解析上游账号列表失败，按 0 条处理: Name={Name}, Url={Url}", upstreamName, url);
                return [];
            }
        }

        /// <summary>
        /// 建立本地账号判重索引：LoginAccount 归一化后优先作为键，ChannelId 同时作为键
        /// </summary>
        /// <param name="accounts">本地账号列表</param>
        /// <returns>判重键到账号的映射</returns>
        private static Dictionary<string, DiscordAccount> BuildAccountIndex(List<DiscordAccount> accounts)
        {
            var index = new Dictionary<string, DiscordAccount>(StringComparer.Ordinal);

            foreach (var account in accounts)
            {
                AddAccountKey(index, NormalizeLoginAccount(account.LoginAccount), account);
                AddAccountKey(index, NormalizeChannelId(account.ChannelId), account);
            }

            return index;
        }

        /// <summary>
        /// 向判重索引添加键；键冲突时保留第一个账号
        /// </summary>
        /// <param name="index">判重索引</param>
        /// <param name="key">判重键</param>
        /// <param name="account">账号</param>
        private static void AddAccountKey(Dictionary<string, DiscordAccount> index, string key, DiscordAccount account)
        {
            if (string.IsNullOrEmpty(key) || account == null)
            {
                return;
            }

            index.TryAdd(key, account);
        }

        /// <summary>
        /// 归一化登录账号：去除首尾空白并转小写
        /// </summary>
        /// <param name="loginAccount">登录账号</param>
        /// <returns>归一化后的账号；空值返回 null</returns>
        private static string NormalizeLoginAccount(string loginAccount)
        {
            return string.IsNullOrWhiteSpace(loginAccount) ? null : loginAccount.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// 归一化频道 ID
        /// </summary>
        /// <param name="channelId">频道 ID</param>
        /// <returns>去除首尾空白后的频道 ID；空值返回 null</returns>
        private static string NormalizeChannelId(string channelId)
        {
            return string.IsNullOrWhiteSpace(channelId) ? null : channelId.Trim();
        }

        /// <summary>
        /// 判断凭证是否可用：空值或上游脱敏占位值 **** 视为不可用
        /// </summary>
        /// <param name="value">凭证值</param>
        /// <returns>是否可用</returns>
        private static bool IsUsableCredential(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value != "****";
        }

        /// <summary>
        /// 校验并归一化上游地址，仅允许 http/https 绝对地址。
        /// 允许只填写上游根地址（如 https://circle.huanwangai.com/），会自动补齐 /mj/admin/accounts；
        /// 已包含 /mj 或 /mj/admin 时按对应层级补齐。
        /// </summary>
        /// <param name="url">配置的上游地址</param>
        /// <param name="normalizedUrl">归一化后的账号列表接口地址</param>
        /// <returns>是否合法</returns>
        private static bool TryNormalizeUpstreamUrl(string url, out string normalizedUrl)
        {
            normalizedUrl = url?.Trim();

            if (string.IsNullOrWhiteSpace(normalizedUrl)
                || !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            normalizedUrl = BuildAccountsUrl(normalizedUrl);

            return Uri.TryCreate(normalizedUrl, UriKind.Absolute, out _);
        }

        /// <summary>
        /// 补齐上游账号列表接口路径。
        /// 兼容只填根地址、/mj、/mj/admin 以及完整账号列表地址，避免用户必须填写完整接口路径。
        /// </summary>
        /// <param name="url">已校验的 http/https 地址</param>
        /// <returns>完整的账号列表接口地址</returns>
        private static string BuildAccountsUrl(string url)
        {
            var trimmed = url.TrimEnd('/');
            var lower = trimmed.ToLowerInvariant();

            if (lower.EndsWith("/mj/admin/accounts"))
            {
                return trimmed;
            }
            if (lower.EndsWith("/mj/admin"))
            {
                return trimmed + "/accounts";
            }
            if (lower.EndsWith("/mj"))
            {
                return trimmed + "/admin/accounts";
            }

            return trimmed + "/mj/admin/accounts";
        }

        /// <summary>
        /// 归一化同步频率：非法值或超出 1-60 范围时按 5 分钟处理
        /// </summary>
        /// <param name="minutes">配置的频率（分钟）</param>
        /// <returns>1-60 之间的有效频率</returns>
        private static int NormalizeSyncInterval(int minutes)
        {
            return minutes < MinSyncIntervalMinutes || minutes > MaxSyncIntervalMinutes ? DefaultSyncIntervalMinutes : minutes;
        }

        /// <summary>
        /// 解析默认固定速度模式；空值或非法值返回 null 并清空上游模式
        /// </summary>
        /// <param name="value">配置的模式字符串</param>
        /// <returns>解析后的速度模式；空或非法返回 null</returns>
        private GenerationSpeedMode? ResolveDefaultMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (TryParseSpeedMode(value, out var mode))
            {
                return mode;
            }

            _logger.Warning("上游订阅默认速度模式非法，已按空值处理: DefaultMode={DefaultMode}", value);
            return null;
        }

        /// <summary>
        /// 解析默认允许速度集合；非法项忽略并记录一次 Warning
        /// </summary>
        /// <param name="values">配置的模式字符串集合</param>
        /// <returns>去重后的允许速度集合；空或全非法返回空集合</returns>
        private List<GenerationSpeedMode> ResolveDefaultAllowModes(List<string> values)
        {
            var modes = new List<GenerationSpeedMode>();
            if (values == null || values.Count == 0)
            {
                return modes;
            }

            var seen = new HashSet<GenerationSpeedMode>();
            foreach (var value in values)
            {
                if (TryParseSpeedMode(value, out var mode))
                {
                    if (seen.Add(mode))
                    {
                        modes.Add(mode);
                    }
                }
                else
                {
                    _logger.Warning("上游订阅默认允许速度模式项非法，已忽略: DefaultAllowMode={DefaultAllowMode}", value);
                }
            }

            return modes;
        }

        /// <summary>
        /// 解析速度模式字符串，不区分大小写，且必须是已定义的枚举值
        /// </summary>
        /// <param name="value">模式字符串</param>
        /// <param name="mode">解析结果</param>
        /// <returns>是否解析成功</returns>
        private static bool TryParseSpeedMode(string value, out GenerationSpeedMode mode)
        {
            mode = default;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Enum.TryParse(value.Trim(), true, out mode) && Enum.IsDefined(mode);
        }

        /// <summary>
        /// 发布上游账号同步完成通知，让各节点重新加载账号
        /// </summary>
        private void PublishSyncedNotification()
        {
            try
            {
                var notification = new RedisNotification { Type = ENotificationType.UpstreamAccountSynced };
                RedisHelper.Publish(RedisHelper.Prefix + Constants.REDIS_NOTIFY_CHANNEL, notification.ToJson());
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "发布上游账号同步完成通知失败");
            }
        }

        /// <summary>
        /// 单个上游账号的同步结果
        /// </summary>
        private enum UpstreamAccountSyncResult
        {
            /// <summary>
            /// 新增
            /// </summary>
            Added,

            /// <summary>
            /// 更新
            /// </summary>
            Updated,

            /// <summary>
            /// 无变化
            /// </summary>
            Unchanged,

            /// <summary>
            /// 跳过
            /// </summary>
            Skipped
        }
    }
}

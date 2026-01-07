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

using System.Diagnostics;
using System.Net;
using System.Text.Json;
using RestSharp;
using Serilog;

namespace Midjourney.Services
{
    /// <summary>
    /// Discord 账号实例创建/选择服务
    /// </summary>
    public class DiscordAccountService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDiscordRuleService _rule;
        private readonly INotifyService _notifyService;
        private readonly Dictionary<string, string> _paramsMap;
        private readonly HashSet<DiscordService> _instances = [];

        public DiscordAccountService(
            INotifyService notifyService,
            IHttpClientFactory httpClientFactory,
            IDiscordRuleService rule)
        {
            _notifyService = notifyService;
            _rule = rule;
            _httpClientFactory = httpClientFactory;

            var paramsMap = new Dictionary<string, string>();
            var assembly = typeof(GlobalConfiguration).Assembly;
            var assemblyName = assembly.GetName().Name;
            var resourceNames = assembly.GetManifestResourceNames()
                .Where(name => name.EndsWith(".json") && name.Contains("Resources.ApiParams"))
                .ToList();

            foreach (var resourceName in resourceNames)
            {
                var fileName = Path.GetFileNameWithoutExtension(resourceName);
                using var stream = assembly.GetManifestResourceStream(resourceName);
                using var reader = new StreamReader(stream);
                var paramsContent = reader.ReadToEnd();

                var fileKey = fileName.TrimPrefix(assemblyName + ".Resources.ApiParams.").TrimSuffix(".json");

                paramsMap[fileKey] = paramsContent;
            }

            _paramsMap = paramsMap;
        }

        /// <summary>
        /// 获取所有实例。
        /// </summary>
        /// <returns>所有实例列表。</returns>
        public List<DiscordService> GetAllInstances() => _instances.ToList();

        /// <summary>
        /// 获取存活的实例。
        /// </summary>
        /// <returns>存活的实例列表。</returns>
        public List<DiscordService> GetAliveInstances() => _instances.Where(c => c != null && c.IsAlive == true).ToList() ?? [];

        /// <summary>
        /// 选择一个实例。
        /// </summary>
        /// <returns>选择的实例。</returns>
        /// <param name="accountFilter"></param>
        /// <param name="isNewTask">是否过滤只接收新任务的实例</param>
        /// <param name="botType">过滤开启指定机器人的账号</param>
        /// <param name="blend">过滤支持 Blend 的账号</param>
        /// <param name="describe">过滤支持 Describe 的账号</param>
        /// <param name="isDomain">过滤垂直领域的账号</param>
        /// <param name="domainIds">过滤垂直领域 ID</param>
        /// <param name="instanceIds">指定 ids 账号</param>
        /// <param name="shorten"></param>
        /// <param name="preferredSpeedMode">首选速度模式，优先使用此模式过滤</param>
        /// <param name="isYm">悠船/官方账号</param>
        /// <param name="isHdVideo"></param>
        /// <param name="isUpscale">是否为 redis 模式下的放大任务，如果 redis 放大任务，则不验证队列长度</param>
        /// <param name="isVideo"></param>
        /// <param name="isYouChuan"></param>
        /// <param name="notInstanceIds">排除的账号</param>
        /// <param name="isActionTask">是否允许变化任务，例如：变化、视频拓展、弹窗任务</param>
        /// <param name="isDiscord">过滤 discord 账号</param>
        public (DiscordService instance, GenerationSpeedMode? confirmMode) ChooseInstance(
            AccountFilter accountFilter = null,
            bool? isNewTask = null,
            EBotType? botType = null,
            bool? blend = null,
            bool? describe = null,
            bool? isDomain = null,
            List<string> domainIds = null,
            List<string> instanceIds = null,
            bool? shorten = null,
            bool? isYm = null,
            bool? isVideo = null,
            bool? isHdVideo = null,
            bool? isYouChuan = null,
            bool? isUpscale = null,
            List<string> notInstanceIds = null,
            bool? isActionTask = null,
            bool? isDiscord = null)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                DiscordService inc = null;

                accountFilter ??= new AccountFilter();
                accountFilter.Modes ??= [];

                var modes = new List<GenerationSpeedMode>(accountFilter.Modes.Distinct());
                if (modes.Count == 0)
                {
                    // 如果没有速度模式，则添加默认的速度模式
                    modes = [GenerationSpeedMode.FAST, GenerationSpeedMode.TURBO, GenerationSpeedMode.RELAX];
                }

                // 根据顺序获取速度模式进行过滤，直到获取到可用实例为止
                // 如果所有速度模式都没有可用实例，则返回 null
                foreach (var mode in modes)
                {
                    var list = GetAliveInstances()
                       // 指定 ID 的实例
                       .WhereIf(!string.IsNullOrWhiteSpace(accountFilter?.InstanceId), c => c.ChannelId == accountFilter.InstanceId)

                       // 过滤指定账号
                       .WhereIf(instanceIds?.Count > 0, c => instanceIds.Contains(c.Account.ChannelId))

                       // 排除指定 ID 的实例
                       .WhereIf(notInstanceIds != null && notInstanceIds.Count > 0, c => !notInstanceIds.Contains(c.ChannelId))

                       // 允许继续绘图
                       .WhereIf(isUpscale != true, c => c.Account.Enable == true && c.IsAllowContinue(mode))

                       // 放大任务只判断启用即可
                       .WhereIf(isUpscale == true, c => c.Account.Enable == true)

                       // 判断悠船或官方账号
                       .WhereIf(isYm == true, c => c.Account.IsYouChuan || c.Account.IsOfficial)

                       // 判断悠船
                       .WhereIf(isYouChuan == true, c => c.Account.IsYouChuan)

                       // 过滤 discord 账号
                       .WhereIf(isDiscord == true, c => c.Account.IsDiscord)

                       // 判断是否允许视频操作
                       .WhereIf(isVideo == true, c => c.Account.IsAllowGenerateVideo())

                       // 高清视频支持
                       .WhereIf(isHdVideo == true, c => c.Account.IsHdVideo)

                       // Midjourney Remix 过滤
                       .WhereIf(accountFilter?.Remix == true, c => c.Account.MjRemixOn == accountFilter.Remix || !c.Account.RemixAutoSubmit)
                       .WhereIf(accountFilter?.Remix == false, c => c.Account.MjRemixOn == accountFilter.Remix)

                       // Niji Remix 过滤
                       .WhereIf(accountFilter?.NijiRemix == true, c => c.Account.NijiRemixOn == accountFilter.NijiRemix || !c.Account.RemixAutoSubmit)
                       .WhereIf(accountFilter?.NijiRemix == false, c => c.Account.NijiRemixOn == accountFilter.NijiRemix)

                       // Remix 自动提交过滤
                       .WhereIf(accountFilter?.RemixAutoConsidered.HasValue == true, c => c.Account.RemixAutoSubmit == accountFilter.RemixAutoConsidered)

                       // 通过备注过滤账号
                       .WhereIf(!string.IsNullOrWhiteSpace(accountFilter?.Remark), c => c.Account.Remark != null && c.Account.Remark.Contains(accountFilter.Remark))

                       // 过滤只接收新任务的实例
                       .WhereIf(isNewTask == true, c => c.Account.IsAcceptNewTask())

                       // 过滤允许变化任务的账号
                       .WhereIf(isActionTask == true, c => c.Account.IsAcceptActionTask())

                       // 过滤开启 niji mj 的账号
                       .WhereIf(botType == EBotType.NIJI_JOURNEY, c => c.Account.EnableNiji == true)
                       .WhereIf(botType == EBotType.MID_JOURNEY, c => c.Account.EnableMj == true)

                       // 过滤开启功能的账号
                       .WhereIf(blend == true, c => c.IsAllowDescribe())
                       .WhereIf(describe == true, c => c.Account.IsDescribe)
                       .WhereIf(shorten == true, c => c.Account.IsShorten)

                       // 领域过滤
                       .WhereIf(isDomain == true && domainIds?.Count > 0, c => c.Account.IsVerticalDomain && c.Account.VerticalDomainIds.Any(x => domainIds.Contains(x)))
                       .WhereIf(isDomain == false, c => c.Account.IsVerticalDomain != true)

                       .ToList();

                    inc = _rule.Choose(list);

                    if (inc != null)
                    {
                        return (inc, mode);
                    }
                }

                return (inc, null);
            }
            finally
            {
                sw.Stop();

                if (sw.ElapsedMilliseconds > 100)
                {
                    Log.Warning("选择实例耗时过长，请联系开发者优化：{ElapsedMilliseconds} ms，过滤参数：{@AccountFilter}, isNewTask={isNewTask}, botType={botType}, blend={blend}, describe={describe}, isDomain={isDomain}, domainIds={domainIds}, ids={ids}, shorten={shorten}, isYm={isYm}, isVideo={isVideo}, isHdVideo={isHdVideo}, isYouChuan={isYouChuan}, isUpscale={isUpscale}",
                        sw.ElapsedMilliseconds,
                        accountFilter,
                        isNewTask,
                        botType,
                        blend,
                        describe,
                        isDomain,
                        domainIds,
                        instanceIds,
                        shorten,
                        isYm,
                        isVideo,
                        isHdVideo,
                        isYouChuan,
                        isUpscale);
                }
            }
        }

        /// <summary>
        /// 选择一个图生文可用实例 - 不判断速度 - 随机获取实例
        /// </summary>
        /// <param name="preferredSpeedMode"></param>
        /// <returns></returns>
        public DiscordService GetDescribeInstance(string instanceId = null)
        {
            var list = GetAliveInstances()
                .WhereIf(!string.IsNullOrWhiteSpace(instanceId), c => c.ChannelId == instanceId)
                .Where(c => c.Account.Enable == true && c.IsAlive && c.Account.IsDescribe && c.Account.IsAcceptNewTask())
                .OrderBy(c => Guid.NewGuid())
                .Take(10)
                .ToList();

            // 为了减少压力，这里随机取 10 个实例进行二次筛选符合的只有筛选不到才全部筛选
            var di = list.Where(c => c.IsAllowDescribe()).OrderBy(c => Guid.NewGuid()).FirstOrDefault();
            if (di != null)
            {
                return di;
            }

            // 实在找不到就全部筛选
            return GetAliveInstances()
                .WhereIf(!string.IsNullOrWhiteSpace(instanceId), c => c.ChannelId == instanceId)
                .Where(c => c.Account.Enable == true && c.IsAlive && c.Account.IsDescribe && c.Account.IsAcceptNewTask())
                .Where(c => c.IsAllowDescribe())
                .OrderBy(c => Guid.NewGuid())
                .FirstOrDefault();
        }

        /// <summary>
        /// 获取指定ID的实例（不判断是否存活）
        /// </summary>
        /// <param name="channelId">实例ID/渠道ID</param>
        /// <returns>实例。</returns>
        public DiscordService GetDiscordInstance(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return null;
            }

            return _instances.FirstOrDefault(c => c.ChannelId == channelId);
        }

        /// <summary>
        /// 获取指定ID的实例（必须是存活的）
        /// </summary>
        /// <param name="channelId">实例ID/渠道ID</param>
        /// <returns>实例。</returns>
        public DiscordService GetDiscordInstanceIsAlive(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return null;
            }

            return _instances.FirstOrDefault(c => c.ChannelId == channelId && c.IsAlive);
        }

        /// <summary>
        /// 添加 Discord 实例
        /// </summary>
        /// <param name="instance"></param>
        public void AddInstance(DiscordService instance) => _instances.Add(instance);

        /// <summary>
        /// 移除
        /// </summary>
        /// <param name="instance"></param>
        public void RemoveInstance(DiscordService instance) => _instances.Remove(instance);

        /// <summary>
        /// 获取一个启用官方个性化的存活的实例
        /// </summary>
        /// <returns></returns>
        public DiscordService GetAliveOfficialPersonalizeInstance()
        {
            var list = GetAliveInstances().Where(c => c.Account.OfficialEnablePersonalize).ToList();

            return _rule.Choose(list);
        }

        /// <summary>
        /// 创建Discord实例。
        /// </summary>
        /// <param name="account">Discord账号信息。</param>
        /// <returns>Discord实例。</returns>
        /// <exception cref="ArgumentException">当guildId, channelId或userToken为空时抛出。</exception>
        public async Task<DiscordService> CreateDiscordInstance(DiscordAccount account)
        {
            if (!account.IsYouChuan && !account.IsOfficial)
            {
                if (string.IsNullOrWhiteSpace(account.GuildId) || string.IsNullOrWhiteSpace(account.ChannelId) || string.IsNullOrWhiteSpace(account.UserToken))
                {
                    throw new ArgumentException("guildId, channelId, userToken must not be blank");
                }
            }

            if (string.IsNullOrWhiteSpace(account.UserAgent))
            {
                account.UserAgent = Constants.DEFAULT_DISCORD_USER_AGENT;
            }

            var discordInstance = new DiscordService(account, _notifyService, _paramsMap, _httpClientFactory);

            if (account.Enable == true)
            {
                if (account.IsYouChuan)
                {
                    if (string.IsNullOrWhiteSpace(account.UserToken))
                    {
                        await discordInstance.YouChuanLogin();
                    }
                }
                else if (account.IsOfficial)
                {
                }
                else
                {
                }
            }

            return discordInstance;
        }

        /// <summary>
        /// 验证账号是否可用
        /// </summary>
        /// <param name="account"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<bool> ValidateAccount(DiscordAccount account)
        {
            if (string.IsNullOrWhiteSpace(account.UserAgent))
            {
                account.UserAgent = Constants.DEFAULT_DISCORD_USER_AGENT;
            }

            WebProxy webProxy = null;
            var setting = GlobalConfiguration.Setting;
            if (!string.IsNullOrEmpty(setting.Proxy?.Host))
            {
                webProxy = new WebProxy(setting.Proxy.Host, setting.Proxy.Port ?? 80);
            }

            var hch = new HttpClientHandler
            {
                UseProxy = webProxy != null,
                Proxy = webProxy
            };

            var client = new HttpClient(hch)
            {
                Timeout = TimeSpan.FromMinutes(10),
            };

            var request = new HttpRequestMessage(HttpMethod.Get, DiscordHelper.DISCORD_VAL_URL);
            request.Headers.Add("Authorization", account.UserToken);
            request.Headers.Add("User-Agent", account.UserAgent);

            var response = await client.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return true;
            }

            var data = JsonDocument.Parse(json).RootElement;
            if (data.TryGetProperty("message", out var message))
            {
                throw new Exception(message.GetString() ?? "账号验证异常");
            }

            return false;
        }

        /// <summary>
        /// 获取私信 ID
        /// </summary>
        /// <param name="account"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<string> GetBotPrivateId(DiscordAccount account, EBotType botType)
        {
            if (string.IsNullOrWhiteSpace(account.UserAgent))
            {
                account.UserAgent = Constants.DEFAULT_DISCORD_USER_AGENT;
            }

            WebProxy webProxy = null;
            var setting = GlobalConfiguration.Setting;
            if (!string.IsNullOrEmpty(setting.Proxy?.Host))
            {
                webProxy = new WebProxy(setting.Proxy.Host, setting.Proxy.Port ?? 80);
            }

            var hch = new HttpClientHandler
            {
                UseProxy = webProxy != null,
                Proxy = webProxy
            };

            var client = new HttpClient(hch)
            {
                Timeout = TimeSpan.FromMinutes(10),
            };

            var request = new HttpRequestMessage(HttpMethod.Post, DiscordHelper.ME_CHANNELS_URL);
            request.Headers.Add("Authorization", account.UserToken);
            request.Headers.Add("User-Agent", account.UserAgent);

            var obj = new
            {
                recipients = new string[] { botType == EBotType.MID_JOURNEY ? Constants.MJ_APPLICATION_ID : Constants.NIJI_APPLICATION_ID }
            };
            var objStr = JsonSerializer.Serialize(obj);
            var content = new StringContent(objStr, null, "application/json");
            request.Content = content;

            var response = await client.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var data = JsonDocument.Parse(json).RootElement;
                if (data.TryGetProperty("id", out var id))
                {
                    return id.GetString();
                }
            }

            throw new Exception($"获取私信 ID 失败 {response?.StatusCode}, {response?.Content}");
        }

        /// <summary>
        /// 自动登录
        /// </summary>
        /// <param name="model"></param>
        /// <param name="beforeEnable">登陆前账号是否启用</param>
        /// <returns></returns>
        public static bool AutoLogin(DiscordAccount model, bool beforeEnable = false)
        {
            if (string.IsNullOrWhiteSpace(model.LoginAccount)
                || string.IsNullOrWhiteSpace(model.LoginPassword)
                || string.IsNullOrWhiteSpace(model.Login2fa))
            {
                return false;
            }

            var setting = GlobalConfiguration.Setting;
            var notifyUrl = $"{setting.CaptchaServer.Trim().TrimEnd('/')}/login/auto";
            var client = new RestClient();
            var request = new RestRequest(notifyUrl, Method.Post);
            request.AddHeader("Content-Type", "application/json");
            var body = new AutoLoginRequest
            {
                Login2fa = model.Login2fa,
                LoginAccount = model.LoginAccount,
                LoginPassword = model.LoginPassword,
                LoginBeforeEnabled = beforeEnable,
                State = model.ChannelId,
                NotifyHook = setting.CaptchaNotifyHook,
                Secret = setting.CaptchaNotifySecret,
            };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(body);
            request.AddJsonBody(json);
            var response = client.Execute(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                model.IsAutoLogining = true;
                model.LoginStart = DateTime.Now;

                FreeSqlHelper.FreeSql.Update("LoginStart,IsAutoLogining", model);

                return true;
            }

            Log.Error($"自动登录失败 failed: {response.Content}");

            return false;
        }
    }
}
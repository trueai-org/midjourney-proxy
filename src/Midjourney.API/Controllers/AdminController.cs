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

using LiteDB;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.StandardTable;
using Midjourney.Infrastructure.Storage;
using MongoDB.Driver;
using Serilog;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Midjourney.API.Controllers
{
    /// <summary>
    /// 管理后台接口
    /// 用于查询、管理账号等
    /// </summary>
    [ApiController]
    [Route("mj/admin")]
    public class AdminController : ControllerBase
    {
        private readonly IMemoryCache _memoryCache;
        private readonly ITaskService _taskService;

        // 是否匿名用户
        private readonly bool _isAnonymous;

        private readonly DiscordLoadBalancer _loadBalancer;
        private readonly DiscordAccountInitializer _discordAccountInitializer;
        private readonly ProxyProperties _properties;
        private readonly WorkContext _workContext;

        public AdminController(
            ITaskService taskService,
            DiscordLoadBalancer loadBalancer,
            DiscordAccountInitializer discordAccountInitializer,
            IMemoryCache memoryCache,
            WorkContext workContext,
            IHttpContextAccessor context)
        {
            _memoryCache = memoryCache;
            _loadBalancer = loadBalancer;
            _taskService = taskService;
            _discordAccountInitializer = discordAccountInitializer;
            _workContext = workContext;

            // 如果不是管理员，并且是演示模式时，则是为匿名用户
            var user = workContext.GetUser();

            _isAnonymous = user?.Role != EUserRole.ADMIN;
            _properties = GlobalConfiguration.Setting;

            // 普通用户，无法登录管理后台，演示模式除外
            // 判断当前用户如果是普通用户
            // 并且不是匿名控制器时
            if (user?.Role != EUserRole.ADMIN)
            {
                var endpoint = context.HttpContext.GetEndpoint();
                var allowAnonymous = endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null;
                if (!allowAnonymous && GlobalConfiguration.IsDemoMode != true)
                {
                    // 如果是普通用户, 并且不是匿名控制器，则返回 401
                    context.HttpContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    context.HttpContext.Response.WriteAsync("Forbidden: User is not admin.");
                    return;
                }
            }
        }

        ///// <summary>
        ///// 重启
        ///// </summary>
        ///// <returns></returns>
        //[HttpPost("restart")]
        //public Result Restart()
        //{
        //    try
        //    {
        //        if (_isAnonymous)
        //        {
        //            return Result.Fail("演示模式，禁止操作");
        //        }

        //        // 使用 dotnet 命令启动 DLL
        //        var fileName = "dotnet";
        //        var arguments = Path.GetFileName(Assembly.GetExecutingAssembly().Location);

        //        var processStartInfo = new ProcessStartInfo
        //        {
        //            FileName = fileName,
        //            Arguments = arguments,
        //            WorkingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
        //            UseShellExecute = true
        //        };
        //        Process.Start(processStartInfo);

        //        // 退出当前应用程序
        //        Environment.Exit(0);

        //        return Result.Ok("Application is restarting...");
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error(ex, "系统自动重启异常");

        //        return Result.Fail("重启失败，请手动重启");
        //    }
        //}

        /// <summary>
        /// 注册用户
        /// </summary>
        /// <param name="registerDto"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("register")]
        [AllowAnonymous]
        public Result Register([FromBody] RegisterDto registerDto)
        {
            if (registerDto == null || string.IsNullOrWhiteSpace(registerDto.Email))
            {
                throw new LogicException("参数错误");
            }

            // 验证长度
            if (registerDto.Email.Length < 5 || registerDto.Email.Length > 50)
            {
                throw new LogicException("邮箱长度错误");
            }

            var mail = registerDto.Email.Trim();

            // 验证 email 格式
            var isMatch = Regex.IsMatch(mail, @"^[\w-]+(\.[\w-]+)*@[\w-]+(\.[\w-]+)+$");
            if (!isMatch)
            {
                throw new LogicException("邮箱格式错误");
            }

            // 判断是否开放注册
            // 如果没有配置邮件服务，则不允许注册
            if (GlobalConfiguration.Setting.EnableRegister != true
                || string.IsNullOrWhiteSpace(GlobalConfiguration.Setting?.Smtp?.FromPassword))
            {
                throw new LogicException("注册已关闭");
            }

            // 每个IP每天只能注册一个账号
            var ip = _workContext.GetIp();
            var key = $"register:{ip}";
            if (_memoryCache.TryGetValue(key, out _))
            {
                throw new LogicException("注册太频繁");
            }

            // 验证用户是否存在
            var user = DbHelper.Instance.UserStore.Single(u => u.Email == mail);
            if (user != null)
            {
                throw new LogicException("用户已存在");
            }

            user = new User
            {
                Id = Guid.NewGuid().ToString("N"),
                Role = EUserRole.USER,
                Status = EUserStatus.NORMAL,
                DayDrawLimit = GlobalConfiguration.Setting.RegisterUserDefaultDayLimit,
                Email = mail,
                RegisterIp = ip,
                RegisterTime = DateTime.Now,
                Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                Name = mail.Split('@').FirstOrDefault()
            };
            DbHelper.Instance.UserStore.Add(user);

            // 发送邮件
            EmailJob.Instance.EmailSend(GlobalConfiguration.Setting.Smtp,
                $"Midjourney Proxy 注册通知", $"您的登录密码为：{user.Token}",
                user.Email);

            // 设置缓存
            _memoryCache.Set(key, true, TimeSpan.FromDays(1));

            return Result.Ok();
        }

        /// <summary>
        /// 管理员登录
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("login")]
        public ActionResult Login([FromBody] string token)
        {
            // 如果没有开启访客模式，则不允许匿名登录
            if (GlobalConfiguration.IsDemoMode != true && string.IsNullOrWhiteSpace(token))
            {
                throw new LogicException("禁止登录");
            }

            // 如果 DEMO 模式，并且没有传入 token，则返回空 token
            if (GlobalConfiguration.IsDemoMode == true && string.IsNullOrWhiteSpace(token))
            {
                return Ok(new
                {
                    code = 1,
                    apiSecret = "",
                });
            }

            //// 如果开启访客
            //if (string.IsNullOrWhiteSpace(token) && GlobalConfiguration.Setting.EnableGuest)
            //{
            //    return Ok(new
            //    {
            //        code = 1,
            //        apiSecret = "",
            //    });
            //}

            var user = DbHelper.Instance.UserStore.Single(u => u.Token == token);
            if (user == null)
            {
                throw new LogicException("用户 Token 错误");
            }

            if (user.Status == EUserStatus.DISABLED)
            {
                throw new LogicException("用户已被禁用");
            }

            // 非演示模式，普通用户和访客无法登录后台
            if (user.Role != EUserRole.ADMIN && GlobalConfiguration.IsDemoMode != true)
            {
                throw new LogicException("用户无权限");
            }

            // 更新最后登录时间
            user.LastLoginTime = DateTime.Now;
            user.LastLoginIp = _workContext.GetIp();

            DbHelper.Instance.UserStore.Update(user);

            return Ok(new
            {
                code = 1,
                apiSecret = user.Token,
            });
        }

        /// <summary>
        /// 管理员退出
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("logout")]
        public ActionResult Logout()
        {
            return Ok();
        }

        /// <summary>
        /// CF 验证通过通知（允许匿名）
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("account-cf-notify")]
        public ActionResult Validate([FromBody] CaptchaVerfyRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.State) && !string.IsNullOrWhiteSpace(request.Url))
            {
                var item = DbHelper.Instance.AccountStore.Single(c => c.ChannelId == request.State);

                if (item != null && item.Lock)
                {
                    var secret = GlobalConfiguration.Setting.CaptchaNotifySecret;
                    if (string.IsNullOrWhiteSpace(secret) || secret == request.Secret)
                    {
                        // 10 分钟之内有效
                        if (item.CfHashCreated != null && (DateTime.Now - item.CfHashCreated.Value).TotalMinutes > 10)
                        {
                            if (request.Success)
                            {
                                request.Success = false;
                                request.Message = "CF 验证过期，超过 10 分钟";
                            }

                            Log.Warning("CF 验证过期，超过 10 分钟 {@0}, time: {@1}", request, item.CfHashCreated);
                        }

                        if (request.Success)
                        {
                            item.Lock = false;
                            item.CfHashUrl = null;
                            item.CfHashCreated = null;
                            item.CfUrl = null;
                            item.DisabledReason = null;
                        }
                        else
                        {
                            // 更新验证失败原因
                            item.DisabledReason = request.Message;
                        }

                        // 更新账号信息
                        DbHelper.Instance.AccountStore.Update(item);

                        // 清空缓存
                        var inc = _loadBalancer.GetDiscordInstance(item.ChannelId);
                        inc?.ClearAccountCache(item.Id);

                        if (!request.Success)
                        {
                            // 发送邮件
                            EmailJob.Instance.EmailSend(_properties.Smtp, $"CF自动真人验证失败-{item.ChannelId}", $"CF自动真人验证失败-{item.ChannelId}, 请手动验证");
                        }
                    }
                    else
                    {
                        // 签名错误
                        Log.Warning("验证通知签名验证失败 {@0}", request);

                        return Ok();
                    }
                }
            }

            return Ok();
        }

        /// <summary>
        /// 当前用户信息
        /// </summary>
        /// <returns></returns>
        [HttpGet("current")]
        [AllowAnonymous]
        public ActionResult Current()
        {
            var user = _workContext.GetUser();

            var token = user?.Token;
            var name = user?.Name ?? "Guest";

            // 如果未开启访客，且未登录，且未开启演示模式，则返回 403
            if (GlobalConfiguration.Setting.EnableGuest != true && user == null && GlobalConfiguration.IsDemoMode != true)
            {
                return StatusCode(403);
            }

            return Ok(new
            {
                id = name,
                userid = name,
                name = name,
                apiSecret = token,
                version = GlobalConfiguration.Version,
                active = true,
                imagePrefix = "",
                avatar = "",
                email = "",
                signature = "",
                title = "",
                group = "",
                tags = new[]
                {
                    new { key = "role",label = user?.Role?.GetDescription() ?? "Guest" },
                },
                notifyCount = 0,
                unreadCount = 0,
                country = "",
                access = "",
                geographic = new
                {
                    province = new { label = "", key = "" },
                    city = new { label = "", key = "" }
                },
                address = "",
                phone = ""
            });
        }

        /// <summary>
        /// 获取日志
        /// </summary>
        /// <param name="tail"></param>
        /// <returns></returns>
        [HttpGet("probe")]
        public IActionResult GetLogs([FromQuery] int tail = 1000)
        {
            // 演示模式 100 条
            if (_isAnonymous)
            {
                tail = 100;
            }

            // 项目目录，而不是 AppContext.BaseDirectory
            var logFilePath = Path.Combine(Directory.GetCurrentDirectory(), $"logs/log{DateTime.Now:yyyyMMdd}.txt");

            if (!System.IO.File.Exists(logFilePath))
            {
                return NotFound("Log file not found.");
            }

            try
            {
                using (var fileStream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var streamReader = new StreamReader(fileStream))
                {
                    var logLines = streamReader.ReadToEnd().Split(Environment.NewLine).Reverse().Take(tail).Reverse().ToArray();
                    return Ok(string.Join("\n", logLines));
                }
            }
            catch (IOException ex)
            {
                return StatusCode(500, $"Error reading log file: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据账号ID获取账号信息
        /// 指定ID获取账号
        /// </summary>
        /// <param name="id">账号ID</param>
        /// <returns>Discord账号信息</returns>
        [HttpGet("account/{id}")]
        public ActionResult<DiscordAccount> Fetch(string id)
        {
            //var instance = _loadBalancer.GetDiscordInstance(id);
            //return instance == null ? (ActionResult<DiscordAccount>)NotFound() : Ok(instance.Account);

            var item = DbHelper.Instance.AccountStore.Get(id);
            if (item == null)
            {
                throw new LogicException("账号不存在");
            }

            if (_isAnonymous)
            {
                // Token 加密
                item.UserToken = item.UserToken?.Substring(0, 4) + "****" + item.UserToken?.Substring(item.UserToken.Length - 4);
                item.BotToken = item.BotToken?.Substring(0, 4) + "****" + item.BotToken?.Substring(item.BotToken.Length - 4);
            }

            return Ok(item);
        }

        /// <summary>
        /// 执行 info 和 setting
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpPost("account-sync/{id}")]
        public async Task<Result> SyncAccount(string id)
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            await _taskService.InfoSetting(id);
            return Result.Ok();
        }

        /// <summary>
        /// 获取 cf 真人验证链接
        /// </summary>
        /// <param name="id"></param>
        /// <param name="refresh">是否获取新链接</param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpGet("account-cf/{id}")]
        public async Task<Result<DiscordAccount>> CfUrlValidate(string id, [FromQuery] bool refresh = false)
        {
            if (_isAnonymous)
            {
                throw new LogicException("演示模式，禁止操作");
            }

            var item = DbHelper.Instance.AccountStore.Get(id);
            if (item == null)
            {
                throw new LogicException("账号不存在");
            }

            if (!item.Lock || string.IsNullOrWhiteSpace(item.CfHashUrl))
            {
                throw new LogicException("CF 验证链接不存在");
            }

            // 发送 hashUrl GET 请求, 返回 {"hash":"OOUxejO94EQNxsCODRVPbg","token":"dXDm-gSb4Zlsx-PCkNVyhQ"}
            // 通过 hash 和 token 拼接验证 CF 验证 URL

            if (refresh)
            {
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
                var hashUrl = item.CfHashUrl;
                var response = await httpClient.GetAsync(hashUrl);
                var con = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(con))
                {
                    // 解析
                    var json = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(con);
                    if (json.TryGetProperty("hash", out var h) && json.TryGetProperty("token", out var to))
                    {
                        var hashStr = h.GetString();
                        var token = to.GetString();

                        if (!string.IsNullOrWhiteSpace(hashStr) && !string.IsNullOrWhiteSpace(token))
                        {
                            // 通过 hash 和 token 拼接验证 CF 验证 URL
                            // https://editor.midjourney.com/captcha/challenge/index.html?hash=OOUxejO94EQNxsCODRVPbg&token=dXDm-gSb4Zlsx-PCkNVyhQ

                            var url = $"https://editor.midjourney.com/captcha/challenge/index.html?hash={hashStr}&token={token}";

                            item.CfUrl = url;

                            // 更新账号信息
                            DbHelper.Instance.AccountStore.Update(item);

                            // 清空缓存
                            var inc = _loadBalancer.GetDiscordInstance(item.ChannelId);
                            inc?.ClearAccountCache(item.Id);
                        }
                    }
                    else
                    {
                        throw new LogicException("生成链接失败");
                    }
                }
            }

            return Result.Ok(item);
        }

        /// <summary>
        /// CF 验证标记完成
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("account-cf/{id}")]
        public Result CfUrlValidateOK(string id)
        {
            if (_isAnonymous)
            {
                throw new LogicException("演示模式，禁止操作");
            }

            var item = DbHelper.Instance.AccountStore.Get(id);
            if (item == null)
            {
                throw new LogicException("账号不存在");
            }

            //if (!item.Lock)
            //{
            //    throw new LogicException("不需要 CF 验证");
            //}

            item.Lock = false;
            item.CfHashUrl = null;
            item.CfHashCreated = null;
            item.CfUrl = null;
            item.DisabledReason = null;

            // 更新账号信息
            DbHelper.Instance.AccountStore.Update(item);

            // 清空缓存
            var inc = _loadBalancer.GetDiscordInstance(item.ChannelId);
            inc?.ClearAccountCache(item.Id);

            return Result.Ok();
        }

        /// <summary>
        /// 修改版本
        /// </summary>
        /// <param name="id"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        [HttpPost("account-change-version/{id}")]
        public async Task<Result> AccountChangeVersion(string id, [FromQuery] string version)
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            await _taskService.AccountChangeVersion(id, version);
            return Result.Ok();
        }

        /// <summary>
        /// 执行操作
        /// </summary>
        /// <param name="id"></param>
        /// <param name="customId"></param>
        /// <param name="botType"></param>
        /// <returns></returns>
        [HttpPost("account-action/{id}")]
        public async Task<Result> AccountAction(string id, [FromQuery] string customId, [FromQuery] EBotType botType)
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            await _taskService.AccountAction(id, customId, botType);
            return Result.Ok();
        }

        /// <summary>
        /// 添加账号
        /// </summary>
        /// <param name="accountConfig"></param>
        /// <returns></returns>
        [HttpPost("account")]
        public Result AccountAdd([FromBody] DiscordAccountConfig accountConfig)
        {
            var setting = GlobalConfiguration.Setting;
            var user = _workContext.GetUser();

            if (user == null)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            if (!setting.EnableAccountSponsor && user.Role != EUserRole.ADMIN)
            {
                return Result.Fail("未开启赞助功能，禁止操作");
            }

            // 同一个用户每天最多只能赞助 10 个账号
            var limitKey = $"{DateTime.Now:yyyyMMdd}:sponsor:{user.Id}";
            var sponsorCount = 0;
            if (setting.EnableAccountSponsor && user.Role != EUserRole.ADMIN)
            {
                if (_memoryCache.TryGetValue(limitKey, out sponsorCount) && sponsorCount > 10)
                {
                    Result.Fail("每天最多只能赞助 10 个账号");
                }
            }

            var model = DbHelper.Instance.AccountStore.Single(c => c.ChannelId == accountConfig.ChannelId);

            if (model != null)
            {
                throw new LogicException("渠道已存在");
            }

            var account = DiscordAccount.Create(accountConfig);

            // 赞助账号
            if (account.IsSponsor)
            {
                account.SponsorUserId = user.Id;

                // 赞助者禁止配置的选项
                if (user.Role != EUserRole.ADMIN)
                {
                    account.Sort = 0;
                    account.SubChannels.Clear();
                    account.WorkTime = null;
                    account.FishingTime = null;
                }

                // 赞助者参数校验
                account.SponsorValidate();
            }

            DbHelper.Instance.AccountStore.Add(account);

            // 后台执行
            _ = _discordAccountInitializer.StartCheckAccount(account);

            // 更新缓存
            if (setting.EnableAccountSponsor && user.Role != EUserRole.ADMIN)
            {
                sponsorCount++;

                _memoryCache.Set(limitKey, sponsorCount, TimeSpan.FromDays(1));
            }

            return Result.Ok();
        }

        /// <summary>
        /// 编辑账号
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        [HttpPut("account/{id}")]
        public async Task<Result> AccountEdit([FromBody] DiscordAccount param)
        {
            var setting = GlobalConfiguration.Setting;
            var user = _workContext.GetUser();

            if (user == null)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            if (!setting.EnableAccountSponsor && user.Role != EUserRole.ADMIN)
            {
                return Result.Fail("未开启赞助功能，禁止操作");
            }

            //if (_isAnonymous)
            //{
            //    return Result.Fail("演示模式，禁止操作");
            //}

            var model = DbHelper.Instance.AccountStore.Get(param.Id);
            if (model == null)
            {
                throw new LogicException("账号不存在");
            }

            if (user.Role != EUserRole.ADMIN && model.SponsorUserId != user.Id)
            {
                return Result.Fail("无权限操作");
            }

            // 赞助者禁止配置的选项
            if (user.Role != EUserRole.ADMIN)
            {
                param.Sort = model.Sort;
                param.SubChannels = model.SubChannels;
                param.WorkTime = model.WorkTime;
                param.FishingTime = model.WorkTime;

                // 赞助者参数校验
                param.SponsorValidate();
            }

            model.NijiBotChannelId = param.NijiBotChannelId;
            model.PrivateChannelId = param.PrivateChannelId;
            model.RemixAutoSubmit = param.RemixAutoSubmit;
            model.TimeoutMinutes = param.TimeoutMinutes;
            model.Weight = param.Weight;
            model.Remark = param.Remark;
            model.Sponsor = param.Sponsor;
            model.Sort = param.Sort;
            model.PermanentInvitationLink = param.PermanentInvitationLink;
            model.IsVerticalDomain = param.IsVerticalDomain;
            model.VerticalDomainIds = param.VerticalDomainIds;
            model.SubChannels = param.SubChannels;
            model.IsBlend = param.IsBlend;
            model.EnableMj = param.EnableMj;
            model.EnableNiji = param.EnableNiji;
            model.IsDescribe = param.IsDescribe;
            model.IsShorten = param.IsShorten;
            model.DayDrawLimit = param.DayDrawLimit;

            // 初始化子频道
            model.InitSubChannels();

            await _discordAccountInitializer.UpdateAccount(model);

            return Result.Ok();
        }

        /// <summary>
        /// 更新账号并重新连接
        /// </summary>
        /// <param name="id"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        [HttpPut("account-reconnect/{id}")]
        public async Task<Result> AccountReconnect(string id, [FromBody] DiscordAccount param)
        {
            if (id != param.Id)
            {
                throw new LogicException("参数错误");
            }

            var setting = GlobalConfiguration.Setting;
            var user = _workContext.GetUser();

            if (user == null)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            if (!setting.EnableAccountSponsor && user.Role != EUserRole.ADMIN)
            {
                return Result.Fail("未开启赞助功能，禁止操作");
            }

            var model = DbHelper.Instance.AccountStore.Get(id);
            if (model == null)
            {
                throw new LogicException("账号不存在");
            }

            if (user.Role != EUserRole.ADMIN && model.SponsorUserId != user.Id)
            {
                return Result.Fail("无权限操作");
            }

            // 不可修改频道 ID
            if (param.GuildId != model.GuildId || param.ChannelId != model.ChannelId)
            {
                return Result.Fail("禁止修改频道 ID 和服务器 ID");
            }

            await _discordAccountInitializer.ReconnectAccount(param);

            return Result.Ok();
        }

        /// <summary>
        /// 删除账号
        /// </summary>
        /// <returns></returns>
        [HttpDelete("account/{id}")]
        public Result AccountDelete(string id)
        {
            var setting = GlobalConfiguration.Setting;
            var user = _workContext.GetUser();

            if (user == null)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            if (!setting.EnableAccountSponsor && user.Role != EUserRole.ADMIN)
            {
                return Result.Fail("未开启赞助功能，禁止操作");
            }

            var model = DbHelper.Instance.AccountStore.Get(id);
            if (model == null)
            {
                throw new LogicException("账号不存在");
            }

            if (user.Role != EUserRole.ADMIN && model.SponsorUserId != user.Id)
            {
                return Result.Fail("无权限操作");
            }

            //if (_isAnonymous)
            //{
            //    return Result.Fail("演示模式，禁止操作");
            //}

            _discordAccountInitializer.DeleteAccount(id);

            return Result.Ok();
        }

        /// <summary>
        /// 获取所有账号信息（只返回启用账号）
        /// </summary>
        /// <returns>所有Discord账号信息</returns>
        [HttpGet("accounts")]
        public ActionResult<List<DiscordAccount>> List()
        {
            var user = _workContext.GetUser();

            var list = DbHelper.Instance.AccountStore.GetAll().Where(c => c.Enable == true)
                .ToList()
                .OrderBy(c => c.Sort).ThenBy(c => c.DateCreated).ToList();

            foreach (var item in list)
            {
                var inc = _loadBalancer.GetDiscordInstance(item.ChannelId);

                item.RunningCount = inc?.GetRunningFutures().Count ?? 0;
                item.QueueCount = inc?.GetQueueTasks().Count ?? 0;
                item.Running = inc?.IsAlive ?? false;

                if (user == null || (user.Role != EUserRole.ADMIN && user.Id != item.SponsorUserId))
                {
                    // Token 加密
                    item.UserToken = item.UserToken?.Substring(0, item.UserToken.Length / 5) + "****";
                    item.BotToken = item.BotToken?.Substring(0, item.BotToken.Length / 5) + "****";

                    item.CfUrl = "****";
                    item.CfHashUrl = "****";
                    item.PermanentInvitationLink = "****";
                    item.Remark = "****";

                    if (item.SubChannels.Count > 0)
                    {
                        // 加密
                        item.SubChannels = item.SubChannels.Select(c => "****").ToList();
                    }
                }
            }

            return Ok(list);
        }

        /// <summary>
        /// 分页获取账号信息
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("accounts")]
        public ActionResult<StandardTableResult<DiscordAccount>> Accounts([FromBody] StandardTableParam<DiscordAccount> request)
        {
            var user = _workContext.GetUser();

            var page = request.Pagination;
            if (page.PageSize > 100)
            {
                page.PageSize = 100;
            }

            // 演示模式 100 条
            if (_isAnonymous)
            {
                page.PageSize = 10;

                if (page.Current > 10)
                {
                    throw new LogicException("演示模式，禁止查看更多数据");
                }
            }

            var sort = request.Sort;
            var param = request.Search;

            var list = new List<DiscordAccount>();
            var count = 0;

            if (GlobalConfiguration.Setting.IsMongo)
            {
                var coll = MongoHelper.GetCollection<DiscordAccount>().AsQueryable();
                var query = coll
                    .WhereIf(!string.IsNullOrWhiteSpace(param.GuildId), c => c.GuildId == param.GuildId)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.ChannelId), c => c.ChannelId == param.ChannelId)
                    .WhereIf(param.Enable.HasValue, c => c.Enable == param.Enable)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Remark), c => c.Remark.Contains(param.Remark))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Sponsor), c => c.Sponsor.Contains(param.Sponsor));

                count = query.Count();
                list = query
                    .OrderByIf(nameof(DiscordAccount.GuildId).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.GuildId, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.ChannelId).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.ChannelId, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.Enable).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.Enable, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.Remark).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.Remark, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.Sponsor).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.Sponsor, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.DateCreated).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.DateCreated, sort.Reverse)
                    .OrderByIf(string.IsNullOrWhiteSpace(sort.Predicate), c => c.Sort, false)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Take(page.PageSize)
                    .ToList();
            }
            else
            {
                var query = LiteDBHelper.AccountStore.GetCollection().Query()
                    .WhereIf(!string.IsNullOrWhiteSpace(param.GuildId), c => c.GuildId == param.GuildId)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.ChannelId), c => c.ChannelId == param.ChannelId)
                    .WhereIf(param.Enable.HasValue, c => c.Enable == param.Enable)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Remark), c => c.Remark.Contains(param.Remark))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Sponsor), c => c.Sponsor.Contains(param.Sponsor));

                count = query.Count();
                list = query
                    .OrderByIf(nameof(DiscordAccount.GuildId).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.GuildId, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.ChannelId).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.ChannelId, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.Enable).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.Enable, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.Remark).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.Remark, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.Sponsor).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.Sponsor, sort.Reverse)
                    .OrderByIf(nameof(DiscordAccount.DateCreated).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.DateCreated, sort.Reverse)
                    .OrderByIf(string.IsNullOrWhiteSpace(sort.Predicate), c => c.Sort, false)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Limit(page.PageSize)
                    .ToList();
            }

            foreach (var item in list)
            {
                var inc = _loadBalancer.GetDiscordInstance(item.ChannelId);

                item.RunningCount = inc?.GetRunningFutures().Count ?? 0;
                item.QueueCount = inc?.GetQueueTasks().Count ?? 0;
                item.Running = inc?.IsAlive ?? false;

                if (user == null || (user.Role != EUserRole.ADMIN && user.Id != item.SponsorUserId))
                {
                    // Token 加密
                    item.UserToken = item.UserToken?.Substring(0, item.UserToken.Length / 5) + "****";
                    item.BotToken = item.BotToken?.Substring(0, item.BotToken.Length / 5) + "****";

                    item.CfUrl = "****";
                    item.CfHashUrl = "****";
                    item.PermanentInvitationLink = "****";
                    item.Remark = "****";

                    if (item.SubChannels.Count > 0)
                    {
                        // 加密
                        item.SubChannels = item.SubChannels.Select(c => "****").ToList();
                    }
                }
            }

            var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

            return Ok(data);
        }

        /// <summary>
        /// 获取所有任务信息
        /// </summary>
        /// <returns>所有任务信息</returns>
        [HttpPost("tasks")]
        public ActionResult<StandardTableResult<TaskInfo>> Tasks([FromBody] StandardTableParam<TaskInfo> request)
        {
            var page = request.Pagination;
            if (page.PageSize > 100)
            {
                page.PageSize = 100;
            }

            // 演示模式 100 条
            if (_isAnonymous)
            {
                page.PageSize = 10;

                if (page.Current > 10)
                {
                    throw new LogicException("演示模式，禁止查看更多数据");
                }
            }

            var param = request.Search;

            // 这里使用原生查询，因为查询条件比较复杂
            if (GlobalConfiguration.Setting.IsMongo)
            {
                var coll = MongoHelper.GetCollection<TaskInfo>().AsQueryable();
                var query = coll
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id || c.State == param.Id)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.InstanceId), c => c.InstanceId == param.InstanceId)
                    .WhereIf(param.Status.HasValue, c => c.Status == param.Status)
                    .WhereIf(param.Action.HasValue, c => c.Action == param.Action)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.FailReason), c => c.FailReason.Contains(param.FailReason))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Description), c => c.Description.Contains(param.Description) || c.Prompt.Contains(param.Description) || c.PromptEn.Contains(param.Description));

                var count = query.Count();
                var list = query
                    .OrderByDescending(c => c.SubmitTime)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Take(page.PageSize)
                    .ToList();

                var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

                return Ok(data);
            }
            else
            {
                var query = LiteDBHelper.TaskStore.GetCollection().Query()
                .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id || c.State == param.Id)
                .WhereIf(!string.IsNullOrWhiteSpace(param.InstanceId), c => c.InstanceId == param.InstanceId)
                .WhereIf(param.Status.HasValue, c => c.Status == param.Status)
                .WhereIf(param.Action.HasValue, c => c.Action == param.Action)
                .WhereIf(!string.IsNullOrWhiteSpace(param.FailReason), c => c.FailReason.Contains(param.FailReason))
                .WhereIf(!string.IsNullOrWhiteSpace(param.Description), c => c.Description.Contains(param.Description) || c.Prompt.Contains(param.Description) || c.PromptEn.Contains(param.Description));

                var count = query.Count();
                var list = query
                    .OrderByDescending(c => c.SubmitTime)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Limit(page.PageSize)
                    .ToList();

                var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

                return Ok(data);
            }
        }

        /// <summary>
        /// 删除作业
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete("task/{id}")]
        public Result TaskDelete(string id)
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            var queueTask = _loadBalancer.GetQueueTasks().FirstOrDefault(t => t.Id == id);
            if (queueTask != null)
            {
                queueTask.Fail("删除任务");

                Thread.Sleep(1000);
            }

            var task = DbHelper.Instance.TaskStore.Get(id);
            if (task != null)
            {
                var ins = _loadBalancer.GetDiscordInstance(task.InstanceId);
                if (ins != null)
                {
                    var model = ins.FindRunningTask(c => c.Id == id).FirstOrDefault();
                    if (model != null)
                    {
                        model.Fail("删除任务");

                        Thread.Sleep(1000);
                    }
                }

                DbHelper.Instance.TaskStore.Delete(id);
            }

            return Result.Ok();
        }

        /// <summary>
        /// 用户列表
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("users")]
        public ActionResult<StandardTableResult<User>> Users([FromBody] StandardTableParam<User> request)
        {
            var page = request.Pagination;

            // 演示模式 100 条
            if (_isAnonymous)
            {
                page.PageSize = 10;

                if (page.Current > 10)
                {
                    throw new LogicException("演示模式，禁止查看更多数据");
                }
            }

            var param = request.Search;

            var count = 0;
            var list = new List<User>();
            if (GlobalConfiguration.Setting.IsMongo)
            {
                var coll = MongoHelper.GetCollection<User>().AsQueryable();
                var query = coll
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Name), c => c.Name.Contains(param.Name))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Email), c => c.Email.Contains(param.Email))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Phone), c => c.Phone.Contains(param.Phone))
                    .WhereIf(param.Role.HasValue, c => c.Role == param.Role)
                    .WhereIf(param.Status.HasValue, c => c.Status == param.Status);

                count = query.Count();
                list = query
                    .OrderByDescending(c => c.UpdateTime)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Take(page.PageSize)
                    .ToList();
            }
            else
            {
                var query = LiteDBHelper.UserStore.GetCollection().Query()
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Name), c => c.Name.Contains(param.Name))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Email), c => c.Email.Contains(param.Email))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Phone), c => c.Phone.Contains(param.Phone))
                    .WhereIf(param.Role.HasValue, c => c.Role == param.Role)
                    .WhereIf(param.Status.HasValue, c => c.Status == param.Status);

                count = query.Count();
                list = query
                   .OrderByDescending(c => c.UpdateTime)
                   .Skip((page.Current - 1) * page.PageSize)
                   .Limit(page.PageSize)
                   .ToList();
            }

            if (_isAnonymous)
            {
                // 对用户信息进行脱敏处理
                foreach (var item in list)
                {
                    item.Name = "***";
                    item.Email = "***";
                    item.Phone = "***";
                    item.Token = "***";
                }
            }

            var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

            return Ok(data);
        }

        /// <summary>
        /// 添加或编辑用户
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("user")]
        public Result UserAddOrEdit([FromBody] User user)
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            var oldToken = user?.Token;

            if (string.IsNullOrWhiteSpace(user.Id))
            {
                user.Id = Guid.NewGuid().ToString();
            }
            else
            {
                var model = DbHelper.Instance.UserStore.Get(user.Id);
                if (model == null)
                {
                    throw new LogicException("用户不存在");
                }

                oldToken = model?.Token;

                user.LastLoginIp = model.LastLoginIp;
                user.LastLoginTime = model.LastLoginTime;
                user.RegisterIp = model.RegisterIp;
                user.RegisterTime = model.RegisterTime;
                user.CreateTime = model.CreateTime;
            }

            // 参数校验
            // token 不能为空
            if (string.IsNullOrWhiteSpace(user.Token))
            {
                throw new LogicException("Token 不能为空");
            }

            // 判断 token 重复
            var tokenUser = DbHelper.Instance.UserStore.Single(c => c.Id != user.Id && c.Token == user.Token);
            if (tokenUser != null)
            {
                throw new LogicException("Token 重复");
            }

            // 用户名不能为空
            if (string.IsNullOrWhiteSpace(user.Name))
            {
                throw new LogicException("用户名不能为空");
            }

            // 角色
            if (user.Role == null)
            {
                user.Role = EUserRole.USER;
            }

            // 状态
            if (user.Status == null)
            {
                user.Status = EUserStatus.NORMAL;
            }

            user.UpdateTime = DateTime.Now;

            DbHelper.Instance.UserStore.Save(user);

            // 清除缓存
            var key = $"USER_{oldToken}";
            _memoryCache.Remove(key);

            return Result.Ok();
        }

        /// <summary>
        /// 删除用户
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete("user/{id}")]
        public Result UserDelete(string id)
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            var model = DbHelper.Instance.UserStore.Get(id);
            if (model == null)
            {
                throw new LogicException("用户不存在");
            }
            if (model.Id == Constants.ADMIN_USER_ID)
            {
                throw new LogicException("不能删除管理员账号");
            }
            if (model.Id == Constants.DEFAULT_USER_ID)
            {
                throw new LogicException("不能删除默认账号");
            }

            // 清除缓存
            var key = $"USER_{model.Token}";
            _memoryCache.Remove(key);

            DbHelper.Instance.UserStore.Delete(id);

            return Result.Ok();
        }

        /// <summary>
        /// 获取所有启动的领域标签
        /// </summary>
        /// <returns></returns>
        [HttpGet("domain-tags")]
        public Result<List<SelectOption>> DomainTags()
        {
            var data = DbHelper.Instance.DomainStore.GetAll()
                .Select(c => new SelectOption()
                {
                    Value = c.Id,
                    Label = c.Name,
                    Disabled = !c.Enable
                }).ToList();

            return Result.Ok(data);
        }

        /// <summary>
        /// 领域标签管理
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("domain-tags")]
        public ActionResult<StandardTableResult<DomainTag>> Domains([FromBody] StandardTableParam<DomainTag> request)
        {
            var page = request.Pagination;

            var firstKeyword = request.Search.Keywords?.FirstOrDefault();
            var param = request.Search;

            var count = 0;
            var list = new List<DomainTag>();
            if (GlobalConfiguration.Setting.IsMongo)
            {
                var coll = MongoHelper.GetCollection<DomainTag>().AsQueryable();
                var query = coll
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                    .WhereIf(!string.IsNullOrWhiteSpace(firstKeyword), c => c.Keywords.Contains(firstKeyword));

                count = query.Count();
                list = query
                    .OrderBy(c => c.Sort)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Take(page.PageSize)
                    .ToList();
            }
            else
            {
                var query = LiteDBHelper.DomainStore.GetCollection().Query()
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                    .WhereIf(!string.IsNullOrWhiteSpace(firstKeyword), c => c.Keywords.Contains(firstKeyword));

                count = query.Count();
                list = query
                   .OrderBy(c => c.Sort)
                   .Skip((page.Current - 1) * page.PageSize)
                   .Limit(page.PageSize)
                   .ToList();
            }

            var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

            return Ok(data);
        }

        /// <summary>
        /// 添加或编辑领域标签
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("domain-tag")]
        public Result DomainAddOrEdit([FromBody] DomainTag domain)
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            if (string.IsNullOrWhiteSpace(domain.Id))
            {
                domain.Id = Guid.NewGuid().ToString();
            }
            else
            {
                var model = DbHelper.Instance.DomainStore.Get(domain.Id);
                if (model == null)
                {
                    throw new LogicException("领域标签不存在");
                }

                domain.CreateTime = model.CreateTime;
            }

            domain.UpdateTime = DateTime.Now;

            domain.Keywords = domain.Keywords.Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim().ToLower())
                .Distinct()
                .ToList();

            DbHelper.Instance.DomainStore.Save(domain);

            _taskService.ClearDomainCache();

            return Result.Ok();
        }

        /// <summary>
        /// 删除领域标签
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete("domain-tag/{id}")]
        public Result DomainDelete(string id)
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            var model = DbHelper.Instance.DomainStore.Get(id);
            if (model == null)
            {
                throw new LogicException("领域标签不存在");
            }

            if (model.Id == Constants.DEFAULT_DOMAIN_ID)
            {
                throw new LogicException("不能删除默认领域标签");
            }

            if (model.Id == Constants.DEFAULT_DOMAIN_FULL_ID)
            {
                throw new LogicException("不能删除默认领域标签");
            }

            DbHelper.Instance.DomainStore.Delete(id);

            _taskService.ClearDomainCache();

            return Result.Ok();
        }

        /// <summary>
        /// 违规词
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("banned-words")]
        public ActionResult<StandardTableResult<BannedWord>> BannedWords([FromBody] StandardTableParam<BannedWord> request)
        {
            var page = request.Pagination;

            var firstKeyword = request.Search.Keywords?.FirstOrDefault();
            var param = request.Search;

            var count = 0;
            var list = new List<BannedWord>();

            if (GlobalConfiguration.Setting.IsMongo)
            {
                var coll = MongoHelper.GetCollection<BannedWord>().AsQueryable();
                var query = coll
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                    .WhereIf(!string.IsNullOrWhiteSpace(firstKeyword), c => c.Keywords.Contains(firstKeyword));

                count = query.Count();
                list = query
                   .OrderBy(c => c.Sort)
                   .Skip((page.Current - 1) * page.PageSize)
                   .Take(page.PageSize)
                   .ToList();
            }
            else
            {
                var query = LiteDBHelper.BannedWordStore.GetCollection().Query()
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                    .WhereIf(!string.IsNullOrWhiteSpace(firstKeyword), c => c.Keywords.Contains(firstKeyword));

                count = query.Count();
                list = query
                    .OrderBy(c => c.Sort)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Limit(page.PageSize)
                    .ToList();
            }

            var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

            return Ok(data);
        }

        /// <summary>
        /// 添加或编辑违规词
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("banned-word")]
        public Result BannedWordAddOrEdit([FromBody] BannedWord param)
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            if (string.IsNullOrWhiteSpace(param.Id))
            {
                param.Id = Guid.NewGuid().ToString();
            }
            else
            {
                var model = DbHelper.Instance.BannedWordStore.Get(param.Id);
                if (model == null)
                {
                    throw new LogicException("违规词不存在");
                }

                model.CreateTime = model.CreateTime;
            }

            param.UpdateTime = DateTime.Now;

            param.Keywords = param.Keywords.Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim().ToLower())
                .Distinct()
                .ToList();

            DbHelper.Instance.BannedWordStore.Save(param);

            _taskService.ClearBannedWordsCache();

            return Result.Ok();
        }

        /// <summary>
        /// 删除违规词
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpDelete("banned-word/{id}")]
        public Result BannedWordDelete(string id)
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            var model = DbHelper.Instance.BannedWordStore.Get(id);
            if (model == null)
            {
                throw new LogicException("违规词不存在");
            }

            if (model.Id == Constants.DEFAULT_BANNED_WORD_ID)
            {
                throw new LogicException("不能删除默认违规词");
            }

            DbHelper.Instance.BannedWordStore.Delete(id);

            _taskService.ClearBannedWordsCache();

            return Result.Ok();
        }

        /// <summary>
        /// 获取系统配置
        /// </summary>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpGet("setting")]
        public Result<Setting> GetSetting()
        {
            var model = LiteDBHelper.SettingStore.Get(Constants.DEFAULT_SETTING_ID);
            if (model == null)
            {
                throw new LogicException("系统配置错误，请重启服务");
            }

            model.IsMongo = GlobalConfiguration.Setting.IsMongo;

            // 演示模式，部分配置不可见
            if (_isAnonymous)
            {
                if (model.Smtp != null)
                {
                    model.Smtp.FromPassword = "****";
                    model.Smtp.FromEmail = "****";
                    model.Smtp.To = "****";
                }

                if (model.BaiduTranslate != null)
                {
                    model.BaiduTranslate.Appid = "****";
                    model.BaiduTranslate.AppSecret = "****";
                }

                if (model.Openai != null)
                {
                    model.Openai.GptApiUrl = "****";
                    model.Openai.GptApiKey = "****";
                }

                if (!string.IsNullOrWhiteSpace(model.MongoDefaultConnectionString))
                {
                    model.MongoDefaultConnectionString = "****";
                }

                if (model.AliyunOss != null)
                {
                    model.AliyunOss.AccessKeyId = "****";
                    model.AliyunOss.AccessKeySecret = "****";
                }

                if (model.Replicate != null)
                {
                    model.Replicate.Token = "****";
                }

                if (model.TencentCos != null)
                {
                    model.TencentCos.SecretId = "****";
                    model.TencentCos.SecretKey = "****";
                }

                if (model.CloudflareR2 != null)
                {
                    model.CloudflareR2.AccessKey = "****";
                    model.CloudflareR2.SecretKey = "****";
                }

                model.CaptchaNotifySecret = "****";
            }

            return Result.Ok(model);
        }

        /// <summary>
        /// 编辑系统配置
        /// </summary>
        /// <param name="setting"></param>
        /// <returns></returns>
        [HttpPost("setting")]
        public Result SettingEdit([FromBody] Setting setting)
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            setting.Id = Constants.DEFAULT_SETTING_ID;

            LiteDBHelper.SettingStore.Update(setting);

            GlobalConfiguration.Setting = setting;

            // 存储服务
            StorageHelper.Configure();

            // 首页缓存
            _memoryCache.Remove("HOME");
            var now = DateTime.Now.ToString("yyyyMMdd");
            var key = $"{now}_home";
            _memoryCache.Remove(key);

            return Result.Ok();
        }

        /// <summary>
        /// MJ Plus 数据迁移（迁移账号数据和任务数据）
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        [HttpPost("mjplus-migration")]
        public async Task<Result> MjPlusMigration([FromBody] MjPlusMigrationDto dto)
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            await _taskService.MjPlusMigration(dto);

            return Result.Ok();
        }

        /// <summary>
        /// 验证 mongo db 是否正常连接
        /// </summary>
        /// <returns></returns>
        [HttpPost("verify-mongo")]
        public Result ValidateMongo()
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            if (string.IsNullOrWhiteSpace(GlobalConfiguration.Setting.MongoDefaultConnectionString)
                || string.IsNullOrWhiteSpace(GlobalConfiguration.Setting.MongoDefaultDatabase))
            {
                return Result.Fail("MongoDB 配置错误，请保存配置后再验证");
            }

            var success = MongoHelper.Verify();

            return success ? Result.Ok() : Result.Fail("连接失败");
        }
    }
}
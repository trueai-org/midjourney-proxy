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
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using Midjourney.License;
using MongoDB.Driver;
using Serilog;

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
        private readonly Setting _properties;
        private readonly WorkContext _workContext;

        private readonly IUpgradeService _upgradeService;

        public AdminController(
            ITaskService taskService,
            DiscordLoadBalancer loadBalancer,
            DiscordAccountInitializer discordAccountInitializer,
            IMemoryCache memoryCache,
            WorkContext workContext,
            IHttpContextAccessor context,
            IUpgradeService upgradeService)
        {
            _upgradeService = upgradeService;
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

        /// <summary>
        /// 注册用户
        /// </summary>
        /// <param name="registerDto"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<Result> Register([FromBody] RegisterDto registerDto)
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
                TotalDrawLimit = GlobalConfiguration.Setting.RegisterUserDefaultTotalLimit,
                CoreSize = GlobalConfiguration.Setting.RegisterUserDefaultCoreSize,
                QueueSize = GlobalConfiguration.Setting.RegisterUserDefaultQueueSize,
                Email = mail,
                RegisterIp = ip,
                RegisterTime = DateTime.Now,
                Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                Name = mail.Split('@').FirstOrDefault()
            };
            DbHelper.Instance.UserStore.Add(user);

            // 发送邮件
            await EmailJob.Instance.EmailSend(GlobalConfiguration.Setting.Smtp,
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
        public async Task<ActionResult> Validate([FromBody] CaptchaVerfyRequest request)
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
                            await EmailJob.Instance.EmailSend(_properties.Smtp, $"CF自动真人验证失败-{item.ChannelId}", $"CF自动真人验证失败-{item.ChannelId}, 请手动验证");
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
        /// <param name="level">0: 默认, 1: 错误</param>
        /// <returns></returns>
        [HttpGet("probe")]
        public IActionResult GetLogs([FromQuery] int tail = 1000, [FromQuery] int level = 0)
        {
            // 演示模式 100 条
            if (_isAnonymous)
            {
                tail = 100;
                return Ok("演示模式，禁止操作");
            }

            var logName = "log";
            if (level >= 1)
            {
                logName = "error";
            }

            // log20250720.txt
            // log20250720_001.txt

            // 要获取最后一个文件
            var dirs = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            if (!Directory.Exists(dirs))
            {
                return Ok("Log directory not found.");
            }

            // 获取最新的日志文件
            var logFiles = Directory.GetFiles(dirs, $"{logName}*.txt")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();


            // 项目目录，而不是 AppContext.BaseDirectory
            //var logFilePath = Path.Combine(Directory.GetCurrentDirectory(), $"logs/{logName}{DateTime.Now:yyyyMMdd}.txt");
            var logFilePath = logFiles.FirstOrDefault()?.FullName;
            if (!System.IO.File.Exists(logFilePath))
            {
                return Ok("Log file not found.");
            }

            try
            {
                // 如果文件超过 100MB
                var fileInfo = new FileInfo(logFilePath);
                if (fileInfo.Length > 100 * 1024 * 1024)
                {
                    return Ok("Log file was too large.");
                }

                using (var fileStream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var streamReader = new StreamReader(fileStream))
                {
                    var logLines = streamReader.ReadToEnd().Split(Environment.NewLine).Reverse().Take(tail).Reverse().ToArray();
                    return Ok(string.Join("\n", logLines));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error reading log file");

                return StatusCode(500, $"Error reading log file");
            }
        }

        /// <summary>
        /// 下载日志 - 最新的错误日志/和最新的日志 zip 打包
        /// </summary>
        /// <param name="top"></param>
        /// <returns></returns>
        [HttpGet("download-logs")]
        public IActionResult DownloadLogs([FromQuery] int top = 10)
        {
            if (_isAnonymous)
            {
                return Ok("演示模式，禁止操作");
            }

            var logName = "log";
            var errorLogName = "error";

            // 获取最新的日志文件
            var dirs = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            if (!Directory.Exists(dirs))
            {
                return Ok("Log directory not found.");
            }

            var logFiles = Directory.GetFiles(dirs, $"{logName}*.txt")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .Take(top)
                .ToList();

            var errorLogFiles = Directory.GetFiles(dirs, $"{errorLogName}*.txt")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .Take(top)
                .ToList();

            //// 最新的日志文件
            //var logFilePath = logFiles.FirstOrDefault()?.FullName;
            //var errorLogFilePath = errorLogFiles.FirstOrDefault()?.FullName;
            //if (string.IsNullOrWhiteSpace(logFilePath) || !System.IO.File.Exists(logFilePath))
            //{
            //    return Ok("Log file not found.");
            //}

            // 打包为 zip
            var zipFilePath = Path.Combine(dirs, $"logs_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            using (var zipStream = new FileStream(zipFilePath, FileMode.Create))
            using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create))
            {
                //// 添加最新的日志文件
                //archive.CreateEntryFromFile(logFilePath, Path.GetFileName(logFilePath));

                //// 添加最新的错误日志文件
                //if (!string.IsNullOrWhiteSpace(errorLogFilePath) && System.IO.File.Exists(errorLogFilePath))
                //{
                //    archive.CreateEntryFromFile(errorLogFilePath, Path.GetFileName(errorLogFilePath));
                //}

                // 添加最新的日志文件
                foreach (var logFile in logFiles)
                {
                    if (logFile.Exists)
                    {
                        //archive.CreateEntryFromFile(logFile.FullName, logFile.Name);

                        using (var fileStream = new FileStream(logFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            var entry = archive.CreateEntry(logFile.Name);
                            using (var entryStream = entry.Open())
                            {
                                fileStream.CopyTo(entryStream);
                            }
                        }
                    }
                }

                // 添加最新的错误日志文件
                foreach (var errorLogFile in errorLogFiles)
                {
                    if (errorLogFile.Exists)
                    {
                        //archive.CreateEntryFromFile(errorLogFile.FullName, errorLogFile.Name);

                        using (var fileStream = new FileStream(errorLogFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            var entry = archive.CreateEntry(errorLogFile.Name);
                            using (var entryStream = entry.Open())
                            {
                                fileStream.CopyTo(entryStream);
                            }
                        }
                    }
                }
            }

            // 返回 zip 文件
            return PhysicalFile(zipFilePath, "application/zip", $"logs_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
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
                item.UserToken = "***";
                item.BotToken = "***";
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
        /// 账号登录（通过账号、密码、2FA）
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpPost("account-login/{id}")]
        public Result AccountLogin(string id)
        {
            var user = _workContext.GetUser();
            if (user == null)
            {
                return Result.Fail("演示模式，禁止操作");
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

            if (string.IsNullOrWhiteSpace(model.LoginAccount)
                || string.IsNullOrWhiteSpace(model.LoginPassword)
                || string.IsNullOrWhiteSpace(model.Login2fa))
            {
                return Result.Fail("账号、密码、2FA 不能为空");
            }

            var ok = DiscordAccountHelper.AutoLogin(model, model.Enable ?? false);
            if (ok)
            {
                return Result.Ok("登录请求已发送，请稍后刷新列表！");
            }

            return Result.Fail($"登录请求失败，请稍后重试！");
        }

        /// <summary>
        /// 账号登录（通过账号、密码、2FA） - 登录完成回调
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("account-login-notify")]
        public async Task<ActionResult> AccountLoginNotify([FromBody] AutoLoginRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.State) && !string.IsNullOrWhiteSpace(request.LoginAccount))
            {
                var item = DbHelper.Instance.AccountStore.Single(c => c.ChannelId == request.State && c.LoginAccount == request.LoginAccount);

                if (item != null && item.IsAutoLogining == true)
                {
                    var secret = GlobalConfiguration.Setting.CaptchaNotifySecret;
                    if (string.IsNullOrWhiteSpace(secret) || secret == request.Secret)
                    {
                        // 10 分钟之内有效
                        if (item.LoginStart != null && (DateTime.Now - item.LoginStart.Value).TotalMinutes > 10)
                        {
                            if (request.Success)
                            {
                                request.Success = false;
                                request.Message = "登录超时，超过 10 分钟";
                            }

                            Log.Warning("登录超时，超过 10 分钟 {@0}, time: {@1}", request, item.LoginStart);
                        }

                        if (request.Success && !string.IsNullOrWhiteSpace(request.Token))
                        {
                            item.IsAutoLogining = false;
                            item.LoginStart = null;
                            item.LoginEnd = null;
                            item.LoginMessage = request.Message;
                            item.UserToken = request.Token;

                            // 如果登录成功，且登录前是启用状态，则更新为启用状态
                            if (item.Enable != true && request.LoginBeforeEnabled)
                            {
                                item.Enable = request.LoginBeforeEnabled;
                            }
                        }
                        else
                        {
                            // 更新失败原因
                            item.LoginMessage = request.Message;
                        }

                        // 更新账号信息
                        DbHelper.Instance.AccountStore.Update(item);

                        // 清空缓存
                        var inc = _loadBalancer.GetDiscordInstance(item.ChannelId);
                        inc?.ClearAccountCache(item.Id);

                        if (!request.Success)
                        {
                            // 发送邮件
                            await EmailJob.Instance.EmailSend(_properties.Smtp, $"自动登录失败-{item.ChannelId}", $"自动登录失败-{item.ChannelId}, {request.Message}, 请手动登录");
                        }
                    }
                    else
                    {
                        // 签名错误
                        Log.Warning("自动登录回调签名验证失败 {@0}", request);

                        return Ok();
                    }
                }
            }

            return Ok();
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

            if (!accountConfig.IsYouChuan && !accountConfig.IsOfficial)
            {
                var model = DbHelper.Instance.AccountStore.Single(c => c.ChannelId == accountConfig.ChannelId);
                if (model != null)
                {
                    throw new LogicException("渠道已存在");
                }
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

            // 悠船账号
            if (account.IsYouChuan || account.IsOfficial)
            {
                account.ChannelId = Guid.NewGuid().ToString("N").Substring(0, 16);
                account.GuildId = Guid.NewGuid().ToString("N").Substring(0, 16);
                account.EnableMj = true;
                account.EnableNiji = true;
                account.IsShorten = false;
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

            model.LoginAccount = param.LoginAccount?.Trim();
            model.LoginPassword = param.LoginPassword?.Trim();
            model.Login2fa = param.Login2fa?.Trim();
            model.IsAutoLogining = false; // 重置自动登录状态
            model.LoginStart = null;
            model.LoginEnd = null;
            model.LoginMessage = null;

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

            // 如果是悠船、官方
            // 清除禁用原因
            if (model.IsYouChuan || model.IsOfficial)
            {
                model.DisabledReason = null;
            }

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

            // 悠船
            if (model.IsYouChuan || model.IsOfficial)
            {
                param.ChannelId = model.ChannelId;
                param.GuildId = model.GuildId;
                param.EnableMj = true;
                param.EnableNiji = true;
                param.IsShorten = false;
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

                item.RunningCount = inc?.GetRunningTaskCount ?? 0;
                item.QueueCount = inc?.GetQueueTaskCount ?? 0;
                item.Running = inc?.IsAlive ?? false;

                if (user == null || (user.Role != EUserRole.ADMIN && user.Id != item.SponsorUserId))
                {
                    // Token 加密
                    item.UserToken = "****";
                    item.BotToken = "****";

                    item.CfUrl = "****";
                    item.CfHashUrl = "****";
                    item.PermanentInvitationLink = "****";
                    item.Remark = "****";

                    item.LoginAccount = "****";
                    item.LoginPassword = "****";
                    item.Login2fa = "****";

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
            var allowModes = param.AllowModes?.ToArray() ?? [];

            var setting = GlobalConfiguration.Setting;
            if (setting.DatabaseType == DatabaseType.MongoDB)
            {
                var coll = MongoHelper.GetCollection<DiscordAccount>().AsQueryable();
                var query = coll
                    .WhereIf(!string.IsNullOrWhiteSpace(param.GuildId), c => c.GuildId == param.GuildId)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.ChannelId), c => c.ChannelId == param.ChannelId)
                    .WhereIf(param.Enable.HasValue, c => c.Enable == param.Enable)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Remark), c => c.Remark.Contains(param.Remark))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Sponsor), c => c.Sponsor.Contains(param.Sponsor))
                    .WhereIf(allowModes.Length == 3, c => c.AllowModes.Contains(allowModes[0]) || c.AllowModes.Contains(allowModes[1]) || c.AllowModes.Contains(allowModes[2]))
                    .WhereIf(allowModes.Length == 2, c => c.AllowModes.Contains(allowModes[0]) || c.AllowModes.Contains(allowModes[1]))
                    .WhereIf(allowModes.Length == 1, c => c.AllowModes.Contains(allowModes[0]));

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
            else if (setting.DatabaseType == DatabaseType.LiteDB)
            {
                var query = LiteDBHelper.AccountStore.GetCollection().Query()
                    .WhereIf(!string.IsNullOrWhiteSpace(param.GuildId), c => c.GuildId == param.GuildId)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.ChannelId), c => c.ChannelId == param.ChannelId)
                    .WhereIf(param.Enable.HasValue, c => c.Enable == param.Enable)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Remark), c => c.Remark.Contains(param.Remark))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Sponsor), c => c.Sponsor.Contains(param.Sponsor));

                if (allowModes.Length > 0)
                {
                    var m1 = allowModes.First();
                    query = query.Where(c => c.AllowModes.Contains(m1));
                }

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
            else
            {
                var freeSql = FreeSqlHelper.FreeSql;
                if (freeSql != null)
                {
                    var query = freeSql.Select<DiscordAccount>()
                        .WhereIf(!string.IsNullOrWhiteSpace(param.GuildId), c => c.GuildId == param.GuildId)
                        .WhereIf(!string.IsNullOrWhiteSpace(param.ChannelId), c => c.ChannelId == param.ChannelId)
                        .WhereIf(param.Enable.HasValue, c => c.Enable == param.Enable)
                        .WhereIf(!string.IsNullOrWhiteSpace(param.Remark), c => c.Remark.Contains(param.Remark))
                        .WhereIf(!string.IsNullOrWhiteSpace(param.Sponsor), c => c.Sponsor.Contains(param.Sponsor));

                    // MYSQL
                    if (param.AllowModes?.Count > 0 && setting.DatabaseType == DatabaseType.MySQL)
                    {
                        // 使用 in sql
                        var allowModesConditions = new List<string>();
                        var parameters = new Dictionary<string, object>();
                        int paramIndex = 0;

                        foreach (var mode in param.AllowModes)
                        {
                            string paramName = $"@p{paramIndex++}";

                            // *** Determine how GenerationSpeedMode is stored in JSON ***
                            // Option A: If stored as string (e.g., "Fast", "Relax")
                            var paramValue = ((int)mode).ToString();

                            // Option B: If stored as integer (e.g., 1, 0)
                            // paramValue = (int)mode;

                            parameters.Add(paramName, paramValue);

                            // Build the JSON_CONTAINS check for this mode.
                            // IMPORTANT: Do NOT include the table alias 'a.' here. FreeSql adds it.
                            // Use the C# property name `AllowModes`. FreeSql maps it to the column.
                            allowModesConditions.Add($"JSON_CONTAINS(`AllowModes`, {paramName})");
                        }

                        if (allowModesConditions.Count > 0)
                        {
                            // Combine the conditions with OR
                            string rawSqlWhere = $"({string.Join(" OR ", allowModesConditions)} OR (JSON_LENGTH(`AllowModes`) = 0))";

                            // Apply the raw SQL condition to the ISelect object
                            query = query.Where(rawSqlWhere, parameters);
                        }
                    }
                    else if (param.AllowModes?.Count > 0)
                    {
                        var m1 = allowModes.First();
                        query = query.Where(c => c.AllowModes.Contains(m1));
                    }

                    count = (int)query.Count();

                    list = query
                        .OrderByIf(nameof(DiscordAccount.GuildId).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.GuildId, sort.Reverse)
                        .OrderByIf(nameof(DiscordAccount.ChannelId).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.ChannelId, sort.Reverse)
                        .OrderByIf(nameof(DiscordAccount.Enable).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.Enable, sort.Reverse)
                        .OrderByIf(nameof(DiscordAccount.Remark).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.Remark, sort.Reverse)
                        .OrderByIf(nameof(DiscordAccount.Sponsor).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.Sponsor, sort.Reverse)
                        .OrderByIf(nameof(DiscordAccount.DateCreated).Equals(sort.Predicate, StringComparison.OrdinalIgnoreCase), c => c.DateCreated, sort.Reverse)
                        .OrderByIf(string.IsNullOrWhiteSpace(sort.Predicate), c => c.Sort, false)
                        .OrderByDescending(c => c.DateCreated)
                        .Skip((page.Current - 1) * page.PageSize)
                        .Take(page.PageSize)
                        .ToList();
                }
            }

            var counter = DrawCounter.AccountTodayCounter;
            foreach (var item in list)
            {
                var inc = _loadBalancer.GetDiscordInstance(item.ChannelId);

                // 当前执行中的任务数
                item.RunningCount = inc?.GetRunningTaskCount ?? 0;

                // 当前队列中任务数
                item.QueueCount = inc?.GetQueueTaskCount ?? 0;

                // 是否运行中
                item.Running = inc?.IsAlive ?? false;

                // 计算今日绘图统计
                var drawKey = $"{DateTime.Now.Date:yyyyMMdd}_{item.ChannelId}";
                if (counter.TryGetValue(drawKey, out var counterValue))
                {
                    item.TodayCounter = counterValue.OrderBy(c => c.Key).ToDictionary(c => c.Key, c => c.Value.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value));

                    if (counterValue.TryGetValue(GenerationSpeedMode.FAST, out var fasts))
                    {
                        item.TodayFastDrawCount = fasts.Sum(x => x.Value);
                    }
                    if (counterValue.TryGetValue(GenerationSpeedMode.TURBO, out var turbos))
                    {
                        item.TodayTurboDrawCount = turbos.Sum(x => x.Value);
                    }
                    if (counterValue.TryGetValue(GenerationSpeedMode.RELAX, out var relaxs))
                    {
                        item.TodayRelaxDrawCount = relaxs.Sum(x => x.Value);
                    }
                }

                if (user == null || (user.Role != EUserRole.ADMIN && user.Id != item.SponsorUserId))
                {
                    // Token 加密
                    item.UserToken = "****";
                    item.BotToken = "****";

                    item.CfUrl = "****";
                    item.CfHashUrl = "****";
                    item.PermanentInvitationLink = "****";
                    item.Remark = "****";

                    item.LoginAccount = "****";
                    item.LoginPassword = "****";
                    item.Login2fa = "****";

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
            var setting = GlobalConfiguration.Setting;
            if (setting.DatabaseType == DatabaseType.MongoDB)
            {
                var coll = MongoHelper.GetCollection<TaskInfo>().AsQueryable();
                var query = coll
                    .WhereIf(param.Mode == GenerationSpeedMode.FAST, c => c.Mode == param.Mode || c.Mode == null)
                    .WhereIf(param.Mode == GenerationSpeedMode.TURBO, c => c.Mode == param.Mode)
                    .WhereIf(param.Mode == GenerationSpeedMode.RELAX, c => c.Mode == param.Mode)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id || c.State == param.Id)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.InstanceId), c => c.InstanceId == param.InstanceId)
                    .WhereIf(param.Status.HasValue, c => c.Status == param.Status)
                    .WhereIf(param.Action.HasValue, c => c.Action == param.Action)
                    .WhereIf(!string.IsNullOrWhiteSpace(param.FailReason), c => c.FailReason.Contains(param.FailReason))
                    .WhereIf(!string.IsNullOrWhiteSpace(param.Description), c => c.Prompt.Contains(param.Description));

                var count = query.Count();
                var list = query
                    .OrderByDescending(c => c.SubmitTime)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Take(page.PageSize)
                    .ToList();

                var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

                return Ok(data);
            }
            else if (setting.DatabaseType == DatabaseType.LiteDB)
            {
                var query = LiteDBHelper.TaskStore.GetCollection().Query()
                .WhereIf(param.Mode == GenerationSpeedMode.FAST, c => c.Mode == param.Mode || c.Mode == null)
                .WhereIf(param.Mode == GenerationSpeedMode.TURBO, c => c.Mode == param.Mode)
                .WhereIf(param.Mode == GenerationSpeedMode.RELAX, c => c.Mode == param.Mode)
                .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id || c.State == param.Id)
                .WhereIf(!string.IsNullOrWhiteSpace(param.InstanceId), c => c.InstanceId == param.InstanceId)
                .WhereIf(param.Status.HasValue, c => c.Status == param.Status)
                .WhereIf(param.Action.HasValue, c => c.Action == param.Action)
                .WhereIf(!string.IsNullOrWhiteSpace(param.FailReason), c => c.FailReason.Contains(param.FailReason))
                .WhereIf(!string.IsNullOrWhiteSpace(param.Description), c => c.Prompt.Contains(param.Description));

                var count = query.Count();
                var list = query
                    .OrderByDescending(c => c.SubmitTime)
                    .Skip((page.Current - 1) * page.PageSize)
                    .Limit(page.PageSize)
                    .ToList();

                var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

                return Ok(data);
            }
            else
            {
                var freeSql = FreeSqlHelper.FreeSql;
                if (freeSql != null)
                {
                    var query = freeSql.Select<TaskInfo>()
                        .WhereIf(param.Mode == GenerationSpeedMode.FAST, c => c.Mode == param.Mode || c.Mode == null)
                        .WhereIf(param.Mode == GenerationSpeedMode.TURBO, c => c.Mode == param.Mode)
                        .WhereIf(param.Mode == GenerationSpeedMode.RELAX, c => c.Mode == param.Mode)
                        .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id || c.State == param.Id)
                        .WhereIf(!string.IsNullOrWhiteSpace(param.InstanceId), c => c.InstanceId == param.InstanceId)
                        .WhereIf(param.Status.HasValue, c => c.Status == param.Status)
                        .WhereIf(param.Action.HasValue, c => c.Action == param.Action)
                        .WhereIf(!string.IsNullOrWhiteSpace(param.FailReason), c => c.FailReason.Contains(param.FailReason))
                        .WhereIf(!string.IsNullOrWhiteSpace(param.Description), c => c.Prompt.Contains(param.Description));

                    var count = (int)query.Count();

                    var list = query
                        .OrderByDescending(c => c.SubmitTime)
                        .Skip((page.Current - 1) * page.PageSize)
                        .Take(page.PageSize)
                        .ToList();

                    var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

                    return Ok(data);
                }
            }

            return Ok(new StandardTableResult<TaskInfo>()
            {
                List = new List<TaskInfo>(),
                Pagination = new StandardTablePagination()
                {
                    Current = page.Current,
                    PageSize = page.PageSize,
                    Total = 0
                }
            });
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

            var userTotalCount = new Dictionary<string, int>();

            var setting = GlobalConfiguration.Setting;
            if (setting.DatabaseType == DatabaseType.MongoDB)
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

                // 计算用户累计绘图
                var userIds = list.Select(c => c.Id).ToList();
                if (userIds.Count > 0)
                {
                    userTotalCount = MongoHelper.GetCollection<TaskInfo>().AsQueryable()
                        .Where(c => userIds.Contains(c.UserId))
                        .GroupBy(c => c.UserId)
                        .Select(g => new
                        {
                            UserId = g.Key,
                            TotalCount = g.Count()
                        })
                        .ToList()
                        .ToDictionary(c => c.UserId, c => c.TotalCount);
                }
            }
            else if (setting.DatabaseType == DatabaseType.LiteDB)
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


                // 计算用户累计绘图
                var userIds = list.Select(c => c.Id).ToList();
                if (userIds.Count > 0)
                {
                    userTotalCount = LiteDBHelper.TaskStore.GetCollection()
                        .Query()
                        .Where(c => userIds.Contains(c.UserId))
                        .Select(c => c.UserId)
                        .ToList()
                        .GroupBy(c => c)
                        .Select(g => new
                        {
                            UserId = g.Key,
                            TotalCount = g.Count()
                        })
                        .ToDictionary(c => c.UserId, c => c.TotalCount);
                }
            }
            else
            {
                var freeSql = FreeSqlHelper.FreeSql;
                if (freeSql != null)
                {
                    var query = freeSql.Select<User>()
                        .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                        .WhereIf(!string.IsNullOrWhiteSpace(param.Name), c => c.Name.Contains(param.Name))
                        .WhereIf(!string.IsNullOrWhiteSpace(param.Email), c => c.Email.Contains(param.Email))
                        .WhereIf(!string.IsNullOrWhiteSpace(param.Phone), c => c.Phone.Contains(param.Phone))
                        .WhereIf(param.Role.HasValue, c => c.Role == param.Role)
                        .WhereIf(param.Status.HasValue, c => c.Status == param.Status);
                    count = (int)query.Count();
                    list = query
                        .OrderByDescending(c => c.UpdateTime)
                        .Skip((page.Current - 1) * page.PageSize)
                        .Take(page.PageSize)
                        .ToList();

                    // 计算用户累计绘图
                    var userIds = list.Select(c => c.Id).ToList();
                    if (userIds.Count > 0)
                    {
                        userTotalCount = freeSql.Select<TaskInfo>().Where(c => userIds.Contains(c.UserId))
                            .GroupBy(c => c.UserId)
                            .ToList((c) => new
                            {
                                UserId = c.Key,
                                TotalCount = c.Count()
                            }).ToDictionary(c => c.UserId, c => c.TotalCount);
                    }
                }
            }

            // 统计今日绘图数量
            var drawCounter = DrawCounter.UserTodayCounter;


            foreach (var item in list)
            {
                DrawCounter.InitUserTodayCounter(item.Id);

                // 今日绘图统计
                var key = $"{DateTime.Now.Date:yyyyMMdd}_{item.Id}";
                if (drawCounter.TryGetValue(key, out var modeDic))
                {
                    item.DayDrawCount = modeDic.Values.SelectMany(c => c.Values).Sum();
                }

                item.TotalDrawCount = userTotalCount.TryGetValue(item.Id, out var totalCount) ? totalCount : 0;
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

            var data = list?.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

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
            var setting = GlobalConfiguration.Setting;
            if (setting.DatabaseType == DatabaseType.MongoDB)
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
            else if (setting.DatabaseType == DatabaseType.LiteDB)
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
            else
            {
                var freeSql = FreeSqlHelper.FreeSql;
                if (freeSql != null)
                {
                    var query = freeSql.Select<DomainTag>()
                        .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                        .WhereIf(!string.IsNullOrWhiteSpace(firstKeyword), c => c.Keywords.Contains(firstKeyword));
                    count = (int)query.Count();
                    list = query
                        .OrderBy(c => c.Sort)
                        .Skip((page.Current - 1) * page.PageSize)
                        .Take(page.PageSize)
                        .ToList();
                }
            }

            var data = list?.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

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

            var setting = GlobalConfiguration.Setting;
            if (setting.DatabaseType == DatabaseType.MongoDB)
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
            else if (setting.DatabaseType == DatabaseType.LiteDB)
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
            else
            {
                var freeSql = FreeSqlHelper.FreeSql;
                if (freeSql != null)
                {
                    var query = freeSql.Select<BannedWord>()
                        .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                        .WhereIf(!string.IsNullOrWhiteSpace(firstKeyword), c => c.Keywords.Contains(firstKeyword));
                    count = (int)query.Count();
                    list = query
                        .OrderBy(c => c.Sort)
                        .Skip((page.Current - 1) * page.PageSize)
                        .Take(page.PageSize)
                        .ToList();
                }
            }

            var data = list?.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, count);

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

                if (model.S3Storage != null)
                {
                    model.S3Storage.AccessKey = "****";
                    model.S3Storage.SecretKey = "****";
                }

                if (!string.IsNullOrWhiteSpace(model.DatabaseConnectionString))
                {
                    model.DatabaseConnectionString = "****";
                }

                model.CaptchaNotifySecret = "****";
                model.LicenseKey = "****";

                if (model.ConsulOptions != null)
                {
                    model.ConsulOptions.ConsulUrl = "****";
                }
            }

            model.UpgradeInfo = _upgradeService.UpgradeInfo;

            return Result.Ok(model);
        }

        /// <summary>
        /// 编辑系统配置
        /// </summary>
        /// <param name="setting"></param>
        /// <returns></returns>
        [HttpPost("setting")]
        public async Task<Result> SettingEdit([FromBody] Setting setting)
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            try
            {
                // 保存时验证授权
                var res = await LicenseKeyHelper.ValidateSync(setting.LicenseKey, setting.EnableYouChuan, setting.EnableOfficial);
                if (!res.IsAuthorized)
                {
                    return Result.Fail("授权验证失败，请检查授权码是否正确，如果没有授权码，请输入默认授权码：trueai.org");
                }

                setting.PrivateFeatures = res.Features ?? [];
            }
            catch (Exception ex)
            {
                Log.Error(ex, "授权验证失败");

                return Result.Fail("授权验证失败，请检查授权码是否正确，如果没有授权码，请输入默认授权码：trueai.org");
            }

            setting.Id = Constants.DEFAULT_SETTING_ID;

            LiteDBHelper.SettingStore.Update(setting);

            GlobalConfiguration.Setting = setting;

            LicenseKeyHelper.LicenseKey = GlobalConfiguration.Setting.LicenseKey;

            // 存储服务
            StorageHelper.Configure();

            // 缓存
            GlobalCacheHelper.Configure();

            // 首页缓存
            _memoryCache.Remove("HOME");

            var now = DateTime.Now.ToString("yyyyMMdd");
            var key = $"{now}_home";

            _memoryCache.Remove(key);

            return Result.Ok();
        }

        /// <summary>
        /// 检查升级
        /// </summary>
        /// <returns></returns>
        [HttpGet("check")]
        public async Task<Result<UpgradeInfo>> CheckForUpdates()
        {
            if (_isAnonymous)
            {
                return Result.Fail<UpgradeInfo>("演示模式，禁止操作");
            }

            var upgradeInfo = await _upgradeService.CheckForUpdatesAsync();
            if (upgradeInfo.HasUpdate)
            {
                await _upgradeService.StartDownloadAsync();
            }

            return Result.Ok(upgradeInfo);
        }

        /// <summary>
        /// 取消更新
        /// </summary>
        /// <returns></returns>
        [HttpPost("cancel-update")]
        public Result CancelUpdate()
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }
            _upgradeService.CancelUpdate();
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
        /// 验证数据库是否正常连接
        /// </summary>
        /// <returns></returns>
        [HttpPost("verify-mongo")]
        public Result ValidateMongo()
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            var success = DbHelper.Verify();

            return success ? Result.Ok() : Result.Fail("连接失败");
        }

        /// <summary>
        /// 重启应用程序
        /// </summary>
        /// <returns></returns>
        [HttpPost("restart")]
        public Result Restart()
        {
            try
            {
                if (_isAnonymous)
                {
                    return Result.Fail("演示模式，禁止操作");
                }

                // 记录重启日志
                Log.Information("系统重启请求，操作者IP: {IP}", _workContext.GetIp());

                // 异步执行重启，避免阻塞当前请求
                Task.Run(async () =>
                {
                    try
                    {
                        // 等待一段时间让响应返回给客户端
                        await Task.Delay(2000);

                        // 执行重启逻辑
                        await RestartApplicationAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "重启应用程序时发生错误");
                    }
                });

                return Result.Ok("应用程序重启命令已发送，请稍候...");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "系统重启异常");
                return Result.Fail("重启失败，请手动重启");
            }
        }

        /// <summary>
        /// 执行应用程序重启
        /// </summary>
        /// <returns></returns>
        private async Task RestartApplicationAsync()
        {
            try
            {
                var isInContainer = IsDockerEnvironment();
                if (isInContainer)
                {
                    // Docker 环境重启
                    await RestartInDockerAsync();
                }
                else
                {
                    // 系统环境重启
                    await RestartInSystemAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "重启应用程序失败");
                throw;
            }
        }

        private bool IsDockerEnvironment()
        {
            try
            {
                return System.IO.File.Exists("/.dockerenv") ||
                Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ||
                Environment.GetEnvironmentVariable("DOCKER_CONTAINER") != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Docker 环境重启
        /// </summary>
        /// <returns></returns>
        private async Task RestartInDockerAsync()
        {
            Log.Information("检测到 Docker 环境，准备重启容器");

            // 在 Docker 环境中，最安全的方式是退出应用程序
            // 让容器的重启策略来处理重启
            await Task.Delay(1000);

            // 优雅关闭应用程序
            Environment.Exit(0);
        }

        /// <summary>
        /// 系统环境重启
        /// </summary>
        /// <returns></returns>
        private async Task RestartInSystemAsync()
        {
            Log.Information("检测到系统环境，准备重启应用程序");

            var currentProcess = Process.GetCurrentProcess();
            var currentAssemblyPath = Assembly.GetExecutingAssembly().Location;
            var currentDirectory = Path.GetDirectoryName(currentAssemblyPath);

            ProcessStartInfo processStartInfo;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows 环境
                processStartInfo = CreateWindowsRestartInfo(currentAssemblyPath, currentDirectory);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux 环境
                processStartInfo = CreateLinuxRestartInfo(currentAssemblyPath, currentDirectory);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS 环境
                processStartInfo = CreateMacOSRestartInfo(currentAssemblyPath, currentDirectory);
            }
            else
            {
                throw new PlatformNotSupportedException("不支持的操作系统平台");
            }

            // 启动新进程
            Process.Start(processStartInfo);

            // 等待一秒后退出当前进程
            await Task.Delay(1000);
            Environment.Exit(0);
        }

        /// <summary>
        /// 创建 Windows 重启信息
        /// </summary>
        /// <param name="assemblyPath"></param>
        /// <param name="workingDirectory"></param>
        /// <returns></returns>
        private ProcessStartInfo CreateWindowsRestartInfo(string assemblyPath, string workingDirectory)
        {
            var fileName = "dotnet";
            var arguments = $"\"{assemblyPath}\"";

            // 检查是否是单文件发布
            if (assemblyPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                fileName = assemblyPath;
                arguments = "";
            }

            return new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
                CreateNoWindow = false
            };
        }

        /// <summary>
        /// 创建 Linux 重启信息
        /// </summary>
        /// <param name="assemblyPath"></param>
        /// <param name="workingDirectory"></param>
        /// <returns></returns>
        private ProcessStartInfo CreateLinuxRestartInfo(string assemblyPath, string workingDirectory)
        {
            var fileName = "dotnet";
            var arguments = $"\"{assemblyPath}\"";

            // 检查是否是单文件发布
            if (!assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                fileName = assemblyPath;
                arguments = "";
            }

            return new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        /// <summary>
        /// 创建 macOS 重启信息
        /// </summary>
        /// <param name="assemblyPath"></param>
        /// <param name="workingDirectory"></param>
        /// <returns></returns>
        private ProcessStartInfo CreateMacOSRestartInfo(string assemblyPath, string workingDirectory)
        {
            var fileName = "dotnet";
            var arguments = $"\"{assemblyPath}\"";

            // 检查是否是单文件发布
            if (!assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                fileName = assemblyPath;
                arguments = "";
            }

            return new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
    }
}
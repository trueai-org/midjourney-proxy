﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.StandardTable;
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
        private readonly ITaskService _taskService;

        // 是否匿名用户
        private readonly bool _isAnonymous;

        private readonly DiscordLoadBalancer _loadBalancer;
        private readonly DiscordAccountInitializer _discordAccountInitializer;
        private readonly ProxyProperties _properties;
        private readonly IMemoryCache _memoryCache;
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
            if (user?.Role == EUserRole.USER)
            {
                var endpoint = context.HttpContext.GetEndpoint();
                var allowAnonymous = endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null;
                if (!allowAnonymous && GlobalConfiguration.IsDemoMode != true)
                {
                    // 如果是普通用户, 并且不是匿名控制器，则返回 403
                    context.HttpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
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
            var user = DbHelper.UserStore.Single(u => u.Email == mail);
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
            DbHelper.UserStore.Add(user);

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
            // 如果 DEMO 模式，并且没有传入 token，则返回空 token
            if (GlobalConfiguration.IsDemoMode == true && string.IsNullOrWhiteSpace(token))
            {
                return Ok(new
                {
                    code = 1,
                    apiSecret = "",
                });
            }

            // 如果开启访客
            if (string.IsNullOrWhiteSpace(token) && GlobalConfiguration.Setting.EnableGuest)
            {
                return Ok(new
                {
                    code = 1,
                    apiSecret = "",
                });
            }

            var user = DbHelper.UserStore.Single(u => u.Token == token);
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

            DbHelper.UserStore.Update(user);

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
                var item = DbHelper.AccountStore.Get(request.State);
                if (item != null && item.CfHashUrl == request.Url && item.Lock)
                {
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
                    DbHelper.AccountStore.Update(item);

                    // 清空缓存
                    var inc = _loadBalancer.GetDiscordInstance(item.ChannelId);
                    inc?.ClearAccountCache(item.Id);

                    if (!request.Success)
                    {
                        // 发送邮件
                        EmailJob.Instance.EmailSend(_properties.Smtp, $"CF自动真人验证失败-{item.ChannelId}", $"CF自动真人验证失败-{item.ChannelId}, 请手动验证");
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

            var item = DbHelper.AccountStore.Get(id);
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

            var item = DbHelper.AccountStore.Get(id);
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
                    var json = JsonSerializer.Deserialize<JsonElement>(con);
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
                            DbHelper.AccountStore.Update(item);

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

            var item = DbHelper.AccountStore.Get(id);
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
            DbHelper.AccountStore.Update(item);

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
        public async Task<Result> AccountAdd([FromBody] DiscordAccountConfig accountConfig)
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            var model = DbHelper.AccountStore.Get(accountConfig.ChannelId);
            if (model != null)
            {
                throw new LogicException("渠道已存在");
            }

            await _discordAccountInitializer.Initialize(accountConfig);
            return Result.Ok();
        }

        /// <summary>
        /// 编辑账号
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        [HttpPut("account/{id}")]
        public Result AccountEdit([FromBody] DiscordAccount param)
        {
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            var model = DbHelper.AccountStore.Get(param.Id);
            if (model == null)
            {
                throw new LogicException("账号不存在");
            }

            model.NijiBotChannelId = param.NijiBotChannelId;
            model.PrivateChannelId = param.PrivateChannelId;
            model.RemixAutoSubmit = param.RemixAutoSubmit;
            model.TimeoutMinutes = param.TimeoutMinutes;
            model.Weight = param.Weight;
            model.Remark = param.Remark;
            model.Sponsor = param.Sponsor;
            model.Sort = param.Sort;

            _discordAccountInitializer.UpdateAccount(model);
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
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            var model = DbHelper.AccountStore.Get(param.Id);
            if (model == null)
            {
                throw new LogicException("账号不存在");
            }

            // 不可修改频道 ID
            if (id != param.ChannelId || param.GuildId != model.GuildId || param.ChannelId != model.ChannelId)
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
            if (_isAnonymous)
            {
                return Result.Fail("演示模式，禁止操作");
            }

            _discordAccountInitializer.DeleteAccount(id);

            return Result.Ok();
        }

        /// <summary>
        /// 获取所有账号信息
        /// </summary>
        /// <returns>所有Discord账号信息</returns>
        [HttpGet("accounts")]
        public ActionResult<List<DiscordAccount>> List()
        {
            var db = DbHelper.AccountStore;
            var data = db.GetAll().OrderBy(c => c.Sort).ThenBy(c => c.DateCreated).ToList();

            foreach (var item in data)
            {
                var inc = _loadBalancer.GetDiscordInstance(item.ChannelId);

                item.RunningCount = inc?.GetRunningFutures().Count ?? 0;
                item.QueueCount = inc?.GetQueueTasks().Count ?? 0;
                item.Running = inc?.IsAlive ?? false;

                if (_isAnonymous)
                {
                    // Token 加密
                    item.UserToken = item.UserToken?.Substring(0, 4) + "****" + item.UserToken?.Substring(item.UserToken.Length - 4);
                    item.BotToken = item.BotToken?.Substring(0, 4) + "****" + item.BotToken?.Substring(item.BotToken.Length - 4);

                    item.CfUrl = item.CfUrl?.Substring(0, item.CfUrl.Length / 2) + "****";
                    item.CfHashUrl = item.CfHashUrl?.Substring(0, item.CfHashUrl.Length / 2) + "****";
                }
            }

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

            var query = DbHelper.TaskStore.GetCollection().Query()
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

            var task = DbHelper.TaskStore.Get(id);
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

                DbHelper.TaskStore.Delete(id);
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

            var xx = DbHelper.UserStore.GetAll();

            var query = DbHelper.UserStore.GetCollection().Query()
                .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                .WhereIf(!string.IsNullOrWhiteSpace(param.Name), c => c.Name.Contains(param.Name))
                .WhereIf(!string.IsNullOrWhiteSpace(param.Email), c => c.Email.Contains(param.Email))
                .WhereIf(!string.IsNullOrWhiteSpace(param.Phone), c => c.Phone.Contains(param.Phone))
                .WhereIf(param.Role.HasValue, c => c.Role == param.Role)
                .WhereIf(param.Status.HasValue, c => c.Status == param.Status);

            var count = query.Count();
            var list = query
                .OrderByDescending(c => c.UpdateTime)
                .Skip((page.Current - 1) * page.PageSize)
                .Limit(page.PageSize)
                .ToList();

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
                var model = DbHelper.UserStore.Get(user.Id);
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
            var tokenUser = DbHelper.UserStore.Single(c => c.Id != user.Id && c.Token == user.Token);
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

            DbHelper.UserStore.Save(user);

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

            var model = DbHelper.UserStore.Get(id);
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

            DbHelper.UserStore.Delete(id);
            return Result.Ok();
        }

        /// <summary>
        /// 获取所有启动的领域标签
        /// </summary>
        /// <returns></returns>
        [HttpGet("domain-tags")]
        public Result<List<SelectOption>> DomainTags()
        {
            var data = DbHelper.DomainStore.GetAll()
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

            var query = DbHelper.DomainStore.GetCollection().Query()
                .WhereIf(!string.IsNullOrWhiteSpace(param.Id), c => c.Id == param.Id)
                .WhereIf(!string.IsNullOrWhiteSpace(firstKeyword), c => c.Keywords.Contains(firstKeyword));

            var count = query.Count();
            var list = query
                .OrderBy(c => c.Sort)
                .Skip((page.Current - 1) * page.PageSize)
                .Limit(page.PageSize)
                .ToList();

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
                var model = DbHelper.DomainStore.Get(domain.Id);
                if (model == null)
                {
                    throw new LogicException("领域标签不存在");
                }

                domain.CreateTime = model.CreateTime;
            }

            domain.UpdateTime = DateTime.Now;

            DbHelper.DomainStore.Save(domain);

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

            var model = DbHelper.DomainStore.Get(id);
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

            DbHelper.DomainStore.Delete(id);

            _taskService.ClearDomainCache();

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
            var model = DbHelper.SettingStore.Get(Constants.DEFAULT_SETTING_ID);
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

            DbHelper.SettingStore.Update(setting);

            GlobalConfiguration.Setting = setting;

            // 首页缓存
            _memoryCache.Remove("HOME");

            return Result.Ok();
        }
    }
}
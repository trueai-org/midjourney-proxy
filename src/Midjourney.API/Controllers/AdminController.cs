using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.StandardTable;
using System.Text.Json;

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

        private readonly string _adminToken;
        private readonly DiscordLoadBalancer _loadBalancer;
        private readonly DiscordAccountInitializer _discordAccountInitializer;
        private readonly ProxyProperties _properties;

        public AdminController(
            ITaskService taskService,
            IConfiguration configuration,
            DiscordLoadBalancer loadBalancer,
            DiscordAccountInitializer discordAccountInitializer,
            IHttpContextAccessor httpContextAccessor,
            IOptionsSnapshot<ProxyProperties> properties)
        {
            _loadBalancer = loadBalancer;
            _taskService = taskService;
            _discordAccountInitializer = discordAccountInitializer;

            _adminToken = configuration["AdminToken"];

            var hasAuthHeader = httpContextAccessor.HttpContext.Request.Headers.TryGetValue("Authorization", out var authHeader);
            var hasApiSecretHeader = httpContextAccessor.HttpContext.Request.Headers.TryGetValue("Mj-Api-Secret", out var apiSecretHeader);
            var token = hasAuthHeader ? authHeader.ToString() : apiSecretHeader.ToString();

            var isAdmin = _adminToken == token;
            var isDemo = GlobalConfiguration.IsDemoMode == true;

            // 如果不是管理员，并且是演示模式时，则是为匿名用户
            _isAnonymous = !isAdmin && isDemo;
            _properties = properties.Value;
        }

        /// <summary>
        /// 管理员登录
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("login")]
        public ActionResult Login([FromBody] string token)
        {
            // 如果匿名登录，并且没有输入 token
            if (_isAnonymous && string.IsNullOrWhiteSpace(token))
            {
                return Ok(new
                {
                    code = 1,
                    apiSecret = "",
                });
            }

            if (!string.IsNullOrWhiteSpace(_adminToken) && token != _adminToken)
            {
                return Ok(new
                {
                    code = 0,
                    description = "登录口令错误",
                });
            }

            return Ok(new
            {
                code = 1,
                apiSecret = _adminToken,
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
        public ActionResult Current()
        {
            var name = "Admin";
            var token = _adminToken;
            if (_isAnonymous)
            {
                name = "Guest";
                token = "";
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
                    new { key = "role", label = "Guest" },
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
                var httpClient = new HttpClient();
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
    }
}
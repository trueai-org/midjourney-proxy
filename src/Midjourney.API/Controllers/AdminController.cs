using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.StandardTable;

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

        private readonly string _adminToken;
        private readonly DiscordLoadBalancer _loadBalancer;
        private readonly DiscordAccountInitializer _discordAccountInitializer;

        public AdminController(
            ITaskService taskService,
            IConfiguration configuration,
            DiscordLoadBalancer loadBalancer,
            DiscordAccountInitializer discordAccountInitializer)
        {
            _loadBalancer = loadBalancer;
            _taskService = taskService;
            _discordAccountInitializer = discordAccountInitializer;

            _adminToken = configuration["AdminToken"];
        }

        /// <summary>
        /// 管理员登录
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("login")]
        public ActionResult Login([FromBody] string token)
        {
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
        /// 当前用户信息
        /// </summary>
        /// <returns></returns>
        [HttpGet("current")]
        public ActionResult Current()
        {
            return Ok(new
            {
                id = "admin",
                userid = "admin",
                name = "Admin",
                apiSecret = _adminToken,
                version = "v2.0.0",
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
            var instance = _loadBalancer.GetDiscordInstance(id);
            return instance == null ? (ActionResult<DiscordAccount>)NotFound() : Ok(instance.Account);
        }

        /// <summary>
        /// 执行 info 和 setting
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpPost("account-sync/{id}")]
        public async Task<Result> SyncAccount(string id)
        {
            await _taskService.InfoSetting(id);
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
            await _taskService.AccountChangeVersion(id, version);
            return Result.Ok();
        }

        /// <summary>
        /// 执行操作
        /// </summary>
        /// <param name="id"></param>
        /// <param name="customId"></param>
        /// <returns></returns>
        [HttpPost("account-action/{id}")]
        public async Task<Result> AccountAction(string id, [FromQuery] string customId)
        {
            await _taskService.AccountAction(id, customId);
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
        /// <param name="account"></param>
        /// <returns></returns>
        [HttpPut("account/{id}")]
        public Result AccountEdit([FromBody] DiscordAccount account)
        {
            var model = DbHelper.AccountStore.Get(account.Id);
            if (model == null)
            {
                throw new LogicException("账号不存在");
            }

            model.PrivateChannelId = account.PrivateChannelId;
            model.RemixAutoSubmit = account.RemixAutoSubmit;
            model.TimeoutMinutes = account.TimeoutMinutes;
            model.Weight = account.Weight;
            model.Remark = account.Remark;

            _discordAccountInitializer.UpdateAccount(model);
            return Result.Ok();
        }

        /// <summary>
        /// 更新账号并重新连接
        /// </summary>
        /// <param name="account"></param>
        /// <returns></returns>
        [HttpPut("account-reconnect/{id}")]
        public async Task<Result> AccountReconnect([FromBody] DiscordAccount account)
        {
            await _discordAccountInitializer.ReconnectAccount(account);
            return Result.Ok();
        }

        /// <summary>
        /// 删除账号
        /// </summary>
        /// <returns></returns>
        [HttpDelete("account/{id}")]
        public Result AccountDelete(string id)
        {
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
            var data = db.GetAll();

            return Ok(data);
        }

        /// <summary>
        /// 获取所有任务信息
        /// </summary>
        /// <returns>所有任务信息</returns>
        [HttpPost("tasks")]
        public ActionResult<StandardTableResult<TaskInfo>> Tasks([FromBody] StandardTableParam request)
        {
            var page = request.Pagination;

            var list = DbHelper.TaskStore.GetCollection().Query()
                .OrderByDescending(c => c.SubmitTime)
                .Skip((page.Current - 1) * page.PageSize)
                .Limit(page.PageSize)
                .ToList();

            var data = list.ToTableResult(request.Pagination.Current, request.Pagination.PageSize, list.Count);

            return Ok(data);
        }
    }
}
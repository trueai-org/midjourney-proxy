using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.LoadBalancer;
using Swashbuckle.AspNetCore.Annotations;

namespace Midjourney.API.Controllers
{
    /// <summary>
    /// 控制器用于查询账号信息
    /// </summary>
    [ApiController]
    [Route("mj/account")]
    [Authorize()]
    public class AccountController : ControllerBase
    {
        private readonly DiscordLoadBalancer _loadBalancer;

        public AccountController(DiscordLoadBalancer loadBalancer)
        {
            _loadBalancer = loadBalancer;
        }

        /// <summary>
        /// 根据账号ID获取账号信息
        /// </summary>
        /// <param name="id">账号ID</param>
        /// <returns>Discord账号信息</returns>
        [HttpGet("{id}/fetch")]
        [SwaggerOperation("指定ID获取账号")]
        public ActionResult<DiscordAccount> Fetch(string id)
        {
            var instance = _loadBalancer.GetDiscordInstance(id);
            return instance == null ? (ActionResult<DiscordAccount>)NotFound() : Ok(instance.Account());
        }

        /// <summary>
        /// 获取所有账号信息
        /// </summary>
        /// <returns>所有Discord账号信息</returns>
        [HttpGet("list")]
        [SwaggerOperation("查询所有账号")]
        public ActionResult<List<DiscordAccount>> List()
        {
            return Ok(_loadBalancer.GetAllInstances().Select(instance => instance.Account()).ToList());
        }
    }
}
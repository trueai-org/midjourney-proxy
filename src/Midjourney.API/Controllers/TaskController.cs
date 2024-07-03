using Microsoft.AspNetCore.Mvc;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Midjourney.API.Controllers
{
    /// <summary>
    /// 控制器用于查询任务信息
    /// </summary>
    [ApiController]
    [Route("task")]
    [Route("mj/task")]
    public class TaskController : ControllerBase
    {
        private readonly ITaskStoreService _taskStoreService;
        private readonly DiscordLoadBalancer _discordLoadBalancer;

        public TaskController(ITaskStoreService taskStoreService, DiscordLoadBalancer discordLoadBalancer)
        {
            _taskStoreService = taskStoreService;
            _discordLoadBalancer = discordLoadBalancer;
        }

        /// <summary>
        /// 根据任务ID获取任务信息
        /// </summary>
        /// <param name="id">任务ID</param>
        /// <returns>任务信息</returns>
        [HttpGet("{id}/fetch")]
        [SwaggerOperation("指定ID获取任务")]
        public ActionResult<TaskInfo> Fetch(string id)
        {
            var queueTask = _discordLoadBalancer.GetQueueTasks().FirstOrDefault(t => t.Id == id);
            return queueTask ?? _taskStoreService.Get(id);
        }

        /// <summary>
        /// 获取任务队列中的所有任务
        /// </summary>
        /// <returns>任务队列中的所有任务</returns>
        [HttpGet("queue")]
        [SwaggerOperation("查询任务队列")]
        public ActionResult<List<TaskInfo>> Queue()
        {
            return Ok(_discordLoadBalancer.GetQueueTasks().OrderBy(t => t.SubmitTime).ToList());
        }

        /// <summary>
        /// 获取所有任务信息
        /// </summary>
        /// <returns>所有任务信息</returns>
        [HttpGet("list")]
        [SwaggerOperation("查询所有任务")]
        public ActionResult<List<TaskInfo>> List()
        {
            return Ok(_taskStoreService.List().OrderByDescending(t => t.SubmitTime).ToList());
        }

        /// <summary>
        /// 根据条件查询任务信息
        /// </summary>
        /// <param name="conditionDTO">任务查询条件</param>
        /// <returns>符合条件的任务信息</returns>
        [HttpPost("list-by-condition")]
        [SwaggerOperation("根据ID列表查询任务")]
        public ActionResult<List<TaskInfo>> ListByCondition([FromBody] TaskConditionDTO conditionDTO)
        {
            if (conditionDTO.Ids == null || !conditionDTO.Ids.Any())
            {
                return Ok(new List<TaskInfo>());
            }

            var result = new List<TaskInfo>();
            var notInQueueIds = new HashSet<string>(conditionDTO.Ids);

            foreach (var task in _discordLoadBalancer.GetQueueTasks())
            {
                if (notInQueueIds.Contains(task.Id))
                {
                    result.Add(task);
                    notInQueueIds.Remove(task.Id);
                }
            }

            foreach (var id in notInQueueIds)
            {
                var task = _taskStoreService.Get(id);
                if (task != null)
                {
                    result.Add(task);
                }
            }

            return Ok(result);
        }
    }
}
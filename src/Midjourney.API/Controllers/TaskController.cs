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

using Microsoft.AspNetCore.Mvc;
using Midjourney.Infrastructure.Data;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.LoadBalancer;
using Midjourney.Infrastructure.Services;
using System.Net;

namespace Midjourney.API.Controllers
{
    /// <summary>
    /// 控制器用于查询任务信息
    /// </summary>
    [ApiController]
    [Route("mj/task")]
    [Route("mj-fast/mj/task")]
    [Route("mj-turbo/mj/task")]
    [Route("mj-relax/mj/task")]
    public class TaskController : ControllerBase
    {
        private readonly ITaskStoreService _taskStoreService;
        private readonly ITaskService _taskService;

        private readonly DiscordLoadBalancer _discordLoadBalancer;
        private readonly WorkContext _workContext;

        public TaskController(
            ITaskStoreService taskStoreService,
            DiscordLoadBalancer discordLoadBalancer,
            ITaskService taskService,
            IHttpContextAccessor httpContextAccessor,
            WorkContext workContext)
        {
            _taskStoreService = taskStoreService;
            _discordLoadBalancer = discordLoadBalancer;
            _taskService = taskService;

            _workContext = workContext;

            var user = _workContext.GetUser();

            // 如果非演示模式、未开启访客，如果没有登录，直接返回 403 错误
            if (GlobalConfiguration.IsDemoMode != true
                && GlobalConfiguration.Setting.EnableGuest != true)
            {
                if (user == null)
                {
                    // 如果是普通用户, 并且不是匿名控制器，则返回 403
                    httpContextAccessor.HttpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    httpContextAccessor.HttpContext.Response.WriteAsync("未登录");
                    return;
                }
            }
        }

        /// <summary>
        /// 根据任务ID获取任务信息
        /// </summary>
        /// <param name="id">任务ID</param>
        /// <returns>任务信息</returns>
        [HttpGet("{id}/fetch")]
        public ActionResult<TaskInfo> Fetch(string id)
        {
            var queueTask = _discordLoadBalancer.GetQueueTasks().FirstOrDefault(t => t.Id == id);
            return queueTask ?? _taskStoreService.Get(id);
        }

        /// <summary>
        /// 取消任务
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpPost("{id}/cancel")]
        public ActionResult<TaskInfo> Cancel(string id)
        {
            if (GlobalConfiguration.IsDemoMode == true)
            {
                // 直接抛出错误
                return BadRequest("演示模式，禁止操作");
            }

            var queueTask = _discordLoadBalancer.GetQueueTasks().FirstOrDefault(t => t.Id == id);
            if (queueTask != null)
            {
                queueTask.Fail("主动取消任务");
            }

            return Ok();
        }

        /// <summary>
        /// 获取任务图片的seed（需设置mj或niji的私信ID）
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet("{id}/image-seed")]
        public async Task<ActionResult<SubmitResultVO>> ImageSeed(string id)
        {
            var targetTask = _taskStoreService.Get(id);
            if (targetTask != null)
            {
                if (!string.IsNullOrWhiteSpace(targetTask.Seed))
                {
                    return Ok(SubmitResultVO.Of(ReturnCode.SUCCESS, "成功", targetTask.Seed));
                }
                else
                {
                    var hash = targetTask.GetProperty<string>(Constants.TASK_PROPERTY_MESSAGE_HASH, default);
                    if (string.IsNullOrWhiteSpace(hash))
                    {
                        return BadRequest(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "关联任务状态错误"));
                    }
                    else
                    {
                        // 有 hash 但没有 seed，说明任务已经完成，但是没有 seed
                        // 重新获取 seed
                        var data = await _taskService.SubmitSeed(targetTask);
                        return Ok(data);
                    }
                }
            }

            return NotFound(SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "关联任务不存在或已失效"));
        }

        /// <summary>
        /// 获取任务队列中的所有任务
        /// </summary>
        /// <returns>任务队列中的所有任务</returns>
        [HttpGet("queue")]
        public ActionResult<List<TaskInfo>> Queue()
        {
            return Ok(_discordLoadBalancer.GetQueueTasks().OrderBy(t => t.SubmitTime).ToList());
        }

        /// <summary>
        /// 获取最新100条任务信息
        /// </summary>
        /// <returns>所有任务信息</returns>
        [HttpGet("list")]
        public ActionResult<List<TaskInfo>> List()
        {
            var data = DbHelper.Instance.TaskStore.Where(c => true, t => t.SubmitTime, false, 100).ToList();
            return Ok(data);
        }

        /// <summary>
        /// 根据条件查询任务信息/根据ID列表查询任务
        /// </summary>
        /// <param name="conditionDTO">任务查询条件</param>
        /// <returns>符合条件的任务信息</returns>
        [HttpPost("list-by-condition")]
        [HttpPost("list-by-ids")]
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

            var list = _taskStoreService.GetList(notInQueueIds.ToList());
            if (list.Any())
            {
                result.AddRange(list);
            }

            //foreach (var id in notInQueueIds)
            //{
            //    var task = _taskStoreService.Get(id);
            //    if (task != null)
            //    {
            //        result.Add(task);
            //    }
            //}

            return Ok(result);
        }
    }
}
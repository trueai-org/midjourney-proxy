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

using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Midjourney.API.Controllers
{
    /// <summary>
    /// 个性化配置 -p
    /// </summary>
    [ApiController]
    [Route("mj/profile")]
    public class ProfileController : ControllerBase
    {
        // 是否匿名用户
        private readonly bool _isAnonymous;

        private readonly DiscordAccountService _accountService;
        private readonly IFreeSql _freeSql = FreeSqlHelper.FreeSql;

        public ProfileController(DiscordAccountService accountService, WorkContext workContext, IHttpContextAccessor context)
        {
            _accountService = accountService;

            // 如果不是管理员，并且是演示模式时，则是为匿名用户
            var user = workContext.GetUser();

            _isAnonymous = user?.Role != EUserRole.ADMIN;

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

            var setting = GlobalConfiguration.Setting;
            if (!setting.PrivateEnableOfficialPersonalize)
            {
                context.HttpContext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                context.HttpContext.Response.WriteAsync("Forbidden: Official Personalize is disabled.");
                return;
            }
        }

        /// <summary>
        /// 创建个性化配置
        /// </summary>
        /// <param name="createDto"></param>
        /// <returns></returns>
        [HttpPost("create")]
        public async Task<ActionResult<SubmitResultVO>> ProfileCreate([FromBody] ProfileCreateDto createDto)
        {
            if (_isAnonymous)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "演示模式，禁止操作"));
            }

            if (createDto == null || string.IsNullOrWhiteSpace(createDto.Title))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "参数错误"));
            }

            var instance = _accountService.GetAliveOfficialPersonalizeInstance();
            if (instance == null)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.FAILURE, "无可用的账号实例"));
            }

            var res = await instance.YmTaskService.ProfileCreateAsync(createDto);
            if (string.IsNullOrWhiteSpace(res?.ProfileId))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.FAILURE, "创建失败"));
            }

            var model = new PersonalizeTag()
            {
                Id = res.ProfileId,
                Title = res.Title,
                Service = res.Service,
                Version = res.Version,
            };
            _freeSql.Add(model);

            return Ok(SubmitResultVO.Of(ReturnCode.SUCCESS, "成功", model.Id));
        }

        /// <summary>
        /// 获取个性化配置
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet("{id}/fetch")]
        public ActionResult<PersonalizeTagResult> ProfileGet(string id)
        {
            var model = _freeSql.Get<PersonalizeTag>(id);
            if (model == null)
            {
                return NotFound();
            }
            var data = model.ToResult();
            return Ok(data);
        }

        /// <summary>
        /// 获取随机配对的个性化配置
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet("pair/{id}")]
        public async Task<ActionResult<SubmitResultVO>> ProfilePair(string id)
        {
            if (_isAnonymous)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.FAILURE, "演示模式，禁止操作"));
            }

            var instance = _accountService.GetAliveOfficialPersonalizeInstance();
            if (instance == null)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.FAILURE, "无可用的账号实例"));
            }

            var model = _freeSql.Get<PersonalizeTag>(id);
            if (model == null)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "个性化配置不存在"));
            }

            if (model.RandomPairs?.Pairs?.Count > 0)
            {
                // 已存在
            }
            else
            {
                var res = await instance.YmTaskService.ProfileCreateSkipAsync(model);
                if (res == null)
                {
                    return Ok(SubmitResultVO.Fail(ReturnCode.FAILURE, "操作失败"));
                }

                model.RandomPairs = res;
                model.UpdateTime = DateTime.Now;

                _freeSql.Update(model);
            }

            if (model.RandomPairs.Pairs?.Count > 0)
            {
                var jobIds = model.RandomPairs.Pairs.First().Jobs.Select(c => new ProfileSkipResultDto
                {
                    JobId = c.Id,
                    ImageUrl = $"https://cdn.midjourney.com/{c.Id}/0_{c.ParentGrid}.jpeg"
                }).ToList();

                return Ok(SubmitResultVO.Of(ReturnCode.SUCCESS, "成功", jobIds));
            }

            return Ok(SubmitResultVO.Fail(ReturnCode.FAILURE, "无可用配对数据"));
        }

        /// <summary>
        /// 获取随机配对的个性化配置 - 跳过
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpPost("pair/skip")]
        public async Task<ActionResult<SubmitResultVO>> ProfileSkip(ProfileSkipDto skipDto)
        {
            if (_isAnonymous)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.FAILURE, "演示模式，禁止操作"));
            }

            if (skipDto == null || string.IsNullOrWhiteSpace(skipDto.ProfileId))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.FAILURE, "参数错误"));
            }

            var id = skipDto.ProfileId;

            var instance = _accountService.GetAliveOfficialPersonalizeInstance();
            if (instance == null)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.FAILURE, "无可用的账号实例"));
            }

            var model = _freeSql.Get<PersonalizeTag>(id);
            if (model == null)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "个性化配置不存在"));
            }

            //var res = await instance.YmTaskService.ProfileCreateSkipAsync(model);
            //if (res?.Pairs?.Count > 0)
            //{
            //    // 正常
            //}
            //else
            //{
            //    return Ok(SubmitResultVO.Fail(ReturnCode.FAILURE, "操作失败"));
            //}

            model.ClickTotal++;
            model.SkipCount++;

            // 跳过时，依然调用评分接口
            var res = await instance.YmTaskService.ProfileCreateRateAsync(model);
            if (res.Pairs?.Count > 0)
            {
                // 正常
            }
            else
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.FAILURE, "操作失败"));
            }

            model.RandomPairs = res;
            model.UpdateTime = DateTime.Now;

            _freeSql.Update(model);

            var jobIds = res.Pairs.First().Jobs.Select(c => new ProfileSkipResultDto
            {
                JobId = c.Id,
                ImageUrl = $"https://cdn.midjourney.com/{c.Id}/0_{c.ParentGrid}.jpeg"
            }).ToList();

            return Ok(SubmitResultVO.Of(ReturnCode.SUCCESS, "成功", jobIds));
        }

        /// <summary>
        /// 提交评分
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        [HttpPost("pair/rate")]
        public async Task<ActionResult<SubmitResultVO>> ProfileRate(ProfileRateDto req)
        {
            if (_isAnonymous)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.FAILURE, "演示模式，禁止操作"));
            }

            if (req == null || string.IsNullOrWhiteSpace(req.ProfileId) || string.IsNullOrWhiteSpace(req.JobId))
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.FAILURE, "参数错误"));
            }

            var id = req.ProfileId;

            var instance = _accountService.GetAliveOfficialPersonalizeInstance();
            if (instance == null)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.FAILURE, "无可用的账号实例"));
            }

            var model = _freeSql.Get<PersonalizeTag>(id);
            if (model == null)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "个性化配置不存在"));
            }

            // 判断 JobId 是否在 RandomPairs 中
            var jobs = model.RandomPairs?.Pairs?.FirstOrDefault()?.Jobs;
            var job = jobs?.FirstOrDefault(c => c.Id == req.JobId);
            if (job == null)
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.NOT_FOUND, "任务不存在"));
            }

            var isRight = jobs[1].Id == req.JobId;

            if (isRight)
            {
                model.ClickRight++;
            }
            else
            {
                model.ClickLeft++;
            }

            model.WinTotal++;
            model.ClickTotal++;

            var res = await instance.YmTaskService.ProfileCreateRateAsync(model, isRight);
            if (res.Pairs?.Count > 0)
            {
                // 正常
            }
            else
            {
                return Ok(SubmitResultVO.Fail(ReturnCode.FAILURE, "操作失败"));
            }

            model.RandomPairs = res;
            model.UpdateTime = DateTime.Now;

            _freeSql.Update(model);

            var jobIds = res.Pairs.First().Jobs.Select(c => new ProfileSkipResultDto
            {
                JobId = c.Id,
                ImageUrl = $"https://cdn.midjourney.com/{c.Id}/0_{c.ParentGrid}.jpeg"
            }).ToList();

            return Ok(SubmitResultVO.Of(ReturnCode.SUCCESS, "成功", jobIds));
        }
    }
}
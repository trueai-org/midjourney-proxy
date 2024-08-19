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
using Microsoft.AspNetCore.Mvc.Filters;
using Serilog;

namespace Midjourney.Captcha.API
{
    /// <summary>
    /// 自定义方法过滤器
    /// </summary>
    public class CustomActionFilterAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (context.HttpContext.Response.StatusCode == StatusCodes.Status401Unauthorized)
            {
                var result = Result.Fail("请重新登录");
                context.Result = new JsonResult(result);
            }
            else if (context.HttpContext.Response.StatusCode == StatusCodes.Status403Forbidden)
            {
                var result = Result.Fail("您无权限访问");
                context.Result = new JsonResult(result);
            }
            else
            {
                if (!context.ModelState.IsValid)
                {
                    var error = context.ModelState.Values.FirstOrDefault()?.Errors?.FirstOrDefault()?.ErrorMessage ?? "参数异常";

                    Log.Logger.Warning("参数异常 {@0} - {@1}", context.HttpContext?.Request?.GetUrl() ?? "", error);

                    context.Result = new JsonResult(Result.Fail(error));
                }
            }
            base.OnActionExecuting(context);
        }

        public override void OnActionExecuted(ActionExecutedContext context)
        {
            if (context.Result is ObjectResult objectResult)
            {
                if (objectResult?.Value is Result result && result.Success && result.Message == null)
                {
                    result.Message = "操作成功";
                    context.Result = new JsonResult(result);
                }
            }
            base.OnActionExecuted(context);
        }
    }
}
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
    /// 自定义逻辑异常特性，以 json 格式返回错误内容
    /// </summary>
    public class CustomLogicExceptionFilterAttribute : ExceptionFilterAttribute
    {
        public override void OnException(ExceptionContext context)
        {
            if (context.Exception != null)
            {
                if (context.Exception is LogicException lex)
                {
                    Log.Logger.Warning(context.Exception, "逻辑错误 {0}", context.Exception.Message);

                    var result = Result.Fail(lex.Message ?? "操作失败");

                    if (lex.Code != 0)
                    {
                        result.Code = lex.Code;
                    }

                    context.Result = new JsonResult(result);
                    context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
                }
                else
                {
                    Log.Logger.Error(context.Exception, "系统异常 {0}", context.Exception.Message);

                    context.Result = new JsonResult(Result.Fail("操作失败"));
                    context.HttpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                }
            }
            base.OnException(context);
        }
    }
}

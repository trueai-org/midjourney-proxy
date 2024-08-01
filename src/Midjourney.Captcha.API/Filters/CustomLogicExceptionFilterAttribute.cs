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

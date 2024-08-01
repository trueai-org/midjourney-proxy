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
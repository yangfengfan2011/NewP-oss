namespace ICP.Platform.Sdk.Http;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

/// <summary>
/// 声明 HTTP endpoint 显式采用平台新 API 的稳定请求校验 ProblemDetails 契约。
/// 该 SDK filter 只转换当前 endpoint 的自动模型绑定失败；未标记的既有 endpoint 保持原有响应。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PlatformRequestValidationAttribute : ActionFilterAttribute
{
    /// <summary>
    /// 创建一个先于框架自动模型状态 filter 执行的显式请求校验 filter。
    /// </summary>
    public PlatformRequestValidationAttribute()
    {
        Order = -3000;
    }

    /// <inheritdoc />
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ModelState.IsValid)
        {
            return;
        }

        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "REQUEST_VALIDATION_FAILED"
        };
        problem.Extensions["code"] = "REQUEST_VALIDATION_FAILED";
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        var result = new BadRequestObjectResult(problem);
        result.ContentTypes.Add("application/problem+json");
        context.Result = result;
    }
}

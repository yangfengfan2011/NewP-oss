using ICP.Platform.Authorization.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>只翻译正式绑定上下文拒绝；业务资格、基础设施故障与取消仍沿原有流水线。</summary>
public sealed class DataExecutionExceptionFilter : IExceptionFilter
{
    /// <inheritdoc/>
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ActionDataAccessException error) return;
        context.Result = new ObjectResult(new ProblemDetails { Status = error.Status, Title = error.Message,
            Extensions = { ["code"] = error.Code } }) { StatusCode = error.Status };
        context.ExceptionHandled = true;
    }
}

using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>只翻译正式绑定上下文拒绝；业务资格、基础设施故障与取消仍沿原有流水线。</summary>
public sealed class DataExecutionExceptionFilter : IExceptionFilter
{
    /// <inheritdoc/>
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ActionDataAccessException error) return;
        context.Result = ActionDataAccessProblem.ToResult(error, error.Message);
        context.ExceptionHandled = true;
    }
}

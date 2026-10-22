using ICP.Platform.Authorization.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Authorization.Http;

/// <summary>声明上下文拒绝的共享HTTP投影；捕获范围、流水线时机和处理状态仍由调用过滤器拥有。</summary>
public static class ActionDataAccessProblem
{
    /// <summary>保留异常的状态与code；title由调用边界按其既有Message或Code语义明确选择。</summary>
    public static ObjectResult ToResult(ActionDataAccessException exception, string? title) =>
        new(new ProblemDetails
        {
            Status = exception.Status,
            Title = title,
            Extensions = { ["code"] = exception.Code }
        }) { StatusCode = exception.Status };
}

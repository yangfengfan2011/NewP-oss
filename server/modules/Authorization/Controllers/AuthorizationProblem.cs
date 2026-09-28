using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Authorization.Controllers;

/// <summary>授权管理 HTTP 边界共用的错误事实投影，模块词条不进入 Shell。</summary>
internal static class AuthorizationProblem
{
    // 保持既有状态及诊断，并附上当前语言呈现所需的稳定事实。
    public static ProblemDetails From(AuthorizationValidationException error, string? publicCode = null)
    {
        var key = "AuthorizationErrors." + error.Code;
        var problem = new ProblemDetails { Title = error.Message, Status = 400 };
        problem.Extensions["code"] = publicCode ?? error.Code;
        problem.Extensions["reasonKey"] = key;
        problem.Extensions["args"] = error.Args;
        if (error.Field is not null)
            problem.Extensions["fieldErrors"] = new[] { new { field = error.Field, code = error.Code, reasonKey = key, args = error.Args, message = error.Message } };
        return problem;
    }
}

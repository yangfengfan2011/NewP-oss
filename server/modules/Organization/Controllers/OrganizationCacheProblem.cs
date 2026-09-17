using ICP.Platform.Persistence.Transactions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

/// <summary>组织专用写入沿用标准 CRUD 的缓存错误契约，明确区分缓存不可用、确认已保存与提交结果未确认。</summary>
internal static class OrganizationCacheProblem
{
    // Controller 只翻译提交事实，不重试业务操作或参与缓存恢复。
    internal static ObjectResult Create(PlatformCacheUnavailableException exception, string traceId)
    {
        var code = exception.Code;
        var problem = new ProblemDetails { Status = StatusCodes.Status503ServiceUnavailable, Title = code, Detail = exception.Message };
        problem.Extensions["code"] = code;
        problem.Extensions["message"] = exception.Message;
        problem.Extensions["traceId"] = traceId;
        return new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { "application/problem+json" } };
    }
}

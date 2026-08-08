using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Authorization.Http;

/// <summary>
/// 保留标准 401/403，并将 PermissionGate 的不可判定结果稳定映射为 503。
/// </summary>
public sealed class PermissionAuthorizationMiddlewareResultHandler
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        var reasons = authorizeResult.AuthorizationFailure?.FailureReasons ?? [];
        if (reasons.Any(static reason => string.Equals(
                reason.Message,
                PermissionGateFailureCodes.DecisionUnavailable,
                StringComparison.Ordinal)))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                code = PermissionGateFailureCodes.DecisionUnavailable,
                message = "Authorization decision is unavailable."
            });
            return;
        }

        if (reasons.Any(static reason => string.Equals(
                reason.Message,
                PermissionGateFailureCodes.SubjectMissing,
                StringComparison.Ordinal)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await _fallback.HandleAsync(next, context, policy, authorizeResult);
    }
}

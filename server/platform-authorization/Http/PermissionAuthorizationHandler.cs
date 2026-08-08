using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ICP.Platform.Authorization.Http;

/// <summary>
/// 在 endpoint 业务委托执行前统一调用 PermissionGate。
/// </summary>
public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionAuthorizationRequirement>
{
    private readonly PermissionGate _gate;
    private readonly ILogger<PermissionAuthorizationHandler> _logger;

    public PermissionAuthorizationHandler(
        PermissionGate gate,
        ILogger<PermissionAuthorizationHandler> logger)
    {
        _gate = gate;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionAuthorizationRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        AuthenticatedSubject subject;
        try
        {
            subject = AuthenticatedSubject.FromPrincipal(context.User);
        }
        catch (PermissionSubjectException exception)
        {
            _logger.LogCritical(
                "PermissionGate could not establish a trusted subject for endpoint {Endpoint}. FailureCode: {FailureCode}",
                GetEndpointDisplayName(context.Resource),
                exception.FailureCode);
            context.Fail(new AuthorizationFailureReason(this, exception.FailureCode));
            return;
        }

        var httpContext = context.Resource as HttpContext;
        var result = await _gate.EvaluateAsync(
            subject,
            requirement.Requirement,
            httpContext?.RequestAborted ?? default);
        switch (result.Decision)
        {
            case PermissionDecision.Granted:
                context.Succeed(requirement);
                break;
            case PermissionDecision.Denied:
            case PermissionDecision.Indeterminate:
                if (result.Decision is PermissionDecision.Indeterminate)
                {
                    _logger.LogError(
                        "PermissionGate decision unavailable for endpoint {Endpoint}, permission {Permission}, subject {SubjectId}, correlation {CorrelationId}. FailureCode: {FailureCode}",
                        GetEndpointDisplayName(context.Resource),
                        requirement.Requirement.PermissionCode,
                        subject.SubjectId,
                        httpContext?.TraceIdentifier,
                        result.FailureCode ?? PermissionGateFailureCodes.DecisionUnavailable);
                }

                context.Fail(new AuthorizationFailureReason(
                    this,
                    result.FailureCode ?? PermissionGateFailureCodes.DecisionUnavailable));
                break;
            default:
                context.Fail(new AuthorizationFailureReason(
                    this,
                    PermissionGateFailureCodes.DecisionUnavailable));
                break;
        }
    }

    private static string GetEndpointDisplayName(object? resource) =>
        resource is HttpContext httpContext
            ? httpContext.GetEndpoint()?.DisplayName ?? "(unknown)"
            : "(unknown)";
}

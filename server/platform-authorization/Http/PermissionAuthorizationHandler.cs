using Microsoft.AspNetCore.Authorization;

namespace ICP.Platform.Authorization.Http;

/// <summary>
/// 在 endpoint 业务委托执行前统一调用 PermissionGate。
/// </summary>
public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionAuthorizationRequirement>
{
    private readonly PermissionGate _gate;

    public PermissionAuthorizationHandler(PermissionGate gate)
    {
        _gate = gate;
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
            context.Fail(new AuthorizationFailureReason(this, exception.FailureCode));
            return;
        }

        var result = await _gate.EvaluateAsync(subject, requirement.Requirement);
        switch (result.Decision)
        {
            case PermissionDecision.Granted:
                context.Succeed(requirement);
                break;
            case PermissionDecision.Denied:
            case PermissionDecision.Indeterminate:
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
}

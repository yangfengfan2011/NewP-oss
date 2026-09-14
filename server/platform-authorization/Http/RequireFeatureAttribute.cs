using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
namespace ICP.Platform.Authorization.Http;

/// <summary>绑定服务端行为；请求必须携带登记给该行为的功能位置。</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true)]
public sealed class RequireFeatureAttribute : AuthorizeAttribute
{
    internal const string Prefix = "Feature:";
    public string BehaviorKey { get; }
    /// <summary>绑定稳定实现身份，不声明另一份权限目录。</summary>
    public RequireFeatureAttribute(string behaviorKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorKey);
        BehaviorKey = behaviorKey;
        Policy = Prefix + behaviorKey;
    }
}

/// <summary>功能位置的在线判权依赖，由 Authorization 模块实现。</summary>
public interface IFeatureDecisionSource
{
    /// <summary>核对位置绑定并以可信主体查询当前有效角色；依赖异常由统一 handler 转译为不可判定。</summary>
    ValueTask<PermissionDecision> EvaluateAsync(string subjectId, string position, string behaviorKey, CancellationToken cancellationToken);
}

/// <summary>为当前会话提供可呈现的功能集合；单点判权不得依赖此批量投影。</summary>
public interface IFeatureGrantProjection
{
    /// <summary>直接读取当前启用角色并集，不缓存角色分配。</summary>
    Task<IReadOnlyList<string>> GetCurrentPositionsAsync(string subjectId, CancellationToken cancellationToken);
}

public sealed record FeatureAuthorizationRequirement(string BehaviorKey) : IAuthorizationRequirement;

/// <summary>位置绑定及可信主体判权发生在业务委托执行前，既有在途行为不复判。</summary>
public sealed class FeatureAuthorizationHandler(IFeatureDecisionSource source) : AuthorizationHandler<FeatureAuthorizationRequirement>
{
    /// <inheritdoc/>
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, FeatureAuthorizationRequirement requirement)
    {
        if (context.Resource is not HttpContext http || context.User.Identity?.IsAuthenticated != true) return;
        var positions = http.Request.Headers["X-NewP-Feature"];
        if (positions.Count != 1 || string.IsNullOrWhiteSpace(positions[0]))
        {
            context.Fail();
            return;
        }
        try
        {
            var subject = AuthenticatedSubject.FromPrincipal(context.User);
            var decision = await source.EvaluateAsync(subject.SubjectId, positions[0]!, requirement.BehaviorKey, http.RequestAborted);
            if (decision == PermissionDecision.Granted) context.Succeed(requirement);
            else context.Fail(new AuthorizationFailureReason(this, decision == PermissionDecision.Denied
                ? "FEATURE_DENIED" : PermissionGateFailureCodes.DecisionUnavailable));
        }
        catch (PermissionSubjectException)
        {
            context.Fail(new AuthorizationFailureReason(this, PermissionGateFailureCodes.SubjectMissing));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            context.Fail(new AuthorizationFailureReason(this, PermissionGateFailureCodes.DecisionUnavailable));
        }
    }
}

using ICP.Platform.Authorization.Abstractions;

namespace ICP.Platform.Authorization;

/// <summary>
/// 把可信主体和显式权限要求适配为稳定三态决策，不暴露授权存储异常细节。
/// </summary>
public sealed class PermissionGate
{
    private readonly IPermissionDecisionSource _decisionSource;

    public PermissionGate(IPermissionDecisionSource decisionSource)
    {
        _decisionSource = decisionSource;
    }

    /// <summary>
    /// 评估一次权限要求；调用方不能直接传入请求参数形式的 userId。
    /// </summary>
    public async ValueTask<PermissionGateResult> EvaluateAsync(
        AuthenticatedSubject subject,
        PermissionRequirement requirement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(requirement);

        try
        {
            var decision = await _decisionSource.EvaluateAsync(
                subject.SubjectId,
                requirement.PermissionCode,
                cancellationToken);
            return decision switch
            {
                PermissionDecision.Granted => new PermissionGateResult(decision, null),
                PermissionDecision.Denied => new PermissionGateResult(
                    decision,
                    PermissionGateFailureCodes.PermissionDenied),
                PermissionDecision.Indeterminate => new PermissionGateResult(
                    decision,
                    PermissionGateFailureCodes.DecisionUnavailable),
                _ => new PermissionGateResult(
                    PermissionDecision.Indeterminate,
                    PermissionGateFailureCodes.DecisionUnavailable)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new PermissionGateResult(
                PermissionDecision.Indeterminate,
                PermissionGateFailureCodes.DecisionUnavailable);
        }
    }
}

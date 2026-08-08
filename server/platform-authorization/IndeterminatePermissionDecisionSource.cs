using ICP.Platform.Authorization.Abstractions;

namespace ICP.Platform.Authorization;

/// <summary>
/// 在组合根未提供权限数据源时保持失败关闭，避免授权管线因依赖缺失而退出。
/// </summary>
internal sealed class IndeterminatePermissionDecisionSource : IPermissionDecisionSource
{
    public ValueTask<PermissionDecision> EvaluateAsync(
        string subjectId,
        string permissionCode,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(PermissionDecision.Indeterminate);
}

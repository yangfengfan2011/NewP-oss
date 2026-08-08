namespace ICP.Platform.Authorization.Abstractions;

/// <summary>
/// 将服务器可信主体标识和权限代码解析为授权数据层决策。
/// </summary>
public interface IPermissionDecisionSource
{
    /// <summary>
    /// 对指定主体和权限作出授权数据层决策。
    /// </summary>
    ValueTask<PermissionDecision> EvaluateAsync(
        string subjectId,
        string permissionCode,
        CancellationToken cancellationToken = default);
}

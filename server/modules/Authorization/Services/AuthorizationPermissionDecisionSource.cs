using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Abstractions;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>
/// 通过当前 Authorization 存储逐请求判定权限，不使用 Cookie 权限快照或角色名旁路。
/// </summary>
public sealed class AuthorizationPermissionDecisionSource : IPermissionDecisionSource
{
    private readonly IAuthorizationService _authorizationService;

    public AuthorizationPermissionDecisionSource(IAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }

    public async ValueTask<PermissionDecision> EvaluateAsync(
        string subjectId,
        string permissionCode,
        CancellationToken cancellationToken = default) =>
        await _authorizationService.HasPermissionAsync(subjectId, permissionCode, cancellationToken)
            ? PermissionDecision.Granted
            : PermissionDecision.Denied;
}

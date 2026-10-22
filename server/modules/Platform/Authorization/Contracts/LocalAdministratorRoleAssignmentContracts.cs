namespace ICP.Platform.Authorization.Contracts;

/// <summary>
/// 表示指定主体与既有 LocalAdmin 角色之间的分配状态。
/// </summary>
public enum LocalAdministratorAssignmentState
{
    NotAssigned,
    Assigned,
    RoleUnavailable,
    Invalid
}

/// <summary>
/// 为 Identity 离线维护提供唯一的窄 LocalAdmin 角色分配边界。
/// </summary>
public interface ILocalAdministratorRoleAssignment
{
    /// <summary>
    /// 在调用方拥有的共享事务中，把指定用户分配给既有 LocalAdmin 角色。
    /// </summary>
    ValueTask AssignAsync(
        string accountEid,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查指定用户是否精确获得一个有效的 LocalAdmin 角色分配。
    /// </summary>
    ValueTask<LocalAdministratorAssignmentState> InspectAsync(
        string accountEid,
        CancellationToken cancellationToken = default);
}

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>
/// 授权检查服务接口
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// 检查用户是否拥有指定权限
    /// </summary>
    Task<bool> HasPermissionAsync(string userId, string permissionCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查用户是否拥有任一权限
    /// </summary>
    Task<bool> HasAnyPermissionAsync(string userId, IEnumerable<string> permissionCodes, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查用户是否拥有所有权限
    /// </summary>
    Task<bool> HasAllPermissionsAsync(string userId, IEnumerable<string> permissionCodes, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户的所有权限代码
    /// </summary>
    Task<List<string>> GetUserPermissionsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取用户的所有角色代码
    /// </summary>
    Task<List<string>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default);
}

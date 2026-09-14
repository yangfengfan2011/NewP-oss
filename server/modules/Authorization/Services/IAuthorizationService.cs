namespace ICP.Platform.Modules.Authorization.Services;
/// <summary>认证主体的当前启用角色投影。</summary>
public interface IAuthorizationService
{
    /// <summary>读取指定主体的启用角色代码，供现有宿主认证约束使用。</summary>
    Task<List<string>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default);
}

namespace ICP.Platform.Authorization.Contracts;
/// <summary>仅供启动期确保受保护管理员角色存在，不经过普通管理命令或审计。</summary>
public interface ILocalAdministratorCatalog
{
    /// <summary>确保管理员定义存在；不分配用户成员或旧业务权限。</summary>
    Task EnsureAsync(CancellationToken cancellationToken = default);
}

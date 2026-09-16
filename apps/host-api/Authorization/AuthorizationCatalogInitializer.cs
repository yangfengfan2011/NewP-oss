using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.HostApi.Authorization;

/// <summary>只组合受保护管理员定义的初始化；功能全集由完整目录同步事务维护。</summary>
public sealed class AuthorizationCatalogInitializer(ILocalAdministratorCatalog catalog)
{
    /// <summary>确保管理员角色存在，不写旧权限目录或普通角色分配。</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default) => catalog.EnsureAsync(cancellationToken);
}

namespace ICP.Platform.Authorization.Contracts;
/// <summary>仅供 Host 启动期使用的管理员目录初始化，不经过普通管理命令或审计。</summary>
public interface ILocalAdministratorCatalog
{
    /// <summary>收敛管理员定义与活动权限全集；不分配任何用户成员。</summary>
    Task SynchronizeAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default);
}

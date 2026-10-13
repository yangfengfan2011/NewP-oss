using ICP.Platform.Persistence.Abstractions;

namespace ICP.Platform.Modules.Organization.Directories;

/// <summary>集中选择对象事务内的 SQL 事实或事务外已发布事实，不接管固定 SQL 的管理查询。</summary>
internal sealed class OrganizationEffectiveFacts(
    IPlatformTransactionExecutor transactions,
    IOrganizationFactsSource published,
    IOrganizationFactsSource transactional)
{
    private IOrganizationFactsSource Current => transactions.IsActive ? transactional : published;

    // 公司业务单读由当前事务状态确定来源，不允许缓存故障时回源。
    internal Task<IReadOnlyDictionary<string, object?>?> FindCompanyAsync(string id, CancellationToken cancellationToken) =>
        Current.FindCompanyAsync(id, cancellationToken);

    // 保留各来源的批量读取与空集合语义，避免绕过已发布视图的可用性检查。
    internal Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FindCompaniesAsync(
        IReadOnlyCollection<string> ids, CancellationToken cancellationToken) =>
        Current.FindCompaniesAsync(ids, cancellationToken);

    // 编码规范化与匹配仍由各事实来源按既有规则执行。
    internal Task<IReadOnlyDictionary<string, object?>?> FindCompanyByCodeAsync(string code, CancellationToken cancellationToken) =>
        Current.FindCompanyByCodeAsync(code, cancellationToken);

    // 根部门只需要公司启用事实，错误解释留给部门规则。
    internal Task<bool> IsCompanyEnabledAsync(string id, CancellationToken cancellationToken) =>
        Current.IsCompanyEnabledAsync(id, cancellationToken);

    // 非根目标沿完整祖先链读取，不在业务校验器中展开来源分支。
    internal Task<bool> IsDepartmentChainEnabledAsync(string companyId, string departmentId, CancellationToken cancellationToken) =>
        Current.IsDepartmentChainEnabledAsync(companyId, departmentId, cancellationToken);
}

/// <summary>Organization 内部真正具有双来源的窄事实集合；不作为跨模块查询协议。</summary>
internal interface IOrganizationFactsSource
{
    // 取得完整公司行，由目录负责面向消费者的投影。
    Task<IReadOnlyDictionary<string, object?>?> FindCompanyAsync(string id, CancellationToken cancellationToken);

    // 批量去重读取公司，省略不存在的记录。
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FindCompaniesAsync(
        IReadOnlyCollection<string> ids, CancellationToken cancellationToken);

    // 按忽略大小写及首尾空格的业务编码查找公司。
    Task<IReadOnlyDictionary<string, object?>?> FindCompanyByCodeAsync(string code, CancellationToken cancellationToken);

    // 只读取根目标所需的公司启用状态。
    Task<bool> IsCompanyEnabledAsync(string id, CancellationToken cancellationToken);

    // 判断同公司完整部门链，保留各来源既有的层级读取保证。
    Task<bool> IsDepartmentChainEnabledAsync(string companyId, string departmentId, CancellationToken cancellationToken);
}

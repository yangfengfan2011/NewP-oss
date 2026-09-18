using ICP.Platform.Organization.Contracts;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using ICP.Platform.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>归属与换父共享同一次组织视图，沿目标的实际祖先索引验证完整启用链。</summary>
internal static class OrganizationTargetChain
{
    // 公司由调用者提供字段级错误；部门链不扫描全组织，也不随层数往返数据库。
    public static bool IsEnabled(IOrganizationReadView view, string companyId, string departmentId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? current = departmentId;
        while (current is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(current)) return false;
            var row = view.Find(OrganizationRecordKind.Department, current);
            if (row is null || row.GetValueOrDefault("IsEnabled") is not true
                || !Equals(row.GetValueOrDefault("CompanyId"), companyId)) return false;
            current = row.GetValueOrDefault("ParentDepartmentId") as string;
        }
        return true;
    }
}

/// <summary>
/// 在对象事务内读取当前 SQL 事实，事务外继续消费同一次已发布组织视图。
/// </summary>
internal sealed class OrganizationTargetChainReader
{
    private readonly IOrganizationQuery _query;
    private readonly IPlatformTransactionExecutor _transactions;
    private readonly ICP.Platform.Persistence.EfCore.EfPlatformDbContext _dbContext;
    private readonly IDepartmentHierarchyQuery _hierarchy;

    // 共享当前 DbContext 与唯一事务 owner，确保 SQL 读取加入现有事务而不另建快照。
    public OrganizationTargetChainReader(
        IOrganizationQuery query,
        IPlatformTransactionExecutor transactions,
        ICP.Platform.Persistence.EfCore.EfPlatformDbContext dbContext)
    {
        _query = query;
        _transactions = transactions;
        _dbContext = dbContext;
        _hierarchy = new SqlServerDepartmentHierarchyQuery(dbContext);
    }

    // 根部门目标只需窄读公司状态；事务外仍使用同一次发布视图。
    internal async Task<bool> IsCompanyEnabledAsync(
        string companyId,
        CancellationToken cancellationToken)
    {
        if (_transactions.IsActive)
        {
            return await _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey("Company"))
                .AsNoTracking()
                .AnyAsync(
                    company => EF.Property<string>(company, "EID") == companyId
                        && EF.Property<bool>(company, "IsEnabled"),
                    cancellationToken);
        }
        return (await _query.ReadAsync(cancellationToken))
            .Find(OrganizationRecordKind.Company, companyId)?["IsEnabled"] is true;
    }

    // 事务内由数据库递归处理且不物化全表；事务外保持公共缓存的单快照语义。
    internal async Task<bool> IsEnabledAsync(
        string companyId,
        string departmentId,
        CancellationToken cancellationToken)
    {
        if (_transactions.IsActive)
        {
            return await _hierarchy.IsEnabledTargetChainAsync(companyId, departmentId, cancellationToken);
        }
        return OrganizationTargetChain.IsEnabled(
            await _query.ReadAsync(cancellationToken),
            companyId,
            departmentId,
            cancellationToken);
    }
}

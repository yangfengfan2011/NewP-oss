using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

/// <summary>复用当前 DbContext 与层级查询读取本事务已保存但尚未提交的组织事实。</summary>
internal sealed class SqlOrganizationFacts(EfPlatformDbContext dbContext, IDepartmentHierarchyQuery hierarchy)
    : IOrganizationFactsSource
{
    private DbSet<Dictionary<string, object?>> Companies =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Company"));

    // AsNoTracking 读取实际持久化结果，避免旧 tracked 行遮蔽前序专用写入。
    public async Task<IReadOnlyDictionary<string, object?>?> FindCompanyAsync(string id, CancellationToken cancellationToken) =>
        await Companies.AsNoTracking().SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == id, cancellationToken);

    // 批量在数据库过滤，不因 ID 数量增加往返；空集合维持 SQL 路径的无查询短路。
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FindCompaniesAsync(
        IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 0) return [];
        return await Companies.AsNoTracking().Where(row => distinct.Contains(EF.Property<string>(row, "EID")))
            .ToListAsync(cancellationToken);
    }

    // 延续目录现有编码比较，不改变数据库排序规则与字符串规范化边界。
    public async Task<IReadOnlyDictionary<string, object?>?> FindCompanyByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToLowerInvariant();
        return await Companies.AsNoTracking().SingleOrDefaultAsync(
            row => EF.Property<string>(row, "ECODE").ToLower() == normalized, cancellationToken);
    }

    // 根目标仅查询启用标量，不为状态判断加载公司全行。
    public Task<bool> IsCompanyEnabledAsync(string id, CancellationToken cancellationToken) =>
        Companies.AsNoTracking().AnyAsync(row => EF.Property<string>(row, "EID") == id
            && EF.Property<bool>(row, "IsEnabled"), cancellationToken);

    // 递归、断链与循环裁决仍由原 SQL Server 单往返查询拥有。
    public Task<bool> IsDepartmentChainEnabledAsync(string companyId, string departmentId, CancellationToken cancellationToken) =>
        hierarchy.IsEnabledTargetChainAsync(companyId, departmentId, cancellationToken);
}

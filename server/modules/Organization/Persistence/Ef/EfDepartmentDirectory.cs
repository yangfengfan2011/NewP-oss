using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

/// <summary>
/// 从 Organization Department 模型提供公司受限目录与数据库侧层级候选读取。
/// </summary>
public sealed class EfDepartmentDirectory : IDepartmentDirectory, IDepartmentHierarchyDirectory
{
    private readonly EfPlatformDbContext _dbContext;
    private readonly IDepartmentHierarchyQuery _hierarchyQuery;
    private readonly IDataScopeReadPlanProvider? _readPlans;

    /// <summary>
    /// 使用 Organization 当前 SQL Server 层级查询构造部门目录。
    /// </summary>
    public EfDepartmentDirectory(EfPlatformDbContext dbContext, IDataScopeReadPlanProvider? readPlans = null)
        : this(dbContext, new SqlServerDepartmentHierarchyQuery(dbContext), readPlans)
    {
    }

    // 测试可替换内部层级查询，普通目录读取仍使用同一动态 EF 上下文。
    internal EfDepartmentDirectory(
        EfPlatformDbContext dbContext,
        IDepartmentHierarchyQuery hierarchyQuery,
        IDataScopeReadPlanProvider? readPlans = null)
    {
        _dbContext = dbContext;
        _hierarchyQuery = hierarchyQuery;
        _readPlans = readPlans;
    }

    /// <inheritdoc/>
    public async Task<Department?> GetByIdAsync(
        string departmentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(row => EF.Property<string>(row, "EID") == departmentId, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, Department>> GetByIdsAsync(
        IReadOnlyCollection<string> departmentIds,
        CancellationToken cancellationToken = default)
    {
        var ids = NormalizeIds(departmentIds);
        if (ids.Count == 0)
        {
            return new Dictionary<string, Department>(StringComparer.Ordinal);
        }

        var entities = await Entities
            .AsNoTracking()
            .Where(row => ids.Contains(EF.Property<string>(row, "EID")))
            .ToListAsync(cancellationToken);
        return entities.Select(Map).ToDictionary(department => department.Id, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public async Task<Department?> GetByCompanyAndCodeAsync(
        string companyId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToLowerInvariant();
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => EF.Property<string>(row, "CompanyId") == companyId
                    && EF.Property<string>(row, "ECODE").ToLower() == normalized,
                cancellationToken);
        return entity is null ? null : Map(entity);
    }

    /// <inheritdoc/>
    public async Task<OrganizationDirectoryPage<Department>> LookupAsync(
        string companyId,
        OrganizationDirectoryLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        var (page, pageSize) = NormalizePage(request);
        var rows = Entities
            .AsNoTracking()
            .Where(row => EF.Property<string>(row, "CompanyId") == companyId);
        var readPredicate = await GetReadPredicateAsync(cancellationToken);
        if (readPredicate is not null) rows = rows.Where(readPredicate);
        if (!request.IncludeDisabled)
        {
            rows = rows.Where(row => EF.Property<bool>(row, "IsEnabled"));
        }

        var keyword = request.Keyword?.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            rows = rows.Where(row =>
                EF.Property<string>(row, "ECODE").Contains(keyword)
                || EF.Property<string>(row, "ENAME").Contains(keyword));
        }

        var pageRows = await Order(rows)
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var currentRows = await ReadCurrentRowsAsync(companyId, request.CurrentValueIds, readPredicate, cancellationToken);
        return new OrganizationDirectoryPage<Department>(
            pageRows.Take(pageSize).Select(Map).ToList(),
            currentRows.Items,
            page,
            pageSize,
            pageRows.Count > pageSize,
            currentRows.MaskedIds);
    }

    /// <inheritdoc/>
    public async Task<OrganizationDirectoryPage<Department>> LookupParentCandidatesAsync(
        string companyId,
        string? currentDepartmentId,
        OrganizationDirectoryLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        if (string.IsNullOrWhiteSpace(currentDepartmentId))
        {
            return await LookupAsync(companyId, request, cancellationToken);
        }

        var (page, pageSize) = NormalizePage(request);
        var readPredicate = await GetReadPredicateAsync(cancellationToken);
        var hierarchyPage = await _hierarchyQuery.LookupParentCandidatesAsync(
            companyId,
            currentDepartmentId,
            request.Keyword,
            page,
            pageSize,
            request.IncludeDisabled,
            readPredicate,
            cancellationToken);
        var currentRows = await ReadCurrentRowsAsync(
            companyId,
            request.CurrentValueIds,
            readPredicate,
            cancellationToken);
        return new OrganizationDirectoryPage<Department>(
            hierarchyPage.Items,
            currentRows.Items,
            page,
            pageSize,
            hierarchyPage.HasMore,
            currentRows.MaskedIds);
    }

    private DbSet<Dictionary<string, object?>> Entities =>
        _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey("Department"));

    // 当前值仍受 CompanyId 约束；分批解析并单列遮蔽身份，避免跨公司或误判字面名称。
    private async Task<(List<Department> Items, List<string> MaskedIds)> ReadCurrentRowsAsync(
        string companyId,
        IReadOnlyCollection<string>? currentValueIds,
        System.Linq.Expressions.Expression<Func<Dictionary<string, object?>, bool>>? readPredicate,
        CancellationToken cancellationToken)
    {
        var result = new List<Department>();
        var maskedIds = new List<string>();
        foreach (var ids in NormalizeIds(currentValueIds ?? []).Chunk(100))
        {
            var rows = Entities.AsNoTracking().Where(row => EF.Property<string>(row, "CompanyId") == companyId
                && ids.Contains(EF.Property<string>(row, "EID")));
            var existing = await rows.Select(row => EF.Property<string>(row, "EID")).ToListAsync(cancellationToken);
            if (readPredicate is not null) rows = rows.Where(readPredicate);
            var readable = (await rows.ToListAsync(cancellationToken))
                .ToDictionary(row => ReadString(row, "EID"), Map, StringComparer.Ordinal);
            foreach (var id in existing)
            {
                if (readable.TryGetValue(id, out var department)) result.Add(department);
                else
                {
                    result.Add(new Department { Id = id, IsEnabled = false });
                    maskedIds.Add(id);
                }
            }
        }
        return (result, maskedIds);
    }

    // 模块候选始终消费规范部门资源的完整读取计划；未启用时返回无附加过滤。
    private async Task<System.Linq.Expressions.Expression<Func<Dictionary<string, object?>, bool>>?> GetReadPredicateAsync(
        CancellationToken cancellationToken) => _readPlans is null ? null
        : (await _readPlans.GetReadPlanAsync("organization.department", cancellationToken)).Predicate;

    // 目录排序必须在 Skip/Take 前落到数据源，并用 EID 消除同名项的不稳定顺序。
    private static IOrderedQueryable<Dictionary<string, object?>> Order(
        IQueryable<Dictionary<string, object?>> rows) =>
        rows.OrderBy(row => EF.Property<string>(row, "ENAME"))
            .ThenBy(row => EF.Property<string>(row, "EID"));

    // 将外部页码约束到可安全计算 Skip 的范围，并限制单页目录读取规模。
    private static (int Page, int PageSize) NormalizePage(OrganizationDirectoryLookupRequest request)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        return (Math.Clamp(request.Page, 1, int.MaxValue / pageSize), pageSize);
    }

    // 批量标识先去空白和重复，避免扩大查询参数与结果索引冲突。
    private static IReadOnlyCollection<string> NormalizeIds(IEnumerable<string> ids) =>
        ids.Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    // 目录只投影人员校验、显示和 Lookup 所需字段，不泄漏动态 EF 行。
    internal static Department Map(IReadOnlyDictionary<string, object?> row) => new()
    {
        Id = ReadString(row, "EID"),
        CompanyId = ReadString(row, "CompanyId"),
        ParentDepartmentId = row.GetValueOrDefault("ParentDepartmentId") as string,
        Code = ReadString(row, "ECODE"),
        Name = ReadString(row, "ENAME"),
        IsEnabled = row.GetValueOrDefault("IsEnabled") is true
    };

    // 动态模型缺失字符串字段时使用空值，保持目录模型非空契约。
    private static string ReadString(IReadOnlyDictionary<string, object?> row, string field) =>
        row.GetValueOrDefault(field)?.ToString() ?? string.Empty;
}

using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

/// <summary>
/// 从 Organization Department 模型提供人员所需的公司受限窄目录读取。
/// </summary>
/// <param name="dbContext">当前请求范围内的动态模型数据库上下文。</param>
public sealed class EfDepartmentDirectory(EfPlatformDbContext dbContext)
    : IDepartmentDirectory, IDepartmentHierarchyDirectory
{
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
        var currentRows = await ReadCurrentRowsAsync(companyId, request.CurrentValueIds, cancellationToken);
        return new OrganizationDirectoryPage<Department>(
            pageRows.Take(pageSize).Select(Map).ToList(),
            currentRows.Select(Map).ToList(),
            page,
            pageSize,
            pageRows.Count > pageSize);
    }

    /// <inheritdoc/>
    public async Task<OrganizationDirectoryPage<Department>> LookupParentCandidatesAsync(
        string companyId,
        string? currentDepartmentId,
        OrganizationDirectoryLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        var (page, pageSize) = NormalizePage(request);
        var excludedIds = await ReadSubtreeIdsAsync(companyId, currentDepartmentId, cancellationToken);
        var rows = Entities.AsNoTracking()
            .Where(row => EF.Property<string>(row, "CompanyId") == companyId);
        if (!request.IncludeDisabled)
        {
            rows = rows.Where(row => EF.Property<bool>(row, "IsEnabled"));
        }

        if (excludedIds.Count > 0)
        {
            rows = rows.Where(row => !excludedIds.Contains(EF.Property<string>(row, "EID")));
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
        var currentRows = await ReadCurrentRowsAsync(companyId, request.CurrentValueIds, cancellationToken);
        return new OrganizationDirectoryPage<Department>(
            pageRows.Take(pageSize).Select(Map).ToList(),
            currentRows.Select(Map).ToList(),
            page,
            pageSize,
            pageRows.Count > pageSize);
    }

    private DbSet<Dictionary<string, object?>> Entities =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Department"));

    // 按层批量读取后代标识；主候选查询再用单个排除谓词在 Skip/Take 前完成筛选。
    private async Task<HashSet<string>> ReadSubtreeIdsAsync(
        string companyId,
        string? currentDepartmentId,
        CancellationToken cancellationToken)
    {
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(currentDepartmentId))
        {
            return excluded;
        }

        excluded.Add(currentDepartmentId);
        IReadOnlyCollection<string> frontier = [currentDepartmentId];
        while (frontier.Count > 0)
        {
            var parentIds = frontier.ToArray();
            var children = await Entities.AsNoTracking()
                .Where(row => EF.Property<string>(row, "CompanyId") == companyId
                    && parentIds.Contains(EF.Property<string?>(row, "ParentDepartmentId")!))
                .Select(row => EF.Property<string>(row, "EID"))
                .ToListAsync(cancellationToken);
            frontier = children.Where(excluded.Add).ToArray();
        }

        return excluded;
    }

    // 当前值仍受 CompanyId 约束，避免依赖切换后解析出另一公司的旧部门。
    private async Task<List<Dictionary<string, object?>>> ReadCurrentRowsAsync(
        string companyId,
        IReadOnlyCollection<string>? currentValueIds,
        CancellationToken cancellationToken)
    {
        var ids = NormalizeIds(currentValueIds ?? []).Take(100).ToArray();
        return ids.Length == 0
            ? []
            : await Order(Entities.AsNoTracking()
                    .Where(row => EF.Property<string>(row, "CompanyId") == companyId
                        && ids.Contains(EF.Property<string>(row, "EID"))))
                .ToListAsync(cancellationToken);
    }

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
    private static Department Map(IReadOnlyDictionary<string, object?> row) => new()
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

using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

/// <summary>
/// 从 Organization Company 模型提供人员所需的窄目录读取。
/// </summary>
/// <param name="dbContext">当前请求范围内的动态模型数据库上下文。</param>
public sealed class EfCompanyDirectory(EfPlatformDbContext dbContext, OrganizationCache? cache = null) : ICompanyDirectory
{
    /// <inheritdoc/>
    public async Task<Company?> GetByIdAsync(
        string companyId,
        CancellationToken cancellationToken = default)
    {
        if (cache is not null)
        {
            var snapshot = await cache.ReadAsync(cancellationToken);
            return snapshot.Companies.TryGetValue(companyId, out var current) ? Map(current) : null;
        }
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(row => EF.Property<string>(row, "EID") == companyId, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, Company>> GetByIdsAsync(
        IReadOnlyCollection<string> companyIds,
        CancellationToken cancellationToken = default)
    {
        var ids = NormalizeIds(companyIds);
        if (ids.Count == 0)
        {
            return new Dictionary<string, Company>(StringComparer.Ordinal);
        }

        if (cache is not null)
        {
            var snapshot = await cache.ReadAsync(cancellationToken);
            return ids.Where(snapshot.Companies.ContainsKey).ToDictionary(id => id, id => Map(snapshot.Companies[id]), StringComparer.Ordinal);
        }
        var entities = await Entities
            .AsNoTracking()
            .Where(row => ids.Contains(EF.Property<string>(row, "EID")))
            .ToListAsync(cancellationToken);
        return entities.Select(Map).ToDictionary(company => company.Id, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public async Task<Company?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (cache is not null)
        {
            var snapshot = await cache.ReadAsync(cancellationToken);
            return snapshot.CompanyCodes.TryGetValue(code.Trim(), out var id) ? Map(snapshot.Companies[id]) : null;
        }
        var normalized = code.Trim().ToLowerInvariant();
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => EF.Property<string>(row, "ECODE").ToLower() == normalized,
                cancellationToken);
        return entity is null ? null : Map(entity);
    }

    /// <inheritdoc/>
    public async Task<OrganizationDirectoryPage<Company>> LookupAsync(
        OrganizationDirectoryLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        var (page, pageSize) = NormalizePage(request);
        var rows = Entities.AsNoTracking().AsQueryable();
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
        var currentRows = await ReadCurrentRowsAsync(request.CurrentValueIds, cancellationToken);
        return new OrganizationDirectoryPage<Company>(
            pageRows.Take(pageSize).Select(Map).ToList(),
            currentRows.Select(Map).ToList(),
            page,
            pageSize,
            pageRows.Count > pageSize);
    }

    private DbSet<Dictionary<string, object?>> Entities =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Company"));

    // 当前值独立读取且不套用启用和关键词条件，保证停用归属仍可解析。
    private async Task<List<Dictionary<string, object?>>> ReadCurrentRowsAsync(
        IReadOnlyCollection<string>? currentValueIds,
        CancellationToken cancellationToken)
    {
        var ids = NormalizeIds(currentValueIds ?? []).Take(100).ToArray();
        return ids.Length == 0
            ? []
            : await Order(Entities.AsNoTracking()
                    .Where(row => ids.Contains(EF.Property<string>(row, "EID"))))
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
    private static Company Map(IReadOnlyDictionary<string, object?> row) => new()
    {
        Id = ReadString(row, "EID"),
        Code = ReadString(row, "ECODE"),
        Name = ReadString(row, "ENAME"),
        IsEnabled = row.GetValueOrDefault("IsEnabled") is true,
        SortOrder = ReadString(row, "SortOrder"),
        CreatedAt = row.GetValueOrDefault("CreatedAt") is DateTime created ? created : default,
        UpdatedAt = row.GetValueOrDefault("UpdatedAt") as DateTime?
    };

    // 动态模型缺失字符串字段时使用空值，保持目录模型非空契约。
    private static string ReadString(IReadOnlyDictionary<string, object?> row, string field) =>
        row.GetValueOrDefault(field)?.ToString() ?? string.Empty;
}

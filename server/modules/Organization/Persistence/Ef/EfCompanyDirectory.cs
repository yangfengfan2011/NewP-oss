using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

/// <summary>
/// 从 Organization Company 模型提供人员所需的窄目录读取。
/// </summary>
public sealed class EfCompanyDirectory : ICompanyDirectory
{
    private readonly EfPlatformDbContext _dbContext;
    private readonly OrganizationEffectiveFacts _facts;
    private readonly IDataScopeReadPlanProvider? _readPlans;

    // Lookup 保留固定 SQL 入口，公司业务事实统一委托模块内部的来源选择者。
    internal EfCompanyDirectory(EfPlatformDbContext dbContext, OrganizationEffectiveFacts facts,
        IDataScopeReadPlanProvider? readPlans = null)
    {
        _dbContext = dbContext;
        _facts = facts;
        _readPlans = readPlans;
    }

    /// <inheritdoc/>
    public async Task<Company?> GetByIdAsync(string companyId, CancellationToken cancellationToken = default)
    {
        var row = await _facts.FindCompanyAsync(companyId, cancellationToken);
        return row is null ? null : Map(row);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, Company>> GetByIdsAsync(
        IReadOnlyCollection<string> companyIds, CancellationToken cancellationToken = default) =>
        (await _facts.FindCompaniesAsync(companyIds, cancellationToken))
            .Select(Map).ToDictionary(company => company.Id, StringComparer.Ordinal);

    /// <inheritdoc/>
    public async Task<Company?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var row = await _facts.FindCompanyByCodeAsync(code, cancellationToken);
        return row is null ? null : Map(row);
    }

    /// <inheritdoc/>
    public async Task<OrganizationDirectoryPage<Company>> LookupAsync(
        OrganizationDirectoryLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        var (page, pageSize) = NormalizePage(request);
        var rows = Entities.AsNoTracking().AsQueryable();
        var readPredicate = _readPlans is null ? null
            : (await _readPlans.GetReadPlanAsync("organization.company", cancellationToken)).Predicate;
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
        var currentRows = await ReadCurrentRowsAsync(request.CurrentValueIds, readPredicate, cancellationToken);
        return new OrganizationDirectoryPage<Company>(
            pageRows.Take(pageSize).Select(Map).ToList(),
            currentRows.Items,
            page,
            pageSize,
            pageRows.Count > pageSize,
            currentRows.MaskedIds);
    }

    private DbSet<Dictionary<string, object?>> Entities =>
        _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey("Company"));

    // 当前值独立分批读取且不套用启用和关键词条件；遮蔽身份与名称文本分别返回。
    private async Task<(List<Company> Items, List<string> MaskedIds)> ReadCurrentRowsAsync(
        IReadOnlyCollection<string>? currentValueIds,
        System.Linq.Expressions.Expression<Func<Dictionary<string, object?>, bool>>? readPredicate,
        CancellationToken cancellationToken)
    {
        var result = new List<Company>();
        var maskedIds = new List<string>();
        foreach (var ids in NormalizeIds(currentValueIds ?? []).Chunk(100))
        {
            var rows = Entities.AsNoTracking().Where(row => ids.Contains(EF.Property<string>(row, "EID")));
            var existing = await rows.Select(row => EF.Property<string>(row, "EID")).ToListAsync(cancellationToken);
            if (readPredicate is not null) rows = rows.Where(readPredicate);
            var readable = (await rows.ToListAsync(cancellationToken))
                .ToDictionary(row => ReadString(row, "EID"), Map, StringComparer.Ordinal);
            foreach (var id in existing)
            {
                if (readable.TryGetValue(id, out var company)) result.Add(company);
                else
                {
                    result.Add(new Company { Id = id, IsEnabled = false });
                    maskedIds.Add(id);
                }
            }
        }
        return (result, maskedIds);
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

using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Services;

/// <summary>组织扩展从当前发布视图解析关系；目录按数据库分页，只投影配置所需身份。</summary>
public sealed class OrganizationDataScopeIdentities(IOrganizationQuery organizations, EfPlatformDbContext db) : IDataScopeIdentityProvider
{
    public IReadOnlyList<DataScopeSubjectType> SubjectTypes { get; } = [new("company", "公司", true), new("department", "部门", true, true)];
    public IReadOnlyList<DataScopeContextValue> ContextValues { get; } =
        [new("current.person", "当前人员", "String", "Organization"), new("current.company", "当前公司", "String", "Organization"), new("current.department", "当前部门", "String", "Organization")];
    public IReadOnlyList<DataScopeContextBinding> ContextBindings { get; } =
        [new("Organization/Person", "EID", "current.person"), new("Organization/Person", "AccountEID", "current.account"),
         new("Organization/Person", "CompanyId", "current.company"), new("Organization/Person", "DepartmentId", "current.department"),
         new("Organization/Company", "EID", "current.company"), new("Organization/Department", "EID", "current.department"),
         new("Organization/Department", "CompanyId", "current.company"), new("Organization/Department", "ParentDepartmentId", "current.department")];

    /// <inheritdoc/>
    public async Task<IDataScopeIdentityFacts> ResolveAsync(string accountId, CancellationToken token) =>
        new Facts((await organizations.ReadAsync(token)).FindByAccount(accountId));

    /// <inheritdoc/>
    public async Task<DataScopeSubjectPage> SearchAsync(string type, string? query, int page, IReadOnlyList<string> currentIds, CancellationToken token)
    {
        var model = type == "company" ? "Company" : "Department";
        var rows = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Organization/" + model)).AsNoTracking();
        var filtered = rows;
        if (!string.IsNullOrWhiteSpace(query)) filtered = filtered.Where(row => EF.Property<string>(row, "ENAME").Contains(query));
        var selected = filtered.OrderBy(row => EF.Property<string>(row, "EID")).Skip((page - 1) * 20).Take(21);
        var items = await Project(selected, type).ToListAsync(token);
        var current = new List<DataScopeSubjectName>();
        foreach (var ids in currentIds.Distinct(StringComparer.Ordinal).Chunk(100))
            current.AddRange(await Project(rows.Where(row => ids.Contains(EF.Property<string>(row, "EID"))), type).ToListAsync(token));
        return new(items.Take(20).ToArray(), items.Count > 20, current);
    }

    // 条件投影在构造查询时选择，避免公司模型访问不存在的部门字段。
    private static IQueryable<DataScopeSubjectName> Project(IQueryable<Dictionary<string, object?>> rows, string type) => type == "company"
        ? rows.Select(row => new DataScopeSubjectName(EF.Property<string>(row, "EID"), EF.Property<string>(row, "ENAME"), null))
        : rows.Select(row => new DataScopeSubjectName(EF.Property<string>(row, "EID"), EF.Property<string>(row, "ENAME"), EF.Property<string?>(row, "ParentDepartmentId")));

    private sealed class Facts(CurrentOrganization? organization) : IDataScopeIdentityFacts
    {
        /// <inheritdoc/>
        public bool Matches(string type, string? id, DataScopeMatchOptions options) => id is not null && type switch
        {
            "company" => id == Id(organization?.Company),
            "department" => id == Id(organization?.Department) || options.IncludeDescendants && organization?.Ancestors.Any(row => Id(row) == id) == true,
            _ => false
        };
        /// <inheritdoc/>
        public string? ContextValue(string key) => key switch
        {
            "current.person" => Id(organization?.Person), "current.company" => Id(organization?.Company), "current.department" => Id(organization?.Department),
            _ => throw new InvalidOperationException("DATA_SCOPE_CONTEXT_UNKNOWN")
        };
        // 规范身份取 owner 当前原始字段；空关系不伪造身份。
        private static string? Id(IReadOnlyDictionary<string, object?>? row) => row?["EID"] as string;
    }
}

using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>Authorization 拥有当前有效角色事实。</summary>
public sealed class AuthorizationDataScopeIdentities(EfPlatformDbContext db) : IDataScopeIdentityProvider
{
    public IReadOnlyList<DataScopeSubjectType> SubjectTypes { get; } = [new("role", "角色", true)];
    public IReadOnlyList<DataScopeContextValue> ContextValues => [];

    /// <inheritdoc/>
    public async Task<IDataScopeIdentityFacts> ResolveAsync(string accountId, CancellationToken token)
    {
        var roles = await (from link in db.Set<Dictionary<string, object?>>(db.ResolveModelKey("UserRole")).AsNoTracking()
            join role in Roles on EF.Property<string>(link, "RoleId") equals EF.Property<string>(role, "EID")
            where EF.Property<string>(link, "UserId") == accountId && EF.Property<bool>(role, "IsEnabled")
            select EF.Property<string>(role, "EID")).ToArrayAsync(token);
        return new Facts(roles.ToHashSet(StringComparer.Ordinal));
    }

    private IQueryable<Dictionary<string, object?>> Roles => db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role")).AsNoTracking();
    /// <inheritdoc/>
    public async Task<DataScopeSubjectPage> SearchAsync(string type, string? query, int page, IReadOnlyList<string> currentIds, CancellationToken token)
    {
        var filtered = Roles;
        if (!string.IsNullOrWhiteSpace(query)) filtered = filtered.Where(row => EF.Property<string>(row, "ENAME").Contains(query));
        var items = await filtered.OrderBy(row => EF.Property<string>(row, "EID")).Skip((page - 1) * 20).Take(21)
            .Select(row => new DataScopeSubjectName(EF.Property<string>(row, "EID"), EF.Property<string>(row, "ENAME"), null)).ToListAsync(token);
        var current = new List<DataScopeSubjectName>();
        foreach (var ids in currentIds.Distinct(StringComparer.Ordinal).Chunk(100))
            current.AddRange(await Roles.Where(row => ids.Contains(EF.Property<string>(row, "EID")))
                .Select(row => new DataScopeSubjectName(EF.Property<string>(row, "EID"), EF.Property<string>(row, "ENAME"), null)).ToListAsync(token));
        return new(items.Take(20).ToArray(), items.Count > 20, current);
    }

    private sealed class Facts(IReadOnlySet<string> roles) : IDataScopeIdentityFacts
    {
        /// <inheritdoc/>
        public bool Matches(string type, string? id, DataScopeMatchOptions options) => type == "role" && id is not null && roles.Contains(id);
        /// <inheritdoc/>
        public IReadOnlyList<DataScopeSubjectMembership> Memberships(string type) =>
            type == "role" ? roles.Select(id => new DataScopeSubjectMembership(type, id)).ToArray() : [];
        /// <inheritdoc/>
        public string? ContextValue(string key) => throw new InvalidOperationException("DATA_SCOPE_CONTEXT_UNKNOWN");
    }
}

/// <summary>所有已认证用户无需角色或组织事实源，不引入不相关的依赖查询。</summary>
public sealed class AllAuthenticatedDataScopeIdentities : IDataScopeIdentityProvider, IDataScopeIdentityFacts
{
    public IReadOnlyList<DataScopeSubjectType> SubjectTypes { get; } = [new("all", "所有已认证用户", false)];
    public IReadOnlyList<DataScopeContextValue> ContextValues => [];
    /// <inheritdoc/>
    public Task<IDataScopeIdentityFacts> ResolveAsync(string accountId, CancellationToken token) => Task.FromResult<IDataScopeIdentityFacts>(this);
    /// <inheritdoc/>
    public Task<DataScopeSubjectPage> SearchAsync(string type, string? query, int page, IReadOnlyList<string> currentIds, CancellationToken token) =>
        Task.FromResult(new DataScopeSubjectPage([], false, []));
    /// <inheritdoc/>
    public bool Matches(string type, string? id, DataScopeMatchOptions options) => type == "all";
    /// <inheritdoc/>
    public IReadOnlyList<DataScopeSubjectMembership> Memberships(string type) =>
        type == "all" ? [new(type, null)] : [];
    /// <inheritdoc/>
    public string? ContextValue(string key) => throw new InvalidOperationException("DATA_SCOPE_CONTEXT_UNKNOWN");
}

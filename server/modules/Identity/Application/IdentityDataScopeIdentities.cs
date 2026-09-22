using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Identity.Application;

/// <summary>账号 owner 提供最小授权目录；不经过业务读取过滤，也不输出账号安全字段。</summary>
public sealed class IdentityDataScopeIdentities(EfPlatformDbContext db) : IDataScopeIdentityProvider
{
    public IReadOnlyList<DataScopeSubjectType> SubjectTypes { get; } = [new("user", "用户", true)];
    public IReadOnlyList<DataScopeContextValue> ContextValues { get; } = [new("current.account", "当前账号", "String", "Identity")];
    public IReadOnlyList<DataScopeContextBinding> ContextBindings { get; } = [new("Identity/IdentityAccount", "EID", "current.account")];

    /// <inheritdoc/>
    public Task<IDataScopeIdentityFacts> ResolveAsync(string accountId, CancellationToken token) => Task.FromResult<IDataScopeIdentityFacts>(new Facts(accountId));

    /// <inheritdoc/>
    public async Task<DataScopeSubjectPage> SearchAsync(string type, string? query, int page, IReadOnlyList<string> currentIds, CancellationToken token)
    {
        var rows = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("IdentityAccount")).AsNoTracking()
            .Where(row => EF.Property<string>(row, "Status") != "Deleted");
        var filtered = rows;
        if (!string.IsNullOrWhiteSpace(query)) filtered = filtered.Where(row => EF.Property<string>(row, "ENAME").Contains(query));
        var items = await filtered.OrderBy(row => EF.Property<string>(row, "EID")).Skip((page - 1) * 20).Take(21)
            .Select(row => new DataScopeSubjectName(EF.Property<string>(row, "EID"), EF.Property<string>(row, "ENAME"), null)).ToListAsync(token);
        var current = new List<DataScopeSubjectName>();
        foreach (var ids in currentIds.Distinct(StringComparer.Ordinal).Chunk(100))
            current.AddRange(await rows.Where(row => ids.Contains(EF.Property<string>(row, "EID")))
                .Select(row => new DataScopeSubjectName(EF.Property<string>(row, "EID"), EF.Property<string>(row, "ENAME"), null)).ToListAsync(token));
        return new(items.Take(20).ToArray(), items.Count > 20, current);
    }

    private sealed class Facts(string accountId) : IDataScopeIdentityFacts
    {
        /// <inheritdoc/>
        public bool Matches(string type, string? id, DataScopeMatchOptions options) => type == "user" && id == accountId;
        /// <inheritdoc/>
        public string? ContextValue(string key) => key == "current.account" ? accountId : throw new InvalidOperationException("DATA_SCOPE_CONTEXT_UNKNOWN");
    }
}

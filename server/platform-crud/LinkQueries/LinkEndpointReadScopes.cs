using System.Data.Common;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Crud.LinkQueries;

/// <summary>Link 列表和出现树共用端点读取计划；不依赖任何源请求或位置策略。</summary>
public sealed class LinkEndpointReadScopes(EfPlatformDbContext db, LinkQueryCatalog catalog, IDataScopeReadPlanProvider reads,
    IEnumerable<ILinkQueryEndpointConstraint> constraints)
{
    /// <summary>交集正式对象 read 与显式端点表达式，不继承列表临时筛选。</summary>
    public async Task<IQueryable<Dictionary<string, object?>>> RowsAsync(LinkQueryEndpoint endpoint, CancellationToken token)
    {
        var rows = db.Set<Dictionary<string, object?>>(endpoint.Model.LogicalKey).AsNoTracking();
        var plan = await reads.GetReadPlanAsync(endpoint.ResourceKey, token);
        if (plan.Predicate is not null) rows = rows.Where(plan.Predicate);
        if (endpoint.ConstraintKey is { } key)
        {
            var constraint = constraints.SingleOrDefault(constraint => constraint.Key == key) ?? throw new InvalidOperationException("LINK_QUERY_ENDPOINT_CONSTRAINT_MISSING");
            rows = rows.Where(await constraint.ReadPredicateAsync(endpoint, token) ?? throw new InvalidOperationException("LINK_QUERY_ENDPOINT_CONSTRAINT_MISSING"));
        }
        return rows;
    }

    /// <summary>将端点裁决与可信 SQL 集合统一下推；名称由代码分配，参数保留 provider 类型。</summary>
    public async Task<LinkQuerySqlScope> ReadAsync(string resourceKey, string modelKey, string scopeName, CancellationToken token = default)
    {
        if (scopeName.Length == 0 || scopeName.Any(character => !char.IsAsciiLetterOrDigit(character))) throw new ArgumentException("Invalid trusted scope name.", nameof(scopeName));
        var definition = catalog.Get(resourceKey);
        if (!definition.Endpoints.TryGetValue(modelKey, out var endpoint)) throw new LinkQueryException(400, "LinkQuery.InvalidType", "target");
        var parameters = new List<DbParameter>(); var rows = await RowsAsync(endpoint, token);
        using var command = rows.Select(row => EF.Property<string>(row, endpoint.IdField)).CreateDbCommand();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DbParameter parameter in command.Parameters)
        {
            var name = "@" + scopeName + "EndpointParameter" + parameters.Count;
            names.Add(parameter.ParameterName, name); var copy = (DbParameter)((ICloneable)parameter).Clone(); copy.ParameterName = name; parameters.Add(copy);
        }
        var sql = LinkQuerySqlScope.RenameParameters(command.CommandText.Trim().TrimEnd(';'), names);
        var scope = endpoint.ConstraintKey is { } key ? await constraints.Single(constraint => constraint.Key == key).ReadSqlScopeAsync(endpoint, scopeName, token) : null;
        if (scope is not null)
        {
            parameters.AddRange(scope.Parameters);
            sql += " INTERSECT SELECT ScopeId FROM (" + scope.IdsQuery + ") scopeKeys(ScopeId)";
        }
        return new(scope?.Preparation ?? "", sql, parameters);
    }
}

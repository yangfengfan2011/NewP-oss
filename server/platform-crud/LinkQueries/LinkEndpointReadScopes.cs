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
        var prepared = await PrepareAsync(endpoint, scopeName, parameters, token);
        return new(prepared.Preparation, prepared.CompileIds(rows, parameters), parameters);
    }

    // 一条命令内每端点只准备一次 SQL 集合，anchor 与关联端复用它但分别编译自己的查询条件。
    internal async Task<PreparedEndpoint> PrepareAsync(LinkQueryEndpoint endpoint, string scopeName,
        List<DbParameter> parameters, CancellationToken token)
    {
        if (scopeName.Length == 0 || scopeName.Any(character => !char.IsAsciiLetterOrDigit(character))) throw new ArgumentException("Invalid trusted scope name.", nameof(scopeName));
        LinkQuerySqlScope? scope = null;
        if (endpoint.ConstraintKey is { } key)
        {
            var constraint = constraints.SingleOrDefault(constraint => constraint.Key == key)
                ?? throw new InvalidOperationException("LINK_QUERY_ENDPOINT_CONSTRAINT_MISSING");
            scope = await constraint.ReadSqlScopeAsync(endpoint, scopeName, token);
        }
        if (scope is not null) parameters.AddRange(scope.Parameters);
        return new(this, endpoint, scopeName, scope);
    }

    // 准备结果只属于当前命令；隐藏强制交集，调用方仅提供当前用途的附加筛选。
    internal sealed class PreparedEndpoint(LinkEndpointReadScopes owner, LinkQueryEndpoint endpoint, string scopeName, LinkQuerySqlScope? scope)
    {
        internal string Preparation => scope?.Preparation ?? "";

        // 公共筛选仍在同一个 EF 查询里下推，再与可信 SQL 集合求交集，不增加查询层次。
        internal async Task<string> ReadIdsAsync(List<DbParameter> parameters, CancellationToken token,
            Func<IQueryable<Dictionary<string, object?>>, IQueryable<Dictionary<string, object?>>>? filter = null)
        {
            var rows = await owner.RowsAsync(endpoint, token);
            return CompileIds(filter is null ? rows : filter(rows), parameters);
        }

        // anchor 保留单对象条件与独立准入检查，不继承关联端的列表筛选。
        internal async Task<string> ReadAnchorAsync(string id, string idParameter, List<DbParameter> parameters, CancellationToken token)
        {
            var rows = await owner.RowsAsync(endpoint, token);
            var sql = CompileRows(rows.Where(row => EF.Property<string>(row, endpoint.IdField) == id), parameters);
            var available = "SELECT 1 WHERE " + idParameter + " IN (" + sql + ")";
            return scope is null ? available : available + " AND " + idParameter + " IN (" + scope.IdsQuery + ")";
        }

        // scope 允许 UNION，必须封闭为派生表后求交集，防止其分支逃出正式 read 和公共筛选。
        internal string CompileIds(IQueryable<Dictionary<string, object?>> rows, List<DbParameter> parameters)
        {
            var sql = CompileRows(rows, parameters);
            return scope is null ? sql : sql + " INTERSECT SELECT ScopeId FROM (" + scope.IdsQuery + ") scopeKeys(ScopeId)";
        }

        // 同一端点的多次编译也使用命令级参数序号，保留 provider 类型并与可信 scope 参数隔离。
        private string CompileRows(IQueryable<Dictionary<string, object?>> rows, List<DbParameter> parameters)
        {
            using var command = rows.Select(row => EF.Property<string>(row, endpoint.IdField)).CreateDbCommand();
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DbParameter parameter in command.Parameters)
            {
                var name = "@" + scopeName + "EndpointParameter" + parameters.Count;
                names.Add(parameter.ParameterName, name);
                var copy = (DbParameter)((ICloneable)parameter).Clone();
                copy.ParameterName = name;
                parameters.Add(copy);
            }
            return LinkQuerySqlScope.RenameParameters(command.CommandText.Trim().TrimEnd(';'), names);
        }
    }
}

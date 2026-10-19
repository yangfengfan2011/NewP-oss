using System.Data.Common;
using System.Text;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ICP.Platform.Crud.Runtime;

/// <summary>标准关系树节点；业务上下文字段来自已校验配置。</summary>
public sealed record RelationTreeNode(string NodeKey, string NodeType, string Label, bool HasChildren,
    string Code, bool IsEnabled, IReadOnlyDictionary<string, object?> Context);
public sealed record RelationTreeSearchItem(RelationTreeNode Node, IReadOnlyList<RelationTreeNode> Path);
public sealed record RelationTreeSearchPage(IReadOnlyList<RelationTreeSearchItem> Items, int Total, int Page, int PageSize);

/// <summary>统一执行根、直属节点、分页搜索与祖先链查询，不在应用内物化全树。</summary>
public sealed class RelationTreeReader
{
    private readonly EfPlatformDbContext db;
    private readonly RelationTreeSql compiler;
    private readonly IDataScopeReadPlanProvider? readPlans;
    private readonly IDataExecutionAccess? executions;

    /// <summary>独立SQL/投影消费者保留原构造；正式模块通过含执行器的构造消费冻结来源。</summary>
    public RelationTreeReader(EfPlatformDbContext db, RelationTreeSql compiler, IDataScopeReadPlanProvider? readPlans = null)
    {
        this.db = db; this.compiler = compiler; this.readPlans = readPlans;
    }

    /// <summary>正式树在原读取计划位置验证同一来源，不在入口枚举ID或预判节点。</summary>
    public RelationTreeReader(EfPlatformDbContext db, RelationTreeSql compiler, IDataScopeReadPlanProvider? readPlans, IDataExecutionAccess executions)
        : this(db, compiler, readPlans)
    {
        this.executions = executions;
    }
    private const string Order = "SortOrder, Code, NodeType, NodeKey";
    // 十六进制身份不含路径分隔符，避免节点 ID 含竖线时被误判为祖先重复。
    private const string Ancestors = """
        , Ancestors AS (
          SELECT s.NodeKey AS TargetKey, n.*, 0 AS Depth,
            CAST(N'|'+CONVERT(varchar(max),CONVERT(varbinary(max),n.NodeKey),2)+N'|' AS nvarchar(max)) AS Visited, CAST(0 AS bit) AS IsCycle
          FROM Seeds s JOIN Nodes n ON n.NodeKey=s.NodeKey
          UNION ALL
          SELECT a.TargetKey, n.*, a.Depth+1,
            CAST(a.Visited+CONVERT(varchar(max),CONVERT(varbinary(max),n.NodeKey),2)+N'|' AS nvarchar(max)),
            CAST(CASE WHEN CHARINDEX(N'|'+CONVERT(varchar(max),CONVERT(varbinary(max),n.NodeKey),2)+N'|',a.Visited)>0 THEN 1 ELSE 0 END AS bit)
          FROM Ancestors a JOIN Nodes n ON n.NodeKey=a.ParentKey AND (__PARENT_MATCH__) WHERE a.IsCycle=0
        )
        SELECT __RESULT_COLUMNS__, a.TargetKey, a.IsCycle, __ACCESS__ AS IsAccessible FROM Ancestors a JOIN Nodes target ON target.NodeKey=a.TargetKey
        ORDER BY target.SortOrder, target.Code, target.NodeType, target.NodeKey, a.Depth DESC OPTION(MAXRECURSION 0);
        """;

    /// <summary>读取完整根集合；当前契约不对根或同级节点分页。</summary>
    public Task<IReadOnlyList<RelationTreeNode>> RootsAsync(RelationTreeDefinition definition, CancellationToken token) =>
        ExecuteAsync(definition, query => query.Sql + " SELECT * FROM " + Source(query) + " WHERE ParentKey IS NULL AND AllowsRoot=1 ORDER BY " + Order + Hint(query), [],
            async (reader, projection) => (IReadOnlyList<RelationTreeNode>)await ReadNodesAsync(reader, projection, token), token);

    /// <summary>一次往返确认父节点并读取其全部直属子节点；不存在与空分支分开。</summary>
    public Task<IReadOnlyList<RelationTreeNode>?> ChildrenAsync(RelationTreeDefinition definition, string nodeKey, CancellationToken token) =>
        ExecuteAsync(definition, query => query.Sql + " SELECT COUNT(*) FROM " + Source(query) + " WHERE NodeKey=@nodeKey" + Hint(query) + " " + query.Sql
            + ChildrenSql(query, nodeKey), new() { ["@nodeKey"] = nodeKey }, async (reader, projection) =>
            {
                await reader.ReadAsync(token);
                var exists = reader.GetInt32(0) > 0;
                await reader.NextResultAsync(token);
                var children = await ReadNodesAsync(reader, projection, token);
                return exists ? (IReadOnlyList<RelationTreeNode>)children : null;
            }, token);

    /// <summary>只返回目标当前祖先链；断链或环以异常失败。</summary>
    public Task<IReadOnlyList<RelationTreeNode>?> PathAsync(RelationTreeDefinition definition, string nodeKey, CancellationToken token) =>
        ExecuteAsync(definition, query => query.Sql + ", Seeds AS (SELECT NodeKey FROM Nodes WHERE NodeKey=@nodeKey) " + AncestorsSql(query),
            new() { ["@nodeKey"] = nodeKey }, async (reader, projection) => (await ReadPathsAsync(reader, projection, token)).GetValueOrDefault(nodeKey), token);

    /// <summary>数据库内先匹配计数并稳定分页，再批量读取本页祖先路径。</summary>
    public Task<RelationTreeSearchPage> SearchAsync(RelationTreeDefinition definition, string keyword, int page, int pageSize, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(keyword) || page < 1 || pageSize < 1 || page > int.MaxValue / pageSize)
            throw new ArgumentException("搜索需要非空关键词、正页码和正整数 pageSize。");
        const string predicate = " WHERE CHARINDEX(@keyword,DisplayName)>0 OR CHARINDEX(@keyword,Code)>0 ";
        return ExecuteAsync(definition, query => query.Sql + " SELECT COUNT(*) FROM " + Source(query) + predicate + Hint(query) + " " + query.Sql
            + ", Seeds AS (SELECT NodeKey FROM " + Source(query) + predicate + " ORDER BY " + Order
            + " OFFSET @offset ROWS FETCH NEXT @take ROWS ONLY) " + AncestorsSql(query),
            new() { ["@keyword"] = keyword.Trim(), ["@offset"] = (page - 1) * pageSize, ["@take"] = pageSize }, async (reader, projection) =>
            {
                await reader.ReadAsync(token);
                var total = reader.GetInt32(0);
                await reader.NextResultAsync(token);
                var paths = await ReadPathsAsync(reader, projection, token);
                return new RelationTreeSearchPage(paths.Values.Select(path => new RelationTreeSearchItem(path[^1], path)).ToArray(), total, page, pageSize);
            }, token);
    }

    // 授权开启时所有树入口只消费从业务根递归抵达的节点。
    private static string Source(RelationTreeQuery query) => query.HasReadScope ? "Reachable" : "Nodes";

    // SQL Server 递归查询不施加业务容量上限；普通查询保留原执行形状。
    private static string Hint(RelationTreeQuery query) => query.HasReadScope ? " OPTION(MAXRECURSION 0);" : ";";

    // 单字段父外键无需额外父集合连接；复合外键保留所有分量匹配。
    private static string ChildrenSql(RelationTreeQuery query, string nodeKey)
    {
        var type = nodeKey.Split(':', 2)[0];
        return query.CompositeParentTypes.Contains(type)
            ? " SELECT a.* FROM " + Source(query) + " a JOIN " + Source(query) + " n ON a.ParentKey=n.NodeKey AND (" + query.ParentMatch + ") WHERE n.NodeKey=@nodeKey ORDER BY a.SortOrder,a.Code,a.NodeType,a.NodeKey" + Hint(query)
            : " SELECT * FROM " + Source(query) + " WHERE ParentKey=@nodeKey ORDER BY " + Order + Hint(query);
    }

    // 递归只携带关系和查询字段；结果不传回循环检测的内部长字符串。
    private static string AncestorsSql(RelationTreeQuery query) => Ancestors.Replace("__PARENT_MATCH__", query.ParentMatch, StringComparison.Ordinal)
        .Replace("__RESULT_COLUMNS__", string.Join(",", query.ResultColumns.Split(',').Select(column => "a." + column)), StringComparison.Ordinal)
        .Replace("__ACCESS__", query.HasReadScope ? "CASE WHEN EXISTS(SELECT 1 FROM Reachable permitted WHERE permitted.NodeKey=a.TargetKey) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END" : "CAST(1 AS bit)", StringComparison.Ordinal);

    // 流式读取本次分支，不查询后代。
    private static async Task<List<RelationTreeNode>> ReadNodesAsync(DbDataReader reader, RelationTreeQuery projection, CancellationToken token)
    {
        var nodes = new List<RelationTreeNode>();
        var readNode = CreateNodeReader(reader, projection);
        while (await reader.ReadAsync(token)) nodes.Add(readNode());
        return nodes;
    }

    // 先用完整业务节点验证真实路径，再按授权可达性删去隐藏目标；环和断链始终是故障。
    private static async Task<Dictionary<string, IReadOnlyList<RelationTreeNode>>> ReadPathsAsync(DbDataReader reader, RelationTreeQuery projection, CancellationToken token)
    {
        var paths = new Dictionary<string, List<RelationTreeNode>>(StringComparer.Ordinal);
        var inaccessible = new HashSet<string>(StringComparer.Ordinal);
        var readNode = CreateNodeReader(reader, projection);
        while (await reader.ReadAsync(token))
        {
            if (reader.GetBoolean(reader.GetOrdinal("IsCycle"))) throw new InvalidOperationException("树关系包含环。");
            var key = reader.GetString(reader.GetOrdinal("TargetKey"));
            if (!reader.GetBoolean(reader.GetOrdinal("IsAccessible"))) inaccessible.Add(key);
            if (!paths.TryGetValue(key, out var path))
            {
                if (!reader.IsDBNull(reader.GetOrdinal("ParentKey")) || !reader.GetBoolean(reader.GetOrdinal("AllowsRoot"))) throw new InvalidOperationException("树节点的祖先路径不完整。");
                paths.Add(key, path = []);
            }
            path.Add(readNode());
        }
        return paths.Where(pair => !inaccessible.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => (IReadOnlyList<RelationTreeNode>)pair.Value, StringComparer.Ordinal);
    }

    // 列序号及上下文映射每个结果集解析一次，避免每个节点重复 LINQ 和名称查找。
    private static Func<RelationTreeNode> CreateNodeReader(DbDataReader reader, RelationTreeQuery projection)
    {
        var key = reader.GetOrdinal("NodeKey");
        var type = reader.GetOrdinal("NodeType");
        var label = reader.GetOrdinal("DisplayName");
        var children = reader.GetOrdinal("HasChildren");
        var code = reader.GetOrdinal("Code");
        var enabled = reader.GetOrdinal("IsEnabled");
        var contexts = projection.ContextColumns.ToDictionary(pair => pair.Key,
            pair => pair.Value.Select(field => (field.Key, Ordinal: reader.GetOrdinal(field.Value.Column), Type: Nullable.GetUnderlyingType(field.Value.ClrType) ?? field.Value.ClrType)).ToArray(), StringComparer.Ordinal);
        return () =>
        {
            var nodeType = reader.GetString(type);
            var fields = contexts[nodeType];
            var context = new Dictionary<string, object?>(fields.Length, StringComparer.Ordinal);
            foreach (var field in fields)
            {
                var value = reader.IsDBNull(field.Ordinal) ? null : reader.GetValue(field.Ordinal);
                context.Add(field.Key, field.Type == typeof(DateOnly) && value is DateTime date ? DateOnly.FromDateTime(date) : value);
            }
            return new RelationTreeNode(reader.GetString(key), nodeType, reader.GetString(label), reader.GetBoolean(children),
                reader.GetString(code), reader.GetBoolean(enabled), context);
        };
    }

    // 保留请求事务和连接所有权；取消贯穿命令及结果读取。
    private async Task<T> ExecuteAsync<T>(RelationTreeDefinition definition, Func<RelationTreeQuery, string> query,
        Dictionary<string, object?> parameters, Func<DbDataReader, RelationTreeQuery, Task<T>> read, CancellationToken token)
    {
        var (filters, scopeParameters) = await ReadFiltersAsync(definition, token);
        var projection = compiler.Build(definition, readFilters: filters);
        var connection = db.Database.GetDbConnection();
        var close = connection.State != System.Data.ConnectionState.Open;
        if (close) await connection.OpenAsync(token);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = query(projection);
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var pair in projection.Parameters.Concat(parameters))
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = pair.Key; parameter.Value = pair.Value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
            foreach (var parameter in scopeParameters) command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(token);
            return await read(reader, projection);
        }
        finally { if (close) await connection.CloseAsync(); }
    }

    // EF 负责把完整裁决表达式翻译成带类型参数的 SQL，只投影当前资源主键供关系树关联。
    private async Task<(Dictionary<string, string> Filters, List<DbParameter> Parameters)> ReadFiltersAsync(
        RelationTreeDefinition definition, CancellationToken token)
    {
        var filters = new Dictionary<string, string>(StringComparer.Ordinal);
        var parameters = new List<DbParameter>();
        if (executions is null && readPlans is null) return (filters, parameters);
        foreach (var node in definition.Source.Nodes.DistinctBy(node => node.ResourceKey))
        {
            var binding = definition.ReadBinding;
            var plan = executions is null ? await readPlans!.GetReadPlanAsync(node.ResourceKey, token)
                : await executions.GetQueryReadPlanAsync(new(new(
                    binding?.Execution ?? throw new InvalidOperationException("RELATION_TREE_DATA_EXECUTION_MISSING"),
                    binding.Provider!, "read"), "query"), node.ResourceKey, token);
            if (plan.Predicate is null) continue;
            var rows = db.Set<Dictionary<string, object?>>(db.ResolveModelKey(node.Model))
                .AsNoTracking().Where(plan.Predicate).Select(row => EF.Property<string>(row, node.IdField));
            using var scoped = rows.CreateDbCommand();
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DbParameter parameter in scoped.Parameters)
            {
                var name = "@treeScope" + parameters.Count;
                names.Add(parameter.ParameterName, name);
                var copy = (DbParameter)((ICloneable)parameter).Clone();
                copy.ParameterName = name;
                parameters.Add(copy);
            }
            filters.Add(node.ResourceKey, RenameParameters(scoped.CommandText.Trim().TrimEnd(';'), names));
        }
        return (filters, parameters);
    }

    // 仅替换 SQL 参数 token；规则值中的引号文本、转义标识符和注释不能被误写。
    internal static string RenameParameters(string sql, IReadOnlyDictionary<string, string> names)
    {
        var result = new StringBuilder(sql.Length);
        for (var index = 0; index < sql.Length;)
        {
            var current = sql[index];
            if (current is '\'' or '[' or '"' || current == '-' && index + 1 < sql.Length && sql[index + 1] == '-'
                || current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                var end = current == '[' ? ']' : current == '"' ? '"' : '\'';
                var line = current == '-';
                var block = current == '/';
                result.Append(current);
                index++;
                if (line || block) { result.Append(sql[index++]); }
                while (index < sql.Length)
                {
                    var value = sql[index++];
                    result.Append(value);
                    if (line && value == '\n') break;
                    if (block && value == '*' && index < sql.Length && sql[index] == '/') { result.Append(sql[index++]); break; }
                    if (!line && !block && value == end)
                    {
                        if (index < sql.Length && sql[index] == end) { result.Append(sql[index++]); continue; }
                        break;
                    }
                }
                continue;
            }
            if (current == '@')
            {
                var start = index++;
                while (index < sql.Length && (char.IsLetterOrDigit(sql[index]) || sql[index] == '_')) index++;
                var name = sql[start..index];
                result.Append(names.TryGetValue(name, out var renamed) ? renamed : name);
                continue;
            }
            result.Append(current);
            index++;
        }
        return result.ToString();
    }
}

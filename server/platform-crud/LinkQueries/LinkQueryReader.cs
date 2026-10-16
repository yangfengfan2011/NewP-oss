using System.Data.Common;
using System.Linq.Expressions;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Planning;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using ICP.Platform.Presentation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace ICP.Platform.Crud.LinkQueries;

/// <summary>统一 Link 规划：真实读取范围与公共条件先下推，各类型 UNION ALL 后统一计数、排序、分页。</summary>
public sealed class LinkQueryReader(EfPlatformDbContext db, LinkQueryCatalog catalog,
    IEnumerable<ILinkQuerySourcePolicy> policies,
    DisplayValueProjection displays, ILogger<LinkQueryReader> logger, LinkEndpointReadScopes endpointScopes, LinkSourceReadScopes sourceScopes, IRuntimeTextCatalog? texts = null, IRequestPresentationContextAccessor? presentation = null)
{
    /// <summary>公开查询只返回一页关系；关系身份与端点身份始终独立。</summary>
    public Task<LinkQueryResult> QueryAsync(string resourceKey, LinkQueryRequest request, CancellationToken token = default) =>
        ReadAsync(resourceKey, request, false, false, token);

    /// <summary>引用写入复用正式端点目录和读取范围；没有任何 Link 的独立对象也能重新准入。</summary>
    public async Task<bool> IsEndpointReadableAsync(string resourceKey, LinkObjectReference target, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(target.Id)) throw new LinkQueryException(400, "LinkQuery.InvalidType", "target");
        var scope = await ReadEndpointScopeAsync(resourceKey, target.ModelKey, "linkTarget", token);
        var parameters = scope.Parameters.ToList();
        var id = Parameter(target.Id, parameters);
        return await ExecuteAsync(scope.Preparation + "\nSELECT 1 WHERE " + id + " IN (" + scope.IdsQuery + ")",
            parameters, async reader => await reader.ReadAsync(token), token);
    }

    /// <summary>可信模块复用正式端点读取交集；范围名由代码分配，不接受请求 SQL 或排序表达式。</summary>
    public Task<LinkQuerySqlScope> ReadEndpointScopeAsync(string resourceKey, string modelKey, string scopeName, CancellationToken token = default) =>
        endpointScopes.ReadAsync(resourceKey, modelKey, scopeName, token);

    /// <summary>受信任树适配读取一层全部节点；不能由 HTTP 请求关闭分页，也不预取整树。</summary>
    public Task<LinkQueryResult> ReadDirectLayerAsync(string resourceKey, LinkQueryRequest request, CancellationToken token = default) =>
        ReadAsync(resourceKey, request, true, true, token);

    /// <summary>hasChildren 与直属层共用相同类型、授权和条件规划，只取存在性。</summary>
    public async Task<bool> HasChildrenAsync(string resourceKey, LinkQueryRequest request, CancellationToken token = default) =>
        (await ReadAsync(resourceKey, request with { Query = request.Query with { Page = 1, PageSize = 1, Fields = ["LID"] } }, false, false, token)).Total > 0;

    // 同一计划用于标准列表和受信任直属层；权限故障、不可翻译条件及损坏存储类型均失败关闭。
    private async Task<LinkQueryResult> ReadAsync(string resourceKey, LinkQueryRequest request,
        bool completeLayer, bool includeChildren, CancellationToken token)
    {
        var definition = catalog.Get(resourceKey);
        if (request.Direction is not ("forward" or "reverse")) throw new LinkQueryException(400, "LinkQuery.InvalidDirection", "direction");
        var forward = request.Direction == "forward";
        if (!definition.Endpoints.TryGetValue(request.Anchor.ModelKey, out var anchor) || string.IsNullOrWhiteSpace(request.Anchor.Id))
            throw new LinkQueryException(400, "LinkQuery.InvalidAnchor", "anchor");
        var combinations = definition.Model.LinkedTypes.Where(pair => (forward ? pair.Parent : pair.Child) == request.Anchor.ModelKey).ToArray();
        if (combinations.Length == 0) throw new LinkQueryException(400, "LinkQuery.InvalidAnchor", "anchor");
        var allowedTypes = combinations.Select(pair => forward ? pair.Child : pair.Parent).Distinct(StringComparer.Ordinal).ToArray();
        if (request.Types?.Any(type => !allowedTypes.Contains(type, StringComparer.Ordinal)) == true)
            throw new LinkQueryException(400, "LinkQuery.InvalidType", "types");
        if (request.Types is { Count: > 0 }) allowedTypes = allowedTypes.Where(type => request.Types.Contains(type, StringComparer.Ordinal)).ToArray();
        if (definition.Metadata.PolicyKey is { } policyKey)
            await GetPolicy(policyKey).ValidateAsync(definition, request, token);
        var parameters = new List<DbParameter>();
        var preparedEndpoints = new Dictionary<string, LinkEndpointReadScopes.PreparedEndpoint>(StringComparer.Ordinal);
        foreach (var endpoint in definition.Endpoints.Values)
        {
            preparedEndpoints.Add(endpoint.Model.LogicalKey,
                await endpointScopes.PrepareAsync(endpoint, "linkEndpoint" + preparedEndpoints.Count, parameters, token));
        }
        var anchorValue = Parameter(request.Anchor.Id, parameters);
        var anchorAvailableSql = await preparedEndpoints[anchor.Model.LogicalKey].ReadAnchorAsync(request.Anchor.Id, anchorValue, parameters, token);
        var columns = definition.Metadata.Columns;
        var queryDescriptor = new PlatformQueryDescriptor
        {
            ResourceKey = resourceKey, ModelName = definition.Model.LogicalKey, IdField = "LID",
            AllowedModelFields = columns.Select(c => c.Field).Prepend("LID").ToArray(),
            DefaultFields = columns.Select(c => c.Field).Prepend("LID").Concat(definition.DisplayValueFields.Select(field => field.Field)).ToArray(),
            SortableFields = columns.Where(c => c.Sortable).Select(c => c.Field).ToArray(),
            DefaultSortField = definition.Metadata.DefaultSortField, DefaultSortDirection = definition.Metadata.DefaultSortDirection,
            DisplayValueFields = definition.DisplayValueFields
        };
        var conditions = BuildConditions(definition, request.Query);
        var requested = request.Query.Fields;
        // canonical raw/display 必须成对，沿用标准列表边界；非请求依赖只参与 SQL，不进入响应。
        if (requested is { Count: > 0 }) requested = requested.SelectMany(field => queryDescriptor.DisplayValueFields
            .Where(d => d.Field == field || d.SourceField == field).SelectMany(d => new[] { d.SourceField, d.Field }).DefaultIfEmpty(field)).Distinct().ToArray();
        var platformRequest = new PlatformQueryRequest(requested, request.Query.Page, request.Query.PageSize, request.Query.SortBy, request.Query.SortDirection, conditions);
        var fieldPlan = PlatformQueryFieldPlanner.Build(queryDescriptor, platformRequest);
        var (sortField, sortDirection) = PlatformQueryFieldPlanner.ResolveSort(queryDescriptor, platformRequest);
        var sourcePredicate = definition.Metadata.PolicyKey is { } sourcePolicy ? await GetPolicy(sourcePolicy).ReadPredicateAsync(token) : null;
        var sourceRows = await sourceScopes.RowsAsync(definition, sourcePredicate, token);
        var sourceSql = SqlFor(sourceRows.Select(row => EF.Property<string>(row, "LID")), parameters);
        var linkEntity = Entity(definition.Model.LogicalKey);
        var linkTable = Table(linkEntity);
        var parent = definition.Metadata.Parent; var child = definition.Metadata.Child;
        var anchorEnd = forward ? parent : child;
        var anchorCondition = EndType(anchorEnd, "l", linkEntity) + "=" + Parameter(request.Anchor.ModelKey, parameters)
            + " AND " + Column(linkEntity, "l", anchorEnd.ReferenceField) + "=" + Parameter(request.Anchor.Id, parameters);
        // 反向类型损坏时不能先按该类型隐藏诊断；同 ID 的其他合法模型仍由完整 validPairs 排除。
        var diagnosticAnchorCondition = forward ? anchorCondition : Column(linkEntity, "l", anchorEnd.ReferenceField) + "=" + anchorValue;
        var sourceCondition = Column(linkEntity, "l", "LID") + " IN (" + sourceSql + ")";
        var validPairs = string.Join(" OR ", definition.Model.LinkedTypes.Select(pair => "(" + EndType(parent, "l", linkEntity) + "=" + Literal(pair.Parent)
            + " AND " + EndType(child, "l", linkEntity) + "=" + Literal(pair.Child) + ")"));
        var branches = new List<string>();
        foreach (var type in allowedTypes)
        {
            var endpoint = definition.Endpoints[type];
            var entity = Entity(type);
            // 共用单模型查询的操作符、分组和参数翻译，仅把公共字段解析为已验证端点字段或显式常量。
            var endpointSql = await preparedEndpoints[type].ReadIdsAsync(parameters, token,
                rows => EfPlatformQueryExecutor.ApplyConditions(rows, queryDescriptor, entity, conditions, (row, condition) =>
            {
                var mapping = columns.Single(c => c.Field == condition.Field).Mappings[type];
                if (mapping is null) return Expression.Constant(condition.Operator == "isNull");
                if (mapping == "$modelKey") return Expression.Constant(condition.Operator switch
                {
                    "=" => type == Convert.ToString(condition.Value), "contains" => type.Contains(Convert.ToString(condition.Value)!, StringComparison.Ordinal), "isNull" => false,
                    _ => throw new LinkQueryException(400, "LinkQuery.InvalidOperator", condition.Field)
                });
                return EfPlatformQueryExecutor.BuildConditionExpression(queryDescriptor, entity, row, condition with { Field = mapping });
            }));
            var associatedEnd = forward ? child : parent;
            var projections = fieldPlan.DbSelectFields.Where(f => f != "LID").Select(field =>
            {
                var mapping = columns.Single(c => c.Field == field).Mappings[type];
                return (mapping is null ? "CAST(NULL AS nvarchar(256))" : mapping == "$modelKey" ? Literal(type) : Column(entity, "e", mapping)) + " AS " + Quote(field);
            });
            // 树只读取同类型直属层；子存在性复用相同 source/endpoint 读取集合，不逐节点发请求。
            var childrenSql = includeChildren ? "CASE WHEN EXISTS(SELECT 1 FROM " + linkTable + " nextLink WHERE "
                + Column(linkEntity, "nextLink", parent.ReferenceField) + "=" + Column(entity, "e", endpoint.IdField)
                + " AND " + EndType(parent, "nextLink", linkEntity) + "=" + Literal(type)
                + " AND " + EndType(child, "nextLink", linkEntity) + "=" + Literal(type)
                + " AND " + Column(linkEntity, "nextLink", "LID") + " IN (" + sourceSql + ")"
                + " AND " + Column(linkEntity, "nextLink", child.ReferenceField) + " IN (" + endpointSql + ")) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END" : "CAST(0 AS bit)";
            branches.Add("SELECT " + Column(linkEntity, "l", "LID") + " AS LID," + EndType(parent, "l", linkEntity) + " AS ParentModelKey,"
                + Column(linkEntity, "l", parent.ReferenceField) + " AS ParentId," + EndType(child, "l", linkEntity) + " AS ChildModelKey,"
                + Column(linkEntity, "l", child.ReferenceField) + " AS ChildId," + Literal(type) + " AS AssociatedModelKey,"
                + Column(entity, "e", endpoint.IdField) + " AS AssociatedId," + childrenSql + " AS HasChildren" + (projections.Any() ? "," + string.Join(",", projections) : "")
                + " FROM " + linkTable + " l JOIN " + Table(entity) + " e ON " + Column(entity, "e", endpoint.IdField) + "=" + Column(linkEntity, "l", associatedEnd.ReferenceField)
                + " WHERE " + anchorCondition + " AND " + sourceCondition + " AND " + EndType(associatedEnd, "l", linkEntity) + "=" + Literal(type)
                + " AND " + Column(entity, "e", endpoint.IdField) + " IN (" + endpointSql + ")");
        }
        var page = request.Query.Page <= 0 ? 1 : request.Query.Page;
        var pageSize = request.Query.PageSize <= 0 ? 20 : request.Query.PageSize;
        if (page > int.MaxValue / pageSize) throw new LinkQueryException(400, "LinkQuery.InvalidPage", "page");
        var union = "WITH Relations AS (" + string.Join(" UNION ALL ", branches) + ") ";
        var associatedEndForDiagnostics = forward ? child : parent;
        var eligibleSourceCount = "SELECT COUNT(*) FROM " + linkTable + " l WHERE " + anchorCondition + " AND " + sourceCondition
            + " AND " + EndType(associatedEndForDiagnostics, "l", linkEntity) + " IN (" + string.Join(",", allowedTypes.Select(Literal)) + ");";
        var sql = string.Join("\n", preparedEndpoints.Values.Select(endpoint => endpoint.Preparation))
            + "\n" + anchorAvailableSql + "; SELECT TOP(1) 1 FROM " + linkTable + " l WHERE " + diagnosticAnchorCondition + " AND " + sourceCondition + " AND NOT(" + validPairs + "); "
            + union + "SELECT COUNT(*) FROM Relations; " + union + "SELECT * FROM Relations ORDER BY " + Quote(sortField!) + " " + sortDirection
            + ",AssociatedModelKey,AssociatedId,LID" + (completeLayer ? ";" : " OFFSET " + Parameter((page - 1) * pageSize, parameters) + " ROWS FETCH NEXT " + Parameter(pageSize, parameters) + " ROWS ONLY;") + eligibleSourceCount;
        var result = await ExecuteAsync(sql, parameters, async reader =>
        {
            if (!await reader.ReadAsync(token)) throw new LinkQueryException(404, "LinkQuery.AnchorUnavailable");
            await reader.NextResultAsync(token);
            if (await reader.ReadAsync(token))
            {
                logger.LogWarning("LINK_QUERY_STORED_TYPE_INVALID: Resource={Resource}", resourceKey);
                throw new InvalidOperationException("LINK_QUERY_STORED_TYPE_INVALID");
            }
            await reader.NextResultAsync(token); await reader.ReadAsync(token); var total = reader.GetInt32(0);
            await reader.NextResultAsync(token); var items = new List<LinkQueryRow>();
            while (await reader.ReadAsync(token))
            {
                var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var field in fieldPlan.DbSelectFields)
                {
                    var index = reader.GetOrdinal(field); var value = reader.IsDBNull(index) ? null : reader.GetValue(index);
                    // SqlClient 的 date/datetime2 都可能返回 DateTime；按公共列的 raw 语义恢复 DateOnly 或 UTC。
                    fields[field] = (value, columns.SingleOrDefault(column => column.Field == field)?.ValueType) switch
                    {
                        (DateTime date, "CalendarDate") => DateOnly.FromDateTime(date),
                        (DateTime instant, "Instant") => DateTime.SpecifyKind(instant, DateTimeKind.Utc),
                        _ => value
                    };
                }
                var parentRef = new LinkObjectReference(reader.GetString(1), reader.GetString(2));
                var childRef = new LinkObjectReference(reader.GetString(3), reader.GetString(4));
                items.Add(new(resourceKey, reader.GetString(0), parentRef, childRef, forward ? childRef : parentRef, fields, reader.GetBoolean(reader.GetOrdinal("HasChildren"))));
            }
            await reader.NextResultAsync(token); await reader.ReadAsync(token);
            // 内部仅记录聚合排除量，覆盖缺失目标、对象范围及筛选；不输出被隐藏的主键或字段。
            logger.LogDebug("LINK_QUERY_EXCLUDED_RELATIONS: Resource={Resource} ExcludedByTargetScopeOrFilter={Excluded}", resourceKey, reader.GetInt32(0) - total);
            return new LinkQueryResult(items, total, page, completeLayer ? items.Count : pageSize, sortField, sortDirection);
        }, token);
        var displayed = await DisplayAsync(definition, result.Items, fieldPlan.ResponseFields, token);
        logger.LogDebug("LINK_QUERY_READ: Resource={Resource} CompleteLayer={CompleteLayer} Total={Total} Materialized={Materialized} DirectReaderCommands=1; excludes source policy, authorization and display", resourceKey, completeLayer, result.Total, result.Items.Count);
        return result with { Items = displayed };
    }

    // 公共筛选值沿标准条件结构；未声明列、操作或值类型拒绝而不是忽略。
    private static IReadOnlyList<PlatformQueryCondition> BuildConditions(LinkQueryDefinition definition, StandardCrudListQuery query)
    {
        var conditions = new List<PlatformQueryCondition>();
        foreach (var pair in query.Filters ?? new Dictionary<string, string?>())
        {
            if (pair.Key == "keyword")
            {
                if (string.IsNullOrWhiteSpace(pair.Value)) continue;
                var searchable = definition.Metadata.Columns.Where(c => c.Operators.Contains("contains", StringComparer.Ordinal)).ToArray();
                if (searchable.Length == 0) throw new LinkQueryException(400, "LinkQuery.KeywordUnsupported", "keyword");
                conditions.AddRange(searchable.Select(c => new PlatformQueryCondition(c.Field, "contains", pair.Value, "keyword", "or")));
                continue;
            }
            var parts = pair.Key.Split(':', 2); var field = parts[0]; var op = parts.Length == 1 ? "=" : parts[1];
            var column = definition.Metadata.Columns.SingleOrDefault(c => c.Field == field)
                ?? throw new LinkQueryException(400, "LinkQuery.UnknownConditionField", field);
            if (!column.Operators.Contains(op, StringComparer.Ordinal) || op is "=" or "contains" && pair.Value is null)
                throw new LinkQueryException(400, "LinkQuery.InvalidCondition", field);
            conditions.Add(new(field, op, pair.Value));
        }
        return conditions;
    }

    // 页内按真实模型批量使用既有 Model display planner 与 resolver，再按请求白名单投影。
    private async Task<IReadOnlyList<LinkQueryRow>> DisplayAsync(LinkQueryDefinition definition, IReadOnlyList<LinkQueryRow> rows,
        IReadOnlyList<string> responseFields, CancellationToken token)
    {
        var result = new List<LinkQueryRow>();
        foreach (var group in rows.GroupBy(r => r.AssociatedObject.ModelKey))
        {
            var endpoint = definition.Endpoints[group.Key];
            var mappings = definition.Metadata.Columns.Where(c => c.Mappings[group.Key] is not null and not "$modelKey").ToArray();
            var groupRows = group.ToArray();
            var values = groupRows.Select(row => (IReadOnlyDictionary<string, object?>)mappings.Where(c => row.Fields.ContainsKey(c.Field))
                .ToDictionary(c => c.Mappings[group.Key]!, c => row.Fields[c.Field], StringComparer.Ordinal)).ToArray();
            var displayFields = endpoint.DisplayProjectionPlan.Fields.Select(ModelDisplayValueFieldProjector.ProjectField)
                .Where(d => mappings.Any(c => c.Mappings[group.Key] == d.SourceField && responseFields.Contains(c.Field + "_DisplayValue", StringComparer.Ordinal))).ToArray();
            var projected = await displays.ApplyDisplayValuesAsync(endpoint.ResourceKey, endpoint.DisplayProjectionPlan, values, displayFields, token);
            for (var index = 0; index < groupRows.Length; index++)
            {
                var output = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var field in responseFields)
                {
                    if (field.EndsWith("_DisplayValue", StringComparison.Ordinal))
                    {
                        var column = definition.Metadata.Columns.Single(c => c.Field + "_DisplayValue" == field);
                        var mapping = column.Mappings[group.Key]!;
                        output[field] = mapping == "$modelKey" ? (texts ?? throw new InvalidOperationException("LINK_QUERY_TYPE_TEXT_CATALOG_MISSING")).GetText(endpoint.TitleKey!, presentation?.Current?.Locale)
                            : mapping is null ? null : projected[index].GetValueOrDefault(mapping + "_DisplayValue");
                    }
                    else output[field] = groupRows[index].Fields.GetValueOrDefault(field);
                }
                result.Add(groupRows[index] with { Fields = output });
            }
        }
        var byId = result.ToDictionary(r => r.LinkId, StringComparer.Ordinal);
        return rows.Select(r => byId[r.LinkId]).ToArray();
    }

    // EF 输出参数通过已存在的 SQL token 重命名器隔离，保留数据库类型及参数化值。
    private static string SqlFor(IQueryable<string> rows, List<DbParameter> parameters)
    {
        using var command = rows.CreateDbCommand(); var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DbParameter parameter in command.Parameters)
        {
            var name = "@linkScope" + parameters.Count; names.Add(parameter.ParameterName, name);
            var copy = (DbParameter)((ICloneable)parameter).Clone(); copy.ParameterName = name; parameters.Add(copy);
        }
        return RelationTreeReader.RenameParameters(command.CommandText.Trim().TrimEnd(';'), names);
    }

    // 保持共享事务和连接所有权，按结果集流式读取本页或直属层。
    private async Task<T> ExecuteAsync<T>(string sql, List<DbParameter> parameters, Func<DbDataReader, Task<T>> read, CancellationToken token)
    {
        var connection = db.Database.GetDbConnection(); var close = connection.State != System.Data.ConnectionState.Open;
        if (close) await connection.OpenAsync(token);
        try
        {
            await using var command = connection.CreateCommand(); command.CommandText = sql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var parameter in parameters) command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(token); return await read(reader);
        }
        finally { if (close) await connection.CloseAsync(); }
    }

    // 已声明策略缺失属于配置故障，不允许无条件读取。
    private ILinkQuerySourcePolicy GetPolicy(string key) => policies.SingleOrDefault(p => p.Key == key) ?? throw new InvalidOperationException("LINK_QUERY_SOURCE_POLICY_MISSING");
    // 只从已编译 EF 模型取得物理标识。
    private IEntityType Entity(string model) => db.Model.FindEntityType(model) ?? throw new InvalidOperationException("LINK_QUERY_ENTITY_MISSING");
    // 方括号转义保护可信物理名，值仍参数化。
    private static string Quote(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
    // 静态规范模型身份来自目录，转义字面值不接受客户端 SQL。
    private static string Literal(string value) => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    // 表名和 schema 只来自 EF 模型。
    private static string Table(IEntityType entity) => Quote(entity.GetSchema() ?? "dbo") + "." + Quote(entity.GetTableName()!);
    // 列名必须属于指定的已验证实体。
    private static string Column(IEntityType entity, string alias, string field) => alias + "." + Quote(entity.FindProperty(field)!.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
    // 两端各自独立使用固定类型或普通类型判别列。
    private static string EndType(LinkQueryEndMetadata end, string alias, IEntityType entity) => end.FixedModelKey is { } type ? Literal(type) : Column(entity, alias, end.TypeField!);
    // 参数类型由 provider 保留；请求数据永不拼接为表达式。
    private string Parameter(object value, List<DbParameter> parameters)
    {
        using var command = db.Database.GetDbConnection().CreateCommand();
        var parameter = command.CreateParameter(); parameter.ParameterName = "@link" + parameters.Count;
        parameter.Value = value; parameters.Add(parameter); return parameter.ParameterName;
    }
}

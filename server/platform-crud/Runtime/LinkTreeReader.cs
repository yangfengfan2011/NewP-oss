using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace ICP.Platform.Crud.Runtime;

/// <summary>冻结的 Link 树目录；未知 View 不允许回退为任意 Link 查询。</summary>
public sealed class LinkTreeCatalog(IReadOnlyList<LinkTreeDefinition> definitions)
{
    public IReadOnlyList<LinkTreeDefinition> Definitions => definitions;
    /// <summary>只解析活动快照签发的树配置。</summary>
    public LinkTreeDefinition Get(string viewKey) => definitions.SingleOrDefault(definition => definition.ViewKey == viewKey)
        ?? throw new LinkQueryException(400, "Platform.TreeUnknownSource");
}

/// <summary>平台拥有 Link 出现树及精确路径；模块只提供业务根。</summary>
public sealed partial class LinkTreeReader(EfPlatformDbContext db, LinkTreeCatalog trees, LinkQueryCatalog links,
    LinkEndpointReadScopes linkReads, LinkSourceReadScopes sourceReads, IEnumerable<ILinkTreeRootPolicy> rootPolicies)
{
    private sealed record Plan(LinkTreeDefinition Definition, LinkQueryDefinition Link, LinkQueryEndpoint Endpoint,
        IEntityType NodeEntity, IEntityType LinkEntity, string LabelField, ILinkTreeRootPolicy Policy);
    private sealed record Root(string Key, string Id, string Label, string? LabelKey, bool HasChildren = false);
    private sealed record StoredLink(string Id, string Parent, string Child, string ParentType, string ChildType);

    /// <summary>启动前核对真实 Link、端点、公共显示列、次序类型和唯一可信根策略。</summary>
    public void Validate(LinkTreeDefinition definition) => Compile(definition);

    /// <summary>调用方的范围只能等于可信策略的既定范围，不从请求接受主体。</summary>
    public string ScopeKind(LinkTreeDefinition definition) => Compile(definition).Policy.ScopeKind;

    // 物理映射与可用类型只来自正式目录，模块不能通过树配置越过拥有边界。
    private Plan Compile(LinkTreeDefinition definition)
    {
        var source = definition.Source; var link = links.Get(source.ResourceKey);
        sourceReads.ValidateTreeSupport(link);
        var policy = rootPolicies.Where(policy => policy.Key == source.RootPolicyKey).ToArray();
        if (link.Model.OwnerModuleId != definition.ModuleId || !link.Model.LinkedTypes.Any(pair => pair.Parent == source.ModelKey && pair.Child == source.ModelKey)
            || !link.Endpoints.TryGetValue(source.ModelKey, out var endpoint) || policy.Length != 1 || policy[0].ScopeKind is not ("global" or "currentUser"))
            throw new InvalidOperationException("LINK_TREE_SOURCE_INVALID");
        var label = link.Metadata.Columns.SingleOrDefault(column => column.Field == source.LabelColumn);
        if (label?.ValueType != "Text" || !label.Mappings.TryGetValue(source.ModelKey, out var field) || field is null || field.StartsWith('$'))
            throw new InvalidOperationException("LINK_TREE_LABEL_INVALID");
        var entity = db.Model.FindEntityType(source.ModelKey)!; var relation = db.Model.FindEntityType(link.Model.LogicalKey)!;
        if (entity?.FindProperty(field)?.ClrType != typeof(string) || entity.FindProperty(endpoint.IdField)?.ClrType != typeof(string)
            || relation?.FindProperty(source.OrderField) is not { ClrType: var orderType } || (Nullable.GetUnderlyingType(orderType) ?? orderType) != typeof(long)) throw new InvalidOperationException("LINK_TREE_FIELD_INVALID");
        return new(definition, link, endpoint, entity, relation, field, policy[0]);
    }

    /// <summary>根读取没有业务写入；策略根与正式端点读取范围共同决定可见根。</summary>
    public async Task<IReadOnlyList<LinkTreeNode>> RootsAsync(LinkTreeDefinition definition, CancellationToken token)
    {
        var plan = Compile(definition); var roots = await ReadRootsAsync(plan, null, token);
        var result = new List<LinkTreeNode>();
        foreach (var root in roots)
        {
            var position = new LinkTreePosition(root.Key, root.Id, []);
            result.Add(Node(plan, root, root.Id, root.Label, position, root.HasChildren, true));
        }
        return result;
    }

    /// <summary>只重验提交的完整链，按路径长度读取，不调用搜索或枚举替代路径。</summary>
    public Task<IReadOnlyList<LinkTreeNode>> PathAsync(string viewKey, LinkTreePosition position, CancellationToken token) =>
        PathAsync(trees.Get(viewKey), position, token);

    /// <summary>空间根、每条关系衔接及每层读取共同裁决，失效不能改选同实体其他链。</summary>
    public async Task<IReadOnlyList<LinkTreeNode>> PathAsync(LinkTreeDefinition definition, LinkTreePosition position, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(position.RootKey) || string.IsNullOrWhiteSpace(position.RootObjectId) || position.LinkIds is null
            || position.LinkIds.Any(string.IsNullOrWhiteSpace) || position.LinkIds.Distinct(StringComparer.Ordinal).Count() != position.LinkIds.Count)
            throw new LinkQueryException(400, "Platform.TreeInvalidPosition");
        var plan = Compile(definition);
        // SQL 等值可能接受大小写或尾空格别名；精确位置仍须逐字匹配可信根身份。
        var root = (await ReadRootsAsync(plan, position, token)).SingleOrDefault(candidate =>
            string.Equals(candidate.Key, position.RootKey, StringComparison.Ordinal)
            && string.Equals(candidate.Id, position.RootObjectId, StringComparison.Ordinal))
            ?? throw new LinkQueryException(404, "Platform.TreePositionUnavailable");
        var relation = plan.Link.Metadata;
        var eligible = await sourceReads.TreeRowsAsync(plan.Link, token);
        var rows = position.LinkIds.Count == 0 ? [] : await eligible
            .Where(row => position.LinkIds.Contains(EF.Property<string>(row, "LID")))
            .Select(row => new StoredLink(EF.Property<string>(row, "LID"), EF.Property<string>(row, relation.Parent.ReferenceField),
                EF.Property<string>(row, relation.Child.ReferenceField), relation.Parent.FixedModelKey ?? EF.Property<string>(row, relation.Parent.TypeField ?? "LID"),
                relation.Child.FixedModelKey ?? EF.Property<string>(row, relation.Child.TypeField ?? "LID"))).ToArrayAsync(token);
        var byId = rows.ToDictionary(row => row.Id, StringComparer.Ordinal); var ids = new List<string> { root.Id };
        foreach (var lid in position.LinkIds)
        {
            if (!byId.TryGetValue(lid, out var edge) || edge.Parent != ids[^1] || edge.ParentType != definition.Source.ModelKey || edge.ChildType != definition.Source.ModelKey)
                throw new LinkQueryException(404, "Platform.TreePositionUnavailable");
            if (ids.Contains(edge.Child, StringComparer.Ordinal)) throw new LinkQueryException(400, "Platform.TreeInvalidPosition");
            ids.Add(edge.Child);
        }
        var scope = await linkReads.ReadAsync(definition.Source.ResourceKey, definition.Source.ModelKey, "treeExact", token);
        var source = await sourceReads.TreeSqlAsync(plan.Link, "treeExactSource", token);
        var parameters = scope.Parameters.Concat(source.Parameters).ToList(); parameters.Add(new SqlParameter("@treeIds", JsonSerializer.Serialize(ids)));
        parameters.Add(new SqlParameter("@exactModel", definition.Source.ModelKey));
        var sql = scope.Preparation + source.Preparation + $"""
            SELECT n.{Column(plan.NodeEntity, plan.Endpoint.IdField)},n.{Column(plan.NodeEntity, plan.LabelField)},
                CAST(CASE WHEN EXISTS(SELECT 1 FROM {Table(plan.LinkEntity)} c WHERE c.{Column(plan.LinkEntity, relation.Parent.ReferenceField)}=n.{Column(plan.NodeEntity, plan.Endpoint.IdField)}
                    AND {EndType(plan, "c", true)}=@exactModel AND {EndType(plan, "c", false)}=@exactModel AND c.{Column(plan.LinkEntity, "LID")} IN ({source.IdsQuery})
                    AND c.{Column(plan.LinkEntity, relation.Child.ReferenceField)} IN ({scope.IdsQuery})) THEN 1 ELSE 0 END AS bit)
                FROM {Table(plan.NodeEntity)} n WHERE n.{Column(plan.NodeEntity, plan.Endpoint.IdField)} IN ({scope.IdsQuery})
                AND n.{Column(plan.NodeEntity, plan.Endpoint.IdField)} IN (SELECT value FROM OPENJSON(@treeIds));
            """;
        var names = await ExecuteAsync(sql, parameters, async reader =>
        {
            var values = new Dictionary<string, (string Label, bool HasChildren)>(StringComparer.Ordinal);
            while (await reader.ReadAsync(token)) values.Add(reader.GetString(0), (reader.GetString(1), reader.GetBoolean(2)));
            return values;
        }, token);
        if (names.Count != ids.Count) throw new LinkQueryException(404, "Platform.TreePositionUnavailable");
        return ids.Select((id, index) => Node(plan, root, id, index == 0 ? root.Label : names[id].Label,
            position with { LinkIds = position.LinkIds.Take(index).ToArray() }, names[id].HasChildren, index == 0)).ToArray();
    }

    /// <summary>直属读取复用精确链校验与数据库同级次序，右侧内容列表排序不参与。</summary>
    public async Task<IReadOnlyList<LinkTreeNode>> ChildrenAsync(LinkTreeDefinition definition, LinkTreePosition position, CancellationToken token)
    {
        var path = await PathAsync(definition, position, token); var plan = Compile(definition);
        var parent = (string)path[^1].Context["id"]!;
        var root = new Root(position.RootKey, position.RootObjectId, path[0].Label, path[0].LabelKey);
        var scope = await linkReads.ReadAsync(definition.Source.ResourceKey, definition.Source.ModelKey, "treeLayer", token);
        var source = await sourceReads.TreeSqlAsync(plan.Link, "treeLayerSource", token);
        var parameters = scope.Parameters.Concat(source.Parameters).ToList(); parameters.Add(new SqlParameter("@treeParent", parent));
        var link = plan.Link.Metadata;
        var sql = scope.Preparation + source.Preparation + $"""
            SELECT l.{Column(plan.LinkEntity, "LID")},n.{Column(plan.NodeEntity, plan.Endpoint.IdField)},n.{Column(plan.NodeEntity, plan.LabelField)},
                l.{Column(plan.LinkEntity, definition.Source.OrderField)},CAST(CASE WHEN EXISTS(SELECT 1 FROM {Table(plan.LinkEntity)} c
                WHERE c.{Column(plan.LinkEntity, link.Parent.ReferenceField)}=n.{Column(plan.NodeEntity, plan.Endpoint.IdField)} AND {EndType(plan, "c", true)}=@treeModel AND {EndType(plan, "c", false)}=@treeModel
                AND c.{Column(plan.LinkEntity, "LID")} IN ({source.IdsQuery}) AND c.{Column(plan.LinkEntity, link.Child.ReferenceField)} IN ({scope.IdsQuery})) THEN 1 ELSE 0 END AS bit)
                FROM {Table(plan.LinkEntity)} l JOIN {Table(plan.NodeEntity)} n ON n.{Column(plan.NodeEntity, plan.Endpoint.IdField)}=l.{Column(plan.LinkEntity, link.Child.ReferenceField)}
                WHERE l.{Column(plan.LinkEntity, "LID")} IN ({source.IdsQuery}) AND l.{Column(plan.LinkEntity, link.Parent.ReferenceField)}=@treeParent AND {EndType(plan, "l", true)}=@treeModel AND {EndType(plan, "l", false)}=@treeModel
                AND n.{Column(plan.NodeEntity, plan.Endpoint.IdField)} IN ({scope.IdsQuery})
                ORDER BY l.{Column(plan.LinkEntity, definition.Source.OrderField)},l.{Column(plan.LinkEntity, "LID")};
            """;
        parameters.Add(new SqlParameter("@treeModel", definition.Source.ModelKey));
        return await ExecuteAsync(sql, parameters, async reader =>
        {
            var result = new List<LinkTreeNode>();
            while (await reader.ReadAsync(token))
            {
                if (reader.IsDBNull(3)) throw new LinkQueryException(409, "Platform.TreeGraphInvalid");
                result.Add(Node(plan, root, reader.GetString(1), reader.GetString(2), position with { LinkIds = [.. position.LinkIds, reader.GetString(0)] }, reader.GetBoolean(4), false));
            }
            return result;
        }, token);
    }

    /// <summary>模块兼容入口与正式平台 API 共用同一直属读取，位置仍须完整重验。</summary>
    public Task<IReadOnlyList<LinkTreeNode>> ChildrenAsync(string viewKey, LinkTreePosition position, CancellationToken token) =>
        ChildrenAsync(trees.Get(viewKey), position, token);

    // 只把匹配策略根及正式可读端点送入应用；精确恢复最多读取一根。
    private async Task<Root[]> ReadRootsAsync(Plan plan, LinkTreePosition? position, CancellationToken token)
    {
        var root = await plan.Policy.BuildAsync(plan.Definition, token);
        var scope = await linkReads.ReadAsync(plan.Definition.Source.ResourceKey, plan.Definition.Source.ModelKey, "treeRoots", token);
        var source = await sourceReads.TreeSqlAsync(plan.Link, "treeRootsSource", token);
        var parameters = root.Parameters.Concat(scope.Parameters).Concat(source.Parameters).ToList();
        parameters.Add(new SqlParameter("@rootTraversalModel", plan.Definition.Source.ModelKey));
        if (position is not null) { parameters.Add(new SqlParameter("@rootKey", position.RootKey)); parameters.Add(new SqlParameter("@rootId", position.RootObjectId)); }
        var filter = position is null ? "" : " AND r.RootKey=@rootKey AND r.RootObjectId=@rootId";
        var link = plan.Link.Metadata;
        var sql = root.Preparation + scope.Preparation + source.Preparation + $"""
            SELECT r.RootKey,r.RootObjectId,r.Label,r.LabelKey,CAST(CASE WHEN EXISTS(SELECT 1 FROM {Table(plan.LinkEntity)} c
                WHERE c.{Column(plan.LinkEntity, link.Parent.ReferenceField)}=r.RootObjectId AND {EndType(plan, "c", true)}=@rootTraversalModel
                AND {EndType(plan, "c", false)}=@rootTraversalModel AND c.{Column(plan.LinkEntity, "LID")} IN ({source.IdsQuery}) AND c.{Column(plan.LinkEntity, link.Child.ReferenceField)} IN ({scope.IdsQuery})) THEN 1 ELSE 0 END AS bit)
                FROM ({root.RootsQuery}) r WHERE r.RootObjectId IN ({scope.IdsQuery}){filter} ORDER BY r.RootOrder,r.RootKey,r.RootObjectId;
            """;
        return await ExecuteAsync(sql, parameters, async reader =>
        {
            var result = new List<Root>();
            while (await reader.ReadAsync(token)) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetBoolean(4)));
            return result.ToArray();
        }, token);
    }

    // 平台保留字段不能被根策略覆盖；同一业务实体的不同 LID 前缀各自生成节点。
    private static LinkTreeNode Node(Plan plan, Root root, string id, string label, LinkTreePosition position, bool hasChildren, bool isRoot)
    {
        var context = new Dictionary<string, object?>(plan.Policy.CreateContext(root.Key, root.Id), StringComparer.Ordinal);
        if (context.ContainsKey("id") || context.ContainsKey("linkIds") || context.ContainsKey("rootKey") || context.ContainsKey("rootObjectId")) throw new InvalidOperationException("LINK_TREE_ROOT_CONTEXT_RESERVED");
        context.Add("id", id); context.Add("linkIds", position.LinkIds);
        return new(LinkTreeNodeKey.Encode(position), plan.Definition.Source.NodeType, label, hasChildren, context, isRoot ? root.LabelKey : null,
            isRoot ? plan.Definition.Source.RootContextMenuKeys : null);
    }

    // 固定类型成为可信参数外的目录常量，动态类型列仍来自正式模型。
    private static string EndType(Plan plan, string alias, bool parent)
    {
        var end = parent ? plan.Link.Metadata.Parent : plan.Link.Metadata.Child;
        return end.FixedModelKey is { } model ? "N'" + model.Replace("'", "''", StringComparison.Ordinal) + "'" : alias + "." + Column(plan.LinkEntity, end.TypeField!);
    }

    // 请求事务、provider timeout、连接所有权与取消沿正式数据库调用保留。
    private async Task<T> ExecuteAsync<T>(string sql, IReadOnlyList<DbParameter> parameters, Func<DbDataReader, Task<T>> consume, CancellationToken token)
    {
        var connection = db.Database.GetDbConnection(); var close = connection.State != ConnectionState.Open;
        if (close) await connection.OpenAsync(token);
        try
        {
            await using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = db.Database.GetCommandTimeout() ?? 30;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var parameter in parameters) command.Parameters.Add((DbParameter)((ICloneable)parameter).Clone());
            await using var reader = await command.ExecuteReaderAsync(token); return await consume(reader);
        }
        catch (SqlException exception) when (exception.Number == 51001) { throw new LinkQueryException(409, "Platform.TreeGraphInvalid"); }
        catch (SqlException exception) when (exception.Number is -2 or 51002 or 8115 or 701 or 8645 or 8651 or 8657) { throw new LinkQueryException(503, "Platform.TreeSearchIncomplete"); }
        finally { if (close) await connection.CloseAsync(); }
    }

    // 物理名称不来自请求或配置字符串；所有字段已通过编译验证。
    private static string Table(IEntityType entity) => Quote(entity.GetSchema() ?? "dbo") + "." + Quote(entity.GetTableName()!);
    // 统一使用当前 EF 的真实列名。
    private static string Column(IEntityType entity, string field) => Quote(entity.FindProperty(field)!.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
    // 即使可信物理名称也转义 SQL 定界符。
    private static string Quote(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
}

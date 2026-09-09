using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text.Json;

namespace ICP.Platform.Crud.Runtime;

/// <summary>结果字段及其模型类型；数据库原生值在代码层恢复领域类型。</summary>
public sealed record RelationTreeContextColumn(string Column, Type ClrType);

/// <summary>配置编译结果；上下文在代码层组装，父关系条件直接比较物理外键。</summary>
public sealed record RelationTreeQuery(string Sql, Dictionary<string, object?> Parameters,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, RelationTreeContextColumn>> ContextColumns,
    string ParentMatch, string ResultColumns, IReadOnlySet<string> CompositeParentTypes);

/// <summary>从已注册资源和 EF 模型编译关系树 SQL；请求不能提供 SQL 标识符。</summary>
public sealed class RelationTreeSql(EfPlatformDbContext db, IStandardCrudResourceRegistry resources,
    IEnumerable<IStandardCrudQueryCustomizer> customizers)
{
    /// <summary>在数据库端构建统一节点投影，同时保留资源查询约束。</summary>
    /// <param name="definition">来自活动模块快照的静态树配置。</param>
    /// <param name="applyResourceConditions">启动静态验证时为 false；真实读取必须为 true，以保留请求主体的数据约束。</param>
    public RelationTreeQuery Build(RelationTreeDefinition definition, bool applyResourceConditions = true)
    {
        if (!db.Database.IsSqlServer()) throw new NotSupportedException("标准关系树当前使用 SQL Server 执行器。");
        var parameters = new Dictionary<string, object?>();
        var nodes = definition.Source.Nodes;
        var entities = nodes.ToDictionary(node => node.Key, ResolveEntity);
        var conditions = nodes.ToDictionary(node => node.Key, node => ReadConditions(node));
        var relations = nodes.SelectMany(node => node.Parents.Select((parent, index) =>
            (Child: node, Parent: nodes.Single(target => target.Key == parent.NodeType), Index: index,
                ForeignKey: ForeignKey(node, parent)))).ToArray();
        // 只投影上下文和复合关系需要的原生字段；同名同类型字段共用结果列。
        var fields = nodes.ToDictionary(node => node.Key, node => node.ContextFields.Values
            .Concat(relations.Where(relation => relation.Child.Key == node.Key).SelectMany(relation => relation.ForeignKey.Properties.Select(property => property.Name)))
            .Concat(relations.Where(relation => relation.Parent.Key == node.Key).SelectMany(relation => relation.ForeignKey.PrincipalKey.Properties.Select(property => property.Name)))
            .Distinct(StringComparer.Ordinal).ToArray());
        foreach (var node in nodes)
            foreach (var field in fields[node.Key]) ColumnFor(node, "e", field);
        var slots = nodes.SelectMany(node => fields[node.Key].Select(field => (Field: field, Type: entities[node.Key].FindProperty(field)!.GetColumnType()!)))
            .Distinct().Select((slot, index) => (slot.Field, slot.Type, Alias: "Value" + index)).ToArray();
        var contexts = nodes.ToDictionary(node => node.Key, node => (IReadOnlyDictionary<string, RelationTreeContextColumn>)node.ContextFields
            .ToDictionary(pair => pair.Key, pair => new RelationTreeContextColumn(Slot(node, pair.Value), entities[node.Key].FindProperty(pair.Value)!.ClrType)));
        var projections = nodes.Select(Project).ToArray();
        var parentMatch = relations.Length == 0 ? "1=0" : string.Join(" OR ", relations.Select(relation =>
            "(a.NodeType=" + Literal(relation.Child.Key) + " AND n.NodeType=" + Literal(relation.Parent.Key)
            + " AND " + string.Join(" AND ", relation.ForeignKey.Properties.Select((property, index) =>
                "a." + Slot(relation.Child, property.Name) + "=n." + Slot(relation.Parent, relation.ForeignKey.PrincipalKey.Properties[index].Name))
                .Concat(relation.Child.Parents.Take(relation.Index).Select(parent => "a." + Slot(relation.Child, parent.Field) + " IS NULL"))) + ")"));
        return new RelationTreeQuery("WITH Nodes AS (" + string.Join(" UNION ALL ", projections) + ")", parameters,
            contexts, parentMatch, "NodeKey,ParentKey,NodeType,DisplayName,Code,IsEnabled,HasChildren,AllowsRoot" + string.Concat(slots.Select(slot => "," + slot.Alias)),
            relations.Where(relation => relation.ForeignKey.Properties.Count > 1).Select(relation => relation.Parent.Key).ToHashSet(StringComparer.Ordinal));

        // 先确认资源和模型归属，再使用其 EF 元数据构造查询结构。
        IEntityType ResolveEntity(RelationTreeNodeSource node)
        {
            var resource = resources.FindByResourceKey(node.ResourceKey) ?? throw new InvalidOperationException($"Tree resource is not registered: {node.ResourceKey}");
            if (resource.ModuleId != definition.ModuleId || resource.ModelName != node.Model || resource.IdField != node.IdField)
                throw new InvalidOperationException($"Tree resource ownership/model mismatch: {node.Key}");
            return db.Model.FindEntityType(db.ResolveModelKey(definition.ModuleId + "/" + node.Model))
                ?? throw new InvalidOperationException($"Unknown tree model: {node.Model}");
        }

        // 通过实际 FK 确定完整匹配列，不执行逐行父表回查。
        IForeignKey ForeignKey(RelationTreeNodeSource node, RelationTreeParentSource parent) =>
            entities[node.Key].GetForeignKeys().SingleOrDefault(fk => fk.PrincipalEntityType == entities[parent.NodeType]
                && fk.Properties.Select((property, index) => property.Name == parent.Field && fk.PrincipalKey.Properties[index].Name == nodes.Single(target => target.Key == parent.NodeType).IdField).Any(matches => matches))
            ?? throw new InvalidOperationException($"Tree parent must reference an unambiguous declared foreign key: {node.Key}/{parent.Field}");

        // 同一字段的 SQL 类型由模型确定，避免 UNION 隐式转换业务值。
        string Slot(RelationTreeNodeSource node, string field) => slots.Single(slot => slot.Field == field && slot.Type == entities[node.Key].FindProperty(field)!.GetColumnType()).Alias;

        // 资源查询条件每个节点只构建一次，并复用于节点读取和子节点存在性判断。
        StandardCrudQueryCondition[] ReadConditions(RelationTreeNodeSource node)
        {
            var resource = resources.FindByResourceKey(node.ResourceKey) ?? throw new InvalidOperationException($"Tree resource is not registered: {node.ResourceKey}");
            return (applyResourceConditions ? customizers.SingleOrDefault(item => item.ResourceKey == node.ResourceKey)?.BuildConditions(resource,
                new StandardCrudListQuery(QueryScope: new Dictionary<string, string?> { ["kind"] = "global" })) ?? [] : [])
                // 与 Standard CRUD 一致：未赋值的可选查询不参与过滤；静态条件仍保持显式语义。
                .Where(condition => condition.Value is not null || condition.Operator is not ("=" or "contains"))
                .Concat(node.Filters.Select(filter => new StandardCrudQueryCondition(filter.Field, filter.Operator, Scalar(filter.Value)))).ToArray();
        }

        // 每次列引用都核对资源读取边界，包括 EXISTS 内的关系和过滤列。
        string ColumnFor(RelationTreeNodeSource node, string alias, string field)
        {
            var resource = resources.FindByResourceKey(node.ResourceKey)!;
            if (!resource.FieldSet.ListFields.Contains(field, StringComparer.Ordinal)) throw new InvalidOperationException($"Tree field is not readable: {node.ResourceKey}/{field}");
            var entity = entities[node.Key];
            var property = entity.FindProperty(field) ?? throw new InvalidOperationException($"Unknown tree field: {field}");
            return alias + "." + Quote(property.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!);
        }

        // 所有条件作用于具体表别名；值参数化，分组语义与资源查询保持一致。
        string Filter(RelationTreeNodeSource node, string alias)
        {
            // 单条条件只使用已验证的列和参数。
            string Condition(StandardCrudQueryCondition condition) => condition.Operator switch
            {
                "isNull" => ColumnFor(node, alias, condition.Field) + " IS NULL",
                "isNotNull" => ColumnFor(node, alias, condition.Field) + " IS NOT NULL",
                "=" or "!=" or ">" or ">=" or "<" or "<=" => ColumnFor(node, alias, condition.Field) + " " + condition.Operator + " " + Parameter(condition.Value),
                "contains" => "CHARINDEX(" + Parameter(condition.Value) + "," + ColumnFor(node, alias, condition.Field) + ")>0",
                _ => throw new InvalidOperationException($"Unsupported tree query condition: {condition.Operator}")
            };
            var filters = conditions[node.Key].Where(condition => condition.GroupKey is null).Select(Condition).ToList();
            foreach (var group in conditions[node.Key].Where(condition => condition.GroupKey is not null).GroupBy(condition => condition.GroupKey))
            {
                var operators = group.Select(condition => condition.GroupOperator ?? "and").Distinct().ToArray();
                if (operators.Length != 1 || operators[0] is not ("and" or "or")) throw new InvalidOperationException("Unsupported tree query condition group.");
                filters.Add("(" + string.Join(operators[0] == "or" ? " OR " : " AND ", group.Select(Condition)) + ")");
            }
            return filters.Count == 0 ? "1=1" : string.Join(" AND ", filters);
        }

        // 资源字段白名单和物理映射都来自服务器；所有值通过参数进入命令。
        string Project(RelationTreeNodeSource node)
        {
            var entity = entities[node.Key];
            if (entity.FindPrimaryKey()?.Properties is not { Count: 1 } primaryKey || primaryKey[0].Name != node.IdField)
                throw new InvalidOperationException($"Tree requires the model's single primary key: {node.Key}");
            if (node.EnabledField is not null && entity.FindProperty(node.EnabledField)?.ClrType != typeof(bool))
                throw new InvalidOperationException($"Tree enabled field must be boolean: {node.Key}");
            // 列映射同时验证当前资源的显式读取白名单。
            string Column(string field) => ColumnFor(node, "e", field);
            // 节点身份、搜索及顺序使用字符串字段，与公共树契约一致。
            foreach (var field in new[] { node.IdField, node.LabelField, node.CodeField, node.SortField })
                if (entity.FindProperty(field) is not { ClrType: var fieldType, IsNullable: false } || fieldType != typeof(string))
                    throw new InvalidOperationException($"Tree identity/display/order requires a non-null string field: {node.Key}/{field}");
            if (entity.FindProperty(node.IdField)?.GetMaxLength() is not { } idLength || idLength + node.Key.Length + 1 > 500)
                throw new InvalidOperationException($"Tree node identity exceeds the 500-character SQL identity contract: {node.Key}");
            var parentParts = node.Parents.Select(parent => "WHEN " + Column(parent.Field) + " IS NOT NULL THEN " + Literal(parent.NodeType + ":") + "+" + Column(parent.Field));
            var parentKey = node.Parents.Count == 0 ? "NULL" : "CASE " + string.Join(" ", parentParts) + " ELSE NULL END";
            // 为每种真正指向本节点的外键生成索引友好的 EXISTS，而非合并后全局分组。
            var children = relations.Where(relation => relation.Parent.Key == node.Key).Select(relation =>
            {
                var child = relation.Child;
                var childEntity = entities[child.Key];
                var matches = relation.ForeignKey.Properties.Select((property, index) => ColumnFor(child, "ch", property.Name)
                    + "=" + Column(relation.ForeignKey.PrincipalKey.Properties[index].Name));
                var preceding = child.Parents.Take(relation.Index).Select(parent => ColumnFor(child, "ch", parent.Field) + " IS NULL");
                return "EXISTS(SELECT 1 FROM " + Quote(childEntity.GetSchema() ?? "dbo") + "." + Quote(childEntity.GetTableName()!)
                    + " ch WHERE " + string.Join(" AND ", matches.Concat(preceding).Append(Filter(child, "ch"))) + ")";
            }).ToArray();
            var values = slots.Select(slot => (fields[node.Key].Contains(slot.Field) && entity.FindProperty(slot.Field)?.GetColumnType() == slot.Type
                ? Column(slot.Field) : "CAST(NULL AS " + slot.Type + ")") + " AS " + slot.Alias);
            return $"SELECT CAST({Literal(node.Key + ":")}+{Column(node.IdField)} AS nvarchar(500)) AS NodeKey, "
                + $"CAST({parentKey} AS nvarchar(500)) AS ParentKey, CAST({Literal(node.Key)} AS nvarchar({nodes.Max(item => item.Key.Length)})) AS NodeType, "
                + $"{Column(node.LabelField)} AS DisplayName, {Column(node.CodeField)} AS Code, {Column(node.SortField)} AS SortOrder, "
                + $"{(node.EnabledField is null ? "CAST(1 AS bit)" : Column(node.EnabledField))} AS IsEnabled, "
                + "CAST(" + (children.Length == 0 ? "0" : "CASE WHEN " + string.Join(" OR ", children) + " THEN 1 ELSE 0 END") + " AS bit) AS HasChildren, "
                + $"CAST({(node.Parents.Count == 0 ? "1" : node.RootWhenParentNull ? "CASE WHEN " + string.Join(" AND ", node.Parents.Select(parent => Column(parent.Field) + " IS NULL")) + " THEN 1 ELSE 0 END" : "0")} AS bit) AS AllowsRoot"
                + (slots.Length == 0 ? "" : ", " + string.Join(", ", values))
                + $" FROM {Quote(entity.GetSchema() ?? "dbo")}.{Quote(entity.GetTableName()!)} e WHERE " + Filter(node, "e");
        }

        // 参数名由编译器生成，配置和请求值均不拼接为 SQL 文本。
        string Parameter(object? value)
        {
            var key = "@tree" + parameters.Count;
            parameters.Add(key, value);
            return key;
        }
    }

    // 节点类型是已签发静态结构；转义为字面量让优化器可裁剪不相关 UNION 分支。
    private static string Literal(string value) => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    // 物理标识符只从 EF 元数据取得，并转义 SQL Server 定界符。
    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";

    // 静态过滤只允许标量；复杂值无法转换为查询条件。
    private static object? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.GetDecimal(),
        _ => throw new InvalidOperationException("Tree filter value must be scalar.")
    };
}

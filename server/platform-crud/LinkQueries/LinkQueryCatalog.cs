using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Persistence.Planning;

namespace ICP.Platform.Crud.LinkQueries;

/// <summary>以活动模型、正式资源及模块依赖闭合 Link authoring；启动期拒绝不完整目录。</summary>
public sealed class LinkQueryCatalog
{
    public IReadOnlyList<LinkQueryDefinition> Definitions { get; }

    /// <summary>只为显式采用的 Link 编译目录；能力依赖消费同一活动计划，资源 owner 仍独立核对。</summary>
    public LinkQueryCatalog(CompiledPersistencePlan? plan, IStandardCrudResourceRegistry crud,
        IEnumerable<DataScopeResource> contributions, CanonicalModulePlan? modules)
    {
        if (plan is null || !plan.Models.Any(model => model.LinkQuery is not null)) { Definitions = []; return; }
        ArgumentNullException.ThrowIfNull(modules);
        var resources = crud.GetAll().Select(r => new DataScopeResource(r.ResourceKey, r.ModuleId, r.ModelName, r.IdField))
            .Concat(contributions).ToArray();
        var models = plan.Models.ToDictionary(m => m.LogicalKey, StringComparer.Ordinal);
        var definitions = new List<LinkQueryDefinition>();
        foreach (var model in plan.Models.Where(m => m.LinkQuery is not null))
        {
            var query = model.LinkQuery!;
            if (model.Kind != ModelKind.Link || string.IsNullOrWhiteSpace(query.ResourceKey) || model.LinkedTypes.Count == 0
                || model.LinkedTypes.Select(pair => (pair.Parent, pair.Child)).Distinct().Count() != model.LinkedTypes.Count)
                throw Invalid(model, "类型组合缺失或重复");
            ValidateEnd(model, query.Parent); ValidateEnd(model, query.Child);
            if (query.Parent.ReferenceField == query.Child.ReferenceField) throw Invalid(model, "父子引用字段不能相同");
            var types = model.LinkedTypes.SelectMany(pair => new[] { pair.Parent, pair.Child }).Distinct(StringComparer.Ordinal).ToArray();
            if (!types.Order().SequenceEqual(query.Endpoints.Keys.Order())) throw Invalid(model, "端点目录必须完整对应 linkedTypes");
            var owner = modules.Modules.Single(m => m.ModuleId == model.OwnerModuleId);
            if (!modules.FeatureCatalog.Any(p => p.ModuleId == owner.ModuleId && (p.BehaviorKey == query.ReadBehavior || p.ReadBehaviors.Contains(query.ReadBehavior))))
                throw Invalid(model, "缺少正式读取功能");
            var endpoints = new Dictionary<string, LinkQueryEndpoint>(StringComparer.Ordinal);
            foreach (var type in types)
            {
                if (!models.TryGetValue(type, out var target) || target.Kind != ModelKind.Entity
                    || !ModuleDependencyPolicy.Allows(modules, owner.ModuleId, target.OwnerModuleId))
                    throw Invalid(model, "端点不是可依赖的规范 Entity");
                var mapping = query.Endpoints[type];
                var resource = resources.SingleOrDefault(r => r.ResourceKey == mapping.ResourceKey)
                    ?? throw Invalid(model, "端点读取资源未登记");
                if (resource.ModelKey != type || resource.ModuleId != target.OwnerModuleId || target.Fields.SingleOrDefault(f => f.Name == resource.IdField)?.IsPrimaryKey != true)
                    throw Invalid(model, "端点资源模型或主键不一致");
                var display = new ModelDisplayProjectionPlanner().Compile(target, plan.Models);
                if (!display.IsSuccess)
                    throw Invalid(model, string.Join(Environment.NewLine, display.Diagnostics.Select(d => $"{d.Code}: {d.ModelName}.{d.FieldName}: {d.Message}")));
                endpoints.Add(type, new(target, resource.ResourceKey, resource.IdField, mapping.ConstraintKey, mapping.TitleKey, mapping.Icon, display.Plan!));
            }
            foreach (var pair in model.LinkedTypes)
                if (query.Parent.FixedModelKey is { } parent && parent != pair.Parent || query.Child.FixedModelKey is { } child && child != pair.Child)
                    throw Invalid(model, "固定类型与 linkedTypes 不一致");
            if (query.Columns.Count == 0 || query.Columns.Select(c => c.Field).Distinct(StringComparer.Ordinal).Count() != query.Columns.Count)
                throw Invalid(model, "公共列缺失或重复");
            foreach (var column in query.Columns)
            {
                if (string.IsNullOrWhiteSpace(column.Field) || new[] { "LID", "ParentModelKey", "ParentId", "ChildModelKey", "ChildId", "AssociatedModelKey", "AssociatedId", "HasChildren" }.Contains(column.Field, StringComparer.Ordinal) || column.Field.EndsWith("_DisplayValue", StringComparison.Ordinal)
                    || !Enum.TryParse<FieldValueType>(column.ValueType, out var valueType) || column.Operators.Any(op => op is not ("=" or "contains" or "isNull")))
                    throw Invalid(model, "公共列值类型或操作无效");
                if (column.Operators.Contains("contains", StringComparer.Ordinal) && valueType is not FieldValueType.Text)
                    throw Invalid(model, "contains 只适用于公共文本列");
                foreach (var endpoint in endpoints.Values)
                {
                    if (!column.Mappings.TryGetValue(endpoint.Model.LogicalKey, out var field)) throw Invalid(model, "公共列映射缺失");
                    if (field is null) { if (!column.Nullable) throw Invalid(model, "非空公共列不能映射为空"); continue; }
                    if (field == "$modelKey") { if (valueType != FieldValueType.Text || string.IsNullOrWhiteSpace(endpoint.TitleKey)) throw Invalid(model, "类型身份缺少文本显示契约"); continue; }
                    var targetField = endpoint.Model.Fields.SingleOrDefault(f => f.Name == field);
                    if (targetField is null || targetField.ValueType != valueType || !column.Nullable && !targetField.Required)
                        throw Invalid(model, "公共列字段缺失、类型或可空性不兼容");
                    // 保留公共用户／时间列的显示契约要求；策略与 companion 资格只消费编译结果。
                    if (valueType is FieldValueType.User or FieldValueType.Instant
                        && !endpoint.DisplayProjectionPlan.Fields.Any(display => display.SourceField == field))
                        throw Invalid(model, "端点缺少正式显示契约");
                }
                if (column.Mappings.Keys.Except(types, StringComparer.Ordinal).Any()) throw Invalid(model, "公共列映射包含未知模型");
            }
            if (!query.Columns.Any(c => c.Field == query.DefaultSortField && c.Sortable) || query.DefaultSortDirection is not ("asc" or "desc"))
                throw Invalid(model, "默认排序无效");
            var source = resources.SingleOrDefault(resource => resource.ResourceKey == query.ResourceKey);
            if (source is not null && (source.ModelKey != model.LogicalKey || source.ModuleId != model.OwnerModuleId || source.IdField != "LID")) throw Invalid(model, "源读取资源与Link身份不一致");
            // 公共 companion 资格来自真实端点计划；无映射或无显示计划的类型只返回空 companion。
            var displayFields = query.Columns.Where(column => column.Mappings.Any(mapping => mapping.Value == "$modelKey"
                || endpoints[mapping.Key].DisplayProjectionPlan.Fields.Any(field => field.SourceField == mapping.Value)))
                .Select(column => new PlatformDisplayValueField(column.Field + "_DisplayValue", column.Field, "link:" + column.Field, "", "")).ToArray();
            definitions.Add(new(model, query, endpoints, displayFields, source is not null));
        }
        if (definitions.Select(d => d.Metadata.ResourceKey).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
            throw new InvalidOperationException("重复 Link 查询资源。");
        Definitions = definitions;
    }

    /// <summary>未知查询资源是输入错误，不能回退为任意模型查询。</summary>
    public LinkQueryDefinition Get(string key) => Definitions.SingleOrDefault(d => d.Metadata.ResourceKey == key)
        ?? throw new LinkQueryException(400, "LinkQuery.UnknownResource", "resourceKey");

    // 固定类型与必填普通判别字段互斥；端点引用必须是非空 ObjectId。
    private static void ValidateEnd(ModelDefinition model, LinkQueryEndMetadata end)
    {
        if (string.IsNullOrWhiteSpace(end.FixedModelKey) == string.IsNullOrWhiteSpace(end.TypeField)
            || model.Fields.SingleOrDefault(f => f.Name == end.ReferenceField) is not { Required: true, ValueType: FieldValueType.ObjectId })
            throw Invalid(model, "端点类型声明歧义或引用字段无效");
        if (end.TypeField is { } field && (field == model.LinkQuery!.Parent.ReferenceField || field == model.LinkQuery.Child.ReferenceField
            || model.Fields.SingleOrDefault(f => f.Name == field) is not { Kind: FieldKind.Scalar, Required: true, ValueType: FieldValueType.Text, IsAuditField: false, IsPrimaryKey: false, OrdinalKey: true }))
            throw Invalid(model, "类型判别字段必须是必填普通完整文本身份");
    }

    // 配置错误保留稳定边界，不以请求值拼接 SQL。
    private static InvalidOperationException Invalid(ModelDefinition model, string reason) => new($"LINK_QUERY_CONFIGURATION_INVALID: {model.LogicalKey}: {reason}");
}

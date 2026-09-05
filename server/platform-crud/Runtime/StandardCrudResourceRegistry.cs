using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Persistence.Planning;

namespace ICP.Platform.Crud.Runtime;

public sealed class StandardCrudResourceRegistry : IStandardCrudResourceRegistry
{
    private readonly IReadOnlyList<StandardCrudDescriptor> _descriptors;
    private readonly IReadOnlyDictionary<string, StandardCrudDescriptor> _byResourceKey;
    private readonly IReadOnlyDictionary<string, StandardCrudDescriptor> _byRoute;

    // 资源解析消费已有规范模型，统一装配显示计划；不建立第二份模型目录。
    public StandardCrudResourceRegistry(IEnumerable<StandardCrudDescriptor> descriptors, CompiledPersistencePlan? persistencePlan = null)
    {
        var descriptorList = descriptors.Select(descriptor => ResolveDisplayPlan(descriptor, persistencePlan)).ToList();
        var byResourceKey = new Dictionary<string, StandardCrudDescriptor>(StringComparer.Ordinal);
        var byRoute = new Dictionary<string, StandardCrudDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptorList)
        {
            StandardCrudDescriptorValidator.Validate(descriptor);

            if (byResourceKey.ContainsKey(descriptor.ResourceKey))
            {
                throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 重复注册。");
            }

            var normalizedRoute = NormalizeRoute(descriptor.Route);
            if (byRoute.TryGetValue(normalizedRoute, out var existing))
            {
                throw new InvalidOperationException(
                    $"标准 CRUD 路由 {normalizedRoute} 被 {existing.ResourceKey} 与 {descriptor.ResourceKey} 重复声明。");
            }

            byResourceKey[descriptor.ResourceKey] = descriptor;
            byRoute[normalizedRoute] = descriptor;
        }

        _descriptors = descriptorList.AsReadOnly();
        _byResourceKey = byResourceKey;
        _byRoute = byRoute;
    }

    // 保留完整计划用于写入拒绝；各读取入口再按自己的字段权限裁剪，业务与审计共用编译器。
    private static StandardCrudDescriptor ResolveDisplayPlan(StandardCrudDescriptor descriptor, CompiledPersistencePlan? persistencePlan)
    {
        if (persistencePlan is null)
        {
            return descriptor;
        }

        var model = persistencePlan.Models.SingleOrDefault(item => item.OwnerModuleId == descriptor.ModuleId
            && (item.Name == descriptor.ModelName || item.LogicalKey == descriptor.ModelName))
            ?? throw new InvalidOperationException($"DISPLAY_MODEL_NOT_FOUND: {descriptor.ResourceKey} ({descriptor.ModuleId}/{descriptor.ModelName})");
        var compilation = new ModelDisplayProjectionPlanner().Compile(model, persistencePlan.Models);
        if (!compilation.IsSuccess)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, compilation.Diagnostics.Select(item => item.Message)));
        }

        return descriptor with
        {
            DisplayValueFields = descriptor.DisplayValueFields.Concat(new ICP.Platform.Crud.Planning.ModelDisplayValueFieldProjector()
                .Project(compilation.Plan! with { Fields = compilation.Plan!.Fields.Where(field => !field.IsEntityAuditField
                    && descriptor.FieldSet.ListFields.Contains(field.SourceField, StringComparer.Ordinal)).ToArray() }))
                .Distinct().ToArray(),
            DisplayProjectionPlan = compilation.Plan
        };
    }

    public IReadOnlyList<StandardCrudDescriptor> GetAll() => _descriptors;

    public StandardCrudDescriptor? FindByResourceKey(string resourceKey) =>
        _byResourceKey.TryGetValue(resourceKey, out var descriptor) ? descriptor : null;

    public StandardCrudDescriptor? FindByRoute(string route)
    {
        var normalizedRoute = NormalizeRoute(route);
        return _byRoute.TryGetValue(normalizedRoute, out var descriptor) ? descriptor : null;
    }

    private static string NormalizeRoute(string route)
    {
        var normalized = route.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("标准 CRUD 路由不能为空。");
        }

        return normalized;
    }
}

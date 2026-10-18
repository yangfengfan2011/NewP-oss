using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling;
using ICP.Platform.Persistence.Planning;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>监听前准备唯一资源目录，只消费显式静态绑定，不构造任何scoped约束。</summary>
public sealed class DataScopeResourceCatalog : IDataScopeResourceCatalog
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> actionKeys;
    /// <summary>将标准描述符和专用资源统一核对到同一活动模型与约束绑定。</summary>
    public DataScopeResourceCatalog(IStandardCrudResourceRegistry standard, CanonicalModulePlan modules,
        CompiledPersistencePlan persistence, IEnumerable<DataScopeResource> contributions,
        IEnumerable<DataScopeResourceReadConstraintBinding> bindings, IServiceProviderIsKeyedService services,
        IEnumerable<DataScopeResourceActionBinding>? actionBindings = null)
    {
        var resources = standard.GetAll().Select(item => new DataScopeResource(item.ResourceKey, item.ModuleId, item.ModelName, item.IdField))
            .Concat(contributions).ToArray();
        if (resources.Select(item => item.ResourceKey).Distinct(StringComparer.Ordinal).Count() != resources.Length)
            throw new InvalidOperationException("DATA_SCOPE_RESOURCE_DUPLICATE");
        foreach (var resource in resources)
        {
            if (string.IsNullOrWhiteSpace(resource.ResourceKey) || !modules.Modules.Any(module => module.ModuleId == resource.ModuleId))
                throw new InvalidOperationException("DATA_SCOPE_RESOURCE_OWNER_INVALID");
            var model = persistence.Models.SingleOrDefault(model => model.LogicalKey == resource.ModelKey)
                ?? throw new InvalidOperationException("DATA_SCOPE_MODEL_UNAVAILABLE");
            if (model.OwnerModuleId != resource.ModuleId) throw new InvalidOperationException("DATA_SCOPE_MODEL_OWNER_MISMATCH");
            var keys = model.Fields.Where(field => field.IsPrimaryKey).ToArray();
            if (keys.Length != 1 || keys[0].Name != resource.IdField || keys[0].ValueType != FieldValueType.ObjectId)
                throw new InvalidOperationException("DATA_SCOPE_RESOURCE_ID_INVALID");
        }
        var declared = bindings.ToArray();
        if (declared.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != declared.Length
            || declared.Select(item => item.ResourceKey).Distinct(StringComparer.Ordinal).Count() != declared.Length)
            throw new InvalidOperationException("DATA_SCOPE_READ_CONSTRAINT_DUPLICATE");
        foreach (var binding in declared)
        {
            var resource = resources.SingleOrDefault(item => item.ResourceKey == binding.ResourceKey)
                ?? throw new InvalidOperationException("DATA_SCOPE_READ_CONSTRAINT_RESOURCE_UNKNOWN");
            if (resource.ModuleId != binding.ModuleId || resource.ReadConstraintKey != binding.Key)
                throw new InvalidOperationException("DATA_SCOPE_READ_CONSTRAINT_OWNER_INVALID");
            if (!services.IsKeyedService(typeof(IDataScopeResourceReadConstraint), binding.Key))
                throw new InvalidOperationException("DATA_SCOPE_READ_CONSTRAINT_BINDING_MISSING");
        }
        if (resources.Any(resource => resource.ReadConstraintKey is not null
            && !declared.Any(binding => binding.Key == resource.ReadConstraintKey && binding.ResourceKey == resource.ResourceKey)))
            throw new InvalidOperationException("DATA_SCOPE_READ_CONSTRAINT_BINDING_MISSING");
        var actions = (actionBindings ?? []).ToArray();
        if (actions.Select(item => item.ResourceKey).Distinct(StringComparer.Ordinal).Count() != actions.Length)
            throw new InvalidOperationException("DATA_SCOPE_RESOURCE_ACTION_DUPLICATE");
        foreach (var binding in actions)
        {
            var resource = resources.SingleOrDefault(item => item.ResourceKey == binding.ResourceKey)
                ?? throw new InvalidOperationException("DATA_SCOPE_RESOURCE_ACTION_RESOURCE_UNKNOWN");
            if (resource.ModuleId != binding.ModuleId) throw new InvalidOperationException("DATA_SCOPE_RESOURCE_ACTION_OWNER_INVALID");
            if (binding.ActionKeys is null
                || binding.ActionKeys.Distinct(StringComparer.Ordinal).Count() != binding.ActionKeys.Count
                || binding.ActionKeys.Any(key => !modules.DataActionCatalog.Any(action => action.Key == key)))
                throw new InvalidOperationException("DATA_SCOPE_RESOURCE_ACTION_KEYS_INVALID");
        }
        actionKeys = actions.ToDictionary(item => item.ResourceKey,
            item => (IReadOnlyList<string>)Array.AsReadOnly(item.ActionKeys.ToArray()), StringComparer.Ordinal);
        foreach (var requirement in modules.ActionDataAccessCatalog)
            foreach (var key in requirement.Resources)
            {
                var resource = resources.SingleOrDefault(item => item.ResourceKey == key)
                    ?? throw new InvalidOperationException("ACTION_DATA_ACCESS_RESOURCE_UNKNOWN");
                if (requirement.Actions.Any(action => actionKeys.TryGetValue(resource.ResourceKey, out var applicable) && !applicable.Contains(action)))
                    throw new InvalidOperationException("ACTION_DATA_ACCESS_RESOURCE_ACTION_INVALID");
            }
        Resources = Array.AsReadOnly(resources);
    }

    public IReadOnlyList<DataScopeResource> Resources { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string>? ActionKeysFor(string resourceKey) => actionKeys.GetValueOrDefault(resourceKey);
}

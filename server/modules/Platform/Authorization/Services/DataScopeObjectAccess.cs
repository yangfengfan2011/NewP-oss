using ICP.Platform.Authorization.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>完整访问服务在静态目录就绪后解析本次scoped约束，底层read plan从不回调这里。</summary>
public sealed class DataScopeObjectAccess(DataScopeCatalog catalog, IDataScopeObjectDecisionProvider decisions,
    IServiceProvider services, IDataScopeEvaluationScope? evaluation = null) : IDataScopeObjectAccess
{
    /// <inheritdoc/>
    public async Task<bool> IsReadableAsync(string resourceKey, string objectId, CancellationToken token = default)
    {
        using var operation = evaluation?.BeginOperation();
        var resource = catalog.Resolve(resourceKey);
        if (!await decisions.IsAllowedAsync(resourceKey, objectId, "read", token)) return false;
        if (resource.ReadConstraintKey is null) return true;
        var constraint = services.GetRequiredKeyedService<IDataScopeResourceReadConstraint>(resource.ReadConstraintKey);
        if (constraint.Key != resource.ReadConstraintKey || constraint.ResourceKey != resourceKey || constraint.ModuleId != resource.ModuleId)
            throw new InvalidOperationException("DATA_SCOPE_READ_CONSTRAINT_BINDING_INVALID");
        return await constraint.IsReadableAsync(objectId, token);
    }
}

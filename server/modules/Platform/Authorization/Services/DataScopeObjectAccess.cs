using ICP.Platform.Authorization.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>完整访问服务在静态目录就绪后解析本次scoped约束，底层read plan从不回调这里。</summary>
public sealed class DataScopeObjectAccess(DataScopeCatalog catalog, IDataScopeObjectDecisionProvider decisions,
    IServiceProvider services, IDataScopeEvaluationScope? evaluation = null, DataScopeRules? rules = null) : IDataScopeObjectAccess
{
    /// <inheritdoc/>
    public Task<bool> IsReadableAsync(string resourceKey, string objectId, CancellationToken token = default) =>
        ReadableAsync(resourceKey, objectId, null, token);

    /// <inheritdoc/>
    public Task<bool> IsReadableAsync(string resourceKey, string objectId, object readContext, CancellationToken token = default) =>
        ReadableAsync(resourceKey, objectId, readContext, token);

    // 有无正式读取上下文始终先执行相同read规则，再交给资源拥有者的完整访问约束。
    private async Task<bool> ReadableAsync(string resourceKey, string objectId, object? readContext, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        using var configuration = rules?.BeginOperation();
        var resource = catalog.Resolve(resourceKey);
        if (!await decisions.IsAllowedAsync(resourceKey, objectId, "read", token)) return false;
        if (resource.ReadConstraintKey is null) return readContext is null;
        var constraint = services.GetRequiredKeyedService<IDataScopeResourceReadConstraint>(resource.ReadConstraintKey);
        if (constraint.Key != resource.ReadConstraintKey || constraint.ResourceKey != resourceKey || constraint.ModuleId != resource.ModuleId)
            throw new InvalidOperationException("DATA_SCOPE_READ_CONSTRAINT_BINDING_INVALID");
        return readContext is null ? await constraint.IsReadableAsync(objectId, token)
            : await constraint.IsReadableAsync(objectId, readContext, token);
    }
}

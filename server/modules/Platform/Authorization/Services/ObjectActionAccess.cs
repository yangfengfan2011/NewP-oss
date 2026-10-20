using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>业务主动检查与按钮自动检查共用完整读取约束、动作目录及原裁决引擎。</summary>
public sealed class ObjectActionAccess(IDataScopeResourceCatalog resources, CanonicalModulePlan plan,
    IDataScopeObjectAccess reads, IDataScopeObjectDecisionProvider decisions) : IObjectActionAccess
{
    /// <inheritdoc/>
    public async Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions,
        CancellationToken cancellationToken = default)
    {
        var resource = resources.Resources.SingleOrDefault(item => item.ModelName == objectName)
            ?? throw new ActionDataAccessException(400, "ButtonExecution.ObjectUnknown");
        if (string.IsNullOrWhiteSpace(objectId) || !objectId.StartsWith(objectName + "_", StringComparison.Ordinal)
            || actions.Count == 0 || actions.Distinct(StringComparer.Ordinal).Count() != actions.Count
            || actions.Any(key => !plan.DataActionCatalog.Any(item => item.Key == key)
                || resources.ActionKeysFor(resource.ResourceKey) is { } applicable && !applicable.Contains(key)))
            throw new ActionDataAccessException(400, "ButtonExecution.TargetInvalid");
        foreach (var key in actions)
        {
            var action = plan.DataActionCatalog.Single(item => item.Key == key);
            if ((key == "read" || action.RequiresRead) && !await reads.IsReadableAsync(resource.ResourceKey, objectId, cancellationToken))
                return DataExecutionDecision.NotFound;
            if (key != "read" && !await decisions.IsAllowedAsync(resource.ResourceKey, objectId, key, cancellationToken))
                return DataExecutionDecision.Denied;
        }
        return DataExecutionDecision.Allowed;
    }
}

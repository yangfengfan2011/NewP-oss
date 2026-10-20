using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class DataExecutionAccess
{
    /// <inheritdoc/>
    public async Task<DataExecutionTargetDecision> EvaluateApiObjectAsync(DataExecutionApiObjectExecutionContext context,
        string role, DataExecutionTargetInput target, CancellationToken cancellationToken = default)
    {
        var contract = ResolveApiSource(context?.Binding, context?.Identity, target.ResourceKey);
        var matches = contract.Requirements.Where(item => item.Role == role && item.Stage == context!.Binding.Stage).ToArray();
        if (context!.Binding.Stage != "submit" || contract.Branches.Count != 0 || matches.Length != 1
            || matches[0].Shape != "object" || matches[0].MinimumCount != 1 || matches[0].MaximumCount != 1
            || matches[0].Branch is not null)
            throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
        var resources = contract.Requirements.SelectMany(item => item.Resources).Distinct(StringComparer.Ordinal).ToArray();
        await foreach (var decision in EvaluateResolvedObjectsAsync(contract.ModuleId, resources, contract.Requirements,
            context.Binding, role, [target], cancellationToken)) return decision;
        throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
    }

    /// <inheritdoc/>
    public Task<DataScopeReadPlan> GetApiObjectReadPlanAsync(DataExecutionApiObjectContext context, string resourceKey, string objectId,
        CancellationToken cancellationToken = default)
    {
        var contract = ResolveApiSource(context?.Binding, context?.Identity, resourceKey);
        if (contract.Requirements.Count != 1 || contract.Branches.Count != 0)
            throw new ActionDataAccessException(400, "ActionDataAccess.QueryInvalid");
        var role = contract.Requirements[0];
        if (context!.Binding.Stage != "read" || context.Role != "target" || role.Role != context.Role || role.Stage != "read" || role.Shape != "object"
            || role.MinimumCount != 1 || role.MaximumCount != 1 || role.Branch is not null || string.IsNullOrWhiteSpace(objectId))
            throw new ActionDataAccessException(400, "ActionDataAccess.QueryInvalid");
        RequireReadPlanRole(role, resourceKey);
        return (queryReads ?? throw new InvalidOperationException("DATA_EXECUTION_QUERY_READS_REQUIRED"))
            .GetReadPlanAsync(resourceKey, cancellationToken);
    }
}

using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class DataExecutionAccess
{
    /// <inheritdoc/>
    public Task<DataScopeReadPlan> GetApiObjectReadPlanAsync(DataExecutionApiObjectContext context, string resourceKey, string objectId,
        CancellationToken cancellationToken = default)
    {
        var contract = ResolveApiReadSource(context?.Binding, context?.Identity, resourceKey);
        if (contract.Requirements.Count != 1 || contract.Branches.Count != 0)
            throw new ActionDataAccessException(400, "ActionDataAccess.QueryInvalid");
        var role = contract.Requirements[0];
        if (context!.Role != "target" || role.Role != context.Role || role.Stage != "read" || role.Shape != "object"
            || role.MinimumCount != 1 || role.MaximumCount != 1 || role.Branch is not null || string.IsNullOrWhiteSpace(objectId))
            throw new ActionDataAccessException(400, "ActionDataAccess.QueryInvalid");
        RequireReadPlanRole(role, resourceKey);
        return (queryReads ?? throw new InvalidOperationException("DATA_EXECUTION_QUERY_READS_REQUIRED"))
            .GetReadPlanAsync(resourceKey, cancellationToken);
    }
}

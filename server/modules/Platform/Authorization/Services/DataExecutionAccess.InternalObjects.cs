using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class DataExecutionAccess
{
    /// <inheritdoc/>
    public async Task<DataExecutionTargetDecision> EvaluateInternalObjectAsync(DataExecutionInternalBinding binding, string role,
        DataExecutionTargetInput target, CancellationToken cancellationToken = default)
    {
        if (http.HttpContext?.User.Identity?.IsAuthenticated != true)
            throw new ActionDataAccessException(401, "ActionDataAccess.SubjectRequired");
        var source = plan.Modules.SelectMany(module => module.DataExecutions).SingleOrDefault(contract => contract.Key == binding.Execution);
        if (source is null || source.Boundary != "internal" || source.Provider != binding.Provider || source.LegacyActions
            || string.IsNullOrWhiteSpace(binding.Member) || !binding.Caller.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Any(method => method.Name == binding.Member)
            || !source.FixedBindings.Any(item => item.Kind == "internal" && item.Identity == source.Key && item.Caller == binding.CallerIdentity))
            throw new ActionDataAccessException(400, "ActionDataAccess.InternalBindingInvalid");
        var contract = DataExecutionProviders.Resolve([source], plan.Modules.SelectMany(module => module.DataExecutionProviders).ToArray()).Single();
        var stages = contract.Requirements.Where(requirement => requirement.Role == role).Select(requirement => requirement.Stage).Distinct(StringComparer.Ordinal).ToArray();
        if (stages.Length != 1) throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
        var reference = new DataExecutionBindingReference(contract.Key, contract.Provider, stages[0]);
        var resources = contract.Requirements.SelectMany(requirement => requirement.Resources).Distinct(StringComparer.Ordinal).ToArray();
        await foreach (var decision in EvaluateResolvedObjectsAsync(contract.ModuleId, resources, contract.Requirements,
            reference, role, [target], cancellationToken)) return decision;
        throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
    }
}

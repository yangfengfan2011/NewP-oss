using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>正式执行仅消费装配结果，客户端声明引用不能替代目标、角色或固定下限。</summary>
public sealed partial class DataExecutionAccess(CanonicalModulePlan plan, IHttpContextAccessor http,
    IDataScopeObjectAccess reads, IDataScopeObjectDecisionProvider decisions, IDataScopeResourceCatalog? resources = null,
    IEnumerable<IDataExecutionTargetResolver>? resolvers = null) : IDataExecutionAccess
{
    private readonly IReadOnlyList<IDataExecutionTargetResolver> _resolvers = (resolvers ?? []).ToArray();
    /// <inheritdoc/>
    public void Validate(IReadOnlyList<DataExecutionBindingReference> bindings, string resourceKey) => Resolve(bindings, resourceKey);

    /// <inheritdoc/>
    public DataExecutionBindingReference? BindResourceEndpoint(string resourceKey)
    {
        var context = http.HttpContext ?? throw new ActionDataAccessException(400, "ActionDataAccess.ContextRequired");
        var owner = context.GetEndpoint()?.Metadata.GetOrderedMetadata<EndpointModuleAttribute>();
        var endpoints = context.GetEndpoint()?.Metadata.GetOrderedMetadata<DataExecutionEndpointAttribute>() ?? [];
        var resource = resources?.Resources.SingleOrDefault(resource => resource.ResourceKey == resourceKey) ?? throw new InvalidOperationException("DATA_EXECUTION_RESOURCE_CATALOG_REQUIRED");
        var providers = plan.Modules.SelectMany(module => module.DataExecutionProviders).ToArray();
        if (owner?.Count != 1 || endpoints.Count == 0 || endpoints.Any(endpoint => !providers.Any(provider =>
            provider.Key == endpoint.Provider && provider.ModuleId == owner[0].ModuleId)))
            throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
        if (endpoints.Any(endpoint => providers.Single(provider => provider.Key == endpoint.Provider).LegacyConsumers.Any(consumer =>
            consumer.ModuleId == resource.ModuleId && consumer.ResourceKey == resourceKey))) return null;
        var ids = context.Request.Headers["X-NewP-Data-Declaration"];
        var requirement = ids.Count == 1 ? plan.ActionDataAccessCatalog.SingleOrDefault(item => item.Id == ids[0]) : null;
        var selected = endpoints.SingleOrDefault(endpoint => endpoint.Provider == requirement?.Provider);
        if (selected is null || requirement is null) throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
        var binding = new DataExecutionBindingReference(requirement.Execution, selected.Provider, selected.Stage);
        Resolve([binding], resourceKey);
        return binding;
    }

    /// <inheritdoc/>
    public async Task<DataExecutionDecision> EvaluateObjectAsync(DataExecutionBindingReference binding, string resourceKey,
        string role, string objectId, CancellationToken cancellationToken = default)
    {
        var requirement = Role(Resolve([binding], resourceKey), binding, role, "object");
        foreach (var branch in requirement.AnyOf)
        {
            var allowed = true;
            foreach (var action in branch)
            {
                if (action == "read")
                {
                    if (!await reads.IsReadableAsync(resourceKey, objectId, cancellationToken)) return DataExecutionDecision.NotFound;
                }
                else if (!await decisions.IsAllowedAsync(resourceKey, objectId, action, cancellationToken)) { allowed = false; break; }
            }
            if (allowed) return DataExecutionDecision.Allowed;
        }
        return DataExecutionDecision.Denied;
    }

    /// <inheritdoc/>
    public async Task<DataExecutionDecision> EvaluateCandidateAsync(DataExecutionBindingReference binding, string resourceKey,
        string role, IReadOnlyDictionary<string, object?> candidate, CancellationToken cancellationToken = default)
    {
        var requirement = Role(Resolve([binding], resourceKey), binding, role, "candidate");
        foreach (var branch in requirement.AnyOf)
        {
            var allowed = true;
            foreach (var action in branch)
                if (!await decisions.IsCandidateAllowedAsync(resourceKey, candidate, action, cancellationToken)) { allowed = false; break; }
            if (allowed) return DataExecutionDecision.Allowed;
        }
        return DataExecutionDecision.Denied;
    }

    // 执行提供者由真实执行端限定；即使引用同一模块其他合法声明也不能串用。
    private ActionDataAccessRequirement Resolve(IReadOnlyList<DataExecutionBindingReference> bindings, string resourceKey)
    {
        var context = http.HttpContext ?? throw new ActionDataAccessException(400, "ActionDataAccess.ContextRequired");
        if (context.User.Identity?.IsAuthenticated != true) throw new ActionDataAccessException(401, "ActionDataAccess.SubjectRequired");
        var ids = context.Request.Headers["X-NewP-Data-Declaration"];
        var position = context.Request.Headers["X-NewP-Feature"];
        var requirement = ids.Count == 1 ? plan.ActionDataAccessCatalog.SingleOrDefault(item => item.Id == ids[0]) : null;
        var owners = context.GetEndpoint()?.Metadata.GetOrderedMetadata<EndpointModuleAttribute>();
        if (requirement?.Requirements is null || owners?.Count != 1 || owners[0].ModuleId != (requirement.ProviderModuleId ?? requirement.ModuleId)
            || !requirement.Resources.Contains(resourceKey, StringComparer.Ordinal)
            || !bindings.Any(binding => binding.Execution == requirement.Execution && binding.Provider == requirement.Provider
                && requirement.Requirements.Any(item => item.Stage == binding.Stage)))
            throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
        if (position.Count != 1 || !requirement.FeaturePositions.Contains(position[0]!, StringComparer.Ordinal))
            throw new ActionDataAccessException(403, "ActionDataAccess.FeatureMismatch");
        var contract = plan.Modules.SelectMany(module => module.DataExecutions).Single(item => item.Key == requirement.Execution);
        if (contract.Branches.Count == 0 ? bindings.Any(binding => binding.Branch is not null)
            : !bindings.Any(binding => binding.Branch is not null && contract.Branches.Contains(binding.Branch, StringComparer.Ordinal)))
            throw new ActionDataAccessException(400, "ActionDataAccess.BranchInvalid");
        if (requirement.ProviderModuleId is not null)
        {
            var resource = resources?.Resources.SingleOrDefault(resource => resource.ResourceKey == resourceKey) ?? throw new InvalidOperationException("DATA_EXECUTION_RESOURCE_CATALOG_REQUIRED");
            var feature = context.GetEndpoint()?.Metadata.GetOrderedMetadata<ResourceFeatureBindingAttribute>();
            if (resource.ModuleId != requirement.ModuleId || feature?.Count != 1
                || !plan.Modules.SelectMany(module => module.DataExecutionProviders).Any(provider => provider.Key == requirement.Provider
                    && provider.ModuleId == requirement.ProviderModuleId && provider.ConsumerBinding == "resource-owner")
                || !plan.FeatureCatalog.Any(point => point.Position == position[0] && point.ModuleId == resource.ModuleId
                    && point.BehaviorKey == resourceKey + "." + feature[0].ActionKey))
                throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
        }
        return requirement;
    }

    // 当前首片只消费单对象和候选；其他形状由实际owner后续接入，不把首个对象代表整批。
    private static DataExecutionRequirement Role(ActionDataAccessRequirement requirement, DataExecutionBindingReference binding, string role, string shape)
    {
        var matches = requirement.Requirements!.Where(item => item.Stage == binding.Stage && item.Role == role && item.Shape == shape
            && (item.Branch is null || item.Branch == binding.Branch)).ToArray();
        if (matches.Length != 1) throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
        return matches[0];
    }
}

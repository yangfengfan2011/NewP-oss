using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>正式执行仅消费装配结果，客户端声明引用不能替代目标、角色或固定下限。</summary>
public sealed partial class DataExecutionAccess(CanonicalModulePlan plan, IHttpContextAccessor http,
    IDataScopeObjectAccess reads, IDataScopeObjectDecisionProvider decisions, IDataScopeResourceCatalog? resources = null,
    IEnumerable<IDataExecutionTargetResolver>? resolvers = null, IDataScopeReadPlanProvider? queryReads = null, IDataScopeReadableIds? targetReads = null,
    IButtonQueryReadScope? buttonQueries = null, IButtonExecutionAccess? buttons = null) : IDataExecutionAccess
{
    private readonly IReadOnlyList<IDataExecutionTargetResolver> _resolvers = (resolvers ?? []).ToArray();
    /// <inheritdoc/>
    public void Validate(IReadOnlyList<DataExecutionBindingReference> bindings, string resourceKey) => Resolve(bindings, resourceKey);

    /// <inheritdoc/>
    public Task<DataScopeReadPlan> GetQueryReadPlanAsync(DataExecutionQueryContext context, string resourceKey,
        CancellationToken cancellationToken = default)
    {
        if (buttonQueries?.Current is { } source)
        {
            if (context.Binding != source.Target.Source.Binding || context.Role != source.Target.Source.Role || context.TargetSets.Count != 0
                || resourceKey != source.Target.ResourceKey || buttons?.BindHandler(source.Invocation.HandlerType) != source.Invocation)
                throw new ActionDataAccessException(400, "ButtonExecution.QuerySourceInvalid");
            var declaration = plan.ActionDataAccessCatalog.Single(item => item.Id == source.Invocation.ButtonId);
            ButtonQuerySources.Validate(plan, declaration, source.Target, resources ?? throw new InvalidOperationException("DATA_EXECUTION_RESOURCE_CATALOG_REQUIRED"));
            return (queryReads ?? throw new InvalidOperationException("DATA_EXECUTION_QUERY_READS_REQUIRED")).GetReadPlanAsync(resourceKey, cancellationToken);
        }
        var effective = Resolve([context.Binding], resourceKey);
        var requirement = Role(effective, context.Binding, context.Role, "query");
        RequireReadPlanRole(requirement, resourceKey);
        var targets = effective.Requirements!.Where(item => item.Stage == context.Binding.Stage && item.Role != context.Role
            && (item.Branch is null || item.Branch == context.Binding.Branch)).ToArray();
        if (context.TargetSets.Count != targets.Length || context.TargetSets.Select(set => set.Role).Distinct(StringComparer.Ordinal).Count() != targets.Length)
            throw new ActionDataAccessException(400, "ActionDataAccess.QueryInvalid");
        foreach (var target in targets)
        {
            var set = context.TargetSets.SingleOrDefault(set => set.Role == target.Role);
            if (target.Shape != "objects" || set is null || set.Ids.Count < target.MinimumCount
                || target.MaximumCount is { } maximum && set.Ids.Count > maximum || set.Ids.Any(string.IsNullOrWhiteSpace))
                throw new ActionDataAccessException(400, "ActionDataAccess.QueryInvalid");
            RequireReadPlanRole(target, resourceKey);
        }
        return (queryReads ?? throw new InvalidOperationException("DATA_EXECUTION_QUERY_READS_REQUIRED")).GetReadPlanAsync(resourceKey, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<DataScopeReadPlan> GetObjectReadPlanAsync(IReadOnlyList<DataExecutionBindingReference> bindings,
        string resourceKey, string role, string objectId, CancellationToken cancellationToken = default)
    {
        var effective = Resolve(bindings, resourceKey);
        var binding = bindings.Single(item => item.Execution == effective.Execution && item.Provider == effective.Provider);
        var requirement = Role(effective, binding, role, "object");
        if (string.IsNullOrWhiteSpace(objectId) || requirement.MinimumCount != 1 || requirement.MaximumCount != 1
            || effective.Requirements!.Count(item => item.Stage == binding.Stage
                && (item.Branch is null || item.Branch == binding.Branch)) != 1)
            throw new ActionDataAccessException(400, "ActionDataAccess.QueryInvalid");
        RequireReadPlanRole(requirement, resourceKey);
        return (queryReads ?? throw new InvalidOperationException("DATA_EXECUTION_QUERY_READS_REQUIRED")).GetReadPlanAsync(resourceKey, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IReadOnlySet<string>> GetInternalReadableIdsAsync(DataExecutionInternalReadBinding binding,
        string resourceKey, IReadOnlyCollection<string> objectIds, CancellationToken cancellationToken = default)
    {
        ValidateInternalRead(binding, resourceKey, objectIds);
        return (targetReads ?? throw new InvalidOperationException("DATA_EXECUTION_TARGET_READS_REQUIRED"))
            .GetReadableIdsAsync(resourceKey, objectIds, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<DataScopeReadPlan> GetInternalReadPlanAsync(DataExecutionInternalReadBinding binding,
        string resourceKey, IReadOnlyCollection<string> objectIds, CancellationToken cancellationToken = default)
    {
        ValidateInternalRead(binding, resourceKey, objectIds);
        return (queryReads ?? throw new InvalidOperationException("DATA_EXECUTION_QUERY_READS_REQUIRED"))
            .GetReadPlanAsync(resourceKey, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<DataScopeReadPlan> GetInternalQueryReadPlanAsync(DataExecutionInternalReadBinding binding,
        string resourceKey, CancellationToken cancellationToken = default)
    {
        var contract = ResolveInternalQuerySource(binding);
        if (contract.Requirements.Count != 1 || contract.Branches.Count != 0)
            throw new ActionDataAccessException(400, "ActionDataAccess.QueryInvalid");
        var requirement = contract.Requirements[0];
        if (requirement.Role != "query" || requirement.Stage != "read" || requirement.Shape != "query"
            || requirement.MinimumCount != 0 || requirement.MaximumCount is not null || requirement.Branch is not null)
            throw new ActionDataAccessException(400, "ActionDataAccess.QueryInvalid");
        RequireReadPlanRole(requirement, resourceKey);
        return (queryReads ?? throw new InvalidOperationException("DATA_EXECUTION_QUERY_READS_REQUIRED"))
            .GetReadPlanAsync(resourceKey, cancellationToken);
    }

    // 查询来源必须属于当前活动owner的实际backend，并精确登记真实类型和成员；存在性核验不能替代固定登记。
    private DataExecutionContract ResolveInternalQuerySource(DataExecutionInternalReadBinding binding)
    {
        var sources = plan.Modules.SelectMany(module => module.DataExecutions.Where(contract => contract.Key == binding.Execution)
            .Select(contract => (Module: module, Contract: contract))).ToArray();
        if (sources.Length != 1) throw new ActionDataAccessException(400, "ActionDataAccess.InternalBindingInvalid");
        var (module, contract) = sources[0];
        if (contract.ModuleId != module.ModuleId || contract.Boundary != "internal"
            || contract.Provider != binding.Provider || module.BackendBehaviorKey != binding.Provider
            || string.IsNullOrWhiteSpace(binding.Member)
            || !contract.FixedBindings.Any(source => source.Kind == "internal" && source.Identity == binding.Execution && source.Caller == binding.CallerIdentity)
            || !binding.Caller.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static).Any(method => method.Name == binding.Member))
            throw new ActionDataAccessException(400, "ActionDataAccess.InternalBindingInvalid");
        return contract;
    }

    // 内部来源必须精确匹配真实调用者/方法及固定角色；不读取或伪造浏览器声明头。
    private void ValidateInternalRead(DataExecutionInternalReadBinding binding, string resourceKey, IReadOnlyCollection<string> objectIds)
    {
        var contract = plan.Modules.SelectMany(module => module.DataExecutions).SingleOrDefault(item => item.Key == binding.Execution);
        if (contract is null || contract.Boundary != "internal" || contract.Provider != binding.Provider
            || !contract.FixedBindings.Any(source => source.Kind == "internal"
                && source.Identity == binding.Execution && source.Caller == binding.CallerIdentity)
            || binding.Member is not null && !binding.Caller.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Any(method => method.Name == binding.Member)
            || contract.Requirements.Count != 1)
            throw new ActionDataAccessException(400, "ActionDataAccess.InternalBindingInvalid");
        var requirement = contract.Requirements[0];
        if (requirement.Role != "targets" || requirement.Stage != "read" || requirement.Shape != "objects"
            || requirement.Branch is not null || requirement.ReadResolverKey is not null
            || objectIds.Count < requirement.MinimumCount || requirement.MaximumCount is { } maximum && objectIds.Count > maximum
            || objectIds.Any(string.IsNullOrWhiteSpace))
            throw new ActionDataAccessException(400, "ActionDataAccess.QueryInvalid");
        RequireReadPlanRole(requirement, resourceKey);
    }

    // 查询与伴随ID集合共用原SQL谓词；不将对象动作或OR悄悄忽略，也不逐ID预检。
    private static void RequireReadPlanRole(DataExecutionRequirement role, string resourceKey)
    {
        if (!role.Resources.Contains(resourceKey, StringComparer.Ordinal) || role.AnyOf.Count != 1
            || !role.AnyOf[0].SequenceEqual(["read"]) || role.ResourceAnyOf is not null || role.ReadResolverKey is not null)
            throw new ActionDataAccessException(400, "ActionDataAccess.QueryInvalid");
    }

    /// <inheritdoc/>
    public DataExecutionBindingReference BindResourceEndpoint(string resourceKey)
    {
        var context = http.HttpContext ?? throw new ActionDataAccessException(400, "ActionDataAccess.ContextRequired");
        var owner = context.GetEndpoint()?.Metadata.GetOrderedMetadata<EndpointModuleAttribute>();
        var endpoints = context.GetEndpoint()?.Metadata.GetOrderedMetadata<DataExecutionEndpointAttribute>() ?? [];
        var resource = resources?.Resources.SingleOrDefault(resource => resource.ResourceKey == resourceKey) ?? throw new InvalidOperationException("DATA_EXECUTION_RESOURCE_CATALOG_REQUIRED");
        var providers = plan.Modules.SelectMany(module => module.DataExecutionProviders).ToArray();
        if (owner?.Count != 1 || endpoints.Count == 0 || endpoints.Any(endpoint => !providers.Any(provider =>
            provider.Key == endpoint.Provider && provider.ModuleId == owner[0].ModuleId)))
            throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
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
        return await EvaluateObjectRequirementAsync(requirement, resourceKey, objectId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DataExecutionDecision> EvaluateCandidateAsync(DataExecutionBindingReference binding, string resourceKey,
        string role, IReadOnlyDictionary<string, object?> candidate, CancellationToken cancellationToken = default)
    {
        var requirement = Role(Resolve([binding], resourceKey), binding, role, "candidate");
        return await EvaluateCandidateRequirementAsync(requirement, resourceKey, candidate, cancellationToken);
    }

    // 执行提供者由真实执行端限定；即使引用同一模块其他合法声明也不能串用。
    private ActionDataAccessRequirement Resolve(IReadOnlyList<DataExecutionBindingReference> bindings, string resourceKey)
    {
        if (http.HttpContext?.Request.Headers.ContainsKey("X-NewP-Button") == true)
            throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
        if (bindings is not { Count: > 0 } || bindings.Any(binding => binding is null))
            throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
        var context = http.HttpContext ?? throw new ActionDataAccessException(400, "ActionDataAccess.ContextRequired");
        if (context.User.Identity?.IsAuthenticated != true) throw new ActionDataAccessException(401, "ActionDataAccess.SubjectRequired");
        var ids = context.Request.Headers["X-NewP-Data-Declaration"];
        var position = context.Request.Headers["X-NewP-Feature"];
        var requirement = ids.Count == 1 ? plan.ActionDataAccessCatalog.SingleOrDefault(item => item.Id == ids[0]) : null;
        var owners = context.GetEndpoint()?.Metadata.GetOrderedMetadata<EndpointModuleAttribute>();
        if (requirement?.Requirements is null || owners?.Count != 1
            || owners[0].ModuleId != (requirement.ProviderModuleId ?? requirement.ModuleId) && owners[0].ModuleId != requirement.ModuleId
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
            var resourceCatalog = resources?.Resources ?? throw new InvalidOperationException("DATA_EXECUTION_RESOURCE_CATALOG_REQUIRED");
            var resource = resourceCatalog.SingleOrDefault(resource => resource.ResourceKey == resourceKey)
                ?? throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
            var provider = plan.Modules.SelectMany(module => module.DataExecutionProviders).SingleOrDefault(provider => provider.Key == requirement.Provider
                && provider.ModuleId == requirement.ProviderModuleId);
            var feature = context.GetEndpoint()?.Metadata.GetOrderedMetadata<ResourceFeatureBindingAttribute>();
            var endpointBehaviors = context.GetEndpoint()?.Metadata.GetOrderedMetadata<RequireFeatureAttribute>()
                .Select(binding => binding.BehaviorKey).Distinct(StringComparer.Ordinal).ToArray() ?? [];
            var generic = owners[0].ModuleId == requirement.ProviderModuleId && feature?.Count == 1
                && plan.FeatureCatalog.Any(point => point.Position == position[0]
                && point.ModuleId == resource.ModuleId && point.BehaviorKey == resourceKey + "." + feature[0].ActionKey);
            var inputRead = endpointBehaviors.Length == 1 && IsDeclaredInputReadSource(requirement, contract, endpointBehaviors[0], position[0]!);
            var ownEndpoint = feature?.Count == 0 && owners[0].ModuleId == requirement.ModuleId && endpointBehaviors.Length == 1
                && (contract.FeatureBehaviors.Contains(endpointBehaviors[0], StringComparer.Ordinal) || inputRead)
                && plan.FeatureCatalog.Any(point => point.Position == position[0] && point.ModuleId == resource.ModuleId
                    && (point.BehaviorKey == endpointBehaviors[0] || point.ReadBehaviors.Contains(endpointBehaviors[0], StringComparer.Ordinal)));
            var ownerEntry = feature?.Count == 0 && owners[0].ModuleId == requirement.ModuleId && endpointBehaviors.Length == 1
                && (contract.FeatureBehaviors.Contains(endpointBehaviors[0], StringComparer.Ordinal) || inputRead)
                && plan.FeatureCatalog.Any(point => point.Position == position[0] && point.ModuleId == requirement.ModuleId
                    && (point.BehaviorKey == endpointBehaviors[0] || point.ReadBehaviors.Contains(endpointBehaviors[0], StringComparer.Ordinal)));
            var providerEntry = generic && resource.ModuleId == requirement.ModuleId && feature![0].ActionKey == "authorize"
                && context.GetEndpoint()!.Metadata.GetOrderedMetadata<DataExecutionEndpointAttribute>().Any(endpoint => endpoint.Provider == requirement.Provider
                    && bindings.Any(binding => binding.Provider == endpoint.Provider && binding.Stage == endpoint.Stage));
            var executionOwner = provider?.ConsumerBinding == "execution-owner" && (ownerEntry || providerEntry)
                && requirement.Resources.All(key => resourceCatalog.SingleOrDefault(candidate => candidate.ResourceKey == key) is { } target
                    && ModuleDependencyPolicy.Allows(plan, requirement.ModuleId, target.ModuleId));
            if (!executionOwner && (provider?.ConsumerBinding != "resource-owner" || resource.ModuleId != requirement.ModuleId || !generic && !ownEndpoint))
                throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
        }
        return requirement;
    }

    // 单对象、候选和查询分别核对角色形状；集合由独立入口核完整基数，不把首个对象代表整批。
    private static DataExecutionRequirement Role(ActionDataAccessRequirement requirement, DataExecutionBindingReference binding, string role, string shape)
    {
        var matches = requirement.Requirements!.Where(item => item.Stage == binding.Stage && item.Role == role && item.Shape == shape
            && (item.Branch is null || item.Branch == binding.Branch)).ToArray();
        if (matches.Length != 1) throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
        return matches[0];
    }
}

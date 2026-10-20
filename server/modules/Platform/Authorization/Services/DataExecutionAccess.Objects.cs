using System.Runtime.CompilerServices;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class DataExecutionAccess
{
    /// <inheritdoc/>
    public async Task<DataExecutionDecision> EvaluateObjectFromBindingsAsync(IReadOnlyList<DataExecutionBindingReference> bindings,
        string resourceKey, string role, string objectId, CancellationToken cancellationToken = default)
    {
        var effective = Resolve(bindings, resourceKey);
        var matches = bindings.Where(binding => binding.Execution == effective.Execution && binding.Provider == effective.Provider
            && effective.Requirements!.Any(item => item.Role == role && item.Stage == binding.Stage)).ToArray();
        if (matches.Length != 1) throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
        await foreach (var decision in EvaluateResolvedObjectsAsync(effective.ModuleId, effective.Resources, effective.Requirements!,
            matches[0], role, [new(resourceKey, objectId)], cancellationToken)) return decision.Decision;
        throw new InvalidOperationException("DATA_EXECUTION_TARGET_RESULT_MISSING");
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<DataExecutionTargetDecision> EvaluateObjectsAsync(DataExecutionBindingReference binding, string role,
        IReadOnlyList<DataExecutionTargetInput> targets, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var ids = http.HttpContext?.Request.Headers["X-NewP-Data-Declaration"];
        var declared = ids?.Count == 1 ? plan.ActionDataAccessCatalog.SingleOrDefault(item => item.Id == ids.Value[0]) : null;
        var resource = targets.FirstOrDefault()?.ResourceKey ?? declared?.Requirements?.FirstOrDefault(item => item.Role == role)?.Resources.FirstOrDefault() ?? "";
        var effective = Resolve([binding], resource);
        await foreach (var decision in EvaluateResolvedObjectsAsync(effective.ModuleId, effective.Resources, effective.Requirements!, binding, role, targets, cancellationToken))
            yield return decision;
    }

    // 入口与固定内部调用共用完整对象角色执行，不复制OR、读取解析器或拒绝顺序。
    private async IAsyncEnumerable<DataExecutionTargetDecision> EvaluateResolvedObjectsAsync(string moduleId,
        IReadOnlyList<string> resources, IReadOnlyList<DataExecutionRequirement> requirements, DataExecutionBindingReference binding,
        string role, IReadOnlyList<DataExecutionTargetInput> targets, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var matches = requirements.Where(item => item.Stage == binding.Stage && item.Role == role
            && (item.Branch is null || item.Branch == binding.Branch)).ToArray();
        if (matches.Length != 1 || matches[0].Shape is not ("object" or "objects"))
            throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
        var requirement = matches[0];
        if (targets.Count < requirement.MinimumCount || requirement.MaximumCount is { } maximum && targets.Count > maximum
            || targets.Any(target => !requirement.Resources.Contains(target.ResourceKey, StringComparer.Ordinal)
                || !resources.Contains(target.ResourceKey, StringComparer.Ordinal)
                || string.IsNullOrWhiteSpace(target.ObjectId) && requirement.ReadResolverKey is null))
            throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
        IDataExecutionTargetResolver? resolver = null;
        if (requirement.ReadResolverKey is { } key)
        {
            var registered = _resolvers.Where(item => item.Key == key && item.ModuleId == moduleId).ToArray();
            if (registered.Length != 1) throw new InvalidOperationException("DATA_EXECUTION_TARGET_RESOLVER_INVALID");
            resolver = registered[0];
        }
        // 惰性逐项消费保留read/动作/业务拒绝的原顺序；不预先裁决后续对象并改变首个错误。
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DataExecutionResolvedTarget? resolved = target.ObjectId is null ? null : new(target.ResourceKey, target.ObjectId);
            var readAttempted = false;
            var readAllowed = false;
            var decision = await AlternativesAsync(requirement.AnyOf);
            if (decision == DataExecutionDecision.Allowed && requirement.ResourceAnyOf is not null)
                decision = await AlternativesAsync(requirement.ResourceAnyOf[target.ResourceKey]);
            yield return new(decision, resolved);

            // 单次对象/阶段内读取一次；它是当前解析事实，不是跨请求或锁阶段的允许缓存。
            async Task<DataExecutionDecision> AlternativesAsync(IReadOnlyList<IReadOnlyList<string>> branches)
            {
                foreach (var branch in branches)
                {
                    var allowed = true;
                    foreach (var action in branch)
                    {
                        if (action == "read")
                        {
                            if (!readAttempted)
                            {
                                readAttempted = true;
                                if (resolver is not null)
                                {
                                    resolved = await resolver.ResolveAsync(target, cancellationToken);
                                    if (resolved is not null && (resolved.ResourceKey != target.ResourceKey || string.IsNullOrWhiteSpace(resolved.ObjectId)
                                        || target.ObjectId is not null && target.ObjectId != resolved.ObjectId))
                                        throw new InvalidOperationException("DATA_EXECUTION_TARGET_IDENTITY_INVALID");
                                    readAllowed = resolved is not null;
                                }
                                else readAllowed = await reads.IsReadableAsync(target.ResourceKey, target.ObjectId!, cancellationToken);
                            }
                            if (!readAllowed) return DataExecutionDecision.NotFound;
                        }
                        else
                        {
                            if (resolved is null) throw new InvalidOperationException("DATA_EXECUTION_TARGET_UNRESOLVED");
                            if (!await decisions.IsAllowedAsync(resolved.ResourceKey, resolved.ObjectId, action, cancellationToken))
                            { allowed = false; break; }
                        }
                    }
                    if (allowed) return DataExecutionDecision.Allowed;
                }
                return DataExecutionDecision.Denied;
            }
        }
    }
}

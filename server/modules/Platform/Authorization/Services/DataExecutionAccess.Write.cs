using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class DataExecutionAccess
{
    /// <inheritdoc/>
    public Task<DataExecutionDecision> EvaluateCandidateAsync(DataExecutionWriteContext source, string resourceKey, string role,
        IReadOnlyDictionary<string, object?> candidate, CancellationToken token = default)
    {
        if (source.ResourceKey != resourceKey || source.ObjectId is not null)
            throw new ActionDataAccessException(400, "ActionDataAccess.WriteBindingInvalid");
        var effective = ResolveWrite(source, resourceKey);
        var requirement = Role(effective, source.Binding, role, "candidate");
        return EvaluateCandidateRequirementAsync(requirement, resourceKey, candidate, token);
    }

    /// <inheritdoc/>
    public Task<DataExecutionDecision> EvaluateObjectAsync(DataExecutionWriteContext source, string role,
        DataExecutionWriteFacts before, CancellationToken token = default)
    {
        if (source.ResourceKey != before.ResourceKey || source.ObjectId != before.ObjectId || string.IsNullOrWhiteSpace(before.ObjectId))
            throw new ActionDataAccessException(400, "ActionDataAccess.WriteBindingInvalid");
        var effective = ResolveWrite(source, before.ResourceKey);
        var requirement = Role(effective, source.Binding, role, "object");
        return EvaluateObjectRequirementAsync(requirement, before.ResourceKey, before.ObjectId, token);
    }

    // 入口保持原声明核对；内部只消费固定caller，不读取或伪造HTTP声明，也不接收允许标志。
    private ActionDataAccessRequirement ResolveWrite(DataExecutionWriteContext source, string resourceKey)
    {
        if (source.Binding.Stage != "submit" || source.Binding.Provider is not ("standardCrud.create" or "standardCrud.update" or "standardCrud.delete"))
            throw new ActionDataAccessException(400, "ActionDataAccess.WriteBindingInvalid");
        if (source.Internal is null) return Resolve([source.Binding], resourceKey);
        var binding = source.Internal;
        var contract = plan.Modules.SelectMany(module => module.DataExecutions).SingleOrDefault(item => item.Key == binding.Execution);
        var resource = resources?.Resources.SingleOrDefault(item => item.ResourceKey == resourceKey)
            ?? throw new InvalidOperationException("DATA_EXECUTION_RESOURCE_CATALOG_REQUIRED");
        if (binding.Execution != source.Binding.Execution || binding.Provider != source.Binding.Provider
            || source.Binding.Stage != "submit" || source.Binding.Branch is not null
            || contract is null || contract.Boundary != "internal" || contract.Provider != binding.Provider
            || contract.ModuleId != resource.ModuleId
            || !contract.FixedBindings.Any(item => item.Kind == "internal" && item.Identity == contract.Key && item.Caller == binding.CallerIdentity)
            || !binding.Caller.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
                .Any(method => method.Name == binding.Member)
            || !contract.Requirements.All(item => item.Resources.SequenceEqual([resourceKey])))
            throw new ActionDataAccessException(400, "ActionDataAccess.InternalBindingInvalid");
        return new(contract.Key, contract.ModuleId, "", "", "", contract.Key, [resourceKey], [], [])
        { Requirements = contract.Requirements, Provider = contract.Provider };
    }

    // 复用原现时对象读取/动作裁决，不用候选接口替代ACL或缓存前阶段允许结果。
    private async Task<DataExecutionDecision> EvaluateObjectRequirementAsync(DataExecutionRequirement requirement, string resourceKey,
        string objectId, CancellationToken cancellationToken)
    {
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

    // 创建候选只在原owner补齐完成后逐要求求值；不对新身份附加完整读取。
    private async Task<DataExecutionDecision> EvaluateCandidateRequirementAsync(DataExecutionRequirement requirement, string resourceKey,
        IReadOnlyDictionary<string, object?> candidate, CancellationToken cancellationToken)
    {
        foreach (var branch in requirement.AnyOf)
        {
            var allowed = true;
            foreach (var action in branch)
                if (!await decisions.IsCandidateAllowedAsync(resourceKey, candidate, action, cancellationToken)) { allowed = false; break; }
            if (allowed) return DataExecutionDecision.Allowed;
        }
        return DataExecutionDecision.Denied;
    }
}

using System.Runtime.CompilerServices;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Testing;

/// <summary>字段、审计和事务单元测试显式注入的执行替身；正式来源及权限由Host真实执行器测试覆盖。</summary>
internal sealed class StandardCrudTestAccess(IDataScopeObjectDecisionProvider? decisions = null,
    IDataScopeReadPlanProvider? reads = null, IDataScopeObjectAccess? objects = null) : IDataExecutionAccess, IDataScopeObjectDecisionProvider
{
    // 未消费的能力明确抛出，字段/拒绝测试不能继承生产接口的过渡实现。
    public Task<IReadOnlySet<string>> GetInternalReadableIdsAsync(DataExecutionInternalReadBinding binding,
        string resourceKey, IReadOnlyCollection<string> objectIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DataScopeReadPlan> GetInternalReadPlanAsync(DataExecutionInternalReadBinding binding,
        string resourceKey, IReadOnlyCollection<string> objectIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DataScopeReadPlan> GetInternalQueryReadPlanAsync(DataExecutionInternalReadBinding binding,
        string resourceKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DataScopeReadPlan> GetApiQueryReadPlanAsync(DataExecutionApiQueryContext context,
        string resourceKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DataScopeReadPlan> GetApiObjectReadPlanAsync(DataExecutionApiObjectContext context, string resourceKey, string objectId,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DataExecutionTargetDecision> EvaluateApiObjectAsync(DataExecutionApiObjectExecutionContext context, string role,
        DataExecutionTargetInput target, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DataExecutionTargetDecision> EvaluateInternalObjectAsync(DataExecutionInternalBinding binding, string role,
        DataExecutionTargetInput target, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken token = default) =>
        decisions?.IsAllowedAsync(resourceKey, objectId, actionKey, token) ?? Task.FromResult(true);
    public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken token = default) =>
        decisions?.IsCandidateAllowedAsync(resourceKey, candidate, actionKey, token) ?? Task.FromResult(true);

    internal static IReadOnlyDictionary<string, string> Bindings(string resource) =>
        new[] { "read", "list", "options", "export" }
            .ToDictionary(operation => operation, operation => resource + "." + operation);

    public void Validate(IReadOnlyList<DataExecutionBindingReference> bindings, string resourceKey)
    {
        if (bindings.Count == 0 || bindings.Any(binding => binding is null)) throw new InvalidOperationException("Test source required.");
    }

    public Task<DataScopeReadPlan> GetQueryReadPlanAsync(DataExecutionQueryContext context, string resourceKey, CancellationToken token = default) =>
        reads?.GetReadPlanAsync(resourceKey, token) ?? Task.FromResult(new DataScopeReadPlan(null));
    public Task<DataScopeReadPlan> GetObjectReadPlanAsync(IReadOnlyList<DataExecutionBindingReference> bindings, string resourceKey, string role, string id, CancellationToken token = default) =>
        reads?.GetReadPlanAsync(resourceKey, token) ?? Task.FromResult(new DataScopeReadPlan(null));

    public Task<DataExecutionDecision> EvaluateObjectAsync(DataExecutionBindingReference binding, string resourceKey, string role, string id, CancellationToken token = default) =>
        Existing(binding, resourceKey, id, token);
    public Task<DataExecutionDecision> EvaluateObjectFromBindingsAsync(IReadOnlyList<DataExecutionBindingReference> bindings, string resource, string role, string id, CancellationToken token = default) =>
        Existing(bindings.Single(), resource, id, token);
    public async IAsyncEnumerable<DataExecutionTargetDecision> EvaluateObjectsAsync(DataExecutionBindingReference binding, string role,
        IReadOnlyList<DataExecutionTargetInput> targets, [EnumeratorCancellation] CancellationToken token = default)
    {
        foreach (var target in targets)
            yield return new(await Existing(binding, target.ResourceKey, target.ObjectId!, token), new(target.ResourceKey, target.ObjectId!));
    }

    // 替身只转发原测试的可控决策，不装配生产provider或从缺来源选路径。
    private async Task<DataExecutionDecision> Existing(DataExecutionBindingReference binding, string resource, string id, CancellationToken token)
    {
        if (objects is not null && !await objects.IsReadableAsync(resource, id, token)
            || objects is null && decisions is not null && !await decisions.IsAllowedAsync(resource, id, "read", token)) return DataExecutionDecision.NotFound;
        return DataExecutionDecision.Allowed;
    }
}

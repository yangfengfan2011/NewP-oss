using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>业务主动检查与按钮自动检查共用完整读取约束、动作目录及原裁决引擎。</summary>
public sealed partial class ObjectActionAccess(IDataScopeResourceCatalog resources, CanonicalModulePlan plan,
    IDataScopeObjectAccess reads, IDataScopeObjectDecisionProvider decisions, Microsoft.AspNetCore.Http.IHttpContextAccessor http) : IObjectActionAccess
{
    private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _http = http;

    /// <inheritdoc/>
    public Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions,
        CancellationToken cancellationToken = default) => CheckAsyncCore(objectName, objectId, actions, null, cancellationToken);

    /// <inheritdoc/>
    public Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions,
        object readContext, CancellationToken cancellationToken = default) => CheckAsyncCore(objectName, objectId, actions, readContext, cancellationToken);

    /// <inheritdoc/>
    public async Task<DataExecutionDecision> CheckCandidateAsync(string objectName, IReadOnlyDictionary<string, object?> candidate,
        IReadOnlyList<string> actions, CancellationToken cancellationToken = default)
    {
        var resource = resources.Resources.SingleOrDefault(item => item.ModelName == objectName)
            ?? throw new ActionDataAccessException(400, "ButtonExecution.ObjectUnknown");
        if (!candidate.TryGetValue(resource.IdField, out var id) || id is not string objectId || !objectId.StartsWith(objectName + "_", StringComparison.Ordinal)
            || actions.Count == 0 || actions.Distinct(StringComparer.Ordinal).Count() != actions.Count
            || actions.Any(key => key == "read" || !plan.DataActionCatalog.Any(item => item.Key == key)
                || resources.ActionKeysFor(resource.ResourceKey) is { } applicable && !applicable.Contains(key)))
            throw new ActionDataAccessException(400, "ButtonExecution.CandidateInvalid");
        foreach (var key in actions)
            if (!await decisions.IsCandidateAllowedAsync(resource.ResourceKey, candidate, key, cancellationToken))
                return DataExecutionDecision.Denied;
        return DataExecutionDecision.Allowed;
    }

    // 读取上下文只是正式输入；完整访问服务仍对当前目标执行read及资源边界，不继承外层允许。
    private async Task<DataExecutionDecision> CheckAsyncCore(string objectName, string objectId, IReadOnlyList<string> actions,
        object? readContext, CancellationToken cancellationToken)
    {
        var resource = ObjectResource(objectName);
        ValidateObjectActions(resource, objectName, objectId, actions);
        foreach (var key in actions)
        {
            var action = plan.DataActionCatalog.Single(item => item.Key == key);
            if ((key == "read" || action.RequiresRead) && !(readContext is null
                ? await reads.IsReadableAsync(resource.ResourceKey, objectId, cancellationToken)
                : await reads.IsReadableAsync(resource.ResourceKey, objectId, readContext, cancellationToken)))
                return DataExecutionDecision.NotFound;
            if (key != "read" && !await CheckCurrentActionAsync(resource, objectId, key, cancellationToken))
                return DataExecutionDecision.Denied;
        }
        return DataExecutionDecision.Allowed;
    }

    // 普通检查与现场完整读取后的检查共用同一正式资源，不接受业务自造资源或动作目录。
    private DataScopeResource ObjectResource(string objectName) => resources.Resources.SingleOrDefault(item => item.ModelName == objectName)
        ?? throw new ActionDataAccessException(400, "ButtonExecution.ObjectUnknown");

    // 目标规范性及动作适用范围始终由平台验证，完整读取不会使未知动作变为合法。
    private void ValidateObjectActions(DataScopeResource resource, string objectName, string objectId, IReadOnlyList<string> actions)
    {
        if (string.IsNullOrWhiteSpace(objectId) || !objectId.StartsWith(objectName + "_", StringComparison.Ordinal)
            || actions.Count == 0 || actions.Distinct(StringComparer.Ordinal).Count() != actions.Count
            || actions.Any(key => !plan.DataActionCatalog.Any(item => item.Key == key)
                || resources.ActionKeysFor(resource.ResourceKey) is { } applicable && !applicable.Contains(key)))
            throw new ActionDataAccessException(400, "ButtonExecution.TargetInvalid");
    }

    // 动作启停、RequiresRead及当前意见仍归原引擎，不在连续读取作用域另算或缓存允许。
    private Task<bool> CheckCurrentActionAsync(DataScopeResource resource, string objectId, string action, CancellationToken token) =>
        decisions.IsAllowedAsync(resource.ResourceKey, objectId, action, token);
}

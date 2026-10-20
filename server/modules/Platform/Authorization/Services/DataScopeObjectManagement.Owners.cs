using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class DataScopeObjectManagement
{
    /// <inheritdoc/>
    public async Task<DataScopeAuthorizationStatus> CheckAsync(string resourceKey, string objectId,
        DataExecutionBindingReference execution, CancellationToken token = default) =>
        AuthorizationStatus(await CheckDataAsync(resourceKey, objectId, token, RequiredOwnerExecution(execution)));

    /// <inheritdoc/>
    public async Task<DataScopeAuthorizationResult<DataScopeObjectConfiguration>> ReadAsync(string resourceKey, string objectId,
        DataExecutionBindingReference execution, CancellationToken token = default)
    {
        using var operation = evaluation?.BeginOperation();
        var status = AuthorizationStatus(await CheckDataAsync(resourceKey, objectId, token, RequiredOwnerExecution(execution)));
        return status == DataScopeAuthorizationStatus.Succeeded
            ? new(status, await grants.GetAsync(resourceKey, objectId, token)) : new(status);
    }

    /// <inheritdoc/>
    public async Task<DataScopeAuthorizationResult> ReplaceAsync(string resourceKey, string objectId,
        DataScopeObjectConfiguration input, DataExecutionBindingReference execution, CancellationToken token = default)
    {
        var result = await SaveConfigurationAsync(resourceKey, objectId, input, token, RequiredOwnerExecution(execution));
        return new(AuthorizationStatus(result.Status), result.ValidationError is null ? null : Error(result.ValidationError));
    }

    /// <inheritdoc/>
    public async Task<DataScopeAuthorizationResult<DataScopeSubjectPage>> SubjectsAsync(string resourceKey, string objectId,
        string type, string? query, int page, IReadOnlyList<string> currentIds, DataExecutionBindingReference execution,
        CancellationToken token = default)
    {
        using var operation = evaluation?.BeginOperation();
        var status = AuthorizationStatus(await CheckDataAsync(resourceKey, objectId, token, RequiredOwnerExecution(execution)));
        if (status != DataScopeAuthorizationStatus.Succeeded) return new(status);
        try { return new(status, await identities.SearchAsync(type, query, page, currentIds, token)); }
        catch (RolePermissionValidationException error) { return new(DataScopeAuthorizationStatus.InvalidInput, Error: Error(error)); }
    }
    /// <inheritdoc/>
    public async Task<DataScopeAuthorizationResult<DataScopeSubjectPage>> SubjectsAsync(string resourceKey, string objectId,
        string type, string? query, int page, IReadOnlyList<string> currentIds, IReadOnlyList<DataExecutionBindingReference> sources,
        CancellationToken token = default)
    {
        using var operation = evaluation?.BeginOperation();
        var decision = await executions!.EvaluateObjectFromBindingsAsync(RequiredOwnerBindings(sources), resourceKey, "target", objectId, token);
        var status = decision switch { DataExecutionDecision.Allowed => DataScopeAuthorizationStatus.Succeeded,
            DataExecutionDecision.NotFound => DataScopeAuthorizationStatus.NotFound, _ => DataScopeAuthorizationStatus.Forbidden };
        if (status != DataScopeAuthorizationStatus.Succeeded) return new(status);
        try { return new(status, await identities.SearchAsync(type, query, page, currentIds, token)); }
        catch (RolePermissionValidationException error) { return new(DataScopeAuthorizationStatus.InvalidInput, Error: Error(error)); }
    }

    // 正式入口先拒绝空来源，不能在配置读取或目标循环前绕过来源校验。
    private static DataExecutionBindingReference RequiredOwnerExecution(DataExecutionBindingReference? execution) => execution
        ?? throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");

    // 有限辅助来源必须完整有效；空集合或空成员不是无声明请求的回退信号。
    private static IReadOnlyList<DataExecutionBindingReference> RequiredOwnerBindings(IReadOnlyList<DataExecutionBindingReference>? bindings) =>
        bindings is { Count: > 0 } && bindings.All(binding => binding is not null) ? bindings
            : throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
}

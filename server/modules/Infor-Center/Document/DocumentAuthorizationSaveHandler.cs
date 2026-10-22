using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Document;

/// <summary>Document 路由提供固定资源身份，共用授权能力拥有原 ACL 边界和实际保存。</summary>
public sealed class DocumentAuthorizationSaveHandler(IDataScopeObjectSaveHandler authorization)
    : IButtonBusinessHandler<DataScopeObjectSaveInput, DataScopeObjectSaveState, DataScopeAuthorizationResult>
{
    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult> WithinBoundaryAsync(DataScopeObjectSaveInput input, string actionKey,
        Func<DataScopeObjectSaveState, CancellationToken, Task<DataScopeAuthorizationResult>> execute, CancellationToken token)
    {
        if (input.ResourceKey != "document.document") throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch");
        return authorization.WithinBoundaryAsync(input, actionKey, execute, token);
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(DataScopeObjectSaveState state) => authorization.Target(state)!;

    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult> ExecuteAsync(DataScopeObjectSaveState state, CancellationToken token) =>
        authorization.ExecuteAsync(state, token);
}

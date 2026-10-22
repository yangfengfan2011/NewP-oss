using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Part;

/// <summary>Part 路由提供固定资源身份，共用授权能力拥有完整管理资格和实际读取。</summary>
public sealed class PartAuthorizationReadHandler(IDataScopeObjectReadHandler authorization)
    : IButtonBusinessHandler<DataScopeObjectReadInput, DataScopeObjectReadState, DataScopeAuthorizationResult<object>>
{
    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult<object>> WithinBoundaryAsync(DataScopeObjectReadInput input, string actionKey,
        Func<DataScopeObjectReadState, CancellationToken, Task<DataScopeAuthorizationResult<object>>> execute, CancellationToken token)
    {
        if (input.ResourceKey != "part.part") throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch");
        return authorization.WithinBoundaryAsync(input, actionKey, execute, token);
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(DataScopeObjectReadState state) => authorization.Target(state)!;

    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult<object>> ExecuteAsync(DataScopeObjectReadState state, CancellationToken token) =>
        authorization.ExecuteAsync(state, token);
}

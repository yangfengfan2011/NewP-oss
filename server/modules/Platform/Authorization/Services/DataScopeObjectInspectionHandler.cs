using ICP.Platform.Authorization.Contracts;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>只读业务实现与保存绑定分离，完整对象管理资格仍由同一服务主动检查。</summary>
public sealed class DataScopeObjectInspectionHandler(IDataScopeObjectReadHandler authorization)
    : IButtonBusinessHandler<DataScopeObjectReadInput, DataScopeObjectReadState, DataScopeAuthorizationResult<object>>
{
    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult<object>> WithinBoundaryAsync(DataScopeObjectReadInput input, string actionKey,
        Func<DataScopeObjectReadState, CancellationToken, Task<DataScopeAuthorizationResult<object>>> execute, CancellationToken token) =>
        authorization.WithinBoundaryAsync(input, actionKey, execute, token);

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(DataScopeObjectReadState state) => authorization.Target(state)!;

    /// <inheritdoc/>
    public Task<DataScopeAuthorizationResult<object>> ExecuteAsync(DataScopeObjectReadState state, CancellationToken token) =>
        authorization.ExecuteAsync(state, token);
}

using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Modules.Workspace;

/// <summary>编辑准备沿实际点击定义与完整出现位置，不复用前次读取或提交允许。</summary>
public sealed record WorkspaceEditValuesInput(string ActorId, WorkspacePosition Position, string LinkId);

/// <summary>表单装载保持原读取生命周期；同一规范目标交由平台检查本按钮要求。</summary>
public sealed class WorkspaceEditValuesHandler(WorkspaceContents contents)
    : IButtonBusinessHandler<WorkspaceEditValuesInput, WorkspaceMutationState, IReadOnlyDictionary<string, object?>>
{
    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, object?>> WithinBoundaryAsync(WorkspaceEditValuesInput input, string actionKey,
        Func<WorkspaceMutationState, CancellationToken, Task<IReadOnlyDictionary<string, object?>>> execute, CancellationToken cancellationToken)
    {
        var position = input.Position with { LinkIds = input.Position.LinkIds.ToArray() };
        var state = await contents.PrepareEditStateAsync(new(input.ActorId, position, [input.LinkId],
            new Dictionary<string, System.Text.Json.JsonElement>(), ObjectOperationAction.Update), cancellationToken);
        return await execute(state, cancellationToken);
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspaceMutationState state)
    {
        var target = state.Targets.Single();
        return new ButtonExecutionTarget(target.Resource, target.Model, target.Id, target.Before);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(WorkspaceMutationState state, CancellationToken cancellationToken) =>
        contents.ReadEditValuesAsync(state, cancellationToken);
}

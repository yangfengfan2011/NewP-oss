using System.Collections.Immutable;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Modules.Workspace;

/// <summary>服务端按真实路由构造对象命令；客户端不能提供规范目标或执行来源。</summary>
public sealed record WorkspaceMutationInput(string ActorId, WorkspacePosition Position, string[] LinkIds,
    IReadOnlyDictionary<string, JsonElement>? Values, ObjectOperationAction Action);
/// <summary>同一图锁及对象行保护内取得的规范对象与前事实。</summary>
public sealed record WorkspaceMutationObject(string Resource, string Model, string Id, IReadOnlyDictionary<string, object?> Before);
/// <summary>平台检查与实际写入共用原对象操作中的一次规范化结果。</summary>
public sealed record WorkspaceMutationState(WorkspaceMutationInput Input, IReadOnlyList<WorkspaceMutationObject> Targets);

/// <summary>仅由服务端处理器创建的continuation，作为原对象行为的保护输入传递，不进入HTTP DTO。</summary>
internal sealed class WorkspaceMutationContinuation
{
    private readonly string actor;
    private readonly string space;
    private readonly string root;
    private readonly ObjectOperationAction action;
    private readonly ImmutableArray<string> path;
    private readonly ImmutableArray<string> selection;
    internal Func<WorkspaceMutationState, CancellationToken, Task<StandardCrudMutationResult>> Execute { get; }

    // 私有值快照不向Before暴露容器；只冻结字典无法保护其中的位置链或选择数组。
    internal WorkspaceMutationContinuation(WorkspaceMutationInput input,
        Func<WorkspaceMutationState, CancellationToken, Task<StandardCrudMutationResult>> execute)
    {
        actor = input.ActorId;
        space = input.Position.SpaceId;
        root = input.Position.RootFolderId;
        action = input.Action;
        path = input.Position.LinkIds.ToImmutableArray();
        selection = input.LinkIds.ToImmutableArray();
        Execute = execute;
    }

    // 核心入口按值核回同一正式输入，再创建独立容器；合法业务字段仍消费Before的受控补充。
    internal WorkspaceMutationInput ReadInput(ObjectOperationContext context)
    {
        if (context.Action != action || context.Values.GetValueOrDefault("ActorId") is not string currentActor || currentActor != actor
            || context.Values.GetValueOrDefault("Position") is not WorkspacePosition position || position.SpaceId != space
            || position.RootFolderId != root || !position.LinkIds.SequenceEqual(path, StringComparer.Ordinal)
            || context.Values.GetValueOrDefault("LinkIds") is not string[] ids || !ids.SequenceEqual(selection, StringComparer.Ordinal))
            throw new InvalidOperationException("WORKSPACE_BUTTON_INPUT_INVALID");
        return new(actor, new(space, root, path.ToArray()), selection.ToArray(),
            (IReadOnlyDictionary<string, JsonElement>?)context.Values.GetValueOrDefault("Values"), action);
    }
}

/// <summary>原对象执行器持有完整事务与扩展链，平台在其核心交接点自动检查整批目标。</summary>
public sealed class WorkspaceMutationHandler(WorkspaceContents contents, Func<IObjectOperationExecutor> operations)
    : IButtonBusinessHandler<WorkspaceMutationInput, WorkspaceMutationState, StandardCrudMutationResult>
{
    internal const string ContinuationField = "ButtonMutationContinuation";

    /// <inheritdoc/>
    public Task<StandardCrudMutationResult> WithinBoundaryAsync(WorkspaceMutationInput input, string actionKey,
        Func<WorkspaceMutationState, CancellationToken, Task<StandardCrudMutationResult>> execute, CancellationToken cancellationToken)
    {
        if (input.Action != ObjectOperationAction.Delete) throw new InvalidOperationException("WORKSPACE_BUTTON_ACTION_INVALID");
        var owned = input with { Position = input.Position with { LinkIds = input.Position.LinkIds.ToArray() }, LinkIds = input.LinkIds.ToArray() };
        return operations().ExecuteAsync(WorkspaceObjectMutation.BehaviorDefinition.ObjectKey, owned.Action, null,
            new Dictionary<string, object?> { ["ActorId"] = owned.ActorId, ["Position"] = owned.Position,
                ["LinkIds"] = owned.LinkIds, ["Values"] = owned.Values,
                [ContinuationField] = new WorkspaceMutationContinuation(owned, execute) }, cancellationToken);
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspaceMutationState state) => new ButtonTargetSet(state.Targets.Select(target =>
        (ButtonObjectTarget)new ButtonExecutionTarget(target.Resource, target.Model, target.Id, target.Before)).ToArray());

    /// <inheritdoc/>
    public Task<StandardCrudMutationResult> ExecuteAsync(WorkspaceMutationState state, CancellationToken cancellationToken) =>
        contents.ExecuteButtonMutationAsync(state, operations(), cancellationToken);
}

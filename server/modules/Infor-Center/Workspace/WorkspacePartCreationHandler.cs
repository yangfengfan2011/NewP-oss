using System.Collections.Immutable;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Modules.Workspace;

/// <summary>当前出现位置与Part业务字段；不接受客户端候选或内部来源。</summary>
public sealed record WorkspacePartCreationInput(string ActorId, WorkspacePosition Position, IReadOnlyDictionary<string, JsonElement> Values);

/// <summary>实际Part writer交出的同一候选与唯一持久化续体，不公开为请求DTO。</summary>
public sealed class WorkspacePartCreationState
{
    public IReadOnlyDictionary<string, object?> Candidate { get; }
    internal Func<CancellationToken, Task<StandardCrudMutationResult>> Persist { get; }
    // 状态只在当前root的实际Part创建回调中形成，不由HTTP或Before补入。
    internal WorkspacePartCreationState(IReadOnlyDictionary<string, object?> candidate, Func<CancellationToken, Task<StandardCrudMutationResult>> persist)
    { Candidate = candidate; Persist = persist; }
}

/// <summary>保留原root/Part对象执行链与图事务，平台独占候选检查到实际写入的衔接。</summary>
public sealed class WorkspacePartCreationHandler(Func<IObjectOperationExecutor> operations)
    : IButtonBusinessHandler<WorkspacePartCreationInput, WorkspacePartCreationState, StandardCrudMutationResult>
{
    internal const string ContinuationField = "PartCreationContinuation";
    /// <inheritdoc/>
    public async Task<StandardCrudMutationResult> WithinBoundaryAsync(WorkspacePartCreationInput input, string actionKey,
        Func<WorkspacePartCreationState, CancellationToken, Task<StandardCrudMutationResult>> execute, CancellationToken cancellationToken)
    {
        var owned = input with { Position = input.Position with { LinkIds = input.Position.LinkIds.ToArray() } };
        var result = await operations().ExecuteAsync(WorkspaceObjectCreation.BehaviorDefinition.ObjectKey, WorkspaceObjectCreation.CreatePart, null,
            new Dictionary<string, object?> { ["ActorId"] = owned.ActorId, ["Position"] = owned.Position, ["Values"] = owned.Values,
                [ContinuationField] = new WorkspacePartCreationContinuation(owned, execute) }, cancellationToken);
        if (!result.Succeeded) throw new WorkspaceCreationRejectedException(result.Error!);
        return result;
    }
    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspacePartCreationState state) => new ButtonCreationTarget("part.part", "Part", state.Candidate);
    /// <inheritdoc/>
    public Task<StandardCrudMutationResult> ExecuteAsync(WorkspacePartCreationState state, CancellationToken cancellationToken) => state.Persist(cancellationToken);
}

internal sealed class WorkspacePartCreationContinuation
{
    private readonly string actor;
    private readonly string space;
    private readonly string root;
    private readonly ImmutableArray<string> path;
    internal Func<WorkspacePartCreationState, CancellationToken, Task<StandardCrudMutationResult>> Execute { get; }
    // 路径的私有快照不与Before.Values的可变容器共享，候选随后只由实际writer生成。
    internal WorkspacePartCreationContinuation(WorkspacePartCreationInput input,
        Func<WorkspacePartCreationState, CancellationToken, Task<StandardCrudMutationResult>> execute)
    {
        actor = input.ActorId; space = input.Position.SpaceId; root = input.Position.RootFolderId;
        path = input.Position.LinkIds.ToImmutableArray(); Execute = execute;
    }
    // 同一保护输入按值核验；业务字段保留原Before补充语义。
    internal WorkspacePartCreationInput ReadInput(ObjectOperationContext context)
    {
        if (context.Action != WorkspaceObjectCreation.CreatePart || context.Values.GetValueOrDefault("ActorId") is not string current || current != actor
            || context.Values.GetValueOrDefault("Position") is not WorkspacePosition position || position.SpaceId != space || position.RootFolderId != root
            || !position.LinkIds.SequenceEqual(path, StringComparer.Ordinal)) throw new InvalidOperationException("WORKSPACE_BUTTON_INPUT_INVALID");
        return new(actor, new(space, root, path.ToArray()), (IReadOnlyDictionary<string, JsonElement>)context.Values["Values"]!);
    }
}

// 字段准备失败仍保留原错误契约；未形成候选不调用平台目标检查，也不能把失败伪作成功boundary。
internal sealed class WorkspaceCreationRejectedException(StandardCrudError error) : Exception(error.Code)
{
    internal StandardCrudError Error { get; } = error;
}

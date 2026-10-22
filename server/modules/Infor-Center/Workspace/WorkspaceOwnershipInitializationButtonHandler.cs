using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Ownership;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Workspace;

/// <summary>真实HTTP入口固定操作，客户端只提供完整位置、LID选择及可选人工账号。</summary>
public sealed record WorkspaceInitializationButtonInput(string ActorId, WorkspacePosition Position, string[] LinkIds,
    string Operation, string? AccountId = null);

/// <summary>原维护边界中的实际来源、逐项规范事实及生命周期，不承载允许结果。</summary>
public sealed class WorkspaceInitializationButtonState
{
    private readonly WorkspaceContents owner;
    private readonly Guid? transactionId;
    private bool active = true;
    private bool preparing;
    private bool prepared;
    private bool consumed;
    private readonly List<EntityOwnershipInitializationObject> objects = [];
    public WorkspaceInitializationButtonInput Input { get; }
    internal IReadOnlyList<LinkObjectReference> References { get; }
    public IReadOnlyList<EntityOwnershipInitializationObject> Objects => objects;
    internal bool BusinessStarted { get; private set; }
    internal bool PreparationFailed { get; private set; }

    // 仅真实来源规范化入口创建状态，图及事务由原维护方法持有。
    internal WorkspaceInitializationButtonState(WorkspaceContents owner, WorkspaceInitializationButtonInput input,
        IReadOnlyList<LinkObjectReference> references, Guid? transactionId)
    { this.owner = owner; Input = input; References = references; this.transactionId = transactionId; }

    // 准备只开始一次，外逃或释放原事务之后不能继续准备目标。
    internal void BeginPreparation(WorkspaceContents currentOwner, Guid? currentTransaction)
    {
        RequireActive(currentOwner, currentTransaction);
        if (preparing) throw new InvalidOperationException("WORKSPACE_OWNERSHIP_STATE_INVALID");
        preparing = true;
    }
    // 所有异步读取两侧均复核原状态，不将序列寿命扩展到回调外。
    internal void RequireActive(WorkspaceContents currentOwner, Guid? currentTransaction)
    {
        if (!active || consumed || !ReferenceEquals(owner, currentOwner) || transactionId is not null && transactionId != currentTransaction)
            throw new InvalidOperationException("WORKSPACE_OWNERSHIP_STATE_INVALID");
    }
    // 这里只记录正式行事实，不记录平台的允许结果。
    internal void AddPrepared(EntityOwnershipInitializationObject item) => objects.Add(item);
    // 提前Dispose不等同完整选择已规范化。
    internal void CompletePreparation() => prepared = objects.Count == References.Count;
    // 保留业务读取故障来源，不能将它误译为yield之后的平台edit拒绝。
    internal void FailPreparation() => PreparationFailed = true;
    // 仅用于区分业务开始后的异常，不参与允许或跳过判断。
    internal void BeginBusiness() => BusinessStarted = true;
    // 只有原写事务及完整规范化状态能提交一次，状态读取不能成为提交凭据。
    internal void Consume(WorkspaceContents currentOwner, Guid? currentTransaction)
    {
        RequireActive(currentOwner, currentTransaction);
        if (!prepared || transactionId is null || transactionId != currentTransaction)
            throw new InvalidOperationException("WORKSPACE_OWNERSHIP_STATE_INVALID");
        consumed = true;
    }
    // 正常返回、取消及故障均立即终结本次状态。
    internal void End() => active = false;
}

/// <summary>维护保留Link业务read及单项/批次各自原约束顺序，平台只负责当前按钮的实际对象要求。</summary>
public sealed class WorkspaceOwnershipInitializationButtonHandler(WorkspaceContents contents, EntityOwnershipBackfill backfill)
    : IButtonBusinessHandler<WorkspaceInitializationButtonInput, WorkspaceInitializationButtonState, IResult>
{
    /// <inheritdoc/>
    public Task<IResult> WithinBoundaryAsync(WorkspaceInitializationButtonInput input, string actionKey,
        Func<WorkspaceInitializationButtonState, CancellationToken, Task<IResult>> execute, CancellationToken cancellationToken)
    {
        if (actionKey is not ("initializeOwnership" or "initializeOwnershipPending") || input.Operation is not ("status" or "single" or "batch")
            || (input.Operation is "status" or "single") && input.LinkIds.Length != 1)
            throw new ActionDataAccessException(400, "ButtonExecution.BindingInvalid");
        var owned = input with { Position = input.Position with { LinkIds = input.Position.LinkIds.ToArray() }, LinkIds = input.LinkIds.ToArray() };
        return contents.WithinOwnershipInitializationAsync(owned, (state, token) => InvokeCheckedAsync(state, execute, token), cancellationToken);
    }
    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspaceInitializationButtonState state) =>
        new ButtonTargetSequence(token => contents.PrepareOwnershipInitializationTargetsAsync(state, token));
    /// <inheritdoc/>
    public Task<IResult> ExecuteAsync(WorkspaceInitializationButtonState state, CancellationToken cancellationToken)
    {
        state.BeginBusiness();
        return contents.ExecuteOwnershipInitializationAsync(state, backfill, cancellationToken);
    }

    // 原Link读取已履行；只保留平台edit阶段的403，业务read/root/账号/审计故障保持原来源。
    private static async Task<IResult> InvokeCheckedAsync(WorkspaceInitializationButtonState state,
        Func<WorkspaceInitializationButtonState, CancellationToken, Task<IResult>> execute, CancellationToken token)
    {
        try { return await execute(state, token); }
        catch (ActionDataAccessException exception) when (!state.BusinessStarted && !state.PreparationFailed
            && (exception.Status == 404 && exception.Code == "StandardCrud.NotFound"
                || exception.Status == 403 && exception.Code == "DataScope.ActionDenied"))
        { throw new LinkQueryException(403, "Workspace.ObjectWriteDenied"); }
    }
}

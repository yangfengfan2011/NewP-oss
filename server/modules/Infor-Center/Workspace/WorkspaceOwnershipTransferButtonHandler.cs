using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Ownership;
using ICP.Platform.Persistence.Ownership;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Workspace;

/// <summary>正式来源只包含真实父位置及LID选择；操作由实际服务端路由固定，接收账号不作为对象目标。</summary>
public sealed record WorkspaceTransferButtonInput(string ActorId, WorkspacePosition Position, string[] LinkIds,
    string Operation, string? RecipientId = null, string? Query = null);
/// <summary>规范目标及业务输入留在同一图锁和归属边界内，不保存独立允许结果。</summary>
public sealed record WorkspaceTransferButtonState(WorkspaceTransferButtonInput Input, EntityOwnershipTransferState Transfer)
{
    internal bool BusinessStarted { get; private set; }
    // 只标记业务异常来源，避免将接收及持久化异常误译为目标判权拒绝。
    internal void BeginBusiness() => BusinessStarted = true;
}

/// <summary>混合选择按每个实际对象匹配按钮要求；所有目标通过后才读取接收账号及写入归属。</summary>
public sealed class WorkspaceOwnershipTransferButtonHandler(WorkspaceContents contents, EntityOwnershipTransfer transfer,
    IAccountOwnershipDirectory accounts) : IButtonBusinessHandler<WorkspaceTransferButtonInput, WorkspaceTransferButtonState, IResult>
{
    /// <inheritdoc/>
    public Task<IResult> WithinBoundaryAsync(WorkspaceTransferButtonInput input, string actionKey,
        Func<WorkspaceTransferButtonState, CancellationToken, Task<IResult>> execute, CancellationToken cancellationToken)
    {
        if (actionKey is not ("transfer" or "transferSelected") || input.Operation is not ("eligibility" or "accounts" or "transfer"))
            throw new ActionDataAccessException(400, "ButtonExecution.BindingInvalid");
        var owned = input with { Position = input.Position with { LinkIds = input.Position.LinkIds.ToArray() }, LinkIds = input.LinkIds.ToArray() };
        return contents.WithinOwnershipTransferAsync(owned, transfer, (state, token) => InvokeCheckedAsync(state, execute, token), cancellationToken);
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspaceTransferButtonState state) => new ButtonTargetSet(state.Transfer.Objects.Select(target =>
        (ButtonObjectTarget)new ButtonExecutionTarget(target.ResourceKey, target.ObjectName, target.ObjectId, target.Before)).ToArray());

    /// <inheritdoc/>
    public async Task<IResult> ExecuteAsync(WorkspaceTransferButtonState state, CancellationToken cancellationToken)
    {
        state.BeginBusiness();
        return state.Input.Operation switch
        {
            "eligibility" => Results.Ok(new { allowed = true }),
            "accounts" => Results.Ok(await accounts.SearchAsync(state.Input.Query, cancellationToken)),
            "transfer" => Results.Ok(await transfer.CommitAsync(state.Transfer, cancellationToken)),
            _ => throw new ActionDataAccessException(400, "ButtonExecution.BindingInvalid")
        };
    }

    // 完整Link读取已先履行；保留原edit拒绝403，只转换所属人确认之后、实际业务之前的判权拒绝。
    private static async Task<IResult> InvokeCheckedAsync(WorkspaceTransferButtonState state,
        Func<WorkspaceTransferButtonState, CancellationToken, Task<IResult>> execute, CancellationToken token)
    {
        try { return await execute(state, token); }
        catch (ActionDataAccessException exception) when (!state.BusinessStarted
            && (exception.Status == 404 && exception.Code == "StandardCrud.NotFound"
                || exception.Status == 403 && exception.Code == "DataScope.ActionDenied"))
        {
            throw new EntityOwnershipException(403, "EntityOwnership.TransferDenied");
        }
    }
}

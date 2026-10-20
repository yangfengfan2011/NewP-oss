using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Persistence.Ownership;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Crud.Ownership;

/// <summary>实际路由选定准备、账号查询或提交；接收账号不作为对象目标。</summary>
public sealed record ObjectTransferButtonInput(string ObjectId, string Operation, string? RecipientId = null, string? Query = null);
/// <summary>按钮与实际提交共用原所属人及保护范围内的同一状态。</summary>
public sealed record ObjectTransferButtonState(ObjectTransferButtonInput Input, EntityOwnershipTransferState Transfer)
{
    internal bool BusinessStarted { get; private set; }
    // 只证明本次实际业务已经进入；用于区分平台目标拒绝与后续业务异常，不参与任何允许或跳过判断。
    internal void BeginBusiness() => BusinessStarted = true;
}

/// <summary>独立对象入口复用同一归属边界，实际资源由所属模块固定，平台执行本次按钮唯一要求。</summary>
public abstract class ObjectOwnershipTransferButtonHandler(string resourceKey, EntityOwnershipTransfer transfer, IAccountOwnershipDirectory accounts)
    : IButtonBusinessHandler<ObjectTransferButtonInput, ObjectTransferButtonState, IResult>
{
    /// <inheritdoc/>
    public Task<IResult> WithinBoundaryAsync(ObjectTransferButtonInput input, string actionKey,
        Func<ObjectTransferButtonState, CancellationToken, Task<IResult>> execute, CancellationToken cancellationToken)
    {
        if (actionKey != "transfer") throw new ActionDataAccessException(400, "ButtonExecution.BindingInvalid");
        EntityOwnershipTransferTarget[] targets = [new(resourceKey, input.ObjectId)];
        return input.Operation switch
        {
            "transfer" => transfer.WithinTransferAsync(targets, input.RecipientId ?? "",
                (state, token) => InvokeCheckedAsync(new(input, state), execute, token), cancellationToken),
            "eligibility" or "accounts" => transfer.WithinPreparationAsync(targets,
                (state, token) => InvokeCheckedAsync(new(input, state), execute, token), cancellationToken),
            _ => throw new ActionDataAccessException(400, "ButtonExecution.BindingInvalid")
        };
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(ObjectTransferButtonState state)
    {
        var target = state.Transfer.Objects.Single();
        return new ButtonExecutionTarget(target.ResourceKey, target.ObjectName, target.ObjectId, target.Before);
    }

    /// <inheritdoc/>
    public async Task<IResult> ExecuteAsync(ObjectTransferButtonState state, CancellationToken cancellationToken)
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

    // 所属人事实已经核实；只保留实际业务开始前的目标拒绝403，接收人/Receive及基础设施异常原样传播。
    private static async Task<IResult> InvokeCheckedAsync(ObjectTransferButtonState state,
        Func<ObjectTransferButtonState, CancellationToken, Task<IResult>> execute, CancellationToken token)
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

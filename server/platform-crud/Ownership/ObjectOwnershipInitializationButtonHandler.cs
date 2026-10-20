using System.Runtime.CompilerServices;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Crud.Ownership;

/// <summary>对象身份来自真实路由或已选ID集合；操作与人工账号由当前服务端端点固定。</summary>
public sealed record ObjectInitializationButtonInput(string[] ObjectIds, string Operation, string? AccountId = null);
/// <summary>当前按钮与实际补齐共用同一状态，业务开始标记只用于保留目标阶段的原错误外观。</summary>
public sealed record ObjectInitializationButtonState(ObjectInitializationButtonInput Input, EntityOwnershipInitializationState Initialization)
{
    internal bool BusinessStarted { get; private set; }
    // 不参与授权，只区分平台目标拒绝和后续真实业务异常。
    internal void BeginBusiness() => BusinessStarted = true;
}

/// <summary>模块固定资源的归属维护适配；AdminOnly由原入口保留，平台自动检查当前实际目标集合。</summary>
public abstract class ObjectOwnershipInitializationButtonHandler(string resourceKey, IStandardCrudResourceRegistry resources,
    EntityOwnershipMaintenance maintenance) : IButtonBusinessHandler<ObjectInitializationButtonInput, ObjectInitializationButtonState, IResult>
{
    /// <inheritdoc/>
    public Task<IResult> WithinBoundaryAsync(ObjectInitializationButtonInput input, string actionKey,
        Func<ObjectInitializationButtonState, CancellationToken, Task<IResult>> execute, CancellationToken cancellationToken)
    {
        if (actionKey is not ("initializeOwnership" or "initializeOwnershipPending") || input.Operation is not ("status" or "single" or "batch")
            || (input.Operation is "status" or "single") && input.ObjectIds.Length != 1)
            throw new ActionDataAccessException(400, "ButtonExecution.BindingInvalid");
        var descriptor = resources.FindByResourceKey(resourceKey) ?? throw new EntityOwnershipException(400, "EntityOwnership.InvalidSelection");
        var owned = input with { ObjectIds = input.ObjectIds.ToArray() };
        return owned.Operation == "status"
            ? maintenance.WithinStatusAsync(descriptor, owned.ObjectIds[0], (state, token) => InvokeCheckedAsync(new(owned, state), execute, token), cancellationToken)
            : maintenance.WithinInitializationAsync(descriptor, owned.ObjectIds, (state, token) => InvokeCheckedAsync(new(owned, state), execute, token), cancellationToken);
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(ObjectInitializationButtonState state) => new ButtonTargetSequence(token => TargetsAsync(state, token));

    // 实际业务逐项read和规范事实，平台完成当前对象edit后才拉取下一项，不预读后项抢首错。
    private async IAsyncEnumerable<ButtonObjectTarget> TargetsAsync(ObjectInitializationButtonState state,
        [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var target in maintenance.PrepareTargetsAsync(state.Initialization, token))
            yield return new ButtonExecutionTarget(target.ResourceKey, target.ObjectName, target.ObjectId, target.Before);
    }

    /// <inheritdoc/>
    public async Task<IResult> ExecuteAsync(ObjectInitializationButtonState state, CancellationToken cancellationToken)
    {
        state.BeginBusiness();
        if (state.Input.Operation == "single") return Results.Ok(new { succeeded = true,
            changed = await maintenance.InitializeOneAsync(state.Initialization, state.Input.AccountId, cancellationToken) });
        if (state.Input.Operation == "batch") return Results.Ok(await maintenance.InitializeSelectedAsync(state.Initialization, cancellationToken));
        var row = state.Initialization.Objects.Single().Before;
        return Results.Ok(new EntityOwnershipState(row.GetValueOrDefault("OwnerId") is null || row.GetValueOrDefault("OwnerDepartmentId") is null,
            row.GetValueOrDefault("OwnerId") as string, row.GetValueOrDefault("OwnerDepartmentId") as string));
    }

    // 原维护404/403只翻译平台目标阶段；进入补齐后的账号、审计及持久化故障保持原来源。
    private static async Task<IResult> InvokeCheckedAsync(ObjectInitializationButtonState state,
        Func<ObjectInitializationButtonState, CancellationToken, Task<IResult>> execute, CancellationToken token)
    {
        try { return await execute(state, token); }
        catch (ActionDataAccessException exception) when (!state.BusinessStarted && !state.Initialization.PreparationFailed && exception.Status == 404 && exception.Code == "StandardCrud.NotFound")
        { throw new EntityOwnershipException(404, "EntityOwnership.TargetUnavailable"); }
        catch (ActionDataAccessException exception) when (!state.BusinessStarted && !state.Initialization.PreparationFailed && exception.Status == 403 && exception.Code == "DataScope.ActionDenied")
        { throw new EntityOwnershipException(403, "EntityOwnership.EditDenied"); }
    }
}

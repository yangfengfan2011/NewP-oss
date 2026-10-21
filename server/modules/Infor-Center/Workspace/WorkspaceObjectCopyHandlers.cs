using System.Collections.Frozen;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Models;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Abstractions;

namespace ICP.Platform.Modules.Workspace;

/// <summary>原图事务内的规范来源与实际业务续体，不进入HTTP输入，也不保存允许结果。</summary>
public sealed class WorkspaceObjectCopyState<TResult> : IDisposable
{
    private readonly EfPlatformDbContext db;
    private readonly IPlatformTransactionExecutor transactions;
    private readonly Guid transactionId;
    private readonly Func<CancellationToken, Task<TResult>> execute;
    private bool active = true;
    internal ButtonBusinessTarget Source { get; }

    // 仅原Workspace图事务能创建状态，登记当前事务而非任何允许结果。
    internal WorkspaceObjectCopyState(EfPlatformDbContext db, IPlatformTransactionExecutor transactions,
        ButtonBusinessTarget source, Func<CancellationToken, Task<TResult>> execute)
    {
        this.db = db; this.transactions = transactions; Source = source; this.execute = execute;
        transactionId = db.Database.CurrentTransaction?.TransactionId ?? throw new InvalidOperationException("WORKSPACE_COPY_TRANSACTION_REQUIRED");
    }

    // 同一状态只在原事务和当前调用范围内交给平台，不能保留到新事务复用。
    internal Task<TResult> WithinAsync(Func<WorkspaceObjectCopyState<TResult>, CancellationToken, Task<TResult>> continuation, CancellationToken token)
    { RequireActive(); return continuation(this, token); }

    // 平台完成来源要求后执行同一状态中原有业务，不接受替代目标或另一个持久化闭包。
    internal Task<TResult> ExecuteAsync(CancellationToken token)
    { RequireActive(); return execute(token); }

    // 不允许在离开图事务后或另一事务中继续本次状态。
    private void RequireActive()
    {
        if (!active || !transactions.IsActive || db.Database.CurrentTransaction?.TransactionId != transactionId)
            throw new InvalidOperationException("WORKSPACE_COPY_BOUNDARY_EXPIRED");
    }

    /// <inheritdoc/>
    public void Dispose() => active = false;
}

/// <summary>平台自动检查实际来源的真副本要求，准备链继续拥有原位置、快照和恢复记录。</summary>
public sealed class WorkspaceObjectCopyPreparationHandler
    : IButtonBusinessHandler<WorkspaceObjectCopyState<WorkspaceObjectCopyPreparation>, WorkspaceObjectCopyState<WorkspaceObjectCopyPreparation>, WorkspaceObjectCopyPreparation>
{
    /// <inheritdoc/>
    public Task<WorkspaceObjectCopyPreparation> WithinBoundaryAsync(WorkspaceObjectCopyState<WorkspaceObjectCopyPreparation> input, string actionKey,
        Func<WorkspaceObjectCopyState<WorkspaceObjectCopyPreparation>, CancellationToken, Task<WorkspaceObjectCopyPreparation>> execute, CancellationToken token) => input.WithinAsync(execute, token);

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspaceObjectCopyState<WorkspaceObjectCopyPreparation> state) => state.Source;

    /// <inheritdoc/>
    public Task<WorkspaceObjectCopyPreparation> ExecuteAsync(WorkspaceObjectCopyState<WorkspaceObjectCopyPreparation> state, CancellationToken token) => state.ExecuteAsync(token);
}

/// <summary>只在原prepare或最终root的图锁状态中自动检查来源，未提交业务仍由原续体拥有。</summary>
public sealed class WorkspaceObjectCopyHandler
    : IButtonBusinessHandler<WorkspaceObjectCopyState<StandardCrudMutationResult>, WorkspaceObjectCopyState<StandardCrudMutationResult>, StandardCrudMutationResult>
{
    /// <inheritdoc/>
    public Task<StandardCrudMutationResult> WithinBoundaryAsync(WorkspaceObjectCopyState<StandardCrudMutationResult> input, string actionKey,
        Func<WorkspaceObjectCopyState<StandardCrudMutationResult>, CancellationToken, Task<StandardCrudMutationResult>> execute, CancellationToken token) => input.WithinAsync(execute, token);
    /// <inheritdoc/>
    public ButtonBusinessTarget Target(WorkspaceObjectCopyState<StandardCrudMutationResult> state) => state.Source;
    /// <inheritdoc/>
    public Task<StandardCrudMutationResult> ExecuteAsync(WorkspaceObjectCopyState<StandardCrudMutationResult> state, CancellationToken token) => state.ExecuteAsync(token);
}


public sealed partial class WorkspaceContents
{
    // 同一图事务持有状态，平台只消费实际来源；拒绝反馈保持原Workspace语义。
    private async Task<WorkspaceObjectCopyPreparation> ExecuteCopyPreparationButtonAsync(ButtonBusinessTarget source,
        Func<CancellationToken, Task<WorkspaceObjectCopyPreparation>> prepare, CancellationToken token)
    {
        using var state = new WorkspaceObjectCopyState<WorkspaceObjectCopyPreparation>(db, transactions, source, prepare);
        try { return await copyButtons.ExecuteAsync(copyPreparationHandler, state, token); }
        catch (ActionDataAccessException exception) when (exception.Code is "DataScope.ActionDenied" or "StandardCrud.NotFound")
        { throw CopySourceProblem(exception); }
    }

    // 原preflight或实际root拥有完整生命周期，只有pending分支进入这一次平台检查。
    private async Task<StandardCrudMutationResult> ExecuteCopyButtonAsync(ButtonBusinessTarget source,
        Func<CancellationToken, Task<StandardCrudMutationResult>> execute, CancellationToken token)
    {
        using var state = new WorkspaceObjectCopyState<StandardCrudMutationResult>(db, transactions, source, execute);
        try { return await copyButtons.ExecuteAsync(copyHandler, state, token); }
        catch (ActionDataAccessException exception) when (exception.Code is "DataScope.ActionDenied" or "StandardCrud.NotFound")
        { throw CopySourceProblem(exception); }
    }

    // 规范LID已解析真实对象；访问引擎仍从数据库重验事实，不伪造完整字段或扫描同类型ID。
    private static ButtonExecutionTarget ObjectCopySourceTarget(LinkObjectReference source) =>
        new(Resource(source.ModelKey), source.ModelKey, source.Id,
            new Dictionary<string, object?> { ["EID"] = source.Id }.ToFrozenDictionary(StringComparer.Ordinal));

    // 只翻译平台实际来源裁决，身份/绑定错误和依赖故障原样保留。
    private static LinkQueryException CopySourceProblem(ActionDataAccessException exception) =>
        new(exception.Status, exception.Status == 404 ? "Workspace.TargetUnavailable" : "Workspace.ObjectCopyDenied");
}

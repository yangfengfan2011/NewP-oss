using System.Runtime.CompilerServices;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Ownership;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

public sealed partial class WorkspaceContents
{
    // status不添加图锁；写入保留原事务、审计作用域及图锁，然后只解析原完整位置/LID引用。
    internal async Task<IResult> WithinOwnershipInitializationAsync(WorkspaceInitializationButtonInput input,
        Func<WorkspaceInitializationButtonState, CancellationToken, Task<IResult>> execute, CancellationToken token)
    {
        if (input.Operation == "status") return await RunOwnershipInitializationBoundaryAsync(input, execute, token);
        return await transactions.ExecuteAsync<IResult>(async cancellation =>
        {
            if (input.Operation == "batch" && (input.LinkIds.Length == 0 || input.LinkIds.Any(string.IsNullOrWhiteSpace)
                || input.LinkIds.Distinct(StringComparer.Ordinal).Count() != input.LinkIds.Length))
                throw new LinkQueryException(400, "EntityOwnership.InvalidSelection");
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            return await RunOwnershipInitializationBoundaryAsync(input, execute, cancellation);
        }, token);
    }

    // 批次仍在任何目标判权前核整份LID所属父；不提前加载后项对象事实。
    private async Task<IResult> RunOwnershipInitializationBoundaryAsync(WorkspaceInitializationButtonInput input,
        Func<WorkspaceInitializationButtonState, CancellationToken, Task<IResult>> execute, CancellationToken token)
    {
        if (input.Operation != "batch") ValidateSelection(input.LinkIds);
        var path = await positions.ResolveAsync(input.ActorId, input.Position, token);
        var targets = input.Operation == "batch" ? await ReadOwnershipSelectionAsync(path.FolderId, input.LinkIds, token)
            : await ReadMutationTargetsAsync(path.FolderId, input.LinkIds, token);
        var state = new WorkspaceInitializationButtonState(this, input, targets,
            input.Operation == "status" ? null : db.Database.CurrentTransaction!.TransactionId);
        try { return await execute(state, token); }
        finally { state.End(); }
    }

    // 批次原顺序是每模型read/root再该模型edit；单对象原顺序是read/edit之后才排除根，不能互套。
    internal async IAsyncEnumerable<ButtonObjectTarget> PrepareOwnershipInitializationTargetsAsync(WorkspaceInitializationButtonState state,
        [EnumeratorCancellation] CancellationToken token)
    {
        state.BeginPreparation(this, db.Database.CurrentTransaction?.TransactionId);
        if (state.Input.Operation == "batch")
        {
            foreach (var group in state.References.GroupBy(target => target.ModelKey))
            {
                await PrepareOwnershipGroupAsync(state, group.Key, group.Select(target => target.Id).ToArray(), token);
                foreach (var target in group)
                {
                    var item = await PrepareOwnershipObjectAsync(state, target, token);
                    state.AddPrepared(item);
                    yield return new ButtonExecutionTarget(item.ResourceKey, item.ObjectName, item.ObjectId, item.Before);
                }
            }
        }
        else
        {
            var target = state.References.Single();
            await PrepareOwnershipEndpointReadAsync(state, target, token);
            var item = await PrepareOwnershipObjectAsync(state, target, token);
            state.AddPrepared(item);
            yield return new ButtonExecutionTarget(item.ResourceKey, item.ObjectName, item.ObjectId, item.Before);
            await CompleteOwnershipSingleTargetAsync(state, target, token);
        }
        state.RequireActive(this, db.Database.CurrentTransaction?.TransactionId);
        state.CompletePreparation();
    }

    // 当前模型的原SQL端点read/root约束只在该组轮到时履行，不抢下一组首错。
    private async Task PrepareOwnershipGroupAsync(WorkspaceInitializationButtonState state, string model, string[] ids, CancellationToken token)
    {
        try
        {
            state.RequireActive(this, db.Database.CurrentTransaction?.TransactionId);
            _ = Resource(model);
            await RequireOwnershipReadableTargetsAsync(model, ids, token);
            state.RequireActive(this, db.Database.CurrentTransaction?.TransactionId);
        }
        catch { state.FailPreparation(); throw; }
    }

    // 单对象仍消费实际Link端点完整read，不新增独立EID的额外资格。
    private async Task PrepareOwnershipEndpointReadAsync(WorkspaceInitializationButtonState state, LinkObjectReference target, CancellationToken token)
    {
        try
        {
            state.RequireActive(this, db.Database.CurrentTransaction?.TransactionId);
            if (!await reader.IsEndpointReadableAsync("workspace.content", target, token)) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            state.RequireActive(this, db.Database.CurrentTransaction?.TransactionId);
        }
        catch { state.FailPreparation(); throw; }
    }

    // 本次该对象的完整事实只在轮到它时读取；平台尚未通过前不读账号或进行补齐。
    private async Task<EntityOwnershipInitializationObject> PrepareOwnershipObjectAsync(WorkspaceInitializationButtonState state,
        LinkObjectReference target, CancellationToken token)
    {
        try
        {
            state.RequireActive(this, db.Database.CurrentTransaction?.TransactionId);
            var row = await db.Set<Dictionary<string, object?>>(target.ModelKey).AsNoTracking()
                .SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == target.Id, token)
                ?? throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            state.RequireActive(this, db.Database.CurrentTransaction?.TransactionId);
            return new(Resource(target.ModelKey), target.ModelKey, target.Id, row);
        }
        catch { state.FailPreparation(); throw; }
    }

    // 单对象根限制保留在原read/edit之后；它仍在原边界内，失败不会进入补齐。
    private async Task CompleteOwnershipSingleTargetAsync(WorkspaceInitializationButtonState state, LinkObjectReference target, CancellationToken token)
    {
        try { state.RequireActive(this, db.Database.CurrentTransaction?.TransactionId); await RequireNonRootMutationTargetAsync(target, token); }
        catch { state.FailPreparation(); throw; }
    }

    // 全部目标经平台检查后才进入原Backfill行锁及当前事实重读；四审计字段由原NULL-only写保持。
    internal async Task<IResult> ExecuteOwnershipInitializationAsync(WorkspaceInitializationButtonState state, EntityOwnershipBackfill backfill, CancellationToken token)
    {
        state.RequireActive(this, db.Database.CurrentTransaction?.TransactionId);
        if (state.Input.Operation == "status")
        {
            var row = state.Objects.Single().Before;
            var owner = row.GetValueOrDefault("OwnerId") as string; var department = row.GetValueOrDefault("OwnerDepartmentId") as string;
            return Results.Ok(new WorkspaceOwnershipState(owner is null || department is null, owner, department, row.GetValueOrDefault("CreatedBy") as string));
        }
        state.Consume(this, db.Database.CurrentTransaction?.TransactionId);
        try
        {
            if (state.Input.Operation == "single")
            {
                var target = state.References.Single();
                return Results.Ok(new { succeeded = true, changed = await backfill.InitializeAsync(target.ModelKey,
                    Resource(target.ModelKey), target.Id, state.Input.AccountId, token) });
            }
            var changed = 0; var unresolved = 0;
            foreach (var group in state.References.Distinct().GroupBy(target => target.ModelKey).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var result = await backfill.InitializeManyAsync(group.Key, Resource(group.Key), group.Select(target => target.Id).ToArray(), token);
                changed += result.Changed; unresolved += result.Unresolved;
            }
            return Results.Ok(new EntityOwnershipBatchResult(changed, unresolved));
        }
        catch (EntityOwnershipException exception) { throw new LinkQueryException(exception.StatusCode, exception.Code); }
    }
}

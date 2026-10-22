using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

public sealed partial class WorkspaceContents
{
    // typed交接只能来自已登记处理器，HTTP字段及Before扩展不能替换真实输入或规范状态。
    private async ValueTask<StandardCrudMutationResult> WithButtonMutationAsync(ObjectOperationContext context,
        WorkspaceMutationContinuation continuation, CancellationToken token)
    {
        var input = continuation.ReadInput(context);

        ValidateButtonMutationInput(input);
        await LockGraphAsync(token);
        return await continuation.Execute(await ReadButtonMutationStateAsync(input, true, token), token);
    }

    // 准备阶段保留原无图写锁读取，提交阶段另在原root锁内规范化并重验全部事实。
    internal Task<WorkspaceMutationState> PrepareEditStateAsync(WorkspaceMutationInput input, CancellationToken token)
    {
        ValidateButtonMutationInput(input);
        return ReadButtonMutationStateAsync(input, false, token);
    }

    // 整批目标来自同一个父位置与LID集合，不以类型或额外引用替代用户实际输入。
    private async Task<WorkspaceMutationState> ReadButtonMutationStateAsync(WorkspaceMutationInput input, bool protect, CancellationToken token)
    {
        var path = await ResolveWritableButtonPositionAsync(input.ActorId, input.Position, token);
        var rows = await Links.AsNoTracking().Where(row => input.LinkIds.Contains(EF.Property<string>(row, "LID"))
                && EF.Property<string>(row, "PEID") == path.FolderId)
            .Select(row => new { Link = EF.Property<string>(row, "LID"), Model = EF.Property<string>(row, "ChildModelKey"),
                Id = EF.Property<string>(row, "CEID") }).ToArrayAsync(token);
        if (rows.Length != input.LinkIds.Length) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
        var byLink = rows.ToDictionary(row => row.Link, StringComparer.OrdinalIgnoreCase);
        var targets = input.LinkIds.Select(id => byLink[id]).Select(row => new LinkObjectReference(row.Model, row.Id)).Distinct().ToArray();
        var facts = new List<WorkspaceMutationObject>();
        var participant = protect ? mutationRepository as IStandardCrudUpdateParticipant
            ?? throw new InvalidOperationException("WORKSPACE_PROTECTED_FACTS_REQUIRED") : null;
        foreach (var target in targets)
        {
            var resource = Resource(target.ModelKey);
            var readable = await objectActions.CheckAsync(target.ModelKey, target.Id, ["read"], token);
            if (readable != DataExecutionDecision.Allowed) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            await RequireNonRootMutationTargetAsync(target, token);
            var descriptor = target.ModelKey == WorkspacePositions.FolderModel ? folderDescriptor.Value
                : resources.FindByResourceKey(resource) ?? throw new LinkQueryException(400, "Workspace.InvalidEndpoint");
            var before = await (participant is null ? mutationRepository.GetByIdAsync(descriptor, target.Id, token)
                : participant.ReadForUpdateAsync(descriptor, target.Id, token))
                ?? throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            facts.Add(new(resource, target.ModelKey, target.Id, before));
        }
        return new(input, facts);
    }

    // 全批按钮要求通过后才进入实际更新或删除；入向引用、owner行为和审计仍在原root事务中。
    internal async Task<StandardCrudMutationResult> ExecuteButtonMutationAsync(WorkspaceMutationState state,
        IObjectOperationExecutor operations, CancellationToken token)
    {
        if (state.Input.Action == ObjectOperationAction.Update)
        {
            var target = state.Targets.Single();
            var values = ReadValues(state.Input.Values!, target.Model);
            if (target.Model == WorkspacePositions.FolderModel) await UpdateFolderAsync(target.Id, values, token);
            else return await operations.ExecuteAsync(target.Resource, ObjectOperationAction.Update, target.Id, values,
                new ObjectOperationWriteSource.Internal(target.Resource, target.Model, ObjectOperationAction.Update, target.Id), token);
            return StandardCrudMutationResult.Success();
        }
        var folders = state.Targets.Where(target => target.Model == WorkspacePositions.FolderModel).Select(target => target.Id).ToArray();
        if (folders.Length > 0 && await Links.AnyAsync(row => folders.Contains(EF.Property<string>(row, "PEID")), token))
            throw new LinkQueryException(409, "Workspace.FolderNotEmpty");
        foreach (var target in state.Targets)
        {
            await foreach (var link in Links.AsNoTracking().Where(row => EF.Property<string>(row, "ChildModelKey") == target.Model
                    && EF.Property<string>(row, "CEID") == target.Id)
                .Select(row => new { Id = EF.Property<string>(row, "LID"), Parent = EF.Property<string>(row, "PEID") })
                .AsAsyncEnumerable().WithCancellation(token))
                await referenceAudit.RemovedAsync(link.Id, link.Parent, new(target.Model, target.Id), token);
            await Links.Where(row => EF.Property<string>(row, "ChildModelKey") == target.Model
                && EF.Property<string>(row, "CEID") == target.Id).ExecuteDeleteAsync(token);
            if (target.Model == WorkspacePositions.FolderModel)
            {
                await Folders.Where(row => EF.Property<string>(row, "EID") == target.Id).ExecuteDeleteAsync(token);
                await referenceAudit.FolderDeletedAsync(target.Before, token);
            }
            else
            {
                var result = await operations.ExecuteAsync(target.Resource, ObjectOperationAction.Delete, target.Id,
                    new Dictionary<string, object?>(),
                    new ObjectOperationWriteSource.Internal(target.Resource, target.Model, ObjectOperationAction.Delete, target.Id), token);
                if (!result.Succeeded) return result;
            }
        }
        return StandardCrudMutationResult.Success();
    }
    // 与原编辑一项/删除1..100项规则一致，未知动作不进入任何读取或锁。
    private static void ValidateButtonMutationInput(WorkspaceMutationInput input)
    {
        ValidateSelection(input.LinkIds);
        if (input.Action != ObjectOperationAction.Update && input.Action != ObjectOperationAction.Delete)
            throw new InvalidOperationException("WORKSPACE_BUTTON_ACTION_INVALID");
        if (input.Action == ObjectOperationAction.Update && (input.LinkIds.Length != 1 || input.Values is null))
            throw new LinkQueryException(400, "Workspace.InvalidSelection");
    }
    // 同一正式位置必须同时满足空间/整链读取与父read/edit，调用方先取得原业务图锁（准备读取除外）。
    private async Task<WorkspacePositionPath> ResolveWritableButtonPositionAsync(string actor, WorkspacePosition position, CancellationToken token)
    {
        var path = await positions.ResolveAsync(actor, position, token);
        var parent = await objectActions.CheckAsync(WorkspacePositions.FolderModel, path.FolderId, ["read", "edit"], position, token);
        if (parent == DataExecutionDecision.NotFound) throw new LinkQueryException(404, "Workspace.PositionUnavailable");
        if (parent != DataExecutionDecision.Allowed) throw new LinkQueryException(403, "Workspace.ParentWriteDenied");
        return path;
    }

    // 原root持有图锁与事务；唯一Part候选由实际owner writer补齐，再交平台检查后建立实体与引用。
    private async ValueTask<StandardCrudMutationResult> CreatePartFromButtonAsync(ObjectOperationContext context,
        WorkspacePartCreationContinuation continuation, IObjectOperationExecutor operations, CancellationToken token)
    {
        var input = continuation.ReadInput(context);
        var values = ReadValues(input.Values, "Part", true);
        await LockGraphAsync(token);
        var path = await ResolveWritableButtonPositionAsync(input.ActorId, input.Position, token);
        RequireContainer(input.Position, path, "Part");
        if (!catalog.Get("workspace.content").Model.LinkedTypes.Any(pair => pair.Parent == WorkspacePositions.FolderModel && pair.Child == "Part"))
            throw new LinkQueryException(400, "Workspace.InvalidEndpoint");
        var source = context.ContinueCreate("part.part", "Part", (candidate, persist, cancellation) =>
            continuation.Execute(new(candidate, persist), cancellation));
        var result = await operations.ExecuteAsync("part.part", ObjectOperationAction.Create, null, values, source, token);
        if (!result.Succeeded) return result;
        var id = result.Detail?.Fields.GetValueOrDefault("EID") as string ?? throw new InvalidOperationException("Created object identity is missing.");
        var link = await RegisterCreatedReferenceAsync(path.FolderId, "Part", id, token);
        await referenceAudit.AddedAsync(link, path.FolderId, new("Part", id), token);
        return StandardCrudMutationResult.Success();
    }
}

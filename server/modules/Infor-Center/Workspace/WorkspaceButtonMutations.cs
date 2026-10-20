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

        ValidateSelection(input.LinkIds);
        await LockGraphAsync(token);
        var path = await positions.ResolveAsync(input.ActorId, input.Position, token);
        // 父资格是本业务的关联检查，不能把它当成所选对象的按钮要求或以LID冒充父EID。
        var parent = await objectActions.CheckAsync(WorkspacePositions.FolderModel, path.FolderId, ["read", "edit"], input.Position, token);
        if (parent == DataExecutionDecision.NotFound) throw new LinkQueryException(404, "Workspace.PositionUnavailable");
        if (parent != DataExecutionDecision.Allowed) throw new LinkQueryException(403, "Workspace.ParentWriteDenied");
        var rows = await Links.AsNoTracking().Where(row => input.LinkIds.Contains(EF.Property<string>(row, "LID"))
                && EF.Property<string>(row, "PEID") == path.FolderId)
            .Select(row => new { Link = EF.Property<string>(row, "LID"), Model = EF.Property<string>(row, "ChildModelKey"),
                Id = EF.Property<string>(row, "CEID") }).ToArrayAsync(token);
        if (rows.Length != input.LinkIds.Length) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
        var byLink = rows.ToDictionary(row => row.Link, StringComparer.OrdinalIgnoreCase);
        var targets = input.LinkIds.Select(id => byLink[id]).Select(row => new LinkObjectReference(row.Model, row.Id)).Distinct().ToArray();
        var facts = new List<WorkspaceMutationObject>();
        var participant = mutationRepository as IStandardCrudUpdateParticipant
            ?? throw new InvalidOperationException("WORKSPACE_PROTECTED_FACTS_REQUIRED");
        foreach (var target in targets)
        {
            var resource = Resource(target.ModelKey);
            var readable = await objectActions.CheckAsync(target.ModelKey, target.Id, ["read"], token);
            if (readable != DataExecutionDecision.Allowed) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            await RequireNonRootMutationTargetAsync(target, token);
            var descriptor = target.ModelKey == WorkspacePositions.FolderModel ? folderDescriptor.Value
                : resources.FindByResourceKey(resource) ?? throw new LinkQueryException(400, "Workspace.InvalidEndpoint");
            var before = await participant.ReadForUpdateAsync(descriptor, target.Id, token)
                ?? throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            facts.Add(new(resource, target.ModelKey, target.Id, before));
        }
        return await continuation.Execute(new(input, facts), token);
    }

    // 全批按钮要求通过后才检查并执行实体删除；入向引用、owner行为和审计仍在原root事务中。
    internal async Task<StandardCrudMutationResult> ExecuteButtonMutationAsync(WorkspaceMutationState state,
        IObjectOperationExecutor operations, CancellationToken token)
    {
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
}

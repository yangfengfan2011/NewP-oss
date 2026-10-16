using System.Text.Json;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ICP.Platform.Modules.Workspace;

public sealed partial class WorkspaceContents
{
    /// <summary>Document先在短图事务重验并固定操作，再于事务外准备字节，最后进入唯一业务提交；Part保持原短事务。</summary>
    public async Task<StandardCrudMutationResult> SubmitObjectCopyAsync(string actor, string operationId,
        IReadOnlyDictionary<string, JsonElement> values, IObjectOperationExecutor operations, CancellationToken token)
    {
        var existing = await ObjectCopies.AsNoTracking().SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == operationId
            && EF.Property<string>(row, "ActorId") == actor, token) ?? throw new LinkQueryException(404, "Workspace.CopyOperationUnavailable");
        var input = JsonSerializer.Deserialize<WorkspaceObjectCopyInput>((string)existing["Input"]!)!;
        if (Equals(existing["State"], "committed")) return StandardCrudMutationResult.Success();
        if (input.SourceModel == "Part") return await Execute(WorkspaceObjectCopyBehavior.CopyPart);
        var candidates = ReadCopyValues(input, values);
        var sources = CopySources(input);
        var invalid = await transactions.ExecuteAsync<StandardCrudError?>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var row = await ReadObjectCopyAsync(actor, operationId, cancellation);
            if (!Equals(row["State"], "ready")) throw new LinkQueryException(409, "Workspace.CopyInProgress");
            for (var index = 0; index < sources.Length; index++)
            {
                var error = await documentCopies.ValidateCopyCandidateAsync(candidates[index], cancellation);
                if (error is not null) return CopyRowError(input, index, error);
            }
            await RequireDocumentSourcesAsync(actor, input, cancellation);
            var target = await WritablePositionAsync(actor, input.TargetPosition, cancellation);
            RequireContainer(input.TargetPosition, target, "Document");
            row["State"] = "preparing";
            audit.ForUpdate((string)row["CreatedBy"]!, (DateTime)row["CreatedAt"]!).ApplyTo(row);
            return null;
        }, token);
        if (invalid is not null) return StandardCrudMutationResult.Failure(invalid);
        // 短事务已持久完成；最终裁决不能沿用本请求tracker中的旧状态或来源。
        db.ChangeTracker.Clear();
        try
        {
            for (var index = 0; index < sources.Length; index++)
                await documentCopies.PrepareCopyFilesAsync(sources[index].Confirmation, CopyOperationKey(input, operationId, index), token);
            db.ChangeTracker.Clear();
            var result = await Execute(WorkspaceObjectCopyBehavior.CopyDocument);
            if (!result.Succeeded && result.Error?.Code is not ("Persistence.CommitOutcomeUnknown" or "Platform.MutationOutcomeUnknown"))
            {
                db.ChangeTracker.Clear();
                await transactions.ExecuteAsync(async cancellation =>
                {
                    using var operation = audit.BeginOperation();
                    await LockGraphAsync(cancellation);
                    var row = await ReadObjectCopyAsync(actor, operationId, cancellation);
                    if (Equals(row["State"], "preparing"))
                    {
                        row["State"] = "ready";
                        audit.ForUpdate((string)row["CreatedBy"]!, (DateTime)row["CreatedAt"]!).ApplyTo(row);
                    }
                    return true;
                }, CancellationToken.None);
            }
            return result;
        }
        catch
        {
            db.ChangeTracker.Clear();
            try { await ObjectCopyResultAsync(actor, operationId, true, CancellationToken.None); }
            catch (Exception recovery) { logger.LogWarning(recovery, "Workspace copy recovery deferred for {OperationId}.", operationId); }
            throw;
        }

        // 对象执行器是唯一最终事务根，服务层不另套业务事务或重复创建。
        async Task<StandardCrudMutationResult> Execute(ObjectOperationAction action) => await operations.ExecuteAsync(
            WorkspaceObjectCopyBehavior.BehaviorDefinition.ObjectKey, action, null,
            new Dictionary<string, object?> { ["ActorId"] = actor, ["OperationId"] = operationId, ["Values"] = values }, token);
    }
}

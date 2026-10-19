using System.Text.Json;
using System.Text.Json.Serialization;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

public sealed record WorkspaceObjectCopyPreparation(string OperationId, IReadOnlyDictionary<string, object?> Values,
    IReadOnlyList<string> SourcePath, IReadOnlyList<string> TargetPath, string ModelKey = "Part", int FileCount = 0, long TotalBytes = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkspaceDocumentCopyItemPreparation[]? Items = null);
public sealed record WorkspaceObjectCopyReceipt(bool Succeeded, string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkspaceDocumentCopyItemReceipt[]? Items = null);
internal sealed record WorkspaceObjectCopyInput(WorkspacePosition SourcePosition, string[] LinkIds, WorkspacePosition TargetPosition,
    string SourceId, string SourceCode, string SourceName, string? SourceDescription, string SourceModel = "Part", string? DocumentConfirmation = null, WorkspaceDocumentCopySource[]? Documents = null);

public sealed partial class WorkspaceContents
{
    /// <summary>短事务保存本主体的确认输入及精确位置；该记录只用于恢复，不替代随后提交授权。</summary>
    public ValueTask<WorkspaceObjectCopyPreparation> PrepareObjectCopyAsync(string actor, string mode, WorkspacePosition sourcePosition,
        string[] linkIds, WorkspacePosition targetPosition, CancellationToken token)
    {
        ValidateSelection(linkIds);
        if (mode != "copy")
            throw new LinkQueryException(400, "Workspace.ObjectCopySourceRequired");
        if (linkIds.Length > 1) return PrepareDocumentBatchAsync(actor, sourcePosition, linkIds, targetPosition, token);
        return transactions.ExecuteAsync<WorkspaceObjectCopyPreparation>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var parent = await positions.ResolveAsync(actor, sourcePosition, cancellation);
            var endpoint = await ResolveObjectCopySourceAsync(parent.FolderId, linkIds, cancellation);
            if (endpoint.ModelKey is not ("Part" or "Document")) throw new LinkQueryException(400, "Workspace.ObjectCopySourceRequired");
            var target = await WritablePositionAsync(actor, targetPosition, cancellation);
            RequireContainer(targetPosition, target, endpoint.ModelKey);
            var detail = await crud.GetDetailFromSourceAsync(resources.FindByResourceKey(Resource(endpoint.ModelKey))!, endpoint.Id,
                new("workspace.objectCopy.detail", "workspace.backend", typeof(WorkspaceContents), nameof(PrepareObjectCopyAsync)), cancellation)
                ?? throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            var fields = detail.Fields;
            var document = endpoint.ModelKey == "Document" ? await documentCopies.ReadCopySourceAsync(endpoint.Id, cancellation) : null;
            var input = new WorkspaceObjectCopyInput(sourcePosition, linkIds, targetPosition, endpoint.Id,
                (string)fields["ECODE"]!, (string)fields["ENAME"]!, fields.GetValueOrDefault("Description") as string, endpoint.ModelKey, document?.Confirmation);
            var id = ids.NewId("WorkspaceObjectCopy");
            var row = new Dictionary<string, object?> { ["EID"] = id, ["ActorId"] = actor, ["State"] = "ready", ["Input"] = JsonSerializer.Serialize(input) };
            audit.ForCreate().ApplyTo(row);
            ObjectCopies.Add(row);
            return new(id, new Dictionary<string, object?> { ["ECODE"] = "", ["ENAME"] = input.SourceName, ["Description"] = input.SourceDescription },
                [.. parent.Names, input.SourceName], target.Names, endpoint.ModelKey, document?.FileCount ?? 0, document?.TotalBytes ?? 0);
        }, token);
    }

    /// <summary>最终重验来源、目标及所属资格；Part、引用、复制审计与结果状态只在同一事务提交。</summary>
    internal async ValueTask<StandardCrudMutationResult> CopyObjectAsync(string actor, string operationId,
        IReadOnlyDictionary<string, JsonElement> input, IObjectOperationExecutor operations, CancellationToken cancellation)
    {
        if (!transactions.IsActive) throw new InvalidOperationException("Workspace.CopyTransactionRequired");
        using var operation = audit.BeginOperation();
        await LockGraphAsync(cancellation);
        var row = await ReadObjectCopyAsync(actor, operationId, cancellation);
        if (Equals(row["State"], "committed")) return StandardCrudMutationResult.Success();
        var prepared = JsonSerializer.Deserialize<WorkspaceObjectCopyInput>((string)row["Input"]!)!;
        if (prepared.Documents is not null) return await CopyDocumentBatchAsync(actor, operationId, row, prepared, input, operations, cancellation);
        if (!Equals(row["State"], prepared.SourceModel == "Document" ? "preparing" : "ready")) throw new LinkQueryException(409, "Workspace.CopyCancelled");
        var parent = await positions.ResolveAsync(actor, prepared.SourcePosition, cancellation);
        var endpoint = await ResolveObjectCopySourceAsync(parent.FolderId, prepared.LinkIds, cancellation);
        if (endpoint.ModelKey != prepared.SourceModel || endpoint.Id != prepared.SourceId) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
        var target = await WritablePositionAsync(actor, prepared.TargetPosition, cancellation);
        RequireContainer(prepared.TargetPosition, target, endpoint.ModelKey);
        if (!catalog.Get("workspace.content").Model.LinkedTypes.Any(pair => pair.Parent == WorkspacePositions.FolderModel && pair.Child == endpoint.ModelKey))
            throw new LinkQueryException(400, "Workspace.InvalidEndpoint");
        var values = ReadValues(input, endpoint.ModelKey);
        var result = endpoint.ModelKey == "Part" ? await operations.ExecuteAsync("part.part.copy", new("copy"), null, new Dictionary<string, object?> {
            ["SourceId"] = endpoint.Id, ["ExpectedCode"] = prepared.SourceCode, ["ExpectedName"] = prepared.SourceName,
            ["ExpectedDescription"] = prepared.SourceDescription, ["Values"] = values }, cancellation)
            : await operations.ExecuteAsync("document.document.copy", new("copy"), null, new Dictionary<string, object?> {
                ["Confirmation"] = prepared.DocumentConfirmation!, ["OperationKey"] = operationId, ["Values"] = values }, cancellation);
        if (!result.Succeeded) return result;
        var id = (string)result.Detail!.Fields["EID"]!;
        var linkId = await RegisterCreatedReferenceAsync(target.FolderId, endpoint.ModelKey, id, cancellation);
        await referenceAudit.ObjectCopiedAsync(prepared.LinkIds[0], parent.FolderId, endpoint, linkId, target.FolderId, id, cancellation);
        // 授权规则写入不取得目录图锁；在业务图和审计登记之后再次裁决，不能把前次允许沿用到提交。
        var finalParent = await positions.ResolveAsync(actor, prepared.SourcePosition, cancellation);
        var finalSource = await ResolveObjectCopySourceAsync(finalParent.FolderId, prepared.LinkIds, cancellation);
        if (finalSource != endpoint) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
        await WritablePositionAsync(actor, prepared.TargetPosition, cancellation);
        if (!await decisions.IsCandidateAllowedAsync(Resource(endpoint.ModelKey), result.Detail.Fields, "create", cancellation))
            throw new LinkQueryException(403, "Workspace.ObjectCreateDenied");
        row["State"] = "committed";
        audit.ForUpdate((string)row["CreatedBy"]!, (DateTime)row["CreatedAt"]!).ApplyTo(row);
        return StandardCrudMutationResult.Success();
    }

    /// <summary>核对与取消均等待同一短图事务；已提交事实不会被取消覆盖，返回值不泄露对象或来源。</summary>
    public async ValueTask<WorkspaceObjectCopyReceipt> ObjectCopyResultAsync(string actor, string operationId, bool cancel, CancellationToken token)
    {
        WorkspaceObjectCopyInput? input = null;
        var receipt = await transactions.ExecuteAsync<WorkspaceObjectCopyReceipt>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var row = await ReadObjectCopyAsync(actor, operationId, cancellation);
            input = JsonSerializer.Deserialize<WorkspaceObjectCopyInput>((string)row["Input"]!)!;
            if (cancel && (Equals(row["State"], "ready") || Equals(row["State"], "preparing")))
            {
                row["State"] = "cancelled";
                audit.ForUpdate((string)row["CreatedBy"]!, (DateTime)row["CreatedAt"]!).ApplyTo(row);
            }
            var state = (string)row["State"]!;
            return CopyReceipt(input, state);
        }, token);
        if (cancel && receipt.State == "cancelled" && input?.SourceModel == "Document")
            foreach (var (source, index) in CopySources(input).Select((source, index) => (source, index)))
                await documentCopies.CancelCopyFilesAsync(source.Confirmation, CopyOperationKey(input, operationId, index), token);
        return receipt;
    }

    private DbSet<Dictionary<string, object?>> ObjectCopies => db.Set<Dictionary<string, object?>>("WorkspaceObjectCopy");

    // 来源资格使用真副本专属拒绝，不能以“写入”提示误导用户额外申请源edit。
    private async Task<ICP.Platform.Crud.LinkQueries.LinkObjectReference> ResolveObjectCopySourceAsync(string parentId, string[] linkIds, CancellationToken token)
    {
        var source = (await ResolveTargetsAsync(parentId, linkIds, "read", token)).Single();
        var action = source.ModelKey switch { "Part" => "partObjectCopy", "Document" => "documentObjectCopy", _ => throw new LinkQueryException(400, "Workspace.ObjectCopySourceRequired") };
        if (!await decisions.IsAllowedAsync(Resource(source.ModelKey), source.Id, action, token)) throw new LinkQueryException(403, "Workspace.ObjectCopyDenied");
        return source;
    }

    // actor 来自认证主体；不存在与属于他人使用同一拒绝，不暴露状态、名称和目标。
    private async Task<Dictionary<string, object?>> ReadObjectCopyAsync(string actor, string operationId, CancellationToken token) =>
        await ObjectCopies.SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == operationId && EF.Property<string>(row, "ActorId") == actor, token)
        ?? throw new LinkQueryException(404, "Workspace.CopyOperationUnavailable");
}

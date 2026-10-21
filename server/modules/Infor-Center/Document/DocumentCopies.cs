using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Modules.Document.Contracts;
using ICP.Platform.Modules.Files.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Document;

internal sealed record DocumentCopyFile(string LinkId, FileUsageReference Source, string Role, long Ordinal);
internal sealed record DocumentCopySnapshot(string DocumentId, string Code, string Name, string? Description, string VersionId,
    long VersionOrdinal, string VersionLabel, string VersionState, string IterationId, long IterationOrdinal, string DatasetType,
    DocumentCopyFile[] Files);

public sealed partial class DocumentFiles
{
    /// <inheritdoc/>
    public async Task<StandardCrudError?> ValidateCopyCandidateAsync(IReadOnlyDictionary<string, object?> values, CancellationToken token = default)
    {
        var candidate = await writer.PrepareCreateCandidateFactsAsync(resources.FindByResourceKey("document.document")!, values, token);
        if (!candidate.Succeeded) return candidate.Error;
        return await objectActions.CheckCandidateAsync("Document", candidate.Detail!.Fields, ["create"], token) == DataExecutionDecision.Allowed
            ? null : CopyCandidateDenied();
    }

    /// <inheritdoc/>
    public async Task<DocumentCopySource> ReadCopySourceAsync(string documentId, CancellationToken token = default)
    {
        var facts = await ReadCopyFactsAsync(documentId, token);
        var snapshot = facts.Snapshot;
        return new(JsonSerializer.Serialize(snapshot), snapshot.Code, snapshot.Name, snapshot.Description, snapshot.Files.Length, facts.TotalBytes);
    }

    // 原owner读取时点逐项检查当前业务资格，返回实际current事实；不消费独立复制权限合同或保存allow。
    private async Task<CopyFacts> ReadCopyFactsAsync(string documentId, CancellationToken token)
    {
        // 只在这个只读单元保留自有连接；不扩大事务、不缓存授权，也不跨字节I/O占用连接。
        var openedHere = db.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        try
        {
            if (openedHere) await db.Database.OpenConnectionAsync(token);
            DocumentCopyFields.RequireRuntime(db);
            if (await objectActions.CheckAsync("Document", documentId, ["read"], token) != DataExecutionDecision.Allowed
                || !await db.Set<Dictionary<string, object?>>("Document").AsNoTracking().AnyAsync(row => EF.Property<string>(row, "EID") == documentId, token))
                throw new FileLifecycleException(404, "Files.UsageUnavailable");
            if (await objectActions.CheckAsync("Document", documentId, ["documentObjectCopy"], token) != DataExecutionDecision.Allowed) throw new FileLifecycleException(403, "Document.CopyDenied");
            var current = await identities.ReadCurrentAsync(documentId, token, tracking: false);
            foreach (var key in new[] { "ECODE", "ENAME", "Description" })
                if (!Equals(current.Master.GetValueOrDefault(key), current.Iteration.GetValueOrDefault(key))) throw new InvalidOperationException("Document.IdentityCorrupted");
            var links = await ReadLinksAsync((string)current.Iteration["EID"]!, false, token);
            long totalBytes = 0;
            using var metadata = copyScope.Begin(current, DocumentFileCopyPhase.Metadata, out var sourceContext);
            foreach (var batch in links.Select(Reference).Chunk(files.MaxReadBatchSize))
                foreach (var item in await files.ReadUsageMetadataAsync(batch, sourceContext, token)) totalBytes = checked(totalBytes + item.Length);
            var snapshot = new DocumentCopySnapshot(documentId, (string)current.Master["ECODE"]!, (string)current.Master["ENAME"]!, current.Master["Description"] as string,
                (string)current.Version["EID"]!, (long)current.Version["VersionOrdinal"]!, (string)current.Version["VersionLabel"]!, (string)current.Version["State"]!,
                (string)current.Iteration["EID"]!, (long)current.Iteration["IterationOrdinal"]!, (string)current.Iteration["DatasetTypeKey"]!,
                links.Select(link => new DocumentCopyFile((string)link["LID"]!, Reference(link), (string)link["Role"]!, (long)link["Ordinal"]!)).ToArray());
            return new(snapshot, current, totalBytes);
        }
        finally { if (openedHere) await db.Database.CloseConnectionAsync(); }
    }

    /// <inheritdoc/>
    public async Task PrepareCopyFilesAsync(string confirmation, string operationKey, CancellationToken token = default)
    {
        if (transactions.IsActive) throw new InvalidOperationException("Document.CopyPreparationRequiresNoTransaction");
        var facts = await RequireCopySnapshotAsync(confirmation, token);
        var source = facts.Snapshot;
        using var origin = copyScope.Begin(facts.Identity, DocumentFileCopyPhase.Prepare, out var sourceContext);
        foreach (var file in source.Files)
            await copies.PrepareCopyAsync(CopyKey(operationKey, file), file.Source, DocumentFileOwnerPolicy.Key, sourceContext, token);
    }

    /// <inheritdoc/>
    public async Task CancelCopyFilesAsync(string confirmation, string operationKey, CancellationToken token = default)
    {
        var source = JsonSerializer.Deserialize<DocumentCopySnapshot>(confirmation) ?? throw new InvalidOperationException("Document.CopySnapshotInvalid");
        foreach (var file in source.Files) await copies.CancelCopyAsync(CopyKey(operationKey, file), DocumentFileOwnerPolicy.Key, token);
    }

    /// <summary>最终原事务内再核当前身份和完整集合，经正常Create产生新初始身份，再以实际Added关联确认独立文件使用。</summary>
    internal async ValueTask<StandardCrudMutationResult> CopyAsync(ObjectOperationContext context, string confirmation, string operationKey,
        IReadOnlyDictionary<string, object?> values, CancellationToken token)
    {
        if (!transactions.IsActive) throw new InvalidOperationException("Document.TransactionRequired");
        var facts = await RequireCopySnapshotAsync(confirmation, token);
        var source = facts.Snapshot;
        using var origin = copyScope.Begin(facts.Identity, DocumentFileCopyPhase.Confirm, out var sourceContext);
        if (values.Keys.Any(key => key is not ("ECODE" or "ENAME" or "Description"))) throw new FileLifecycleException(400, "Document.ServerFieldForbidden");
        var prepared = new List<(DocumentCopyFile Source, FileCopyPreparation Copy)>();
        foreach (var file in source.Files)
        {
            var item = await copies.ReadCopyAsync(CopyKey(operationKey, file), DocumentFileOwnerPolicy.Key, token) ?? throw new FileLifecycleException(409, "Files.CopyNotPrepared");
            if (item.State != "prepared" || string.IsNullOrWhiteSpace(item.FileId)) throw new FileLifecycleException(409, "Files.CopyNotPrepared");
            prepared.Add((file, item));
        }
        var creationSource = context.ContinueCreate("document.document", "Document", async (candidate, persist, cancellation) =>
        {
            if (await objectActions.CheckCandidateAsync("Document", candidate, ["create"], cancellation) != DataExecutionDecision.Allowed)
                return StandardCrudMutationResult.Failure(CopyCandidateDenied());
            return await persist(cancellation);
        });
        var result = await operations().ExecuteAsync("document.document", ObjectOperationAction.Create, null, values, creationSource, token);
        // 普通创建可在文件准备后赢得唯一编码；仅在真实数据库核实重复时转换为可修正的编码错误。
        if (result.Error?.Code == "StandardCrud.PersistenceConflict" && values.GetValueOrDefault("ECODE") is string code
            && await db.Set<Dictionary<string, object?>>("Document").AsNoTracking().AnyAsync(row => EF.Property<string>(row, "ECODE") == code, token))
            return StandardCrudMutationResult.Failure(new() { StatusCode = 409, Code = "Document.CodeDuplicated", ReasonKey = "Document.CodeDuplicated",
                Message = "图文档编码必须唯一。", FieldErrors = [new("ECODE", "Document.CodeDuplicated", "图文档编码必须唯一。")] });
        if (!result.Succeeded) return result;
        var identity = await identities.ReadCurrentAsync((string)result.Detail!.Fields["EID"]!, token);
        using var proof = creation.BeginCopied(identity, out var targetSource);
        var stamp = audit.Current();
        var links = prepared.Select(item => new Dictionary<string, object?> {
            ["LID"] = ids.NewId("DocumentFileLink"), ["PEID"] = identity.Iteration["EID"], ["CEID"] = item.Copy.FileId,
            ["Role"] = item.Source.Role, ["Ordinal"] = item.Source.Ordinal,
            ["LCreatedBy"] = stamp.ActorId, ["LCreatedAt"] = stamp.OccurredAtUtc, ["LUpdatedBy"] = stamp.ActorId, ["LUpdatedAt"] = stamp.OccurredAtUtc }).ToArray();
        db.Set<Dictionary<string, object?>>("DocumentFileLink").AddRange(links);
        foreach (var item in prepared.Select((item, index) => (item.Copy, Link: links[index])).OrderBy(item => item.Copy.FileId, StringComparer.Ordinal))
        {
            var confirmed = await copies.ConfirmCopyAsync(item.Copy.RequestKey, new(DocumentFileOwnerPolicy.Key, (string)item.Link["LID"]!),
                sourceContext, targetSource, token);
            if (confirmed.FileId != item.Copy.FileId) throw new InvalidOperationException("Document.FileAssociationCorrupted");
            item.Link["FileUsageId"] = confirmed.UsageId;
        }
        if (links.Length > 0) await RegisterFileEventAsync("document.files.copied", identity, links, token);
        return result;
    }

    // 确认来自本owner持久输入；当前字段、正规身份或任一文件回链变化都要求重新确认，不混用快照。
    private async Task<CopyFacts> RequireCopySnapshotAsync(string confirmation, CancellationToken token)
    {
        var expected = JsonSerializer.Deserialize<DocumentCopySnapshot>(confirmation) ?? throw new InvalidOperationException("Document.CopySnapshotInvalid");
        var current = await ReadCopyFactsAsync(expected.DocumentId, token);
        if (JsonSerializer.Serialize(current.Snapshot) != confirmation) throw new FileLifecycleException(409, "Workspace.CopySourceChanged");
        return current;
    }

    private sealed record CopyFacts(DocumentCopySnapshot Snapshot, DocumentCurrentIdentity Identity, long TotalBytes);

    // 保留原正常创建候选的数据拒绝外观，依赖异常不转换成拒绝。
    private static StandardCrudError CopyCandidateDenied() => new()
    {
        StatusCode = 403, Code = "DataScope.ActionDenied", ReasonKey = "dataScope.actionDenied",
        Message = "标准 CRUD 资源 document.document 的数据动作未获允许。"
    };

    // 持久Workspace操作和真实FileLink固定每个文件的相关键，未知prepare可原样读取/取消，无需内存清单。
    private static string CopyKey(string operationKey, DocumentCopyFile file) => operationKey + "." + file.LinkId;
}

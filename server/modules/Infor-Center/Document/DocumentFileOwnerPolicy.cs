using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Modules.Document;

/// <summary>从 Document 自有真实关联与当前资格核验文件使用，不把 Link 伪装成实体授权资源。</summary>
public sealed class DocumentFileOwnerPolicy(EfPlatformDbContext db, DocumentFileCreationScope creation,
    IDataScopeObjectDecisionProvider decisions, IDataScopeReadableIds readable, IOptions<DocumentOptions> options,
    IDataExecutionAccess access, DocumentFileReadScope readScope) : IFileOwnerPolicy
{
    public const string Key = "document.fileLink";
    public string OwnerKey => Key;
    private static readonly DataExecutionInternalBinding ReleaseBinding = new("document.files.release", "document.files.release",
        typeof(DocumentFileOwnerPolicy), nameof(VerifyBatchAsync));

    /// <inheritdoc/>
    public void ValidateUpload(string safeFileName, string contentType)
    {
        if (!options.Value.AllowedFileExtensions.Contains(Path.GetExtension(safeFileName), StringComparer.OrdinalIgnoreCase))
            throw new FileLifecycleException(400, "Document.ExtensionNotAllowed", "file");
    }

    /// <inheritdoc/>
    public async Task VerifyAsync(FileOwnerReference owner, string fileId, string? usageId, FileOwnerOperation operation, FileOwnerOperationContext? context, CancellationToken token)
    {
        if (owner.Key != Key) throw new FileLifecycleException(404, "Files.UsageUnavailable");
        if (operation == FileOwnerOperation.Confirm) ValidateCreationSource(context);
        if (operation == FileOwnerOperation.ConfirmCopy) ValidateLegacyCopySource(context, nameof(IFileCopyService.ConfirmCopyAsync));
        if (operation is FileOwnerOperation.Confirm or FileOwnerOperation.ConfirmCopy && usageId is null)
        {
            var entry = db.ChangeTracker.Entries().SingleOrDefault(entry => entry.Metadata.Name == "DocumentFileLink"
                && Equals(entry.Property("LID").CurrentValue, owner.Id));
            if (entry is null || entry.State != EntityState.Added || entry.Entity is not Dictionary<string, object?> link
                || !Equals(link["CEID"], fileId) || link.GetValueOrDefault("PEID") is not string iterationId
                || !(operation == FileOwnerOperation.ConfirmCopy ? creation.ProvesCopyIteration(iterationId) : creation.ProvesIteration(iterationId, (FileOwnerCreationContext)context!))
                || !HasValidRole(link, operation == FileOwnerOperation.ConfirmCopy ? 100 : options.Value.MaxAttachmentsPerDocument)
                || link.GetValueOrDefault("FileUsageId") is not null)
                throw new FileLifecycleException(409, "Document.CreationProofRequired");
            return;
        }
        if (usageId is null) throw new InvalidOperationException("Document.FileAssociationCorrupted");
        await VerifyBatchAsync([new(usageId, fileId, owner)], operation, context, token);
    }

    // 新建上限使用当前配置；已持久集合的读取、下载与释放不受后续调低配置影响。
    private static bool HasValidRole(IReadOnlyDictionary<string, object?> link, int maxAttachments) =>
        Equals(link.GetValueOrDefault("Role"), "main") && Equals(link.GetValueOrDefault("Ordinal"), 0L)
        || Equals(link.GetValueOrDefault("Role"), "attachment") && link.GetValueOrDefault("Ordinal") is long ordinal
            && ordinal >= 1 && ordinal <= maxAttachments;

    /// <inheritdoc/>
    public async Task VerifyBatchAsync(IReadOnlyList<FileUsageReference> references, FileOwnerOperation operation, FileOwnerOperationContext? context, CancellationToken token)
    {
        if (operation is FileOwnerOperation.Read or FileOwnerOperation.Download or FileOwnerOperation.CopySource)
            ValidateReadSource(operation, context);
        if (operation == FileOwnerOperation.Confirm) ValidateCreationSource(context);
        if (operation == FileOwnerOperation.ConfirmCopy) ValidateLegacyCopySource(context, nameof(IFileCopyService.ConfirmCopyAsync));
        if (operation == FileOwnerOperation.Release && (context is not FileOwnerReleaseContext release
            || release.ResourceKey != "document.document" || string.IsNullOrWhiteSpace(release.ObjectId)))
            throw new FileLifecycleException(400, "Document.FileWriteContextInvalid");
        if (references.Count == 0) return;
        if (references.Any(reference => reference.Owner.Key != Key)) throw new FileLifecycleException(404, "Files.UsageUnavailable");
        var lids = references.Select(reference => reference.Owner.Id).Distinct(StringComparer.Ordinal).ToArray();
        var links = await db.Set<Dictionary<string, object?>>("DocumentFileLink").AsNoTracking()
            .Where(row => lids.Contains(EF.Property<string>(row, "LID"))).ToArrayAsync(token);
        if (links.Length != lids.Length) throw new FileLifecycleException(404, "Files.UsageUnavailable");
        var iterationIds = links.Select(row => (string)row["PEID"]!).Distinct().ToArray();
        var iterations = await db.Set<Dictionary<string, object?>>("DocumentIteration").AsNoTracking()
            .Where(row => iterationIds.Contains(EF.Property<string>(row, "EID"))).ToArrayAsync(token);
        var versionIds = iterations.Select(row => (string)row["VersionId"]!).Distinct().ToArray();
        var versions = await db.Set<Dictionary<string, object?>>("DocumentVersion").AsNoTracking()
            .Where(row => versionIds.Contains(EF.Property<string>(row, "EID"))).ToArrayAsync(token);
        var documentIds = versions.Select(row => (string)row["DocumentId"]!).Distinct().ToArray();
        var sourceId = context switch
        {
            FileOwnerExecutionContext source => source.ObjectId,
            FileOwnerCreationContext source => source.ObjectId,
            FileOwnerReleaseContext source => source.ObjectId,
            _ => null
        };
        if (sourceId is not null && (documentIds.Length != 1 || documentIds[0] != sourceId))
            throw new FileLifecycleException(404, "Files.UsageUnavailable");
        var masters = await db.Set<Dictionary<string, object?>>("Document").AsNoTracking()
            .Where(row => documentIds.Contains(EF.Property<string>(row, "EID"))).ToArrayAsync(token);
        if (operation is not (FileOwnerOperation.Confirm or FileOwnerOperation.ConfirmCopy))
        {
            var allowed = await readable.GetReadableIdsAsync("document.document", documentIds, token);
            if (allowed.Count != documentIds.Length) throw new FileLifecycleException(404, "Files.UsageUnavailable");
        }
        var readFacts = new Dictionary<string, DocumentCurrentIdentity>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            var link = links.Single(row => Equals(row["LID"], reference.Owner.Id));
            var iteration = iterations.SingleOrDefault(row => Equals(row["EID"], link["PEID"]));
            var version = iteration is null ? null : versions.SingleOrDefault(row => Equals(row["EID"], iteration["VersionId"]));
            var master = version is null ? null : masters.SingleOrDefault(row => Equals(row["EID"], version["DocumentId"]));
            if (iteration is null || version is null || master is null
                || !Equals(master["LatestVersionId"], version["EID"]) || !Equals(version["CurrentIterationId"], iteration["EID"])
                || !Equals(iteration["Kind"], "regular") || !Equals(link["CEID"], reference.FileId)
                || !Equals(link["FileUsageId"], reference.UsageId) || !HasValidRole(link, 100))
                throw new InvalidOperationException("Document.FileAssociationCorrupted");
            if (operation is FileOwnerOperation.Download or FileOwnerOperation.Release) readFacts[(string)master["EID"]!] = new(master, version, iteration);
        }
        if (operation is FileOwnerOperation.Download or FileOwnerOperation.Release)
        {
            foreach (var documentId in documentIds)
            {
                // 每次policy独立完成本批read与真实回链；锁内Release不能借用Open或元数据阶段的事实。
                using var read = readScope.Begin(documentId, operation == FileOwnerOperation.Download
                    ? DocumentFileReadPhase.PolicyDownload : DocumentFileReadPhase.PolicyRelease, readFacts[documentId]);
                if (operation == FileOwnerOperation.Download)
                {
                    await foreach (var decision in access.EvaluateObjectsAsync(DocumentFiles.FileDownloadBinding, "target", [readScope.Target(documentId)], token))
                        DocumentFiles.RequireDownloadDecision(decision.Decision);
                }
                else
                {
                    var decision = await access.EvaluateInternalObjectAsync(ReleaseBinding, "target", readScope.Target(documentId), token);
                    if (decision.Decision == DataExecutionDecision.NotFound) throw new FileLifecycleException(404, "Files.UsageUnavailable");
                    if (decision.Decision != DataExecutionDecision.Allowed) throw new FileLifecycleException(403, "Document.DeleteDenied");
                }
            }
        }
        if (operation == FileOwnerOperation.CopySource)
            foreach (var documentId in documentIds)
                if (!await decisions.IsAllowedAsync("document.document", documentId, "documentObjectCopy", token))
                    throw new FileLifecycleException(403, "Document.CopyDenied");
    }

    // 上传确认只核原新建来源与凭据；首次真实Added关联证明由下方原事务检查完成，不增加新对象read。
    private static void ValidateCreationSource(FileOwnerOperationContext? context)
    {
        if (context is not FileOwnerCreationContext source || string.IsNullOrWhiteSpace(source.ObjectId)
            || source.Source.ResourceKey != "document.document" || source.Source.ObjectId is not null
            || source.Source.Binding != new DataExecutionBindingReference("document.document.internal.create", "standardCrud.create", "submit")
            || source.Source.Internal != new DataExecutionInternalBinding("document.document.internal.create", "standardCrud.create", typeof(DocumentFiles), nameof(DocumentFiles.CreateWithFilesAsync)))
            throw new FileLifecycleException(400, "Document.FileWriteContextInvalid");
    }

    // 317迁移前按实际阶段列出Files成员；CopySource有准备/确认两处读取，目标确认仍只允许确认方法。
    private static void ValidateLegacyCopySource(FileOwnerOperationContext? context, params string[] members)
    {
        if (context is not FileOwnerLegacyCopyReadContext legacy
            || legacy.Caller.FullName != "ICP.Platform.Modules.Files.Services.FileLifecycleService" || !members.Contains(legacy.Member, StringComparer.Ordinal))
            throw new FileLifecycleException(400, "Document.FileReadContextInvalid");
    }

    // 旧复制只保留真实调用者的明确分支；read/download缺上下文不能回退到它。
    private void ValidateReadSource(FileOwnerOperation operation, FileOwnerOperationContext? context)
    {
        if (context is FileOwnerExecutionContext formal && operation is FileOwnerOperation.Read or FileOwnerOperation.Download)
        {
            var expected = operation == FileOwnerOperation.Read ? DocumentFiles.FileReadBinding : DocumentFiles.FileDownloadBinding;
            if (formal.Binding != expected || formal.ResourceKey != "document.document" || string.IsNullOrWhiteSpace(formal.ObjectId))
                throw new FileLifecycleException(400, "Document.FileReadContextInvalid");
            access.Validate([expected], formal.ResourceKey);
            return;
        }
        if (operation == FileOwnerOperation.CopySource)
        {
            ValidateLegacyCopySource(context, nameof(IFileCopyService.PrepareCopyAsync), nameof(IFileCopyService.ConfirmCopyAsync));
            return;
        }
        if (context is FileOwnerLegacyCopyReadContext legacy && operation == FileOwnerOperation.Read
            && legacy.Caller == typeof(DocumentFiles) && legacy.Member == nameof(DocumentFiles.ReadCopySourceAsync)) return;
        throw new FileLifecycleException(400, "Document.FileReadContextInvalid");
    }
}

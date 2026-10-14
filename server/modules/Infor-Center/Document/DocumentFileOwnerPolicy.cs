using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Modules.Document;

/// <summary>从 Document 自有真实关联与当前资格核验文件使用，不把 Link 伪装成实体授权资源。</summary>
public sealed class DocumentFileOwnerPolicy(EfPlatformDbContext db, DocumentFileCreationScope creation,
    IDataScopeObjectDecisionProvider decisions, IDataScopeReadableIds readable, IOptions<DocumentOptions> options) : IFileOwnerPolicy
{
    public const string Key = "document.fileLink";
    public string OwnerKey => Key;

    /// <inheritdoc/>
    public void ValidateUpload(string safeFileName, string contentType)
    {
        if (!options.Value.AllowedFileExtensions.Contains(Path.GetExtension(safeFileName), StringComparer.OrdinalIgnoreCase))
            throw new FileLifecycleException(400, "Document.ExtensionNotAllowed", "file");
    }

    /// <inheritdoc/>
    public async Task VerifyAsync(FileOwnerReference owner, string fileId, string? usageId, FileOwnerOperation operation, CancellationToken token)
    {
        if (owner.Key != Key) throw new FileLifecycleException(404, "Files.UsageUnavailable");
        if (operation == FileOwnerOperation.Confirm && usageId is null)
        {
            var entry = db.ChangeTracker.Entries().SingleOrDefault(entry => entry.Metadata.Name == "DocumentFileLink"
                && Equals(entry.Property("LID").CurrentValue, owner.Id));
            if (entry is null || entry.State != EntityState.Added || entry.Entity is not Dictionary<string, object?> link
                || !Equals(link["CEID"], fileId) || link.GetValueOrDefault("PEID") is not string iterationId
                || !creation.ProvesIteration(iterationId) || !HasValidRole(link, options.Value.MaxAttachmentsPerDocument)
                || link.GetValueOrDefault("FileUsageId") is not null)
                throw new FileLifecycleException(409, "Document.CreationProofRequired");
            return;
        }
        if (usageId is null) throw new InvalidOperationException("Document.FileAssociationCorrupted");
        await VerifyBatchAsync([new(usageId, fileId, owner)], operation, token);
    }

    // 新建上限使用当前配置；已持久集合的读取、下载与释放不受后续调低配置影响。
    private static bool HasValidRole(IReadOnlyDictionary<string, object?> link, int maxAttachments) =>
        Equals(link.GetValueOrDefault("Role"), "main") && Equals(link.GetValueOrDefault("Ordinal"), 0L)
        || Equals(link.GetValueOrDefault("Role"), "attachment") && link.GetValueOrDefault("Ordinal") is long ordinal
            && ordinal >= 1 && ordinal <= maxAttachments;

    /// <inheritdoc/>
    public async Task VerifyBatchAsync(IReadOnlyList<FileUsageReference> references, FileOwnerOperation operation, CancellationToken token)
    {
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
        var masters = await db.Set<Dictionary<string, object?>>("Document").AsNoTracking()
            .Where(row => documentIds.Contains(EF.Property<string>(row, "EID"))).ToArrayAsync(token);
        if (operation != FileOwnerOperation.Confirm)
        {
            var allowed = await readable.GetReadableIdsAsync("document.document", documentIds, token);
            if (allowed.Count != documentIds.Length) throw new FileLifecycleException(404, "Files.UsageUnavailable");
        }
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
        }
        if (operation is FileOwnerOperation.Download or FileOwnerOperation.Release)
            foreach (var documentId in documentIds)
                if (!await decisions.IsAllowedAsync("document.document", documentId,
                    operation == FileOwnerOperation.Download ? "download" : "delete", token))
                    throw new FileLifecycleException(403, operation == FileOwnerOperation.Download ? "Document.DownloadDenied" : "Document.DeleteDenied");
    }
}

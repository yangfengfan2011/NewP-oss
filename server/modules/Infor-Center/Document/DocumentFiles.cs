using System.Text.RegularExpressions;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Document.Contracts;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Document;

/// <summary>完整图文档文件操作复用原 Create 与 Files 稳定契约，所有业务关联归本模块。</summary>
public sealed partial class DocumentFiles(EfPlatformDbContext db, DocumentInitialIdentity identities,
    DocumentFileCreationScope creation, IFileLifecycleService files, Func<IObjectOperationExecutor> operations,
    IStandardCrudResourceRegistry resources, StandardCrudDefaultWriter writer, IDataScopeObjectDecisionProvider decisions,
    IPlatformTransactionExecutor transactions, IPlatformObjectIdGenerator ids, IEntityAuditValueProvider audit,
    DocumentFileOwnerPolicy policy) : IDocumentFiles
{
    /// <inheritdoc/>
    public async Task<StandardCrudError?> ValidateUploadAsync(IReadOnlyDictionary<string, object?> values, string fileName,
        string contentType, CancellationToken cancellationToken = default)
    {
        var result = await writer.PrepareCreateCandidateAsync(resources.FindByResourceKey("document.document")!, values, cancellationToken);
        if (!result.Succeeded) return result.Error;
        policy.ValidateUpload(fileName, contentType);
        return null;
    }

    /// <inheritdoc/>
    public async Task<StandardCrudMutationResult> CreateWithFilesAsync(IReadOnlyDictionary<string, object?> values,
        DocumentUploadSet? uploads, CancellationToken cancellationToken = default)
    {
        if (!transactions.IsActive || db.Database.CurrentTransaction is null) throw new InvalidOperationException("Document.TransactionRequired");
        StagedFileInfo? staged = null;
        if (uploads is not null)
        {
            var mainId = uploads.MainUploadId;
            if (string.IsNullOrEmpty(mainId) || !UploadId().IsMatch(mainId) || uploads.AttachmentUploadIds is { Count: > 0 })
                return InvalidUploadSet();
            staged = (await files.ReadOwnedStagedAsync([mainId], DocumentFileOwnerPolicy.Key, cancellationToken)).Single();
        }
        var result = await operations().ExecuteAsync("document.document", ObjectOperationAction.Create, null, values, cancellationToken);
        if (!result.Succeeded || staged is null) return result;
        var current = await identities.ReadCurrentAsync((string)result.Detail!.Fields["EID"]!, cancellationToken);
        using var proof = creation.Begin(current);
        var stamp = audit.Current();
        var link = new Dictionary<string, object?>
        {
            ["LID"] = ids.NewId("DocumentFileLink"), ["PEID"] = current.Iteration["EID"], ["CEID"] = staged.FileId,
            ["Role"] = "main", ["Ordinal"] = 0L,
            ["LCreatedBy"] = stamp.ActorId, ["LCreatedAt"] = stamp.OccurredAtUtc,
            ["LUpdatedBy"] = stamp.ActorId, ["LUpdatedAt"] = stamp.OccurredAtUtc
        };
        db.Set<Dictionary<string, object?>>("DocumentFileLink").Add(link);
        // Added Link 暂未填 UsageId 时禁止 Flush；Confirm 只登记，完整 owner 填齐后由外层保存。
        var confirmed = await files.ConfirmUsageAsync(staged.UploadId,
            new(DocumentFileOwnerPolicy.Key, (string)link["LID"]!), cancellationToken);
        if (confirmed.FileId != staged.FileId) throw new InvalidOperationException("Document.FileAssociationCorrupted");
        link["FileUsageId"] = confirmed.UsageId;
        return result;
    }

    /// <inheritdoc/>
    public async Task<DocumentFileList> ReadAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await RequireReadAsync(documentId, cancellationToken);
        var current = await identities.ReadCurrentAsync(documentId, cancellationToken);
        var links = await ReadLinksAsync((string)current.Iteration["EID"]!, false, cancellationToken);
        var metadata = new Dictionary<string, FileUsageMetadata>(StringComparer.Ordinal);
        foreach (var batch in links.Select(Reference).Chunk(files.MaxReadBatchSize))
            foreach (var item in await files.ReadUsageMetadataAsync(batch, cancellationToken))
                if (!metadata.TryAdd(item.UsageId, item)) throw new InvalidOperationException("Document.FileAssociationCorrupted");
        var result = links.Select(link =>
        {
            var usageId = (string)link["FileUsageId"]!;
            if (!metadata.TryGetValue(usageId, out var item) || item.FileId != (string)link["CEID"]!)
                throw new InvalidOperationException("Document.FileAssociationCorrupted");
            return new DocumentFileItem((string)link["LID"]!, (string)link["Role"]!, (long)link["Ordinal"]!,
                item.FileName, item.Length, item.ContentType);
        }).ToArray();
        return new(documentId, (string)current.Version["EID"]!, (string)current.Iteration["EID"]!,
            await decisions.IsAllowedAsync("document.document", documentId, "download", cancellationToken), result);
    }

    /// <inheritdoc/>
    public async Task<FileUsageContent> OpenAsync(string documentId, string fileLinkId, CancellationToken cancellationToken = default)
    {
        await RequireReadAsync(documentId, cancellationToken);
        if (!await decisions.IsAllowedAsync("document.document", documentId, "download", cancellationToken))
            throw new FileLifecycleException(403, "Document.DownloadDenied");
        var current = await identities.ReadCurrentAsync(documentId, cancellationToken);
        var link = await db.Set<Dictionary<string, object?>>("DocumentFileLink").AsNoTracking().SingleOrDefaultAsync(row =>
            EF.Property<string>(row, "LID") == fileLinkId && EF.Property<string>(row, "PEID") == (string)current.Iteration["EID"]!, cancellationToken);
        if (link is null) throw new FileLifecycleException(404, "Files.UsageUnavailable");
        return await files.OpenUsageAsync(Reference(link), cancellationToken);
    }

    /// <summary>原 delete 事务内先登记所有使用释放与关联删除，不在此保存或删除物理内容。</summary>
    public async Task ReleaseAsync(string documentId, CancellationToken cancellationToken)
    {
        if (!transactions.IsActive) throw new InvalidOperationException("Document.TransactionRequired");
        var current = await identities.ReadCurrentAsync(documentId, cancellationToken);
        var links = await ReadLinksAsync((string)current.Iteration["EID"]!, true, cancellationToken);
        foreach (var link in links.OrderBy(link => (string)link["CEID"]!, StringComparer.Ordinal))
            await files.ReleaseUsageAsync(Reference(link), cancellationToken);
        db.Set<Dictionary<string, object?>>("DocumentFileLink").RemoveRange(links);
    }

    // 先判 read，避免未授权目标借身份损坏或关联状态泄漏内部事实。
    private async Task RequireReadAsync(string documentId, CancellationToken token)
    {
        if (!await decisions.IsAllowedAsync("document.document", documentId, "read", token))
            throw new FileLifecycleException(404, "Files.UsageUnavailable");
        if (!await db.Set<Dictionary<string, object?>>("Document").AsNoTracking()
            .AnyAsync(row => EF.Property<string>(row, "EID") == documentId, token))
            throw new FileLifecycleException(404, "Files.UsageUnavailable");
    }

    // 只加载 current regular 的有界集合，额外一项用于明确拒绝损坏的超额事实。
    private async Task<Dictionary<string, object?>[]> ReadLinksAsync(string iterationId, bool tracking, CancellationToken token)
    {
        var query = db.Set<Dictionary<string, object?>>("DocumentFileLink").Where(row => EF.Property<string>(row, "PEID") == iterationId);
        if (!tracking) query = query.AsNoTracking();
        var links = await query.OrderBy(row => EF.Property<long>(row, "Ordinal")).ThenBy(row => EF.Property<string>(row, "LID")).Take(102).ToArrayAsync(token);
        if (links.Length > 101 || links.Any(link => !Equals(link["Role"], "main") || !Equals(link["Ordinal"], 0L)) || links.Length > 1)
            throw new InvalidOperationException("Document.FileAssociationCorrupted");
        return links;
    }

    // 引用只从已读取的真实 Link 派生；缺失必填持久身份是完整性故障。
    private static FileUsageReference Reference(Dictionary<string, object?> link) =>
        link.GetValueOrDefault("FileUsageId") is string usageId && link.GetValueOrDefault("CEID") is string fileId
            ? new(usageId, fileId, new(DocumentFileOwnerPolicy.Key, (string)link["LID"]!))
            : throw new InvalidOperationException("Document.FileAssociationCorrupted");

    // 当票单 main，附件显式拒绝且不读取或消费其中任一 token。
    private static StandardCrudMutationResult InvalidUploadSet() => StandardCrudMutationResult.Failure(new()
    {
        StatusCode = 400, Code = "Document.InvalidUploadSet", ReasonKey = "Document.InvalidUploadSet", Message = "请选择一个有效主文件。",
        FieldErrors = [new("uploads", "Document.InvalidUploadSet", "请选择一个有效主文件。")]
    });

    // 本票局部 lease 身份规则，保持服务端生成器的 ordinal 字符串语义。
    [GeneratedRegex("\\AFileUploadLease_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex UploadId();
}

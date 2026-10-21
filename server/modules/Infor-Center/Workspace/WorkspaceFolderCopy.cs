using System.Text.Json;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Authorization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

public sealed record WorkspaceFolderCopyPreparation(string SourceName, IReadOnlyList<string> SourcePath, IReadOnlyList<string> TargetPath);

public sealed partial class WorkspaceContents
{
    /// <summary>弹层只读核证完整位置及当前源名；不预取子内容，不缓存资格，也不替代图锁内提交重验。</summary>
    public Task<WorkspaceFolderCopyPreparation> PrepareFolderCopyAsync(string actor, string mode, WorkspacePosition sourcePosition,
        string[] linkIds, WorkspacePosition targetPosition, CancellationToken token) =>
        PrepareFolderCopyButtonAsync(actor, mode, sourcePosition, linkIds, targetPosition, token);

    /// <summary>结构名称准备使用自己的真实声明，不与浅副本来源串用。</summary>
    public Task<WorkspaceFolderCopyPreparation> PrepareFolderStructureAsync(string actor, string mode, WorkspacePosition sourcePosition,
        string[] linkIds, WorkspacePosition targetPosition, CancellationToken token) =>
        PrepareFolderCopyButtonAsync(actor, mode, sourcePosition, linkIds, targetPosition, token);

    /// <summary>浅副本在唯一图锁事务内核证全部直接内容，再创建独立根和共享引用；结果读取不属于提交。</summary>
    public ValueTask<WorkspacePasteResult> CopyFolderAsync(string actor, string mode, WorkspacePosition sourcePosition,
        string[] linkIds, WorkspacePosition targetPosition, string name, CancellationToken token) =>
        CopyFolderAsync(actor, mode, sourcePosition, linkIds, targetPosition, name, false, token);

    /// <summary>显式父授权选项只作用于新根；直接子对象仍共享，不读取或复制来源授权。</summary>
    public ValueTask<WorkspacePasteResult> CopyFolderAsync(string actor, string mode, WorkspacePosition sourcePosition,
        string[] linkIds, WorkspacePosition targetPosition, string name, bool copyParentAuthorization, CancellationToken token)
    {
        if (mode != "copy" || linkIds.Length != 1 || string.IsNullOrWhiteSpace(linkIds[0]))
            throw new LinkQueryException(400, "Workspace.FolderCopySourceRequired");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256) throw new LinkQueryException(400, "Workspace.InvalidName", "ENAME");
        sourcePosition = FreezeClipboardPosition(sourcePosition);
        targetPosition = FreezeClipboardPosition(targetPosition);
        linkIds = linkIds.ToArray();
        return transactions.ExecuteAsync<WorkspacePasteResult>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var sourceParent = await positions.ResolveAsync(actor, sourcePosition, cancellation);
            var source = await ReadFolderCopyButtonSourceAsync(sourceParent.FolderId, linkIds, cancellation);
            var transaction = db.Database.CurrentTransaction;
            return await ExecuteFolderCopyButtonAsync(folderCopyHandler, source,
                () => transactions.IsActive && transaction is not null && ReferenceEquals(transaction, db.Database.CurrentTransaction), async checkedToken =>
            {
                await RequireNonRootClipboardTargetAsync(source, checkedToken);
                var target = await ResolveWritableButtonPositionAsync(actor, targetPosition, checkedToken);
                var sourceFields = await Folders.AsNoTracking().Where(row => EF.Property<string>(row, "EID") == source.Id)
                    .Select(row => new { Description = EF.Property<string?>(row, "Description") }).SingleAsync(checkedToken);
                var children = await ReadCopyChildrenAsync(source.Id, checkedToken);
                // 来源名称和次序按真实 SQL collation 校验；不可见冲突同样使整次失败。
                if (await Links.Where(row => EF.Property<string>(row, "PEID") == source.Id && EF.Property<string>(row, "ChildModelKey") == WorkspacePositions.FolderModel)
                    .Join(Folders, link => EF.Property<string>(link, "CEID"), folder => EF.Property<string>(folder, "EID"),
                        (link, folder) => EF.Property<string>(folder, "ENAME")).GroupBy(value => value).AnyAsync(group => group.Count() > 1, checkedToken))
                    throw new LinkQueryException(409, "Workspace.FolderCopyUnavailable");
                foreach (var child in children)
                {
                    var endpoint = new LinkObjectReference(child.Model, child.Id);
                    if (!await reader.IsEndpointReadableAsync("workspace.content", endpoint, checkedToken)
                        || !await FolderCopyPurposeAllowedAsync(endpoint, checkedToken))
                        throw new LinkQueryException(403, "Workspace.FolderCopyUnavailable");
                    if (child.Model == WorkspacePositions.FolderModel && (await WouldCycleAsync(source.Id, child.Id, checkedToken)
                        || await WouldCycleAsync(target.FolderId, child.Id, checkedToken)
                        || await db.Set<Dictionary<string, object?>>("WorkspaceSpace").AnyAsync(row => EF.Property<string>(row, "RootFolderId") == child.Id, checkedToken)))
                        throw new LinkQueryException(409, "Workspace.FolderCopyUnavailable");
                }
                // 普通创建拥有名称、Description、目标归属和候选create规则；嵌套执行共享当前事务及图锁。
                var createdId = await CreateFolderCoreAsync(actor, targetPosition, new Dictionary<string, JsonElement> {
                    ["ENAME"] = JsonSerializer.SerializeToElement(name), ["Description"] = JsonSerializer.SerializeToElement(sourceFields.Description) },
                    copyParentAuthorization, new FolderCreationSource.Copy(), checkedToken);
                var rootLinkId = (string)db.ChangeTracker.Entries<Dictionary<string, object?>>()
                    .Single(entry => entry.Metadata.Name == WorkspacePositions.ContentModel
                        && Equals(entry.Entity["PEID"], target.FolderId) && Equals(entry.Entity["CEID"], createdId)).Entity["LID"]!;
                long ordinal = 0;
                foreach (var child in children.OrderBy(child => child.Ordinal).ThenBy(child => child.LinkId, StringComparer.Ordinal))
                {
                    var createdLink = RegisterCopiedReference(createdId, child, child.Model == WorkspacePositions.FolderModel ? ++ordinal : null, isScopeNeutral: true);
                    await referenceAudit.CopiedAsync(child.LinkId, source.Id, createdLink, createdId, new(child.Model, child.Id), checkedToken);
                }
                await referenceAudit.FolderCopiedAsync(linkIds[0], source.Id, createdId, target.FolderId, rootLinkId, checkedToken);
                return new(true, false);
                }, cancellation);
        }, token);
    }

    private sealed record FolderCopyChild(string LinkId, string Model, string Id, long? Ordinal);

    // 浅副本边不贡献旧对象空间范围，结构新对象边正常；调用方明确选择，不沿用源边授权语义。
    private string RegisterCopiedReference(string parentId, FolderCopyChild child, long? ordinal, bool isScopeNeutral)
    {
        var stamp = audit.Current(); var linkId = ids.NewId(WorkspacePositions.ContentModel);
        Links.Add(new() { ["LID"] = linkId, ["PEID"] = parentId, ["CEID"] = child.Id, ["ChildModelKey"] = child.Model,
            ["TreeOrdinal"] = ordinal, ["IsScopeNeutral"] = isScopeNeutral, ["LCreatedBy"] = stamp.ActorId, ["LCreatedAt"] = stamp.OccurredAtUtc,
            ["LUpdatedBy"] = stamp.ActorId, ["LUpdatedAt"] = stamp.OccurredAtUtc });
        return linkId;
    }

    // 窄投影只保留实际边，不读取业务实体或套用列表分页；逐批读取保持单次读取与参数规模有界。
    private async Task<List<FolderCopyChild>> ReadCopyChildrenAsync(string parentId, CancellationToken token)
    {
        var result = new List<FolderCopyChild>(); string? last = null;
        while (true)
        {
            var query = Links.AsNoTracking().Where(row => EF.Property<string>(row, "PEID") == parentId);
            if (last is not null) query = query.Where(row => string.Compare(EF.Property<string>(row, "LID"), last) > 0);
            var batch = await query.OrderBy(row => EF.Property<string>(row, "LID")).Take(256)
                .Select(row => new FolderCopyChild(EF.Property<string>(row, "LID"), EF.Property<string>(row, "ChildModelKey"),
                    EF.Property<string>(row, "CEID"), EF.Property<long?>(row, "TreeOrdinal"))).ToArrayAsync(token);
            result.AddRange(batch);
            if (batch.Length < 256) break;
            last = batch[^1].LinkId;
        }
        if (result.Any(child => child.Model is not (WorkspacePositions.FolderModel or "Part" or "Document")
                || child.Model == WorkspacePositions.FolderModel && (child.Ordinal is null or <= 0)
                || child.Model != WorkspacePositions.FolderModel && child.Ordinal is not null)
            || result.GroupBy(child => (child.Model, child.Id)).Any(group => group.Count() > 1)
            || result.Where(child => child.Model == WorkspacePositions.FolderModel).GroupBy(child => child.Ordinal).Any(group => group.Count() > 1))
            throw new LinkQueryException(409, "Workspace.FolderCopyUnavailable");
        return result;
    }
}

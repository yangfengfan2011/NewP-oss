using System.Globalization;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Ownership;
using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Presentation;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

public sealed partial class WorkspaceContents
{
    /// <summary>锁内重解完整来源后，以规范对象身份执行整批转交；不要求源父 edit 或接收者 read。</summary>
    public ValueTask<EntityOwnershipTransferResult> TransferOwnershipAsync(string actor, WorkspacePosition position, string[] linkIds,
        string recipientId, EntityOwnershipTransfer transfer, CancellationToken token) => transactions.ExecuteAsync<EntityOwnershipTransferResult>(async cancellation =>
    {
        if (linkIds.Length == 0 || linkIds.Any(string.IsNullOrWhiteSpace)) throw new LinkQueryException(400, "Workspace.InvalidSelection");
        await LockGraphAsync(cancellation);
        var path = await positions.ResolveAsync(actor, position, cancellation);
        var targets = await ResolveOwnershipTargetsAsync(path.FolderId, linkIds.Distinct(StringComparer.Ordinal).ToArray(), cancellation);
        try { return await transfer.TransferAsync(targets.Select(target => new EntityOwnershipTransferTarget(Resource(target.ModelKey), target.Id)).ToArray(), recipientId, cancellation); }
        catch (EntityOwnershipException exception) { throw new LinkQueryException(exception.StatusCode, exception.Code); }
    }, token);

    /// <summary>候选查询先按完整来源核整批当前资格，不因普通功能或管理员身份公开账号目录。</summary>
    public async ValueTask CheckOwnershipTransferAsync(string actor, WorkspacePosition position, string[] linkIds,
        EntityOwnershipTransfer transfer, CancellationToken token)
    {
        await transactions.ExecuteAsync<bool>(async cancellation =>
        {
            if (linkIds.Length == 0 || linkIds.Any(string.IsNullOrWhiteSpace)) throw new LinkQueryException(400, "Workspace.InvalidSelection");
            await LockGraphAsync(cancellation);
            var path = await positions.ResolveAsync(actor, position, cancellation);
            var targets = await ResolveOwnershipTargetsAsync(path.FolderId, linkIds.Distinct(StringComparer.Ordinal).ToArray(), cancellation);
            try { await transfer.CheckAsync(targets.Select(target => new EntityOwnershipTransferTarget(Resource(target.ModelKey), target.Id)).ToArray(), cancellation); }
            catch (EntityOwnershipException exception) { throw new LinkQueryException(exception.StatusCode, exception.Code); }
            return true;
        }, token);
    }

    // 通知来源仅在当前真实根链可读且仍指向接收目录时投影；失效不恢复裸对象入口。
    internal async ValueTask<bool> IsReceivingPositionReadableAsync(string actor, WorkspacePosition position, string directoryId, CancellationToken token)
    {
        try { return (await positions.ResolveAsync(actor, position, token)).FolderId == directoryId; }
        catch (LinkQueryException exception) when (exception.Status is 403 or 404 || exception.Code == "Workspace.InvalidPosition") { return false; }
    }

    // 所有入口（包括独立对象入口）在规范对象行锁前消费同一既有图锁，避免相反的持锁顺序。
    internal async ValueTask PrepareOwnershipReceivingAsync(CancellationToken token) => await LockGraphAsync(token);

    // 接收登记只消费已经重验的规范对象；系统目录不走 sender 对 recipient 私有父的普通 create/edit 准入。
    internal async ValueTask ReceiveOwnershipAsync(AccountOwnership recipient, IReadOnlyCollection<EntityOwnershipTransferTarget> targets,
        IRuntimeTextCatalog texts, IRequestPresentationContextAccessor presentation, IUserDisplayNameDirectory names, CancellationToken token)
    {
        if (!transactions.IsActive) throw new InvalidOperationException("ENTITY_OWNERSHIP_TRANSACTION_REQUIRED");
        var stamp = audit.Current();
        // 接收人尚无个人空间时与本人初始化走同一创建流程；根归属是 recipient，而非执行转交的 sender。
        var root = await spaceInitialization.EnsureAsync(recipient.AccountId, (row, _) =>
        {
            StampReceivingFolder(row, recipient);
            return ValueTask.CompletedTask;
        }, token);
        var senderNames = await names.FindManyAsync([stamp.ActorId], token);
        var sender = senderNames.GetValueOrDefault(stamp.ActorId) ?? texts.GetText("Workspace.TransferSenderUnavailable", presentation.Current?.Locale);
        var local = TimeZoneInfo.ConvertTimeFromUtc(stamp.OccurredAtUtc, TimeZoneInfo.FindSystemTimeZoneById(presentation.Current?.TimeZoneId ?? "UTC"));
        var suffix = " " + texts.GetText("Workspace.TransferFolderLabel", presentation.Current?.Locale) + " " + local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var directoryName = sender[..Math.Min(sender.Length, Math.Max(0, 256 - suffix.Length))] + suffix;
        var directory = await EnsureReceivingDirectoryAsync(root, recipient, directoryName, token);
        foreach (var target in targets)
        {
            var model = target.ResourceKey switch
            {
                "workspace.folder" => WorkspacePositions.FolderModel, "part.part" => "Part", "document.document" => "Document",
                _ => throw new LinkQueryException(400, "Workspace.InvalidEndpoint")
            };
            if (!catalog.Get("workspace.content").Model.LinkedTypes.Any(pair => pair.Parent == WorkspacePositions.FolderModel && pair.Child == model))
                throw new LinkQueryException(400, "Workspace.InvalidEndpoint");
            var existing = await Links.SingleOrDefaultAsync(row => EF.Property<string>(row, "PEID") == directory
                && EF.Property<string>(row, "ChildModelKey") == model && EF.Property<string>(row, "CEID") == target.ObjectId, token);
            string linkId;
            if (existing is null)
            {
                if (model == WorkspacePositions.FolderModel)
                {
                    if (await WouldCycleAsync(directory, target.ObjectId, token)) throw new LinkQueryException(409, "Workspace.CycleForbidden");
                    var name = await Folders.Where(row => EF.Property<string>(row, "EID") == target.ObjectId)
                        .Select(row => EF.Property<string>(row, "ENAME")).SingleAsync(token);
                    if (await Links.AnyAsync(link => EF.Property<string>(link, "PEID") == directory && EF.Property<string>(link, "ChildModelKey") == model
                        && Folders.Any(folder => EF.Property<string>(folder, "EID") == EF.Property<string>(link, "CEID") && EF.Property<string>(folder, "ENAME") == name), token))
                        throw new LinkQueryException(409, "Workspace.SiblingNameConflict");
                }
                // 转交是原对象的普通新引用，沿既有显式引用合同；浅副本的中性边不套用于此。
                linkId = await RegisterCreatedReferenceAsync(directory, model, target.ObjectId, token, isScopeNeutral: false);
                await referenceAudit.AddedAsync(linkId, directory, new(model, target.ObjectId), token);
            }
            else linkId = (string)existing["LID"]!;
            db.Set<Dictionary<string, object?>>("WorkspaceTransferNotification").Add(new()
            {
                ["EID"] = ids.NewId("WorkspaceTransferNotification"), ["RecipientId"] = recipient.AccountId,
                ["SenderId"] = stamp.ActorId, ["ResourceKey"] = target.ResourceKey, ["ObjectId"] = target.ObjectId,
                ["SpaceId"] = root.SpaceId, ["DirectoryId"] = directory, ["LinkId"] = linkId
            });
        }
    }

    // 同分钟目录名称按数据库 collation 比较；只复用接收人本根下同名目录，不因别处已有对象引用而跳过。
    private async Task<string> EnsureReceivingDirectoryAsync(WorkspaceSpaceRoot root, AccountOwnership recipient, string name, CancellationToken token)
    {
        var existing = await Folders.Where(folder => EF.Property<string>(folder, "ENAME") == name
            && Links.Any(link => EF.Property<string>(link, "PEID") == root.RootFolderId
                && EF.Property<string>(link, "ChildModelKey") == WorkspacePositions.FolderModel && EF.Property<string>(link, "CEID") == EF.Property<string>(folder, "EID")))
            .Select(folder => EF.Property<string>(folder, "EID")).SingleOrDefaultAsync(token);
        if (existing is not null) return existing;
        var id = ids.NewId(WorkspacePositions.FolderModel);
        var folder = new Dictionary<string, object?> { ["EID"] = id, ["ENAME"] = name, ["Description"] = null, ["OriginSpaceId"] = root.SpaceId };
        StampReceivingFolder(folder, recipient);
        Folders.Add(folder);
        await ownershipAudit.RegisterAsync("workspace.folder", id, folder, false, token);
        await referenceAudit.FolderCreatedAsync(folder, token);
        await transactions.FlushAsync(token);
        var linkId = await RegisterCreatedReferenceAsync(root.RootFolderId, WorkspacePositions.FolderModel, id, token);
        await referenceAudit.AddedAsync(linkId, root.RootFolderId, new(WorkspacePositions.FolderModel, id), token);
        return id;
    }

    // CreatedBy 仍是执行系统接收登记的可信转交者；Owner 单独是接收者，空部门不被猜测为 sender 部门。
    private void StampReceivingFolder(Dictionary<string, object?> row, AccountOwnership recipient)
    {
        row["OwnerId"] = recipient.AccountId;
        row["OwnerDepartmentId"] = recipient.DepartmentId;
        audit.ForCreate().ApplyTo(row);
    }
}

/// <summary>Workspace 将公共转交协作绑定到自己的空间、图和事务通知，不引入跨模块私有依赖。</summary>
public sealed class WorkspaceOwnershipTransferReceiver(WorkspaceContents contents, IRuntimeTextCatalog texts,
    IRequestPresentationContextAccessor presentation, IUserDisplayNameDirectory names) : IEntityOwnershipTransferReceiver
{
    /// <inheritdoc/>
    public ValueTask PrepareAsync(CancellationToken cancellationToken = default) => contents.PrepareOwnershipReceivingAsync(cancellationToken);

    /// <inheritdoc/>
    public ValueTask ReceiveAsync(AccountOwnership recipient, IReadOnlyCollection<EntityOwnershipTransferTarget> targets,
        CancellationToken cancellationToken = default) => contents.ReceiveOwnershipAsync(recipient, targets, texts, presentation, names, cancellationToken);
}

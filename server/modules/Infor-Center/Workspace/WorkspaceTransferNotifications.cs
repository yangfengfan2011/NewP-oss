using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Ownership;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Presentation;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>私有记录键可见；对象位置仅在当前接收人通过完整来源及 read 后公开，不作为权限凭证。</summary>
public sealed record WorkspaceTransferNotificationItem(string Id, DateTime OccurredAt, string? SenderName, string? TargetName, string Status, WorkspaceTransferNotificationLocation? Location);
public sealed record WorkspaceTransferNotificationPage(IReadOnlyList<WorkspaceTransferNotificationItem> Items, int Total, int Page, int PageSize);
public sealed record WorkspaceTransferNotificationLocation(string ObjectId, string ModelKey, string SourceLinkId, WorkspacePosition Position);

/// <summary>站内转交通知属于 Workspace 私有记录，每次读取与打开仍交集当前对象读取。</summary>
public sealed class WorkspaceTransferNotifications(EfPlatformDbContext db, EntityOwnershipTransferDisplay display,
    IUserDisplayNameDirectory names, IDataScopeReadableIds readableIds, WorkspaceContents contents)
{
    /// <summary>仅分页读取可信当前接收人的记录；隐藏目标保留通用占位，不输出原名称或 ID。</summary>
    public async Task<WorkspaceTransferNotificationPage> ListAsync(string actorId, int page, int pageSize, CancellationToken token)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 100);
        if (page > int.MaxValue / pageSize) throw new LinkQueryException(400, "Workspace.InvalidPage");
        var query = Rows().AsNoTracking().Where(row => EF.Property<string>(row, "RecipientId") == actorId);
        var total = await query.CountAsync(token);
        var rows = await query.OrderByDescending(row => EF.Property<DateTime>(row, "CreatedAt")).ThenByDescending(row => EF.Property<string>(row, "EID"))
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token);
        var targets = rows.Select(row => new EntityOwnershipTransferTarget((string)row["ResourceKey"]!, (string)row["ObjectId"]!)).ToArray();
        var targetNames = await display.FindNamesAsync(targets, token);
        var locations = await ReadLocationsAsync(actorId, rows, targetNames, token);
        var senderIds = rows.Select(row => (string)row["SenderId"]!).Distinct(StringComparer.Ordinal).ToArray();
        var visibleSenders = await readableIds.GetReadableIdsAsync("identity.account", senderIds, token);
        var senderNames = await names.FindManyAsync(visibleSenders.ToArray(), token);
        var items = rows.Select(row =>
        {
            var target = new EntityOwnershipTransferTarget((string)row["ResourceKey"]!, (string)row["ObjectId"]!);
            var location = locations.GetValueOrDefault((string)row["EID"]!);
            var available = location is not null && targetNames.TryGetValue(target, out _);
            var name = available ? targetNames[target] : null;
            return new WorkspaceTransferNotificationItem((string)row["EID"]!, (DateTime)row["CreatedAt"]!,
                senderNames.GetValueOrDefault((string)row["SenderId"]!), name, available ? "available" : "unavailable", location);
        }).ToArray();
        return new(items, total, page, pageSize);
    }

    // 当前页先批量读取空间及关系身份；同一接收目录只校验一次完整路径，不逐通知重解整棵图。
    private async Task<IReadOnlyDictionary<string, WorkspaceTransferNotificationLocation>> ReadLocationsAsync(string actor,
        IReadOnlyList<Dictionary<string, object?>> rows, IReadOnlyDictionary<EntityOwnershipTransferTarget, string> readableNames, CancellationToken token)
    {
        var result = new Dictionary<string, WorkspaceTransferNotificationLocation>(StringComparer.Ordinal);
        if (rows.Count == 0) return result;
        var spaceIds = rows.Select(row => (string)row["SpaceId"]!).Distinct(StringComparer.Ordinal).ToArray();
        var spaces = await WorkspaceSpaces.Admitted(db, actor).AsNoTracking()
            .Where(space => spaceIds.Contains(EF.Property<string>(space, "EID"))).ToDictionaryAsync(space => (string)space["EID"]!, token);
        if (spaces.Count == 0) return result;
        var roots = spaces.Values.Select(space => (string)space["RootFolderId"]!).Distinct(StringComparer.Ordinal).ToArray();
        var directories = rows.Select(row => (string)row["DirectoryId"]!).Distinct(StringComparer.Ordinal).ToArray();
        var linkIds = rows.Select(row => (string)row["LinkId"]!).Distinct(StringComparer.Ordinal).ToArray();
        var links = db.Set<Dictionary<string, object?>>(WorkspacePositions.ContentModel).AsNoTracking();
        var rootLinks = await links.Where(link => roots.Contains(EF.Property<string>(link, "PEID"))
            && directories.Contains(EF.Property<string>(link, "CEID")) && EF.Property<string>(link, "ChildModelKey") == WorkspacePositions.FolderModel).ToArrayAsync(token);
        var selectedLinks = await links.Where(link => linkIds.Contains(EF.Property<string>(link, "LID")))
            .ToDictionaryAsync(link => (string)link["LID"]!, token);
        foreach (var group in rows.GroupBy(row => (Space: (string)row["SpaceId"]!, Directory: (string)row["DirectoryId"]!)))
        {
            if (!spaces.TryGetValue(group.Key.Space, out var space)) continue;
            var rootId = (string)space["RootFolderId"]!;
            var parents = rootLinks.Where(link => Equals(link["PEID"], rootId) && Equals(link["CEID"], group.Key.Directory)).ToArray();
            if (parents.Length != 1) continue;
            var position = new WorkspacePosition(group.Key.Space, rootId, [(string)parents[0]["LID"]!]);
            if (!await contents.IsReceivingPositionReadableAsync(actor, position, group.Key.Directory, token)) continue;
            foreach (var row in group)
            {
                var target = new EntityOwnershipTransferTarget((string)row["ResourceKey"]!, (string)row["ObjectId"]!);
                if (!readableNames.ContainsKey(target) || !selectedLinks.TryGetValue((string)row["LinkId"]!, out var link)
                    || !Equals(link["PEID"], group.Key.Directory) || !Equals(link["CEID"], target.ObjectId)) continue;
                var model = target.ResourceKey switch { "workspace.folder" => WorkspacePositions.FolderModel, "part.part" => "Part", "document.document" => "Document", _ => null };
                if (model is null || !Equals(link["ChildModelKey"], model)) continue;
                result.Add((string)row["EID"]!, new(target.ObjectId, model, (string)row["LinkId"]!, position));
            }
        }
        return result;
    }

    // 通知按接收人强制过滤，模型不登记 DataScopeResource，不成为新的直接 ACL 对象。
    private DbSet<Dictionary<string, object?>> Rows() => db.Set<Dictionary<string, object?>>("WorkspaceTransferNotification");
}

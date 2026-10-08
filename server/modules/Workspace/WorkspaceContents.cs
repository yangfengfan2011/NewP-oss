using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>Workspace 拥有位置内的原子写入；通用查询没有任意 Link CRUD。</summary>
public sealed class WorkspaceContents(EfPlatformDbContext db, WorkspacePositions positions, IPlatformTransactionExecutor transactions,
    IDataScopeObjectDecisionProvider decisions, IPlatformObjectIdGenerator ids, IEntityAuditValueProvider audit, LinkQueryCatalog catalog)
{
    /// <summary>在有效位置内创建实体与 Link，同父名称比较及并发串行化由 SQL Server 事务锁保护。</summary>
    public ValueTask<string> CreateFolderAsync(string actorId, WorkspacePosition position, IReadOnlyDictionary<string, JsonElement> values, CancellationToken token)
    {
        if (values.Keys.Any(key => key is not ("ENAME" or "Description"))) throw new LinkQueryException(400, "Workspace.ServerFieldForbidden", values.Keys.First(key => key is not ("ENAME" or "Description")));
        if (!values.TryGetValue("ENAME", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) || name.GetString()!.Length > 256)
            throw new LinkQueryException(400, "Workspace.InvalidName", "ENAME");
        var description = values.GetValueOrDefault("Description");
        if (description.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.String) || description.ValueKind == JsonValueKind.String && description.GetString()!.Length > 500)
            throw new LinkQueryException(400, "Workspace.DescriptionTooLong", "Description");
        return transactions.ExecuteAsync<string>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            var path = await positions.ResolveAsync(actorId, position, cancellation);
            if (!await decisions.IsAllowedAsync("workspace.folder", path.FolderId, "edit", cancellation)) throw new LinkQueryException(403, "Workspace.ParentWriteDenied");
            // 锁资源是父 EID，与当前路径无关，避免同一实体的不同出现位置并发制造重名。
            await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result = sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @result < 0 THROW 51000, 'Workspace content lock failed.', 1;",
                [new SqlParameter("@resource", "Workspace.Content." + path.FolderId)], cancellation);
            // 取得写保护后重新验证位置，不能用等待锁前的可读路径提交。
            path = await positions.ResolveAsync(actorId, position, cancellation);
            var links = db.Set<Dictionary<string, object?>>(WorkspacePositions.ContentModel);
            var folders = db.Set<Dictionary<string, object?>>(WorkspacePositions.FolderModel);
            if (await links.AnyAsync(link => EF.Property<string>(link, "PEID") == path.FolderId && EF.Property<string>(link, "ChildModelKey") == WorkspacePositions.FolderModel
                && folders.Any(folder => EF.Property<string>(folder, "EID") == EF.Property<string>(link, "CEID") && EF.Property<string>(folder, "ENAME") == name.GetString()), cancellation))
                throw new LinkQueryException(409, "Workspace.SiblingNameConflict", "ENAME");
            var id = ids.NewId("WorkspaceFolder");
            var candidate = new Dictionary<string, object?> { ["EID"] = id, ["ENAME"] = name.GetString(),
                ["Description"] = description.ValueKind == JsonValueKind.String ? description.GetString() : null, ["OriginSpaceId"] = position.SpaceId };
            audit.ForCreate().ApplyTo(candidate);
            if (!await decisions.IsCandidateAllowedAsync("workspace.folder", candidate, "create", cancellation)) throw new LinkQueryException(403, "Workspace.FolderCreateDenied");
            // 实际端点及配对必须通过同一受信目录；新对象没有外部写入其类型的机会。
            var definition = catalog.Get("workspace.content");
            if (!definition.Model.LinkedTypes.Any(pair => pair.Parent == WorkspacePositions.FolderModel && pair.Child == WorkspacePositions.FolderModel)
                || !await folders.AnyAsync(folder => EF.Property<string>(folder, "EID") == path.FolderId, cancellation))
                throw new LinkQueryException(400, "Workspace.InvalidEndpoint");
            folders.Add(candidate);
            await transactions.FlushAsync(cancellation);
            var stamp = audit.Current();
            links.Add(new()
            {
                ["LID"] = ids.NewId("WorkspaceFolderContent"), ["PEID"] = path.FolderId, ["CEID"] = id, ["ChildModelKey"] = WorkspacePositions.FolderModel,
                ["LCreatedBy"] = stamp.ActorId, ["LCreatedAt"] = stamp.OccurredAtUtc, ["LUpdatedBy"] = stamp.ActorId, ["LUpdatedAt"] = stamp.OccurredAtUtc
            });
            return id;
        }, token);
    }
}

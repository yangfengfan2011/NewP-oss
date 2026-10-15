using System.Text.Json;
using ICP.Platform.Modules.Document.Contracts;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>集中编排 Workspace 完整图命令、位置校验和引用规则；所属对象规则仍通过正式行为执行。</summary>
public sealed partial class WorkspaceContents(EfPlatformDbContext db, WorkspacePositions positions, IPlatformTransactionExecutor transactions,
    IDataScopeObjectDecisionProvider decisions, IPlatformObjectIdGenerator ids, IEntityAuditValueProvider audit, LinkQueryCatalog catalog,
    WorkspaceFolderScope folderScope, LinkQueryReader reader, IStandardCrudService crud, IStandardCrudResourceRegistry resources,
    IDocumentFiles documentFiles, WorkspaceTreeOrder treeOrder)
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
            await LockGraphAsync(cancellation);
            var path = await WritablePositionAsync(actorId, position, cancellation);
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
            await RegisterCreatedReferenceAsync(path.FolderId, WorkspacePositions.FolderModel, id, cancellation);
            return id;
        }, token);
    }

    /// <summary>已有对象引用共享真实实体；锁内重验位置、配对与目标读取，文件夹另维护完整图不变量。</summary>
    public ValueTask<bool> AddReferenceAsync(string actorId, WorkspacePosition position, LinkObjectReference target, CancellationToken token) =>
        transactions.ExecuteAsync<bool>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var path = await WritablePositionAsync(actorId, position, cancellation);
            RequireContainer(position, path, target.ModelKey);
            await AddTargetReferenceAsync(path.FolderId, target, cancellation);
            return true;
        }, token);

    /// <summary>树排序只接受完整父位置与单条关系，父写入和两端读取在同一图锁事务内重验。</summary>
    public ValueTask<WorkspaceTreeOrderResult> ReorderAsync(string actorId, WorkspacePosition parentPosition, string linkId, string direction, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(linkId) || direction is not ("up" or "down")) throw new LinkQueryException(400, "Workspace.InvalidTreeOrderInput");
        return transactions.ExecuteAsync<WorkspaceTreeOrderResult>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var path = await WritablePositionAsync(actorId, parentPosition, cancellation);
            return await treeOrder.ReorderAsync(actorId, path.FolderId, linkId, direction == "up", cancellation);
        }, token);
    }

    /// <summary>移除只删除指定 LID；关系当前父端、目标读取与有效出现位置均由提交时事实决定。</summary>
    public ValueTask<bool> RemoveAsync(string actorId, WorkspacePosition position, string linkId, CancellationToken token) =>
        RemoveReferencesAsync(actorId, position, [linkId], token);

    /// <summary>单条和一页批量移除共用完整位置、父edit及端点read；只删除确切LID，不消费对象delete或Files。</summary>
    public ValueTask<bool> RemoveReferencesAsync(string actorId, WorkspacePosition position, string[] linkIds, CancellationToken token)
    {
        ValidateSelection(linkIds);
        return transactions.ExecuteAsync<bool>(async cancellation =>
        {
            await LockGraphAsync(cancellation);
            var path = await WritablePositionAsync(actorId, position, cancellation);
            await ResolveClipboardTargetsAsync(path.FolderId, linkIds, "remove", cancellation);
            await Links.Where(row => linkIds.Contains(EF.Property<string>(row, "LID"))
                && EF.Property<string>(row, "PEID") == path.FolderId).ExecuteDeleteAsync(cancellation);
            return true;
        }, token);
    }

    // 已登记对象行为进入后由此完整创建所属对象与引用，调用方不再拼接准备和关系登记。
    internal async ValueTask<StandardCrudMutationResult> CreateObjectAsync(ObjectOperationContext context,
        IObjectOperationExecutor operations, CancellationToken cancellationToken)
    {
        var (resource, model) = context.Action == WorkspaceObjectCreation.CreatePart ? ("part.part", "Part")
            : context.Action == WorkspaceObjectCreation.CreateDocument ? ("document.document", "Document")
            : throw new InvalidOperationException("Unsupported Workspace creation action.");
        var actor = (string)context.Values["ActorId"]!;
        var position = (WorkspacePosition)context.Values["Position"]!;
        var input = (IReadOnlyDictionary<string, JsonElement>)context.Values["Values"]!;
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (field, value) in input)
        {
            if (field is not ("ECODE" or "ENAME" or "Description"))
                throw new LinkQueryException(400, "Workspace.ServerFieldForbidden", field);
            if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new LinkQueryException(400, "Workspace.InvalidCreateInput", field);
            values[field] = value.ValueKind == JsonValueKind.Null ? null : value.GetString();
        }
        await LockGraphAsync(cancellationToken);
        var path = await WritablePositionAsync(actor, position, cancellationToken);
        RequireContainer(position, path, model);
        if (!catalog.Get("workspace.content").Model.LinkedTypes.Any(pair => pair.Parent == WorkspacePositions.FolderModel && pair.Child == model))
            throw new LinkQueryException(400, "Workspace.InvalidEndpoint");
        var result = model == "Document"
            ? await documentFiles.CreateWithFilesAsync(values, context.Values.GetValueOrDefault("Uploads") as DocumentUploadSet, cancellationToken)
            : await operations.ExecuteAsync(resource, ObjectOperationAction.Create, null, values, cancellationToken);
        if (!result.Succeeded) return result;
        var id = result.Detail?.Fields.GetValueOrDefault("EID") as string
            ?? throw new InvalidOperationException("Created object identity is missing.");
        await RegisterCreatedReferenceAsync(path.FolderId, model, id, cancellationToken);
        // 不执行展示读取；事务完成后由独立列表读取决定新对象的可见性。
        return StandardCrudMutationResult.Success();
    }

    /// <summary>上传前只读重验完整位置、父 edit 与正式 Document 候选，不取得图锁或创建对象。</summary>
    public async Task<StandardCrudError?> ValidateDocumentUploadAsync(string actorId, WorkspacePosition position,
        IReadOnlyDictionary<string, JsonElement> input, string fileName, string contentType, CancellationToken token)
    {
        var path = await WritablePositionAsync(actorId, position, token);
        RequireContainer(position, path, "Document");
        if (!catalog.Get("workspace.content").Model.LinkedTypes.Any(pair => pair.Parent == WorkspacePositions.FolderModel && pair.Child == "Document"))
            throw new LinkQueryException(400, "Workspace.InvalidEndpoint");
        return await documentFiles.ValidateUploadAsync(ReadValues(input, "Document"), fileName, contentType, token);
    }

    // 锁后位置只解析一次；目标规则、所属行为和全部引用清理在同一对象操作事务内完成。
    internal async ValueTask<StandardCrudMutationResult> MutateObjectsAsync(ObjectOperationContext context,
        IObjectOperationExecutor operations, CancellationToken cancellationToken)
    {
        var actor = (string)context.Values["ActorId"]!;
        var position = (WorkspacePosition)context.Values["Position"]!;
        var linkIds = (string[])context.Values["LinkIds"]!;
        var editing = context.Action == ObjectOperationAction.Update;
        ValidateSelection(linkIds);
        if (editing && linkIds.Length != 1) throw new LinkQueryException(400, "Workspace.InvalidSelection");
        // 与创建、引用变更取得同一图锁；等待之后重新验证整个位置和对象资格。
        await LockGraphAsync(cancellationToken);
        var path = await WritablePositionAsync(actor, position, cancellationToken);
        var targets = await ResolveTargetsAsync(path.FolderId, linkIds, editing ? "edit" : "delete", cancellationToken);
        if (!editing)
        {
            var folders = targets.Where(target => target.ModelKey == WorkspacePositions.FolderModel).Select(target => target.Id).ToArray();
            // 在任何引用被清理前检查整批文件夹，避免选择的其他对象使原本非空的文件夹变空。
            if (folders.Length > 0 && await Links.AnyAsync(row => folders.Contains(EF.Property<string>(row, "PEID")), cancellationToken))
                throw new LinkQueryException(409, "Workspace.FolderNotEmpty");
        }
        foreach (var target in targets)
        {
            if (editing)
            {
                var values = ReadValues((IReadOnlyDictionary<string, JsonElement>)context.Values["Values"]!, target.ModelKey);
                if (target.ModelKey == WorkspacePositions.FolderModel) await UpdateFolderAsync(target.Id, values, cancellationToken);
                else
                {
                    var result = await operations.ExecuteAsync(Resource(target.ModelKey), ObjectOperationAction.Update, target.Id, values, cancellationToken);
                    if (!result.Succeeded) return result;
                }
            }
            else
            {
                // 清理全部入向关系留在 SQL 内；后续对象失败时同一事务回滚，包括先前成功的批量项。
                await Links.Where(row => EF.Property<string>(row, "ChildModelKey") == target.ModelKey && EF.Property<string>(row, "CEID") == target.Id)
                    .ExecuteDeleteAsync(cancellationToken);
                if (target.ModelKey == WorkspacePositions.FolderModel)
                    await Folders.Where(row => EF.Property<string>(row, "EID") == target.Id).ExecuteDeleteAsync(cancellationToken);
                else
                {
                    var result = await operations.ExecuteAsync(Resource(target.ModelKey), ObjectOperationAction.Delete, target.Id, new Dictionary<string, object?>(), cancellationToken);
                    if (!result.Succeeded) return result;
                }
            }
        }
        return StandardCrudMutationResult.Success();
    }

    /// <summary>编辑装载不取图写锁，仍验证完整位置和 edit 资格；提交时另行执行锁后重验。</summary>
    public async Task<IReadOnlyDictionary<string, object?>> ReadEditValuesAsync(string actor, WorkspacePosition position, string linkId, CancellationToken token)
    {
        ValidateSelection([linkId]);
        var path = await WritablePositionAsync(actor, position, token);
        var target = (await ResolveTargetsAsync(path.FolderId, [linkId], "edit", token)).Single();
        IReadOnlyDictionary<string, object?> fields;
        if (target.ModelKey == WorkspacePositions.FolderModel)
            fields = await Folders.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == target.Id, token);
        else
            fields = (await crud.GetDetailAsync(resources.FindByResourceKey(Resource(target.ModelKey))!, target.Id, token))?.Fields
                ?? throw new LinkQueryException(404, "Workspace.TargetUnavailable");
        string[] names = target.ModelKey == WorkspacePositions.FolderModel ? ["ENAME", "Description"] : ["ECODE", "ENAME", "Description"];
        return names.ToDictionary(name => name, name => fields.GetValueOrDefault(name));
    }

    private DbSet<Dictionary<string, object?>> Links => db.Set<Dictionary<string, object?>>(WorkspacePositions.ContentModel);
    private DbSet<Dictionary<string, object?>> Folders => db.Set<Dictionary<string, object?>>(WorkspacePositions.FolderModel);

    // 用本次已验证的父身份批量匹配 LID，再对最多一页对象重验端点和动作；不重新解析位置或接受客户端类型/EID。
    private async Task<LinkObjectReference[]> ResolveTargetsAsync(string parentId, string[] linkIds, string action, CancellationToken token)
    {
        var rows = await Links.AsNoTracking().Where(row => linkIds.Contains(EF.Property<string>(row, "LID")) && EF.Property<string>(row, "PEID") == parentId)
            .Select(row => new { Model = EF.Property<string>(row, "ChildModelKey"), Id = EF.Property<string>(row, "CEID") }).ToArrayAsync(token);
        if (rows.Length != linkIds.Length) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
        var targets = rows.Select(row => new LinkObjectReference(row.Model, row.Id)).Distinct().ToArray();
        foreach (var target in targets)
        {
            var resource = Resource(target.ModelKey);
            if (!await reader.IsEndpointReadableAsync("workspace.content", target, token)) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            if (!await decisions.IsAllowedAsync(resource, target.Id, action, token)) throw new LinkQueryException(403, "Workspace.ObjectWriteDenied");
            if (target.ModelKey == WorkspacePositions.FolderModel && await db.Set<Dictionary<string, object?>>("WorkspaceSpace")
                .AnyAsync(row => EF.Property<string>(row, "RootFolderId") == target.Id, token)) throw new LinkQueryException(409, "Workspace.RootMutationForbidden");
        }
        return targets;
    }

    // 每个请求最多消费标准列表一页；不接受空、重复或无界选择。
    private static void ValidateSelection(string[] ids)
    {
        if (ids.Length is < 1 or > 100 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length)
            throw new LinkQueryException(400, "Workspace.InvalidSelection");
    }

    // 静态允许的类型与行为对应关系属于 Workspace；未知端点失败关闭。
    private static string Resource(string model) => model switch
    {
        WorkspacePositions.FolderModel => "workspace.folder", "Part" => "part.part", "Document" => "document.document",
        _ => throw new LinkQueryException(400, "Workspace.InvalidEndpoint")
    };

    // 请求字段白名单在进入目标行为前检查；所属模块仍执行规范化、长度和唯一性规则。
    private static Dictionary<string, object?> ReadValues(IReadOnlyDictionary<string, JsonElement> input, string model)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in input)
        {
            if (key is not ("ENAME" or "Description") && (key != "ECODE" || model == WorkspacePositions.FolderModel))
                throw new LinkQueryException(400, "Workspace.ServerFieldForbidden", key);
            if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw new LinkQueryException(400, "Workspace.InvalidEditInput", key);
            values[key] = value.ValueKind == JsonValueKind.Null ? null : value.GetString();
        }
        return values;
    }

    // 同名约束作用于所有真实父文件夹；只检查是否冲突，不物化或返回不可见路径。
    private async Task UpdateFolderAsync(string id, IReadOnlyDictionary<string, object?> values, CancellationToken token)
    {
        var folder = await Folders.SingleAsync(row => EF.Property<string>(row, "EID") == id, token);
        if (values.TryGetValue("ENAME", out var name))
        {
            if (name is not string text || string.IsNullOrWhiteSpace(text) || text.Length > 256) throw new LinkQueryException(400, "Workspace.InvalidName", "ENAME");
            if (await Links.AnyAsync(sibling => EF.Property<string>(sibling, "ChildModelKey") == WorkspacePositions.FolderModel
                && EF.Property<string>(sibling, "CEID") != id
                && Links.Any(parent => EF.Property<string>(parent, "ChildModelKey") == WorkspacePositions.FolderModel && EF.Property<string>(parent, "CEID") == id
                    && EF.Property<string>(parent, "PEID") == EF.Property<string>(sibling, "PEID"))
                && Folders.Any(other => EF.Property<string>(other, "EID") == EF.Property<string>(sibling, "CEID") && EF.Property<string>(other, "ENAME") == text), token))
                throw new LinkQueryException(409, "Workspace.SiblingNameConflict", "ENAME");
        }
        if (values.GetValueOrDefault("Description") is string { Length: > 500 }) throw new LinkQueryException(400, "Workspace.DescriptionTooLong", "Description");
        foreach (var (key, value) in values) folder[key] = value;
        audit.ForUpdate((string)folder["CreatedBy"]!, (DateTime)folder["CreatedAt"]!).ApplyTo(folder);
    }

    /// <summary>仅消费同事务内所属行为返回的真实身份；关系审计复用本次对象操作的时间和主体。</summary>
    private async Task RegisterCreatedReferenceAsync(string parentId, string modelKey, string objectId, CancellationToken token)
    {
        if (!transactions.IsActive) throw new InvalidOperationException("Workspace creation requires an active transaction.");
        var stamp = audit.Current();
        db.Set<Dictionary<string, object?>>(WorkspacePositions.ContentModel).Add(new()
        {
            ["LID"] = ids.NewId("WorkspaceFolderContent"), ["PEID"] = parentId, ["CEID"] = objectId, ["ChildModelKey"] = modelKey,
            ["TreeOrdinal"] = modelKey == WorkspacePositions.FolderModel ? await treeOrder.NextOrdinalAsync(parentId, token) : (long?)null,
            ["LCreatedBy"] = stamp.ActorId, ["LCreatedAt"] = stamp.OccurredAtUtc, ["LUpdatedBy"] = stamp.ActorId, ["LUpdatedAt"] = stamp.OccurredAtUtc
        });
    }

    // 所有内容写入按相同顺序取得跨 Host 的事务级图锁，防止互成环及 create/add 同级名竞争。
    private Task LockGraphAsync(CancellationToken token)
    {
        if (!transactions.IsActive) throw new InvalidOperationException("Workspace graph writes require an active transaction.");
        return db.Database.ExecuteSqlRawAsync(
            "DECLARE @result int; EXEC @result = sp_getapplock @Resource=N'Workspace.FolderGraph', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @result < 0 THROW 51000, 'Workspace content lock failed.', 1;", token);
    }

    // 写命令在图锁后重验位置和父动作；编辑装载只读取资格，不能替代提交时重验。
    private async Task<WorkspacePositionPath> WritablePositionAsync(string actorId, WorkspacePosition position, CancellationToken token)
    {
        var path = await positions.ResolveAsync(actorId, position, token);
        if (!await decisions.IsAllowedAsync("workspace.folder", path.FolderId, "edit", token)) throw new LinkQueryException(403, "Workspace.ParentWriteDenied");
        return path;
    }

    // 防环观察完整存储图，包括不可见节点与跨空间关系；只返回布尔值，不泄露名称或路径。
    private async Task<bool> WouldCycleAsync(string parent, string child, CancellationToken token)
    {
        var link = db.Model.FindEntityType(WorkspacePositions.ContentModel)!;
        var idType = db.Model.FindEntityType(WorkspacePositions.FolderModel)!.FindProperty("EID")!.GetColumnType()!;
        var sql = $"""
            SET NOCOUNT ON;
            DECLARE @seen TABLE(EID {idType} COLLATE DATABASE_DEFAULT PRIMARY KEY);
            DECLARE @frontier TABLE(EID {idType} COLLATE DATABASE_DEFAULT PRIMARY KEY);
            DECLARE @next TABLE(EID {idType} COLLATE DATABASE_DEFAULT PRIMARY KEY);
            INSERT INTO @seen VALUES(@child); INSERT INTO @frontier VALUES(@child);
            WHILE EXISTS(SELECT 1 FROM @frontier) AND NOT EXISTS(SELECT 1 FROM @seen WHERE EID=@parent)
            BEGIN
                INSERT INTO @next SELECT DISTINCT l.{WorkspaceFolderScope.Column(link, "CEID")} FROM {WorkspaceFolderScope.Table(link)} l
                    JOIN @frontier f ON f.EID=l.{WorkspaceFolderScope.Column(link, "PEID")}
                    WHERE l.{WorkspaceFolderScope.Column(link, "ChildModelKey")}=N'WorkspaceFolder'
                    AND NOT EXISTS(SELECT 1 FROM @seen v WHERE v.EID=l.{WorkspaceFolderScope.Column(link, "CEID")});
                DELETE FROM @frontier; INSERT INTO @frontier SELECT EID FROM @next;
                INSERT INTO @seen SELECT EID FROM @next; DELETE FROM @next;
            END;
            SELECT 1 WHERE EXISTS(SELECT 1 FROM @seen WHERE EID=@parent);
            """;
        return await folderScope.ExecuteAsync(sql, [new SqlParameter("@parent", parent), new SqlParameter("@child", child)], async reader => await reader.ReadAsync(token), token);
    }
}

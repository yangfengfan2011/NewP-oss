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
    IPlatformObjectIdGenerator ids, IEntityAuditValueProvider audit, LinkQueryCatalog catalog,
    WorkspaceFolderScope folderScope, LinkQueryReader reader, IStandardCrudService crud, IStandardCrudResourceRegistry resources,
    IDocumentFiles documentFiles, IDocumentCopies documentCopies, WorkspaceTreeOrder treeOrder, WorkspaceReferenceAudit referenceAudit,
    Microsoft.Extensions.Logging.ILogger<WorkspaceContents> logger,
    ICP.Platform.Crud.Ownership.EntityOwnershipValues ownership,
    ICP.Platform.Crud.Ownership.EntityOwnershipAudit ownershipAudit, WorkspaceFolderReadDescriptor folderDescriptor,
    IDataScopeObjectInitializer parentAuthorizations, WorkspaceSpaceInitialization spaceInitialization, IDataExecutionAccess executionAccess)
{
    /// <summary>保留旧调用不复制父授权的行为；新的初始化语义须由真实业务入口明确选择。</summary>
    public ValueTask<string> CreateFolderAsync(string actorId, WorkspacePosition position, IReadOnlyDictionary<string, JsonElement> values, CancellationToken token) =>
        CreateFolderAsync(actorId, position, values, false, token);

    /// <summary>在有效位置内创建实体与 Link，同父名称比较及并发串行化由 SQL Server 事务锁保护。</summary>
    public ValueTask<string> CreateFolderAsync(string actorId, WorkspacePosition position, IReadOnlyDictionary<string, JsonElement> values,
        bool copyParentAuthorization, CancellationToken token) =>
        CreateFolderCoreAsync(actorId, position, values, copyParentAuthorization, new FolderCreationSource.Declared(WorkspaceFolderExecutions.CreateBinding(), WorkspaceFolderExecutions.CreateCaller()), token);

    // 正式创建与明确旧复制共享原事务；来源由实际调用分支选择，缺声明不能触发旧路径。
    private ValueTask<string> CreateFolderCoreAsync(string actorId, WorkspacePosition position, IReadOnlyDictionary<string, JsonElement> values,
        bool copyParentAuthorization, FolderCreationSource source, CancellationToken token)
    {
        if (values.Keys.Any(key => key is not ("ENAME" or "Description"))) throw new LinkQueryException(400, "Workspace.ServerFieldForbidden", values.Keys.First(key => key is not ("ENAME" or "Description")));
        if (!values.TryGetValue("ENAME", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) || name.GetString()!.Length > 256)
            throw new LinkQueryException(400, "Workspace.InvalidName", "ENAME");
        var description = values.GetValueOrDefault("Description");
        if (description.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.String) || description.ValueKind == JsonValueKind.String && description.GetString()!.Length > 500)
            throw new LinkQueryException(400, "Workspace.DescriptionTooLong", "Description");
        if (source is FolderCreationSource.Declared declared) executionAccess.Validate([declared.Binding], "workspace.folder");
        return transactions.ExecuteAsync<string>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var path = await ResolveFolderCreationPositionAsync(source, actorId, position, cancellation);
            var links = db.Set<Dictionary<string, object?>>(WorkspacePositions.ContentModel);
            var folders = db.Set<Dictionary<string, object?>>(WorkspacePositions.FolderModel);
            if (await links.AnyAsync(link => EF.Property<string>(link, "PEID") == path.FolderId && EF.Property<string>(link, "ChildModelKey") == WorkspacePositions.FolderModel
                && folders.Any(folder => EF.Property<string>(folder, "EID") == EF.Property<string>(link, "CEID") && EF.Property<string>(folder, "ENAME") == name.GetString()), cancellation))
                throw new LinkQueryException(409, "Workspace.SiblingNameConflict", "ENAME");
            var candidate = await PrepareFolderCandidateAsync(name.GetString()!, description.ValueKind == JsonValueKind.String ? description.GetString() : null, position.SpaceId, source, cancellation);
            var id = (string)candidate["EID"]!;
            // 实际端点及配对必须通过同一受信目录；新对象没有外部写入其类型的机会。
            var definition = catalog.Get("workspace.content");
            if (!definition.Model.LinkedTypes.Any(pair => pair.Parent == WorkspacePositions.FolderModel && pair.Child == WorkspacePositions.FolderModel)
                || !await folders.AnyAsync(folder => EF.Property<string>(folder, "EID") == path.FolderId, cancellation))
                throw new LinkQueryException(400, "Workspace.InvalidEndpoint");
            folders.Add(candidate);
            await ownershipAudit.RegisterAsync("workspace.folder", id, candidate, false, cancellation);
            await referenceAudit.FolderCreatedAsync(candidate, cancellation);
            // 空间根不是普通父授权来源；选项不扩大到Part/Document，也不复制源文件夹ACL。
            if (copyParentAuthorization && path.FolderId != position.RootFolderId)
            {
                var initialized = await parentAuthorizations.InitializeFromParentAsync("workspace.folder", path.FolderId, id,
                    FolderInitializationSource(source, [path.FolderId], candidate), cancellation);
                // 通用授权保存不取图锁；父ACL锁保持到提交，在此重验读取与edit，空配置也不能跳过。
                await ResolveFolderCreationPositionAsync(source, actorId, position, cancellation);
                if (initialized) await referenceAudit.ParentAuthorizationInitializedAsync(path.FolderId, candidate, cancellation);
            }
            await transactions.FlushAsync(cancellation);
            var linkId = await RegisterCreatedReferenceAsync(path.FolderId, WorkspacePositions.FolderModel, id, cancellation);
            await referenceAudit.AddedAsync(linkId, path.FolderId, new(WorkspacePositions.FolderModel, id), cancellation);
            return id;
        }, token);
    }

    // 普通创建和结构复制共用字段、目标归属、可信审计及实际候选create；准备不写入或要求新对象read。
    private async Task<Dictionary<string, object?>> PrepareFolderCandidateAsync(string name, string? description, string spaceId,
        FolderCreationSource source, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256) throw new LinkQueryException(400, "Workspace.InvalidName", "ENAME");
        if (description?.Length > 500) throw new LinkQueryException(400, "Workspace.DescriptionTooLong", "Description");
        var candidate = new Dictionary<string, object?> { ["EID"] = ids.NewId(WorkspacePositions.FolderModel), ["ENAME"] = name,
            ["Description"] = description, ["OriginSpaceId"] = spaceId };
        audit.ForCreate().ApplyTo(candidate);
        await ownership.ApplyCreateAsync(candidate, ["OwnerId", "OwnerDepartmentId"], token);
        var allowed = source switch
        {
            FolderCreationSource.Declared declared => await executionAccess.EvaluateCandidateAsync(declared.Binding, "workspace.folder", "candidate", candidate, token) == DataExecutionDecision.Allowed,
            _ => throw new ActionDataAccessException(400, "ActionDataAccess.InitializationSourceInvalid")
        };
        if (!allowed) throw new LinkQueryException(403, "Workspace.FolderCreateDenied");
        return candidate;
    }

    private abstract record FolderCreationSource
    {
        public sealed record Declared(DataExecutionBindingReference Binding, DataExecutionInternalBinding Caller) : FolderCreationSource;
    }

    // 正式parent角色复用原位置解析事实；普通创建和两种复制各自携带实际entry来源。
    private Task<WorkspacePositionPath> ResolveFolderCreationPositionAsync(FolderCreationSource source, string actor,
        WorkspacePosition position, CancellationToken token) => source switch
    {
        FolderCreationSource.Declared declared => ResolveClipboardPositionAsync(declared.Binding, "parent", position, token),
        _ => throw new ActionDataAccessException(400, "ActionDataAccess.InitializationSourceInvalid")
    };

    // 新目标身份与父集合来自刚形成的实际候选和锁内位置；源身份不来自JSON或允许标记。
    private static DataScopeObjectInitializationSource FolderInitializationSource(FolderCreationSource source,
        IReadOnlyList<string> parents, IReadOnlyDictionary<string, object?> candidate)
    {
        var target = new DataExecutionWriteFacts("workspace.folder", (string)candidate["EID"]!, candidate);
        return source switch
        {
            FolderCreationSource.Declared declared => new DataScopeObjectInitializationSource.Declared(declared.Caller, declared.Binding, parents, target),
            _ => throw new ActionDataAccessException(400, "ActionDataAccess.InitializationSourceInvalid")
        };
    }

    /// <summary>已有对象引用共享真实实体；锁内重验位置、配对与目标读取，文件夹另维护完整图不变量。</summary>
    public ValueTask<bool> AddReferenceAsync(string actorId, WorkspacePosition position, LinkObjectReference target, CancellationToken token) =>
        transactions.ExecuteAsync<bool>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var path = await ResolveReferenceParentAsync(position, cancellation);
            RequireContainer(position, path, target.ModelKey);
            var linkId = await AddTargetReferenceAsync(path.FolderId, target, ReferenceReadSource.Api, cancellation);
            await referenceAudit.AddedAsync(linkId, path.FolderId, target, cancellation);
            return true;
        }, token);

    // 正式输入在原图锁内解析父对象；平台continuation在同一范围检查并执行，不把所选LID当作父EID。
    internal ValueTask<WorkspaceTreeOrderResult> WithReorderBoundaryAsync(WorkspaceReorderInput input, string actionKey,
        Func<WorkspaceReorderState, CancellationToken, Task<WorkspaceTreeOrderResult>> execute, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(input.LinkId) || input.Direction is not ("up" or "down")
            || actionKey != (input.Direction == "up" ? "moveUp" : "moveDown")) throw new LinkQueryException(400, "Workspace.InvalidTreeOrderInput");
        return transactions.ExecuteAsync<WorkspaceTreeOrderResult>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var path = await positions.ResolveAsync(input.ActorId, input.ParentPosition, cancellation);
            var before = await Folders.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == path.FolderId, cancellation);
            return await execute(new(input.ActorId, path.FolderId, input.LinkId, input.Direction == "up", before, input.ParentPosition), cancellation);
        }, token);
    }

    // 只有统一处理器完成当前父目标检查后进入实际交换；所选和邻居读取继续由原排序模块拥有。
    internal Task<WorkspaceTreeOrderResult> ExecuteReorderAsync(WorkspaceReorderState state, CancellationToken token) =>
        treeOrder.ReorderAsync(state.ActorId, state.ParentId, state.LinkId, state.Up, token);

    /// <summary>移除只删除指定 LID；关系当前父端、目标读取与有效出现位置均由提交时事实决定。</summary>
    public ValueTask<bool> RemoveAsync(string actorId, WorkspacePosition position, string linkId, CancellationToken token) =>
        RemoveReferencesAsync(actorId, position, [linkId], token);

    /// <summary>单条和一页批量移除共用完整位置、父edit及端点read；确切LID删除及旧关系业务记录共享原事务，不消费对象delete或Files。</summary>
    public ValueTask<bool> RemoveReferencesAsync(string actorId, WorkspacePosition position, string[] linkIds, CancellationToken token)
    {
        ValidateSelection(linkIds);
        var binding = WorkspaceClipboardExecutions.Binding(WorkspaceClipboardExecutions.Remove, "submit");
        executionAccess.Validate([binding], "workspace.folder");
        return transactions.ExecuteAsync<bool>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            await LockGraphAsync(cancellation);
            var path = await ResolveClipboardPositionAsync(binding, "sourceParent", position, cancellation);
            var targets = await ResolveDeclaredClipboardTargetsAsync(path.FolderId, linkIds, binding, cancellation);
            await Links.Where(row => linkIds.Contains(EF.Property<string>(row, "LID"))
                && EF.Property<string>(row, "PEID") == path.FolderId).ExecuteDeleteAsync(cancellation);
            for (var index = 0; index < targets.Length; index++)
                await referenceAudit.RemovedAsync(linkIds[index], path.FolderId, targets[index], cancellation);
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
        var binding = model == "Part" ? WorkspaceObjectExecutions.CreatePartBinding() : WorkspaceObjectExecutions.CreateDocumentBinding("submit");
        executionAccess.Validate([binding], "workspace.folder");
        await LockGraphAsync(cancellationToken);
        var path = await ResolveClipboardPositionAsync(binding, "parent", position, cancellationToken);
        RequireContainer(position, path, model);
        if (!catalog.Get("workspace.content").Model.LinkedTypes.Any(pair => pair.Parent == WorkspacePositions.FolderModel && pair.Child == model))
            throw new LinkQueryException(400, "Workspace.InvalidEndpoint");
        var result = model == "Document"
            ? await documentFiles.CreateWithFilesAsync(values, context.Values.GetValueOrDefault("Uploads") as DocumentUploadSet, cancellationToken)
            : await operations.ExecuteAsync(resource, ObjectOperationAction.Create, null, values,
                WorkspaceObjectExecutions.PartCreateSource(), cancellationToken);
        if (!result.Succeeded) return result;
        var id = result.Detail?.Fields.GetValueOrDefault("EID") as string
            ?? throw new InvalidOperationException("Created object identity is missing.");
        var linkId = await RegisterCreatedReferenceAsync(path.FolderId, model, id, cancellationToken);
        await referenceAudit.AddedAsync(linkId, path.FolderId, new(model, id), cancellationToken);
        // 不执行展示读取；事务完成后由独立列表读取决定新对象的可见性。
        return StandardCrudMutationResult.Success();
    }

    /// <summary>上传前只读重验完整位置、父 edit 与正式 Document 候选，不取得图锁或创建对象。</summary>
    public async Task<StandardCrudError?> ValidateDocumentUploadAsync(string actorId, WorkspacePosition position,
        IReadOnlyDictionary<string, JsonElement> input, string fileName, string contentType, CancellationToken token)
    {
        var binding = WorkspaceObjectExecutions.CreateDocumentBinding("prepare");
        executionAccess.Validate([binding], "workspace.folder");
        var path = await ResolveClipboardPositionAsync(binding, "parent", position, token);
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
        var binding = editing ? WorkspaceFolderExecutions.EditBinding("submit") : WorkspaceObjectExecutions.DeleteBinding();
        executionAccess.Validate([binding], "workspace.folder");
        await LockGraphAsync(cancellationToken);
        var path = await ResolveClipboardPositionAsync(binding, "parent", position, cancellationToken);
        var targets = editing
            ? await ResolveFolderEditTargetsAsync(path.FolderId, linkIds, false, cancellationToken)
            : await ResolveDeclaredMutationTargetsAsync(path.FolderId, linkIds, WorkspaceObjectExecutions.DeleteTargetCaller(), "target", cancellationToken);
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
                    var result = await operations.ExecuteAsync(Resource(target.ModelKey), ObjectOperationAction.Update, target.Id, values,
                        WorkspaceObjectExecutions.MutationSource(target.ModelKey, target.Id, ObjectOperationAction.Update), cancellationToken);
                    if (!result.Succeeded) return result;
                }
            }
            else
            {
                var deletedFolder = target.ModelKey == WorkspacePositions.FolderModel
                    ? await Folders.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == target.Id, cancellationToken) : null;
                // 旧行仍在时流式登记全部入向事实；不把删除后的LID再查回来，也不引入关系数量上限。
                await foreach (var link in Links.AsNoTracking()
                    .Where(row => EF.Property<string>(row, "ChildModelKey") == target.ModelKey && EF.Property<string>(row, "CEID") == target.Id)
                    .Select(row => new { Id = EF.Property<string>(row, "LID"), ParentId = EF.Property<string>(row, "PEID") })
                    .AsAsyncEnumerable().WithCancellation(cancellationToken))
                    await referenceAudit.RemovedAsync(link.Id, link.ParentId, new(target.ModelKey, target.Id), cancellationToken);
                // 清理全部入向关系留在 SQL 内；后续对象失败时同一事务回滚，包括先前成功的批量项。
                await Links.Where(row => EF.Property<string>(row, "ChildModelKey") == target.ModelKey && EF.Property<string>(row, "CEID") == target.Id)
                    .ExecuteDeleteAsync(cancellationToken);
                if (target.ModelKey == WorkspacePositions.FolderModel)
                {
                    await Folders.Where(row => EF.Property<string>(row, "EID") == target.Id).ExecuteDeleteAsync(cancellationToken);
                    await referenceAudit.FolderDeletedAsync(deletedFolder!, cancellationToken);
                }
                else
                {
                    var result = await operations.ExecuteAsync(Resource(target.ModelKey), ObjectOperationAction.Delete, target.Id, new Dictionary<string, object?>(),
                        WorkspaceObjectExecutions.MutationSource(target.ModelKey, target.Id, ObjectOperationAction.Delete), cancellationToken);
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
        var binding = WorkspaceFolderExecutions.EditBinding("prepare");
        executionAccess.Validate([binding], "workspace.folder");
        var path = await ResolveClipboardPositionAsync(binding, "parent", position, token);
        var target = (await ResolveFolderEditTargetsAsync(path.FolderId, [linkId], true, token)).Single();
        var detail = await crud.GetDetailFromSourceAsync(target.ModelKey == WorkspacePositions.FolderModel
                ? folderDescriptor.Value : resources.FindByResourceKey(Resource(target.ModelKey))!, target.Id,
            new("workspace.editValues.detail", "workspace.backend", typeof(WorkspaceContents), nameof(ReadEditValuesAsync)), token);
        var fields = detail?.Fields ?? throw new LinkQueryException(404, "Workspace.TargetUnavailable");
        string[] writable = target.ModelKey == WorkspacePositions.FolderModel ? ["ENAME", "Description"] : ["ECODE", "ENAME", "Description"];
        string[] names = [.. writable, "OwnerId", "OwnerDepartmentId", "OwnerId_DisplayValue", "OwnerDepartmentId_DisplayValue"];
        return names.ToDictionary(name => name, name => fields.GetValueOrDefault(name));
    }

    private DbSet<Dictionary<string, object?>> Links => db.Set<Dictionary<string, object?>>(WorkspacePositions.ContentModel);
    private DbSet<Dictionary<string, object?>> Folders => db.Set<Dictionary<string, object?>>(WorkspacePositions.FolderModel);

    // 用本次已验证的父身份批量匹配LID；正式内部完整读取保留原端点read、独立data-read和根约束顺序。
    private async Task<LinkObjectReference[]> ResolveTargetsAsync(string parentId, string[] linkIds, CancellationToken token)
    {
        var targets = await ReadMutationTargetsAsync(parentId, linkIds, token);
        foreach (var target in targets)
        {
            var resource = Resource(target.ModelKey);
            var result = await executionAccess.EvaluateInternalObjectAsync(new(WorkspacePermissionTargetReadResolver.Execution,
                "workspace.backend", typeof(WorkspaceContents), nameof(ResolveTargetsAsync)), "target", new(resource, target.Id, target), token);
            if (result.Decision == DataExecutionDecision.NotFound) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            if (result.Decision == DataExecutionDecision.Denied) throw new LinkQueryException(403, "Workspace.ObjectWriteDenied");
            if (result.Target?.ResourceKey != resource || result.Target.ObjectId != target.Id)
                throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
            await RequireNonRootMutationTargetAsync(target, token);
        }
        return targets;
    }

    // 共享原LID查询仍在父裁决之后执行一次；不预读目标、改选其他父或重新解析位置。
    private async Task<LinkObjectReference[]> ReadMutationTargetsAsync(string parentId, string[] linkIds, CancellationToken token)
    {
        var rows = await Links.AsNoTracking().Where(row => linkIds.Contains(EF.Property<string>(row, "LID")) && EF.Property<string>(row, "PEID") == parentId)
            .Select(row => new { Model = EF.Property<string>(row, "ChildModelKey"), Id = EF.Property<string>(row, "CEID") }).ToArrayAsync(token);
        if (rows.Length != linkIds.Length) throw new LinkQueryException(404, "Workspace.ReferenceUnavailable");
        return rows.Select(row => new LinkObjectReference(row.Model, row.Id)).Distinct().ToArray();
    }

    // 文件夹内容的实际目标复用原阶段与读取解析器；对象所属写入仍在原before-facts点复核。
    private Task<LinkObjectReference[]> ResolveFolderEditTargetsAsync(string parentId, string[] linkIds, bool preparing, CancellationToken token) =>
        ResolveDeclaredMutationTargetsAsync(parentId, linkIds, WorkspaceFolderExecutions.EditTargetCaller(preparing),
            WorkspaceFolderExecutions.EditTargetRole(preparing), token);

    // 父裁决之后只执行原一次LID查询，并按原目标顺序消费固定内部read/action及根约束。
    private async Task<LinkObjectReference[]> ResolveDeclaredMutationTargetsAsync(string parentId, string[] linkIds,
        DataExecutionInternalBinding source, string role, CancellationToken token)
    {
        var targets = await ReadMutationTargetsAsync(parentId, linkIds, token);
        foreach (var target in targets)
        {
            var resource = Resource(target.ModelKey);
            var result = await executionAccess.EvaluateInternalObjectAsync(source, role, new(resource, target.Id, target), token);
            if (result.Decision == DataExecutionDecision.NotFound) throw new LinkQueryException(404, "Workspace.TargetUnavailable");
            if (result.Decision == DataExecutionDecision.Denied) throw new LinkQueryException(403, "Workspace.ObjectWriteDenied");
            if (result.Target?.ResourceKey != resource || result.Target.ObjectId != target.Id)
                throw new ActionDataAccessException(400, "ActionDataAccess.ShapeInvalid");
            await RequireNonRootMutationTargetAsync(target, token);
        }
        return targets;
    }

    // 根约束保持在该对象read/edit裁决之后，不能因正式绑定而提前或省略。
    private async Task RequireNonRootMutationTargetAsync(LinkObjectReference target, CancellationToken token)
    {
        if (target.ModelKey == WorkspacePositions.FolderModel && await db.Set<Dictionary<string, object?>>("WorkspaceSpace")
            .AnyAsync(row => EF.Property<string>(row, "RootFolderId") == target.Id, token)) throw new LinkQueryException(409, "Workspace.RootMutationForbidden");
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
        var changed = values.Any(pair => !Equals(folder.GetValueOrDefault(pair.Key), pair.Value));
        foreach (var (key, value) in values) folder[key] = value;
        audit.ForUpdate((string)folder["CreatedBy"]!, (DateTime)folder["CreatedAt"]!).ApplyTo(folder);
        if (changed) await referenceAudit.FolderUpdatedAsync(folder, token);
    }

    /// <summary>普通新引用可贡献空间范围；移动时保留原边语义，盖章复用当前操作的时间和主体。</summary>
    private async Task<string> RegisterCreatedReferenceAsync(string parentId, string modelKey, string objectId, CancellationToken token, bool? isScopeNeutral = false)
    {
        if (!transactions.IsActive) throw new InvalidOperationException("Workspace creation requires an active transaction.");
        var stamp = audit.Current();
        var linkId = ids.NewId("WorkspaceFolderContent");
        db.Set<Dictionary<string, object?>>(WorkspacePositions.ContentModel).Add(new()
        {
            ["LID"] = linkId, ["PEID"] = parentId, ["CEID"] = objectId, ["ChildModelKey"] = modelKey,
            ["TreeOrdinal"] = modelKey == WorkspacePositions.FolderModel ? await treeOrder.NextOrdinalAsync(parentId, token) : (long?)null,
            ["IsScopeNeutral"] = isScopeNeutral,
            ["LCreatedBy"] = stamp.ActorId, ["LCreatedAt"] = stamp.OccurredAtUtc, ["LUpdatedBy"] = stamp.ActorId, ["LUpdatedAt"] = stamp.OccurredAtUtc
        });
        return linkId;
    }

    // 所有内容写入按相同顺序取得跨 Host 的事务级图锁，防止互成环及 create/add 同级名竞争。
    private Task LockGraphAsync(CancellationToken token)
    {
        if (!transactions.IsActive) throw new InvalidOperationException("Workspace graph writes require an active transaction.");
        return db.Database.ExecuteSqlRawAsync(
            "DECLARE @result int; EXEC @result = sp_getapplock @Resource=N'Workspace.FolderGraph', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @result < 0 THROW 51000, 'Workspace content lock failed.', 1;", token);
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

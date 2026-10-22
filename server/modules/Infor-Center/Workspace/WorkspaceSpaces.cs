using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>仅公开导航所需的空间与根身份，不暴露主体或文件夹来源。</summary>
public sealed record WorkspaceSpaceRoot(string SpaceId, string SpaceKind, string RootFolderId, string Name, bool HasChildren = false);

/// <summary>空间初始化与强制准入；普通内容权限不由初始化命令授予。</summary>
public sealed class WorkspaceSpaces(EfPlatformDbContext db, IPlatformTransactionExecutor transactions, Func<IDataExecutionAccess> executionAccess,
    IStandardCrudService details, WorkspaceFolderReadDescriptor descriptor, WorkspaceFolderScope folderScope,
    ICP.Platform.Crud.Ownership.EntityOwnershipValues ownership, WorkspaceSpaceInitialization initialization,
    ICP.Platform.Persistence.Auditing.IEntityAuditValueProvider audit)
{
    private const string SpaceModel = "WorkspaceSpace";
    private const string FolderModel = "WorkspaceFolder";

    /// <summary>只查询公共空间及可信当前账号的个人空间，GET 不创建记录。</summary>
    public async Task<IReadOnlyList<WorkspaceSpaceRoot>> ListAsync(string actorId, CancellationToken token = default)
    {
        var folders = await ReadableFoldersAsync(token);
        return await Admitted(db, actorId).Where(space => folders.Any(folder =>
                EF.Property<string>(folder, "EID") == EF.Property<string>(space, "RootFolderId")))
            .OrderByDescending(r => EF.Property<string>(r, "SpaceKind"))
            .Select(r => new WorkspaceSpaceRoot(EF.Property<string>(r, "EID"), EF.Property<string>(r, "SpaceKind"),
                EF.Property<string>(r, "RootFolderId"), EF.Property<string>(r, "ENAME"))).ToArrayAsync(token);
    }

    /// <summary>已存在空间原样复用；同一主体的并发命令由事务级数据库锁及唯一索引共同保护。</summary>
    public async Task<WorkspaceSpaceRoot> EnsureAsync(string actorId, bool personal, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        return await transactions.ExecuteAsync<WorkspaceSpaceRoot>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            // 本人初始化的根归属由可信当前账号填写；接收转交使用同一创建流程但提供 recipient 归属。
            return await initialization.EnsureAsync(personal ? actorId : null,
                (root, stamping) => ownership.ApplyCreateAsync(root, ["OwnerId", "OwnerDepartmentId"], stamping), cancellation);
        }, token);
    }

    /// <summary>初始化回显明确使用内部来源；先在数据库判定空间准入及真实根配对。</summary>
    public async Task<Dictionary<string, object?>?> ReadRootAsync(string actorId, string spaceId, CancellationToken token = default)
    {
        var root = await ResolveRootAsync(actorId, spaceId, token);
        if (root is null) return null;
        var detail = await details.GetDetailFromSourceAsync(descriptor.Value, root.RootFolderId,
            new("workspace.space.root.read", "workspace.backend", typeof(WorkspaceSpaces), nameof(ReadRootAsync)), token);
        return detail is null ? null : new(detail.Fields);
    }

    /// <summary>无UI固定API保留原空间准入与根配对，只在原详情计划点消费该API来源。</summary>
    public async Task<Dictionary<string, object?>?> ReadRootFromApiAsync(string actorId, string spaceId,
        DataExecutionApiObjectContext source, CancellationToken token)
    {
        var root = await ResolveRootAsync(actorId, spaceId, token);
        if (root is null) return null;
        var detail = await details.GetDetailFromApiAsync(descriptor.Value, root.RootFolderId, source, token);
        return detail is null ? null : new(detail.Fields);
    }

    /// <summary>内部根定位保留真实EID与当前准入，不把非根对象作为空间根。</summary>
    public async Task<Dictionary<string, object?>?> ReadRootByIdAsync(string actorId, string rootId, CancellationToken token = default)
    {
        var spaceId = await Admitted(db, actorId).Where(r => EF.Property<string>(r, "RootFolderId") == rootId)
            .Select(r => EF.Property<string>(r, "EID")).SingleOrDefaultAsync(token);
        return spaceId is null ? null : await ReadRootAsync(actorId, spaceId, token);
    }

    /// <summary>固定根EID API沿原唯一根归属解析空间，不从外部来源选择内部执行。</summary>
    public async Task<Dictionary<string, object?>?> ReadRootByIdFromApiAsync(string actorId, string rootId,
        DataExecutionApiObjectContext source, CancellationToken token)
    {
        var spaceId = await Admitted(db, actorId).Where(r => EF.Property<string>(r, "RootFolderId") == rootId)
            .Select(r => EF.Property<string>(r, "EID")).SingleOrDefaultAsync(token);
        return spaceId is null ? null : await ReadRootFromApiAsync(actorId, spaceId, source, token);
    }

    // 两类调用共用原空间查询及配对SQL；来源选择不提前读取或省略业务准入。
    private async Task<WorkspaceSpaceRoot?> ResolveRootAsync(string actorId, string spaceId, CancellationToken token)
    {
        var space = await Admitted(db, actorId).SingleOrDefaultAsync(r => EF.Property<string>(r, "EID") == spaceId, token);
        return space is null ? null : await ValidateRootAsync(db, space, token);
    }

    /// <summary>写后回执明确使用内部来源；保留可读根路径范围及字段投影。</summary>
    internal async Task<Dictionary<string, object?>?> ReadFolderAsync(string actorId, string id, CancellationToken token)
    {
        if (!await folderScope.ContainsAsync(actorId, id, token)) return null;
        var detail = await details.GetDetailFromSourceAsync(descriptor.Value, id,
            new("workspace.folder.receipt.read", "workspace.backend", typeof(WorkspaceSpaces), nameof(ReadFolderAsync)), token);
        return detail is null ? null : new(detail.Fields);
    }

    /// <summary>真实Folder详情入口消费其view绑定，原来源路径范围仍先行校验。</summary>
    internal async Task<Dictionary<string, object?>?> ReadFolderFromEntryAsync(string actorId, string id, CancellationToken token)
    {
        if (!await folderScope.ContainsAsync(actorId, id, token)) return null;
        var detail = await details.GetDetailFromEntryAsync(descriptor.Value, id,
            [new("workspace.folder.read", "workspace.backend", "read")], token);
        return detail is null ? null : new(detail.Fields);
    }

    // 两个根是固定有界集合；范围在数据库先过滤，调用方不能提交主体或关闭该条件。
    internal static IQueryable<Dictionary<string, object?>> Admitted(EfPlatformDbContext db, string actorId) => db.Set<Dictionary<string, object?>>(SpaceModel).AsNoTracking().Where(r =>
        EF.Property<string>(r, "SpaceKind") == "Public" && EF.Property<string?>(r, "SubjectId") == null
        || EF.Property<string>(r, "SpaceKind") == "Personal" && EF.Property<string?>(r, "SubjectId") == actorId);

    // 无根或来源损坏是不可用状态，不能重建空间或回退全局目录。
    internal static async Task<WorkspaceSpaceRoot> ValidateRootAsync(EfPlatformDbContext db, Dictionary<string, object?> space, CancellationToken token)
    {
        var kind = (string)space["SpaceKind"]!;
        if (!(kind == "Public" && space["SubjectId"] is null)
            && !(kind == "Personal" && space["SubjectId"] is string { Length: > 0 }))
            throw new InvalidOperationException("工作空间主体配对无效。");
        var spaceId = (string)space["EID"]!;
        var rootId = (string)space["RootFolderId"]!;
        if (!await db.Set<Dictionary<string, object?>>(FolderModel).AsNoTracking().AnyAsync(r =>
            EF.Property<string>(r, "EID") == rootId && EF.Property<string>(r, "OriginSpaceId") == spaceId, token))
            throw new InvalidOperationException("工作空间根不可用。");
        return new(spaceId, (string)space["SpaceKind"]!, rootId, (string)space["ENAME"]!);
    }

    // 初始化不修改对象授权；所有可见根和详情都交集正式读取计划，依赖失败继续传播。
    private async Task<IQueryable<Dictionary<string, object?>>> ReadableFoldersAsync(CancellationToken token)
    {
        var plan = await executionAccess().GetApiQueryReadPlanAsync(
            new(new("workspace.spaces.list.api", "workspace.backend", "read"), "workspace.spaces.list.api", "query"), "workspace.folder", token);
        var folders = db.Set<Dictionary<string, object?>>(FolderModel).AsNoTracking();
        return plan.Predicate is null ? folders : folders.Where(plan.Predicate);
    }
}

using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>仅公开导航所需的空间与根身份，不暴露主体或文件夹来源。</summary>
public sealed record WorkspaceSpaceRoot(string SpaceId, string SpaceKind, string RootFolderId, string Name, bool HasChildren = false);

/// <summary>空间初始化与强制准入；普通内容权限不由初始化命令授予。</summary>
public sealed class WorkspaceSpaces(EfPlatformDbContext db, IPlatformTransactionExecutor transactions, IDataScopeReadPlanProvider readPlans,
    IStandardCrudService details, WorkspaceFolderReadDescriptor descriptor, WorkspaceFolderScope folderScope,
    ICP.Platform.Crud.Ownership.EntityOwnershipValues ownership, ICP.Platform.Crud.Ownership.EntityOwnershipAudit ownershipAudit,
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
        var kind = personal ? "Personal" : "Public";
        return await transactions.ExecuteAsync<WorkspaceSpaceRoot>(async cancellation =>
        {
            using var operation = audit.BeginOperation();
            // SQL Server 的事务锁跨 Host 生效；按账号分锁，不把多标签页竞争退化为先查后插。
            var resource = "Workspace.Space." + (personal ? actorId : "Public");
            await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result = sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @result < 0 THROW 51000, 'Workspace initialization lock failed.', 1;",
                [new SqlParameter("@resource", resource)], cancellation);
            var existing = SpaceRows().Where(r => EF.Property<string>(r, "SpaceKind") == kind);
            if (personal) existing = existing.Where(r => EF.Property<string?>(r, "SubjectId") == actorId);
            var current = await existing.SingleOrDefaultAsync(cancellation);
            if (current is not null) return await ValidateRootAsync(db, current, cancellation);
            var spaceId = Guid.NewGuid().ToString("N");
            var rootId = Guid.NewGuid().ToString("N");
            var name = personal ? "个人空间" : "公共空间";
            var root = new Dictionary<string, object?>
            {
                ["EID"] = rootId, ["ENAME"] = name, ["OriginSpaceId"] = spaceId, ["Description"] = null
            };
            await ownership.ApplyCreateAsync(root, ["OwnerId", "OwnerDepartmentId"], cancellation);
            db.Set<Dictionary<string, object?>>(FolderModel).Add(root);
            await ownershipAudit.RegisterAsync("workspace.folder", rootId, root, false, cancellation);
            // RootFolderId 非空 FK；先保存预分配来源的根，再保存空间，两次保存仍属于同一平台事务。
            await transactions.FlushAsync(cancellation);
            db.Set<Dictionary<string, object?>>(SpaceModel).Add(new()
            {
                ["EID"] = spaceId, ["ENAME"] = name, ["SpaceKind"] = kind,
                ["SubjectId"] = personal ? actorId : null,
                ["RootFolderId"] = rootId
            });
            return new(spaceId, kind, rootId, name);
        }, token);
    }

    /// <summary>先在数据库判定空间准入，再读取其唯一根；管理员也不能绕过个人主体比较。</summary>
    public async Task<Dictionary<string, object?>?> ReadRootAsync(string actorId, string spaceId, CancellationToken token = default)
    {
        var space = await Admitted(db, actorId).SingleOrDefaultAsync(r => EF.Property<string>(r, "EID") == spaceId, token);
        if (space is null) return null;
        var root = await ValidateRootAsync(db, space, token);
        var detail = await details.GetDetailAsync(descriptor.Value, root.RootFolderId, token);
        return detail is null ? null : new(detail.Fields);
    }

    /// <summary>共享详情按真实根 EID 读取；唯一根归属与当前准入条件都在服务端解析，非根不能借此读取。</summary>
    public async Task<Dictionary<string, object?>?> ReadRootByIdAsync(string actorId, string rootId, CancellationToken token = default)
    {
        var spaceId = await Admitted(db, actorId).Where(r => EF.Property<string>(r, "RootFolderId") == rootId)
            .Select(r => EF.Property<string>(r, "EID")).SingleOrDefaultAsync(token);
        return spaceId is null ? null : await ReadRootAsync(actorId, spaceId, token);
    }

    /// <summary>独立详情交集对象读取与来源或可读根路径范围，不公开私人来源。</summary>
    internal async Task<Dictionary<string, object?>?> ReadFolderAsync(string actorId, string id, CancellationToken token)
    {
        if (!await folderScope.ContainsAsync(actorId, id, token)) return null;
        var detail = await details.GetDetailAsync(descriptor.Value, id, token);
        return detail is null ? null : new(detail.Fields);
    }

    // 两个根是固定有界集合；范围在数据库先过滤，调用方不能提交主体或关闭该条件。
    internal static IQueryable<Dictionary<string, object?>> Admitted(EfPlatformDbContext db, string actorId) => db.Set<Dictionary<string, object?>>(SpaceModel).AsNoTracking().Where(r =>
        EF.Property<string>(r, "SpaceKind") == "Public" && EF.Property<string?>(r, "SubjectId") == null
        || EF.Property<string>(r, "SpaceKind") == "Personal" && EF.Property<string?>(r, "SubjectId") == actorId);

    // Workspace 只读自己拥有的动态模型，不绑定另一个模块的 EF 实现。
    private IQueryable<Dictionary<string, object?>> SpaceRows() => db.Set<Dictionary<string, object?>>(SpaceModel).AsNoTracking();

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
        var plan = await readPlans.GetReadPlanAsync("workspace.folder", token);
        var folders = db.Set<Dictionary<string, object?>>(FolderModel).AsNoTracking();
        return plan.Predicate is null ? folders : folders.Where(plan.Predicate);
    }
}

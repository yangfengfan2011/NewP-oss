using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>空间与唯一根的创建知识只在此处维护；准入和根的归属事实由调用用例提供。</summary>
public sealed class WorkspaceSpaceInitialization(EfPlatformDbContext db, IPlatformTransactionExecutor transactions, IPlatformObjectIdGenerator ids,
    ICP.Platform.Crud.Ownership.EntityOwnershipAudit ownershipAudit)
{
    private const string SpaceModel = "WorkspaceSpace";

    // 已存在空间原样复用并校验根；新建时根先于空间保存，全部写入参与调用方已开启的平台事务。
    // 存储名称是不随请求语言变化的固定值，界面显示由 SpaceKind 对应的 LabelKey 本地化。
    internal async Task<WorkspaceSpaceRoot> EnsureAsync(string? subjectId,
        Func<Dictionary<string, object?>, CancellationToken, ValueTask> stampRoot, CancellationToken token)
    {
        if (!transactions.IsActive) throw new InvalidOperationException("Workspace space initialization requires an active transaction.");
        var personal = subjectId is not null;
        var kind = personal ? "Personal" : "Public";
        // SQL Server 的事务锁跨 Host 生效；按账号分锁，本人初始化与转交接收对同一账号互斥。
        await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result = sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @result < 0 THROW 51000, 'Workspace initialization lock failed.', 1;",
            [new SqlParameter("@resource", "Workspace.Space." + (subjectId ?? "Public"))], token);
        var spaces = db.Set<Dictionary<string, object?>>(SpaceModel);
        var current = await spaces.AsNoTracking().SingleOrDefaultAsync(row => EF.Property<string>(row, "SpaceKind") == kind
            && EF.Property<string?>(row, "SubjectId") == subjectId, token);
        if (current is not null) return await WorkspaceSpaces.ValidateRootAsync(db, current, token);
        var spaceId = ids.NewId(SpaceModel);
        var rootId = ids.NewId(WorkspacePositions.FolderModel);
        var name = personal ? "个人空间" : "公共空间";
        var root = new Dictionary<string, object?> { ["EID"] = rootId, ["ENAME"] = name, ["OriginSpaceId"] = spaceId, ["Description"] = null };
        await stampRoot(root, token);
        db.Set<Dictionary<string, object?>>(WorkspacePositions.FolderModel).Add(root);
        await ownershipAudit.RegisterAsync("workspace.folder", rootId, root, false, token);
        // RootFolderId 非空 FK；先保存预分配来源的根，再保存空间，后续同事务写入可直接引用二者。
        await transactions.FlushAsync(token);
        spaces.Add(new() { ["EID"] = spaceId, ["ENAME"] = name, ["SpaceKind"] = kind, ["SubjectId"] = subjectId, ["RootFolderId"] = rootId });
        await transactions.FlushAsync(token);
        return new(spaceId, kind, rootId, name);
    }
}

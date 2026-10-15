using System.Data.Common;
using System.Security.Claims;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Workspace;

/// <summary>只提供工作空间根及当前主体强制准入，查询与逐层读取由平台 Link 树拥有。</summary>
public sealed class WorkspaceTreeRoots(EfPlatformDbContext db, IHttpContextAccessor context) : ILinkTreeRootPolicy
{
    public string Key => "workspace.spaces";
    public string ScopeKind => "currentUser";

    /// <inheritdoc/>
    public Task<LinkTreeRootSql> BuildAsync(LinkTreeDefinition definition, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var actor = context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new LinkQueryException(403, "Workspace.SubjectRequired");
        var parameters = new List<DbParameter>();
        var folders = db.Set<Dictionary<string, object?>>(WorkspacePositions.FolderModel).AsNoTracking();
        var roots = WorkspaceSpaces.Admitted(db, actor);
        var invalid = WorkspaceFolderScope.QuerySql(roots.Where(space => !folders.Any(folder =>
            EF.Property<string>(folder, "EID") == EF.Property<string>(space, "RootFolderId") && EF.Property<string>(folder, "OriginSpaceId") == EF.Property<string>(space, "EID")))
            .Select(space => EF.Property<string>(space, "EID")), "treeRootIntegrity", parameters);
        var query = WorkspaceFolderScope.QuerySql(roots.Select(space => new
        {
            RootKey = EF.Property<string>(space, "EID"), RootObjectId = EF.Property<string>(space, "RootFolderId"), Label = EF.Property<string>(space, "ENAME"),
            LabelKey = EF.Property<string>(space, "SpaceKind") == "Public" ? "Workspace.Public" : "Workspace.Personal",
            RootOrder = EF.Property<string>(space, "SpaceKind") == "Public" ? 0L : 1L
        }), "treeRootPolicy", parameters);
        return Task.FromResult(new LinkTreeRootSql($"IF EXISTS({invalid}) THROW 51001,'Platform.TreeGraphInvalid',1;", query, parameters));
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, object?> CreateContext(string rootKey, string rootObjectId) =>
        new Dictionary<string, object?> { ["spaceId"] = rootKey, ["rootFolderId"] = rootObjectId };
}

using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Models;
using ICP.Platform.Sdk.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ICP.Platform.Modules.Workspace;

/// <summary>正式创建仅接受当前出现位置与可写字段；所有业务规则仍在应用服务。</summary>
[ApiController, Route("api/workspace"), EndpointModule("Workspace"), PlatformRequestValidation]
public sealed class WorkspaceContentsController(WorkspaceContents contents, WorkspaceSpaces spaces, WorkspacePositions positions,
    LinkQueryReader links, ILogger<WorkspaceContentsController> logger, WorkspaceFolderScope folderScope) : ControllerBase
{
    public sealed record CreateFolderInput(WorkspacePosition Position, Dictionary<string, JsonElement> Values);
    public sealed record AddReferenceInput(WorkspacePosition Position, LinkObjectReference Target);
    public sealed record RemoveReferenceInput(WorkspacePosition Position);

    /// <summary>添加只提交引用；回执不绑定提交后的读取资格，不会因刷新故障重放命令。</summary>
    [HttpPost("content/references"), RequireFeature("workspace.folder.add")]
    public async Task<IResult> AddReference(AddReferenceInput input, CancellationToken token)
    {
        try
        {
            if (input.Position is null || input.Target is null) throw new LinkQueryException(400, "Workspace.InvalidReferenceInput");
            await contents.AddFolderAsync(Actor(), input.Position, input.Target, token);
            return Results.Ok(new { succeeded = true });
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }

    /// <summary>移除所选关系始终带当前完整位置，裸父或对象 EID 不能替代 LID。</summary>
    [HttpDelete("content/references/{linkId}"), RequireFeature("workspace.folder.remove")]
    public async Task<IResult> RemoveReference(string linkId, RemoveReferenceInput input, CancellationToken token)
    {
        try
        {
            if (input.Position is null) throw new LinkQueryException(400, "Workspace.InvalidReferenceInput");
            await contents.RemoveAsync(Actor(), input.Position, linkId, token);
            return Results.Ok(new { succeeded = true });
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }

    /// <summary>正式单选候选只返回安全字段，不能请求来源、私人路径或任意排序表达式。</summary>
    [HttpGet("folders/candidates"), RequireFeature("workspace.folder.read")]
    public async Task<IResult> Candidates([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null, CancellationToken token = default)
    {
        try
        {
            if (Request.Query.Any(pair => pair.Key is not ("pageIndex" or "pageSize" or "keyword") || pair.Value.Count != 1))
                throw new LinkQueryException(400, "Workspace.InvalidCandidateQuery");
            return Results.Ok(await folderScope.CandidatesAsync(Actor(), pageIndex, pageSize, keyword, token));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }

    /// <summary>创建已提交后展示失败只返回成功回执，不泄露结果、不重放创建。</summary>
    [HttpPost("content/folders"), RequireFeature("workspace.folder.create")]
    public async Task<IResult> CreateFolder(CreateFolderInput input, CancellationToken token)
    {
        string id;
        try
        {
            if (input.Position is null || input.Values is null) throw new LinkQueryException(400, "Workspace.InvalidCreateInput");
            id = await contents.CreateFolderAsync(Actor(), input.Position, input.Values, token);
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        try
        {
            var fields = await spaces.ReadFolderAsync(Actor(), id, token);
            return fields is null ? Results.Ok(new { succeeded = true }) : Results.Ok(new { id, fields });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("WORKSPACE_POST_WRITE_READ_UNAVAILABLE: {FailureType}", exception.GetType().Name);
            return Results.Ok(new { succeeded = true });
        }
    }

    /// <summary>共享详情按真实 Folder EID 读取，不接受 LID 代替对象身份。</summary>
    [HttpGet("folders/{id}"), RequireFeature("workspace.folder.read")]
    public async Task<IResult> Folder(string id, CancellationToken token)
    {
        if (Request.Query.Count > 0) return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidDetailQuery"));
        var fields = await spaces.ReadFolderAsync(Actor(), id, token);
        return fields is null ? Results.NotFound() : Results.Ok(new { id, fields });
    }

    /// <summary>受信任树入口完整读取当前层，只允许文件夹类型；右侧筛选不能改变树计划。</summary>
    [HttpGet("content/children"), RequireFeature("tree:workspace.structure")]
    public async Task<IResult> Children([FromQuery] string spaceId, [FromQuery] string rootFolderId, [FromQuery] string linkIds, CancellationToken token)
    {
        try
        {
            var scope = new Dictionary<string, string?> { ["spaceId"] = spaceId, ["rootFolderId"] = rootFolderId, ["linkIds"] = linkIds };
            if (Request.Query.Any(q => !scope.ContainsKey(q.Key) || q.Value.Count != 1)) throw new LinkQueryException(400, "Workspace.InvalidPosition");
            var path = await positions.ResolveAsync(Actor(), WorkspacePositions.FromScope(scope), token);
            return Results.Ok(await links.ReadDirectLayerAsync("workspace.content", new("forward", new(WorkspacePositions.FolderModel, path.FolderId),
                new StandardCrudListQuery(Fields: ["LID", "Name"], SortBy: "Name", SortDirection: "asc", QueryScope: scope), [WorkspacePositions.FolderModel]), token));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }

    /// <summary>已知位置恢复返回同一条经完整授权的链，不寻找替代根或父路径。</summary>
    [HttpGet("content/path"), RequireFeature("tree:workspace.structure")]
    public async Task<IResult> Path([FromQuery] string spaceId, [FromQuery] string rootFolderId, [FromQuery] string linkIds, CancellationToken token)
    {
        try
        {
            var scope = new Dictionary<string, string?> { ["spaceId"] = spaceId, ["rootFolderId"] = rootFolderId, ["linkIds"] = linkIds };
            if (Request.Query.Any(q => !scope.ContainsKey(q.Key) || q.Value.Count != 1)) throw new LinkQueryException(400, "Workspace.InvalidPosition");
            return Results.Ok(await positions.ResolveAsync(Actor(), WorkspacePositions.FromScope(scope), token));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }

    // 只从已认证主体取得账号，客户端位置不携带所有者。
    private string Actor() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new LinkQueryException(403, "Workspace.SubjectRequired");
}

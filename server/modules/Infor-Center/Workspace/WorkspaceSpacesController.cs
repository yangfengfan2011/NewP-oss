using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Sdk.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;

namespace ICP.Platform.Modules.Workspace;

[ApiController]
[Route("api/workspace/spaces")]
[EndpointModule("Workspace")]
[PlatformRequestValidation]
public sealed class WorkspaceSpacesController(WorkspaceSpaces spaces, ILogger<WorkspaceSpacesController> logger,
    ILocalAdministratorRoleAssignment administrators, ICP.Platform.Crud.LinkQueries.LinkQueryReader links, IDataScopeEvaluationScope? evaluation = null) : ControllerBase
{
    /// <summary>显式接入命令初始化公共空间；入口资格不自动授予初始化权。</summary>
    [HttpPost("public/initialize")]
    [RequireFeature("workspace.spaces.access")]
    public async Task<ActionResult<WorkspaceSpaceRoot>> InitializePublic(CancellationToken token)
    {
        if (await administrators.InspectAsync(ActorId(), token) != LocalAdministratorAssignmentState.Assigned) return Forbid();
        return await InitializationResultAsync(await spaces.EnsureAsync(ActorId(), false, token), token);
    }

    /// <summary>个人初始化只消费认证主体，不接受客户端所有者或来源字段。</summary>
    [HttpPost("personal/ensure")]
    [RequireFeature("workspace.spaces.access")]
    public async Task<ActionResult<WorkspaceSpaceRoot>> EnsurePersonal(CancellationToken token,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] JsonElement? request = null)
    {
        if (request is not null || Request.Query.Count > 0)
        {
            var problem = new ProblemDetails { Status = 400, Title = "个人空间命令不接受客户端主体。" };
            problem.Extensions["code"] = "Workspace.Personal.SubjectInputForbidden";
            problem.Extensions["reasonKey"] = "Workspace.SubjectInputForbidden";
            return BadRequest(problem);
        }
        return await InitializationResultAsync(await spaces.EnsureAsync(ActorId(), true, token), token);
    }

    /// <summary>固定外部API读取当前可准入根，不隐式创建空间或借用view声明。</summary>
    [HttpGet]
    [RequireFeature("workspace.spaces.access")]
    public async Task<ActionResult<IReadOnlyList<WorkspaceSpaceRoot>>> List(CancellationToken token)
    {
        try
        {
            var roots = await spaces.ListAsync(ActorId(), token);
            var result = new List<WorkspaceSpaceRoot>();
            // 空间根最多两个；存在性由通用Link规划判定，不把第一页或关系数量当作树分支。
            foreach (var root in roots)
            {
                var scope = new Dictionary<string, string?> { ["spaceId"] = root.SpaceId, ["rootFolderId"] = root.RootFolderId, ["linkIds"] = "[]" };
                result.Add(root with { HasChildren = await links.HasChildrenAsync("workspace.content", new("forward", new(WorkspacePositions.FolderModel, root.RootFolderId),
                    new ICP.Platform.Crud.Models.StandardCrudListQuery(QueryScope: scope), [WorkspacePositions.FolderModel]), token) });
            }
            return Ok(result);
        }
        catch (ActionDataAccessException exception) { return ApiSourceFailure(exception); }
    }

    /// <summary>固定外部API读取当前空间真实根，不借view来源或接受裸文件夹ID。</summary>
    [HttpGet("{spaceId}/root")]
    [RequireFeature("workspace.spaces.access")]
    public async Task<IActionResult> Root(string spaceId, CancellationToken token)
    {
        if (Request.Query.Count > 0) return InvalidDetailQuery();
        try
        {
            var fields = await spaces.ReadRootFromApiAsync(ActorId(), spaceId,
                new(new("workspace.space.root.api", "workspace.backend", "read"), "workspace.space.root.api", "target"), token);
            return fields is null ? NotFound() : Ok(new { id = fields["EID"], fields });
        }
        catch (ActionDataAccessException exception) { return ApiSourceFailure(exception); }
    }

    /// <summary>固定外部API按真实根EID读取；保留空间归属和当前准入。</summary>
    [HttpGet("roots/{rootFolderId}")]
    [RequireFeature("workspace.spaces.access")]
    public async Task<IActionResult> RootById(string rootFolderId, CancellationToken token)
    {
        if (Request.Query.Count > 0) return InvalidDetailQuery();
        try
        {
            var fields = await spaces.ReadRootByIdFromApiAsync(ActorId(), rootFolderId,
                new(new("workspace.space.rootById.api", "workspace.backend", "read"), "workspace.space.rootById.api", "target"), token);
            return fields is null ? NotFound() : Ok(new { id = rootFolderId, fields });
        }
        catch (ActionDataAccessException exception) { return ApiSourceFailure(exception); }
    }

    // 只翻译固定API来源的稳定拒绝；不切换到entry或内部执行。
    private ObjectResult ApiSourceFailure(ActionDataAccessException exception) => Problem(statusCode: exception.Status, title: exception.Code,
        extensions: new Dictionary<string, object?> { ["code"] = exception.Code });

    // 认证流水线的稳定账号 EID 是唯一个人主体来源；缺失时安全拒绝。
    private string ActorId() => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("当前账号不可用。");

    // 固定详情投影不接受字段选择，内部来源不能借 query 参数扩展公开字段目录。
    private ObjectResult InvalidDetailQuery() => Problem(statusCode: 400, title: "Workspace.InvalidDetailQuery",
        extensions: new Dictionary<string, object?> { ["code"] = "Workspace.InvalidDetailQuery" });

    // 命令已经提交后，读取拒绝或展示故障不改变成功事实，也不泄露不可读根或引导重放。
    private async Task<ActionResult<WorkspaceSpaceRoot>> InitializationResultAsync(WorkspaceSpaceRoot root, CancellationToken token)
    {
        try
        {
            using var observation = evaluation?.BeginReadAfterWrite();
            if (await spaces.ReadRootAsync(ActorId(), root.SpaceId, token) is not null) return Ok(root);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "工作空间初始化已提交，结果读取不可用。");
        }
        return Ok(new { initialized = true });
    }
}

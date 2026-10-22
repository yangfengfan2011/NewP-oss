using System.Security.Claims;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Sdk.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Workspace;

[ApiController, Route("api/workspace/transfers/notifications"), EndpointModule("Workspace")]
public sealed class WorkspaceTransferNotificationsController(WorkspaceTransferNotifications notifications) : ControllerBase
{
    /// <summary>接收通知入口读取当前接收人安全通知，不接收客户端 recipient 身份。</summary>
    [HttpGet, RequireFeature("workspace.transfers.read")]
    public async Task<IResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken token = default)
    {
        try { return Results.Ok(await notifications.ListAsync(Actor(), page, pageSize, token)); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }

    // 只消费认证会话的账号身份，不允许 query/body 冒充接收人。
    private string Actor() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
}

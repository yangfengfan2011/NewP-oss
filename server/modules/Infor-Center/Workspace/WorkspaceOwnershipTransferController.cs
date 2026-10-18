using System.Text.Json;
using System.Text.Json.Serialization;
using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Ownership;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Workspace;

public sealed partial class WorkspaceContentsController
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record OwnershipTransferInput(WorkspacePosition? Position, string[]? LinkIds, string? RecipientId);

    /// <summary>完整父位置及显式选择共同确定本次准备范围，不把页面缓存视为提交许可。</summary>
    [HttpGet("ownership/transfer/eligibility"), RequireFeature("workspace.folder.transfer")]
    public async Task<IResult> TransferEligibility([FromQuery] string? queryScope, [FromQuery] string? selectedIds,
        [FromServices] EntityOwnershipTransfer transfer, CancellationToken token)
    {
        try
        {
            var selection = ReadTransferSelection(queryScope, selectedIds, false);
            await contents.CheckOwnershipTransferAsync(Actor(), selection.Position, selection.LinkIds, transfer, token);
            return Results.Ok(new { allowed = true });
        }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidSelection")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }

    /// <summary>候选只是当前可引用账号片段；本源整批资格不满足时不返回账号或组织事实。</summary>
    [HttpGet("ownership/transfer/accounts"), RequireFeature("workspace.folder.transfer")]
    public async Task<IResult> TransferAccounts([FromQuery] string? queryScope, [FromQuery] string? selectedIds, [FromQuery] string? query,
        [FromServices] EntityOwnershipTransfer transfer, [FromServices] IAccountOwnershipDirectory directory, CancellationToken token)
    {
        try
        {
            var selection = ReadTransferSelection(queryScope, selectedIds, true);
            await contents.CheckOwnershipTransferAsync(Actor(), selection.Position, selection.LinkIds, transfer, token);
            return Results.Ok(await directory.SearchAsync(query, token));
        }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidSelection")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }

    // 只允许完整父位置、JSON选择数组及可选搜索字串；拒绝重复参数或裸EID降级。
    private (WorkspacePosition Position, string[] LinkIds) ReadTransferSelection(string? queryScope, string? selectedIds, bool search)
    {
        if (queryScope is null || selectedIds is null || Request.Query.Any(pair => pair.Value.Count != 1
            || pair.Key != "queryScope" && pair.Key != "selectedIds" && !(search && pair.Key == "query")))
            throw new LinkQueryException(400, "Workspace.InvalidSelection");
        var ids = JsonSerializer.Deserialize<string[]>(selectedIds);
        if (ids is null || ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace)) throw new LinkQueryException(400, "Workspace.InvalidSelection");
        return (ParseOwnershipPosition(queryScope), ids);
    }

    /// <summary>真实来源与规范对象转交一次提交；不接受客户端所属部门、操作者或接收者资格断言。</summary>
    [HttpPost("ownership/transfer"), RequireFeature("workspace.folder.transfer")]
    public async Task<IResult> TransferOwnership(OwnershipTransferInput input, [FromServices] EntityOwnershipTransfer transfer, CancellationToken token)
    {
        try
        {
            if (input.Position is null || input.LinkIds is null || string.IsNullOrWhiteSpace(input.RecipientId))
                throw new LinkQueryException(400, "EntityOwnership.InvalidTransfer");
            return Results.Ok(await contents.TransferOwnershipAsync(Actor(), input.Position, input.LinkIds, input.RecipientId, transfer, token));
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using ICP.Platform.Authorization.Contracts;
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
    [HttpGet("ownership/transfer/eligibility"), RequireFeature("workspace.folder.transfer"), ButtonBusinessHandler(typeof(WorkspaceOwnershipTransferButtonHandler))]
    public async Task<IResult> TransferEligibility([FromQuery] string? queryScope, [FromQuery] string? selectedIds,
        [FromServices] IButtonBusinessExecutor buttons, [FromServices] WorkspaceOwnershipTransferButtonHandler handler, CancellationToken token)
    {
        try
        {
            var selection = ReadTransferSelection(queryScope, selectedIds, false);
            return await buttons.ExecuteAsync(handler, new(Actor(), selection.Position, selection.LinkIds, "eligibility"), token);
        }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidSelection")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (EntityOwnershipException exception) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(exception.StatusCode, exception.Code)); }
        catch (ActionDataAccessException exception) { return OwnershipDeclarationFailure(exception); }
    }

    /// <summary>候选只是当前可引用账号片段；本源整批资格不满足时不返回账号或组织事实。</summary>
    [HttpGet("ownership/transfer/accounts"), RequireFeature("workspace.folder.transfer"), ButtonBusinessHandler(typeof(WorkspaceOwnershipTransferButtonHandler))]
    public async Task<IResult> TransferAccounts([FromQuery] string? queryScope, [FromQuery] string? selectedIds, [FromQuery] string? query,
        [FromServices] IButtonBusinessExecutor buttons, [FromServices] WorkspaceOwnershipTransferButtonHandler handler, CancellationToken token)
    {
        try
        {
            var selection = ReadTransferSelection(queryScope, selectedIds, true);
            return await buttons.ExecuteAsync(handler, new(Actor(), selection.Position, selection.LinkIds, "accounts", Query: query), token);
        }
        catch (JsonException) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(400, "Workspace.InvalidSelection")); }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (EntityOwnershipException exception) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(exception.StatusCode, exception.Code)); }
        catch (ActionDataAccessException exception) { return OwnershipDeclarationFailure(exception); }
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
    [HttpPost("ownership/transfer"), RequireFeature("workspace.folder.transfer"), ButtonBusinessHandler(typeof(WorkspaceOwnershipTransferButtonHandler))]
    public async Task<IResult> TransferOwnership(OwnershipTransferInput input, [FromServices] IButtonBusinessExecutor buttons, [FromServices] WorkspaceOwnershipTransferButtonHandler handler, CancellationToken token)
    {
        try
        {
            if (input.Position is null || input.LinkIds is null || string.IsNullOrWhiteSpace(input.RecipientId))
                throw new LinkQueryException(400, "EntityOwnership.InvalidTransfer");
            return await buttons.ExecuteAsync(handler, new(Actor(), input.Position, input.LinkIds, "transfer", input.RecipientId), token);
        }
        catch (LinkQueryException exception) { return LinkQueryEndpointExtensions.Problem(exception); }
        catch (EntityOwnershipException exception) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(exception.StatusCode, exception.Code)); }
        catch (ActionDataAccessException exception) { return OwnershipDeclarationFailure(exception); }
    }
}

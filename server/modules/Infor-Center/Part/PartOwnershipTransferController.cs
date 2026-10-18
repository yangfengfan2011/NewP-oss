using System.Text.Json.Serialization;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Ownership;
using ICP.Platform.Persistence.Ownership;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Part;

/// <summary>独立对象的正式转交入口；缺接收模块时仅此操作明确不可用，不影响原模块启动。</summary>
[ApiController, Route("api/part/parts/{objectId}/transfer"), EndpointModule("Part")]
public sealed class PartOwnershipTransferController(EntityOwnershipTransfer transfer) : ControllerBase
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PartOwnershipTransferInput(string? RecipientId);

    /// <summary>普通所属人的准备查询不能由管理员身份替代，不返回对象私有字段。</summary>
    [HttpGet("eligibility"), RequireFeature("part.part.transfer")]
    public async Task<IResult> Eligibility(string objectId, CancellationToken token)
    {
        try { await transfer.CheckAsync([new("part.part", objectId)], token); return Results.Ok(new { allowed = true }); }
        catch (EntityOwnershipException exception) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(exception.StatusCode, exception.Code)); }
    }

    /// <summary>只有当前可转交此对象的所属人可搜索账号片段，停用及空部门不成为额外资格。</summary>
    [HttpGet("accounts"), RequireFeature("part.part.transfer")]
    public async Task<IResult> Accounts(string objectId, [FromQuery] string? query, [FromServices] IAccountOwnershipDirectory directory, CancellationToken token)
    {
        try { await transfer.CheckAsync([new("part.part", objectId)], token); return Results.Ok(await directory.SearchAsync(query, token)); }
        catch (EntityOwnershipException exception) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(exception.StatusCode, exception.Code)); }
    }

    /// <summary>服务端从当前对象核所属人及 edit，只消费接收账号，不接受客户端部门或 ACL。</summary>
    [HttpPost, RequireFeature("part.part.transfer")]
    public async Task<IResult> Transfer(string objectId, PartOwnershipTransferInput input, CancellationToken token)
    {
        try { return Results.Ok(await transfer.TransferAsync([new("part.part", objectId)], input.RecipientId ?? "", token)); }
        catch (EntityOwnershipException exception) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(exception.StatusCode, exception.Code)); }
    }
}

using System.Text.Json.Serialization;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Ownership;
using ICP.Platform.Persistence.Ownership;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Document;

/// <summary>独立对象的正式转交入口；缺接收模块时仅此操作明确不可用，不影响原模块启动。</summary>
[ApiController, Route("api/document/documents/{objectId}/transfer"), EndpointModule("Document")]
public sealed class DocumentOwnershipTransferController(EntityOwnershipTransfer transfer) : ControllerBase
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record DocumentOwnershipTransferInput(string? RecipientId);

    /// <summary>普通所属人的准备查询不能由管理员身份替代，不返回对象私有字段。</summary>
    [HttpGet("eligibility"), RequireFeature("document.document.transfer")]
    public async Task<IResult> Eligibility(string objectId, CancellationToken token)
    {
        try { await transfer.CheckAsync([new("document.document", objectId)], Source("prepare"), token); return Results.Ok(new { allowed = true }); }
        catch (EntityOwnershipException exception) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(exception.StatusCode, exception.Code)); }
        catch (ActionDataAccessException exception) { return DeclarationProblem(exception); }
    }

    /// <summary>只有当前可转交此对象的所属人可搜索账号片段，停用及空部门不成为额外资格。</summary>
    [HttpGet("accounts"), RequireFeature("document.document.transfer")]
    public async Task<IResult> Accounts(string objectId, [FromQuery] string? query, [FromServices] IAccountOwnershipDirectory directory, CancellationToken token)
    {
        try { await transfer.CheckAsync([new("document.document", objectId)], Source("prepare"), token); return Results.Ok(await directory.SearchAsync(query, token)); }
        catch (EntityOwnershipException exception) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(exception.StatusCode, exception.Code)); }
        catch (ActionDataAccessException exception) { return DeclarationProblem(exception); }
    }

    /// <summary>服务端从当前对象核所属人及 edit，只消费接收账号，不接受客户端部门或 ACL。</summary>
    [HttpPost, RequireFeature("document.document.transfer")]
    public async Task<IResult> Transfer(string objectId, DocumentOwnershipTransferInput input, CancellationToken token)
    {
        try { return Results.Ok(await transfer.TransferAsync([new("document.document", objectId)], input.RecipientId ?? "", Source("submit"), token)); }
        catch (EntityOwnershipException exception) { return LinkQueryEndpointExtensions.Problem(new LinkQueryException(exception.StatusCode, exception.Code)); }
        catch (ActionDataAccessException exception) { return DeclarationProblem(exception); }
    }

    // 只翻译正式来源错误，原持久化/取消异常不吞掉。
    private static IResult DeclarationProblem(ActionDataAccessException exception) => Results.Problem(statusCode: exception.Status, title: exception.Code,
        extensions: new Dictionary<string, object?> { ["code"] = exception.Code });

    // 来源属于当前真实Document入口；不是客户端可写的provider或角色参数。
    private static DataExecutionBindingReference Source(string stage) => new("document.document.transfer", "entityOwnership.transfer", stage);
}

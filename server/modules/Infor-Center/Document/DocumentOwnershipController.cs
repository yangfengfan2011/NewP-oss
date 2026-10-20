using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Ownership;
using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Sdk.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Document;

/// <summary>无目录引用的旧对象也由真实owner提供受控归属补齐，不开放普通编辑代转。</summary>
[ApiController, Route("api/document/documents/ownership"), EndpointModule("Document"), Authorize(Policy = "AdminOnly")]
public sealed class DocumentOwnershipController(EntityOwnershipMaintenance maintenance, IStandardCrudResourceRegistry resources,
    IAccountOwnershipDirectory accounts, IButtonBusinessExecutor buttons, DocumentOwnershipInitializationButtonHandler handler) : ControllerBase
{
    public sealed record InitializeInput(string? AccountId = null);

    public sealed record InitializeBatchInput(string[]? Ids);

    /// <summary>明确选择的旧对象同事务自动补齐；未知账号留待人工，不扩展为全库任务或代转。</summary>
    [HttpPost("initialize"), RequireFeature("document.document.initializeOwnership"), ButtonBusinessHandler(typeof(DocumentOwnershipInitializationButtonHandler))]
    public async Task<IResult> InitializeBatch(InitializeBatchInput input, CancellationToken token)
    {
        try { return await buttons.ExecuteAsync(handler, new(input.Ids ?? [], "batch"), token); }
        catch (EntityOwnershipException exception) { return Failure(exception); }
        catch (ActionDataAccessException exception) { return DeclarationFailure(exception); }
    }
    /// <summary>受控维护资格只向管理员成立，不由普通功能授权提升身份。</summary>
    [HttpGet("eligibility"), RequireFeature("document.document.initializeOwnership")]
    public IResult Eligibility() => Results.Ok(new { allowed = true });
    /// <summary>只列出当前可读的缺失对象，SQL筛选与稳定分页先于响应。</summary>
    [HttpGet("pending"), RequireFeature("document.document.initializeOwnership")]
    public async Task<IResult> Pending([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? query = null, CancellationToken token = default)
    {
        try { return Results.Ok(await maintenance.PendingAsync(resources.FindByResourceKey("document.document")!, page, pageSize, query, Source("read"), token)); }
        catch (EntityOwnershipException exception) { return Failure(exception); }
        catch (ActionDataAccessException exception) { return DeclarationFailure(exception); }
    }

    /// <summary>人工补齐仅查询最小账号候选，人员档案不因本入口扩大读取权限。</summary>
    [HttpGet("accounts"), RequireFeature("document.document.initializeOwnership")]
    public async Task<IResult> Accounts([FromQuery] string? query, CancellationToken token) => Results.Ok(await accounts.SearchAsync(query, token));

    /// <summary>对象状态核对沿真实read/edit，不信任客户端声明的归属或授权。</summary>
    [HttpGet("{id}/status"), RequireFeature("document.document.initializeOwnership"), ButtonBusinessHandler(typeof(DocumentOwnershipInitializationButtonHandler))]
    public async Task<IResult> Status(string id, CancellationToken token)
    {
        try { return await buttons.ExecuteAsync(handler, new([id], "status"), token); }
        catch (EntityOwnershipException exception) { return Failure(exception); }
        catch (ActionDataAccessException exception) { return DeclarationFailure(exception); }
    }

    /// <summary>仅初始化NULL归属，完整对象无变化不记成功事件，事务失败全部回滚。</summary>
    [HttpPost("{id}"), RequireFeature("document.document.initializeOwnership"), ButtonBusinessHandler(typeof(DocumentOwnershipInitializationButtonHandler))]
    public async Task<IResult> Initialize(string id, InitializeInput input, CancellationToken token)
    {
        try { return await buttons.ExecuteAsync(handler, new([id], "single", input.AccountId), token); }
        catch (EntityOwnershipException exception) { return Failure(exception); }
        catch (ActionDataAccessException exception) { return DeclarationFailure(exception); }
    }

    // 待补齐SQL查询保留独立读取来源；资格及账号候选仍不新增对象裁决。
    private static DataExecutionBindingReference Source(string stage) => new("document.document.initializeOwnership", "entityOwnership.initialize", stage);

    // 只翻译正式来源错误，不捕获事务、取消或持久化故障。
    private static IResult DeclarationFailure(ActionDataAccessException exception) => Results.Problem(statusCode: exception.Status, title: exception.Code,
        extensions: new Dictionary<string, object?> { ["code"] = exception.Code });

    // 稳定业务失败沿平台Problem Details，不返回隐藏对象或原始持久化内容。
    private static IResult Failure(EntityOwnershipException exception) =>
        LinkQueryEndpointExtensions.Problem(new LinkQueryException(exception.StatusCode, exception.Code));
}

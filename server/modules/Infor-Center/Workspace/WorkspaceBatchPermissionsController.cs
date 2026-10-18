using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.LinkQueries;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace ICP.Platform.Modules.Workspace;

public sealed partial class WorkspaceContentsController
{
    /// <summary>完整来源集合及按资源分组的主体项一次提交，不接受客户端声称的对象身份。</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record BatchPermissionsInput(IReadOnlyList<WorkspaceBatchPermissionSource> Sources, string Mode,
        IReadOnlyList<DataScopeBatchResourceConfiguration> Resources, bool ConfirmClear = false);

    /// <summary>未知结果核对只接收完整来源，不携带重放模板、请求版本或提交结论。</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record BatchCurrentPermissionsInput(IReadOnlyList<WorkspaceBatchPermissionSource> Sources);

    /// <summary>读取完整匹配候选；目录及字段只来自当前可读对象，未授管理资格明确反馈。</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record BatchPermissionCollectionInput(IReadOnlyList<WorkspaceBatchPermissionSource> Sources,
        bool IncludeDescendants = false, WorkspaceBatchPermissionFilters? Filters = null);

    /// <summary>初选和剪贴板来源每次重解；添加下级只遍历Folder实际内容，不扩展文件或其他假对象。</summary>
    [HttpPost("content/permissions/batch/prepare"), RequireFeature("workspace.folder.authorize")]
    public async Task<IResult> PrepareBatchPermissions(BatchPermissionCollectionInput input,
        [FromServices] IDataScopeObjectAuthorization authorization, [FromServices] IDataScopeObjectPermissionQueries queries, CancellationToken token)
    {
        using var observation = evaluation?.BeginOperation();
        try { return Results.Ok(await contents.PrepareBatchPermissionsAsync(Actor(), input.Sources, input.IncludeDescendants, input.Filters,
            authorization, queries, token)); }
        catch (LinkQueryException error) { return LinkQueryEndpointExtensions.Problem(error); }
    }

    /// <summary>同一个正式授权功能内执行整批保存；配置、审计共同提交，成功不回读敏感直接项。</summary>
    [HttpPost("content/permissions/batch"), RequireFeature("workspace.folder.authorize")]
    public async Task<IResult> ReplaceBatchPermissions(BatchPermissionsInput input,
        [FromServices] IDataScopeBatchAuthorization authorization, CancellationToken token)
    {
        try
        {
            var result = await contents.ReplaceBatchPermissionsAsync(Actor(), input.Sources,
                new(input.Mode, input.Resources, input.ConfirmClear), authorization, token);
            return result.Status == DataScopeAuthorizationStatus.Succeeded ? Results.Ok(result.Value)
                : PermissionFailure(result.Status, result.Error);
        }
        catch (LinkQueryException error) { return LinkQueryEndpointExtensions.Problem(error); }
    }
    /// <summary>显式核对当前直接项；全组资格不成立时不返回任何部分配置。</summary>
    [HttpPost("content/permissions/batch/current"), RequireFeature("workspace.folder.authorize")]
    public async Task<IResult> ReadCurrentBatchPermissions(BatchCurrentPermissionsInput input,
        [FromServices] IDataScopeBatchAuthorization authorization, CancellationToken token)
    {
        using var observation = evaluation?.BeginOperation();
        try
        {
            var result = await contents.ReadCurrentBatchPermissionsAsync(Actor(), input.Sources, authorization, token);
            return result.Status == DataScopeAuthorizationStatus.Succeeded ? Results.Ok(result.Value)
                : PermissionFailure(result.Status, result.Error);
        }
        catch (LinkQueryException error) { return LinkQueryEndpointExtensions.Problem(error); }
    }

}

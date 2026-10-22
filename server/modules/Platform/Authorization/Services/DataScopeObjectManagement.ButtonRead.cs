using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class DataScopeObjectManagement
{
    /// <inheritdoc/>
    public async Task<DataScopeAuthorizationResult<object>> WithinBoundaryAsync(DataScopeObjectReadInput input, string actionKey,
        Func<DataScopeObjectReadState, CancellationToken, Task<DataScopeAuthorizationResult<object>>> execute, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        var status = await CheckManagementActionAsync(input.ResourceKey, input.ObjectId, token);
        if (status != DataScopeObjectEligibility.Allowed)
            throw new ActionDataAccessException(status == DataScopeObjectEligibility.NotFound ? 404 : 403,
                status == DataScopeObjectEligibility.NotFound ? "StandardCrud.NotFound" : "DataScope.ActionDenied");
        var resource = catalog.Resolve(input.ResourceKey);
        var before = await catalog.ReadObjectAsync(input.ResourceKey, input.ObjectId, token)
            ?? throw new ActionDataAccessException(404, "StandardCrud.NotFound");
        return await execute(new ObjectReadState(input, new(input.ResourceKey, resource.ModelName, input.ObjectId, before)), token);
    }

    /// <inheritdoc/>
    public ButtonBusinessTarget Target(DataScopeObjectReadState state) => state.Target;

    /// <inheritdoc/>
    public async Task<DataScopeAuthorizationResult<object>> ExecuteAsync(DataScopeObjectReadState state, CancellationToken token)
    {
        if (state is not ObjectReadState owned) throw new InvalidOperationException("DATA_SCOPE_OBJECT_READ_STATE_INVALID");
        var input = owned.Input;
        return input.Operation switch
        {
            "eligibility" => new(DataScopeAuthorizationStatus.Succeeded),
            "configuration" => new(DataScopeAuthorizationStatus.Succeeded, await grants.GetAsync(input.ResourceKey, input.ObjectId, token)),
            "catalog" => new(DataScopeAuthorizationStatus.Succeeded, new DataScopeObjectCatalog(catalog.ActionsFor(input.ResourceKey), catalog.DirectSubjects.Types(input.ResourceKey))),
            "subjects" => await ReadManagedSubjectsAsync(input, token),
            "explanation" => await ReadManagedExplanationAsync(input, token),
            _ => throw new InvalidOperationException("DATA_SCOPE_OBJECT_READ_OPERATION_INVALID")
        };
    }

    // 仅客户端主体目录输入错误沿原结果返回，裁决及解释事实源故障不伪装成配置校验失败。
    private async Task<DataScopeAuthorizationResult<object>> ReadManagedSubjectsAsync(DataScopeObjectReadInput input, CancellationToken token)
    {
        try
        {
            return new(DataScopeAuthorizationStatus.Succeeded, await identities.SearchAsync(input.SubjectType!, input.Query,
                input.Page, input.CurrentIds ?? [], token));
        }
        catch (RolePermissionValidationException error)
        {
            return new(DataScopeAuthorizationStatus.InvalidInput, Error: Error(error));
        }
    }

    // generic解释保持完整管理资格与原安全DTO，不把owner纯查看接口的read门槛套入。
    private async Task<DataScopeAuthorizationResult<object>> ReadManagedExplanationAsync(DataScopeObjectReadInput input, CancellationToken token)
    {
        var accountId = input.AccountId ?? context.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(accountId) || accountId.Length > 128) return new(DataScopeAuthorizationStatus.InvalidInput);
        var directory = await identities.SearchAsync("user", null, 1, [accountId], token);
        var account = directory.CurrentValues.SingleOrDefault(item => item.Id == accountId);
        if (account is null) return new(DataScopeAuthorizationStatus.NotFound);
        var actions = await ((DataScopeReadPlans)decisions).ExplainAsync(input.ResourceKey, input.ObjectId, accountId, token);
        return new(DataScopeAuthorizationStatus.Succeeded, new DataScopeExplanationDto(input.ResourceKey, input.ObjectId, accountId, account.Name, actions));
    }

    private sealed record ObjectReadState(DataScopeObjectReadInput Input, ButtonExecutionTarget Target) : DataScopeObjectReadState(Target);
}

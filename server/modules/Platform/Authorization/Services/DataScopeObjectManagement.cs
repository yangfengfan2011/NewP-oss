using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>完整对象管理操作拥有目标资格、输入校验与执行顺序，调用方无需预先取得资格。</summary>
public sealed class DataScopeObjectManagement(DataScopeCatalog catalog, CanonicalModulePlan plan,
    IFeatureDecisionSource features, IDataScopeObjectDecisionProvider decisions, DataScopeObjectGrants grants,
    DataScopeIdentities identities, IHttpContextAccessor context, IDataScopeObjectAccess? access = null,
    IDataScopeEvaluationScope? evaluation = null) : IDataScopeObjectAuthorization
{
    /// <summary>校验具体资源的授权功能位置，不能借另一资源的入口或请求头获得资格。</summary>
    public async Task<DataScopeObjectEligibility> CheckAsync(string resourceKey, string objectId, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        var resource = catalog.Resolve(resourceKey);
        var http = context.HttpContext ?? throw new InvalidOperationException("DATA_SCOPE_HTTP_CONTEXT_REQUIRED");
        var actor = AuthenticatedSubject.FromPrincipal(http.User).SubjectId;
        var positions = http.Request.Headers["X-NewP-Feature"];
        if (positions.Count != 1 || string.IsNullOrWhiteSpace(positions[0])) return DataScopeObjectEligibility.Forbidden;
        var position = positions[0]!;
        var expected = resourceKey + ".authorize";
        if (!plan.FeatureCatalog.Any(point => point.Position == position && point.ModuleId == resource.ModuleId
            && point.BehaviorKey == expected))
            return DataScopeObjectEligibility.Forbidden;
        var feature = await features.EvaluateAsync(actor, position, resource.ModuleId, expected, token);
        if (feature == PermissionDecision.Indeterminate) throw new InvalidOperationException("DATA_SCOPE_FEATURE_DECISION_UNAVAILABLE");
        if (feature != PermissionDecision.Granted)
            return DataScopeObjectEligibility.Forbidden;
        return await CheckDataAsync(resourceKey, objectId, token);
    }

    // 统一数据资格供旧generic功能门禁和已验证来源的owner入口复用，不接收skip或功能头替代值。
    private async Task<DataScopeObjectEligibility> CheckDataAsync(string resourceKey, string objectId, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        var resource = catalog.Resolve(resourceKey);
        // 手工构造的旧无约束消费者保留原构造方式；显式声明约束绝不能退回纯数据read。
        if (access is null && resource.ReadConstraintKey is not null)
            throw new InvalidOperationException("DATA_SCOPE_OBJECT_ACCESS_MISSING");
        var readable = access is not null ? await access.IsReadableAsync(resourceKey, objectId, token)
            : await decisions.IsAllowedAsync(resourceKey, objectId, "read", token);
        if (!readable) return DataScopeObjectEligibility.NotFound;
        if (!await decisions.IsAllowedAsync(resourceKey, objectId, "authorize", token)) return DataScopeObjectEligibility.Forbidden;
        return DataScopeObjectEligibility.Allowed;
    }

    /// <summary>取得目标管理资格后读取完整对象配置，包含自身或其他主体项。</summary>
    public async Task<DataScopeObjectResult<DataScopeObjectDto>> GetAsync(string resourceKey, string objectId, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        var eligibility = await CheckAsync(resourceKey, objectId, token);
        if (eligibility != DataScopeObjectEligibility.Allowed) return new(DeniedStatus(eligibility));
        return new(DataScopeObjectStatus.Succeeded, await grants.GetAsync(resourceKey, objectId, token));
    }

    /// <summary>资格仅在保存前验证；提交后失去自身资格仍返回成功。</summary>
    public async Task<DataScopeObjectResult> SaveAsync(string resourceKey, string objectId, DataScopeObjectDto input, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        var eligibility = await CheckAsync(resourceKey, objectId, token);
        if (eligibility != DataScopeObjectEligibility.Allowed) return new(DeniedStatus(eligibility));
        return await SaveConfigurationAsync(resourceKey, objectId, input, token);
    }

    // 两个正式入口只在资格来源不同，目标一致性、校验和保存事务共用原实现。
    private async Task<DataScopeObjectResult> SaveConfigurationAsync(string resourceKey, string objectId, DataScopeObjectDto input, CancellationToken token)
    {
        try
        {
            if (input.ResourceKey != resourceKey || input.ObjectId != objectId)
                throw new RolePermissionValidationException("对象授权目标与请求不一致。", "DATA_SCOPE_OBJECT_TARGET_MISMATCH", field: "objectId");
            await grants.SaveAsync(input, token);
            return new(DataScopeObjectStatus.Succeeded);
        }
        catch (RolePermissionValidationException exception)
        { return new(DataScopeObjectStatus.InvalidInput, exception.Message, exception); }
    }

    /// <summary>被分析账号必须存在；复用正式裁决实现，不改变操作者的管理资格或请求身份。</summary>
    public async Task<DataScopeObjectResult<DataScopeExplanationDto>> ExplainAsync(string resourceKey, string objectId, string? accountId, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        var eligibility = await CheckAsync(resourceKey, objectId, token);
        if (eligibility != DataScopeObjectEligibility.Allowed) return new(DeniedStatus(eligibility));
        var target = accountId ?? context.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(target) || target.Length > 128) return new(DataScopeObjectStatus.InvalidInput);
        var directory = await identities.SearchAsync("user", null, 1, [target], token);
        var account = directory.CurrentValues.SingleOrDefault(item => item.Id == target);
        if (account is null) return new(DataScopeObjectStatus.NotFound);
        var actions = await ((DataScopeReadPlans)decisions).ExplainAsync(resourceKey, objectId, target, token);
        return new(DataScopeObjectStatus.Succeeded, new(resourceKey, objectId, target, account.Name, actions));
    }

    /// <summary>对象资格通过后公开完整动作及主体类型目录，关闭动作仍可用于配置。</summary>
    public async Task<DataScopeObjectResult<DataScopeObjectCatalog>> CatalogAsync(string resourceKey, string objectId, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        var eligibility = await CheckAsync(resourceKey, objectId, token);
        if (eligibility != DataScopeObjectEligibility.Allowed) return new(DeniedStatus(eligibility));
        return new(DataScopeObjectStatus.Succeeded, new(catalog.ActionsFor(resourceKey), catalog.DirectSubjects.Types(resourceKey)));
    }

    /// <summary>最小主体目录只在对象管理资格下开放，不调用业务 Lookup。</summary>
    public async Task<DataScopeObjectResult<DataScopeSubjectPage>> SubjectsAsync(string resourceKey, string objectId,
        string type, string? query, int page, IReadOnlyList<string> currentIds, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        var eligibility = await CheckAsync(resourceKey, objectId, token);
        if (eligibility != DataScopeObjectEligibility.Allowed) return new(DeniedStatus(eligibility));
        try { return new(DataScopeObjectStatus.Succeeded, await identities.SearchAsync(type, query, page, currentIds, token)); }
        catch (RolePermissionValidationException exception)
        { return new(DataScopeObjectStatus.InvalidInput, ValidationMessage: exception.Message, ValidationError: exception); }
    }

    /// <inheritdoc/>
    async Task<DataScopeAuthorizationStatus> IDataScopeObjectAuthorization.CheckAsync(string resourceKey, string objectId, CancellationToken token) =>
        AuthorizationStatus(await CheckDataAsync(resourceKey, objectId, token));

    /// <inheritdoc/>
    async Task<DataScopeAuthorizationResult<DataScopeObjectConfiguration>> IDataScopeObjectAuthorization.ReadAsync(string resourceKey, string objectId, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        var status = AuthorizationStatus(await CheckDataAsync(resourceKey, objectId, token));
        return status == DataScopeAuthorizationStatus.Succeeded
            ? new(status, await grants.GetAsync(resourceKey, objectId, token)) : new(status);
    }

    /// <inheritdoc/>
    async Task<DataScopeAuthorizationResult> IDataScopeObjectAuthorization.ReplaceAsync(string resourceKey, string objectId, DataScopeObjectConfiguration input, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        var status = AuthorizationStatus(await CheckDataAsync(resourceKey, objectId, token));
        if (status != DataScopeAuthorizationStatus.Succeeded) return new(status);
        var result = await SaveConfigurationAsync(resourceKey, objectId, new(input.ResourceKey, input.ObjectId, input.Subjects), token);
        return result.Status == DataScopeObjectStatus.Succeeded ? new(DataScopeAuthorizationStatus.Succeeded)
            : new(DataScopeAuthorizationStatus.InvalidInput, Error(result.ValidationError!));
    }

    /// <inheritdoc/>
    async Task<DataScopeAuthorizationResult<DataScopeSubjectPage>> IDataScopeObjectAuthorization.SubjectsAsync(string resourceKey, string objectId,
        string type, string? query, int page, IReadOnlyList<string> currentIds, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        var status = AuthorizationStatus(await CheckDataAsync(resourceKey, objectId, token));
        if (status != DataScopeAuthorizationStatus.Succeeded) return new(status);
        try { return new(status, await identities.SearchAsync(type, query, page, currentIds, token)); }
        catch (RolePermissionValidationException error) { return new(DataScopeAuthorizationStatus.InvalidInput, Error: Error(error)); }
    }

    // 稳定契约只携带原错误事实，不跨模块传出Authorization私有异常类型。
    private static DataScopeConfigurationError Error(AuthorizationValidationException error) =>
        new(error.Message, error.Code, "AuthorizationErrors." + error.Code, error.Args, error.Field);

    // 完整对象资格的结果映射不依赖两个enum的数值顺序。
    private static DataScopeAuthorizationStatus AuthorizationStatus(DataScopeObjectEligibility eligibility) => eligibility switch
    {
        DataScopeObjectEligibility.Allowed => DataScopeAuthorizationStatus.Succeeded,
        DataScopeObjectEligibility.Forbidden => DataScopeAuthorizationStatus.Forbidden,
        DataScopeObjectEligibility.NotFound => DataScopeAuthorizationStatus.NotFound,
        _ => throw new ArgumentOutOfRangeException(nameof(eligibility))
    };

    // 资格拒绝属于完整操作的正常结果；事实源故障仍由原异常传播。
    private static DataScopeObjectStatus DeniedStatus(DataScopeObjectEligibility eligibility) => eligibility switch
    {
        DataScopeObjectEligibility.Forbidden => DataScopeObjectStatus.Forbidden,
        DataScopeObjectEligibility.NotFound => DataScopeObjectStatus.NotFound,
        _ => throw new ArgumentOutOfRangeException(nameof(eligibility))
    };
}

public enum DataScopeObjectEligibility { Allowed, Forbidden, NotFound }

public enum DataScopeObjectStatus { Succeeded, Forbidden, NotFound, InvalidInput }

/// <summary>模块内完整操作结果；校验文案仅用于配置错误，缺省表示不公开细节的无效输入。</summary>
public record DataScopeObjectResult(DataScopeObjectStatus Status, string? ValidationMessage = null, AuthorizationValidationException? ValidationError = null);

/// <summary>读取成功时携带已有响应数据，拒绝时不携带目录或对象内容。</summary>
public sealed record DataScopeObjectResult<T>(DataScopeObjectStatus Status, T? Value = default, string? ValidationMessage = null, AuthorizationValidationException? ValidationError = null)
    : DataScopeObjectResult(Status, ValidationMessage, ValidationError);

/// <summary>对象管理可配置目录，不裁掉已关闭动作或操作者不可读的主体类型。</summary>
public sealed record DataScopeObjectCatalog(IReadOnlyList<DataActionDeclaration> Actions, IReadOnlyList<DataScopeSubjectType> SubjectTypes);

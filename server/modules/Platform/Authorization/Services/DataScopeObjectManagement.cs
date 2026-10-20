using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Crud.ObjectIds;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>完整对象管理操作拥有目标资格、输入校验与执行顺序，调用方无需预先取得资格。</summary>
public sealed partial class DataScopeObjectManagement(IDataExecutionAccess executions, DataScopeCatalog catalog, CanonicalModulePlan plan,
    IFeatureDecisionSource features, IDataScopeObjectDecisionProvider decisions, DataScopeObjectGrants grants,
    DataScopeIdentities identities, IHttpContextAccessor context, IPlatformTransactionExecutor transactions, IEntityAuditValueProvider audit, IPlatformObjectIdGenerator ids,
    IDataScopeEvaluationScope? evaluation = null) : IDataScopeObjectAuthorization, IDataScopeBatchAuthorization
{
    /// <summary>只读操作组合功能位置与当前数据资格；手工写入只前置功能资格，数据资格由锁内流程拥有。</summary>
    public async Task<DataScopeObjectEligibility> CheckAsync(string resourceKey, string objectId, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        var feature = await CheckFeatureAsync(resourceKey, token);
        return feature == DataScopeObjectEligibility.Allowed
            ? await CheckDataAsync(resourceKey, objectId, token, executions.BindResourceEndpoint(resourceKey)) : feature;
    }

    /// <summary>校验具体资源的授权功能位置，不能借另一资源的入口或请求头获得资格。</summary>
    private async Task<DataScopeObjectEligibility> CheckFeatureAsync(string resourceKey, CancellationToken token)
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
        return DataScopeObjectEligibility.Allowed;
    }

    // 统一数据资格供旧generic功能门禁和已验证来源的owner入口复用，不接收skip或功能头替代值。
    private async Task<DataScopeObjectEligibility> CheckDataAsync(string resourceKey, string objectId, CancellationToken token,
        DataExecutionBindingReference execution)
    {
        using var operation = evaluation?.BeginOperation();
        await foreach (var decision in executions.EvaluateObjectsAsync(RequiredOwnerExecution(execution), "target", [new(resourceKey, objectId)], token))
            return decision.Decision switch
        {
            DataExecutionDecision.Allowed => DataScopeObjectEligibility.Allowed,
            DataExecutionDecision.NotFound => DataScopeObjectEligibility.NotFound,
            _ => DataScopeObjectEligibility.Forbidden
        };
        throw new InvalidOperationException("DATA_EXECUTION_TARGET_RESULT_MISSING");
    }

    /// <summary>取得目标管理资格后读取完整对象配置，包含自身或其他主体项。</summary>
    public async Task<DataScopeObjectResult<DataScopeObjectDto>> GetAsync(string resourceKey, string objectId, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        var eligibility = await CheckAsync(resourceKey, objectId, token);
        if (eligibility != DataScopeObjectEligibility.Allowed) return new(DeniedStatus(eligibility));
        return new(DataScopeObjectStatus.Succeeded, await grants.GetAsync(resourceKey, objectId, token));
    }

    /// <summary>功能资格前置；最终对象资格、配置和保存由共同锁内流程执行，成功后不回读自身资格。</summary>
    public async Task<DataScopeObjectResult> SaveAsync(string resourceKey, string objectId, DataScopeObjectDto input, CancellationToken token)
    {
        using var operation = evaluation?.BeginOperation();
        var eligibility = await CheckFeatureAsync(resourceKey, token);
        if (eligibility != DataScopeObjectEligibility.Allowed) return new(DeniedStatus(eligibility));
        return await SaveConfigurationAsync(resourceKey, objectId, input, token, executions.BindResourceEndpoint(resourceKey));
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

    // 手工操作保留原generic结果与owner合同，不依赖两个枚举的数值布局。
    private static DataScopeAuthorizationStatus AuthorizationStatus(DataScopeObjectStatus status) => status switch
    {
        DataScopeObjectStatus.Succeeded => DataScopeAuthorizationStatus.Succeeded,
        DataScopeObjectStatus.Forbidden => DataScopeAuthorizationStatus.Forbidden,
        DataScopeObjectStatus.NotFound => DataScopeAuthorizationStatus.NotFound,
        DataScopeObjectStatus.InvalidInput => DataScopeAuthorizationStatus.InvalidInput,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
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

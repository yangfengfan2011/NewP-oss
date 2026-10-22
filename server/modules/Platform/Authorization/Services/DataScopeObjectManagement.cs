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
public sealed partial class DataScopeObjectManagement(IObjectActionAccess objectActions, DataScopeCatalog catalog, CanonicalModulePlan plan,
    IFeatureDecisionSource features, IDataScopeObjectDecisionProvider decisions, DataScopeObjectGrants grants,
    DataScopeIdentities identities, IHttpContextAccessor context, IPlatformTransactionExecutor transactions, IEntityAuditValueProvider audit, IPlatformObjectIdGenerator ids,
    IDataScopeEvaluationScope? evaluation = null)
    : IDataScopeObjectSaveHandler, IDataScopeObjectReadHandler, IDataScopeBatchSaveHandler, IDataScopeBatchReadHandler
{
    /// <summary>校验具体资源的授权功能位置，不能借另一资源的入口或请求头获得资格。</summary>
    internal async Task<DataScopeObjectEligibility> CheckFeatureAsync(string resourceKey, CancellationToken token)
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

    // 稳定契约只携带原错误事实，不跨模块传出Authorization私有异常类型。
    private static DataScopeConfigurationError Error(AuthorizationValidationException error) =>
        new(error.Message, error.Code, "AuthorizationErrors." + error.Code, error.Args, error.Field);

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

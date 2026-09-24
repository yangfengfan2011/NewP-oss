using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>管理资格同时绑定目标资源的功能位置、最终读取与授权动作。</summary>
public sealed class DataScopeObjectManagement(DataScopeCatalog catalog, CanonicalModulePlan plan,
    IFeatureDecisionSource features, IDataScopeObjectDecisionProvider decisions, DataScopeObjectGrants grants,
    DataScopeIdentities identities, IHttpContextAccessor context)
{
    /// <summary>校验具体资源的授权功能位置，不能借另一资源的入口或请求头获得资格。</summary>
    public async Task<DataScopeObjectEligibility> CheckAsync(string resourceKey, string objectId, CancellationToken token)
    {
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
        if (!await decisions.IsAllowedAsync(resourceKey, objectId, "read", token)) return DataScopeObjectEligibility.NotFound;
        if (!await decisions.IsAllowedAsync(resourceKey, objectId, "authorize", token)) return DataScopeObjectEligibility.Forbidden;
        return DataScopeObjectEligibility.Allowed;
    }

    /// <summary>已获资格的调用方读取完整对象配置，包含自身或其他主体项。</summary>
    public Task<DataScopeObjectDto> GetAsync(string resourceKey, string objectId, CancellationToken token) =>
        grants.GetAsync(resourceKey, objectId, token);

    /// <summary>资格仅在保存前验证；提交后失去自身资格仍返回成功。</summary>
    public Task SaveAsync(DataScopeObjectDto input, CancellationToken token) => grants.SaveAsync(input, token);

    /// <summary>被分析账号必须存在；复用正式裁决实现，不改变操作者的管理资格或请求身份。</summary>
    public async Task<DataScopeExplanationDto?> ExplainAsync(string resourceKey, string objectId, string accountId, CancellationToken token)
    {
        var directory = await identities.SearchAsync("user", null, 1, [accountId], token);
        var account = directory.CurrentValues.SingleOrDefault(item => item.Id == accountId);
        if (account is null) return null;
        var actions = await ((DataScopeReadPlans)decisions).ExplainAsync(resourceKey, objectId, accountId, token);
        return new(resourceKey, objectId, accountId, account.Name, actions);
    }

    /// <summary>最小主体目录只在对象管理资格下开放，不调用业务 Lookup。</summary>
    public Task<DataScopeSubjectPage> SubjectsAsync(string type, string? query, int page, IReadOnlyList<string> currentIds, CancellationToken token) =>
        identities.SearchAsync(type, query, page, currentIds, token);
}

public enum DataScopeObjectEligibility { Allowed, Forbidden, NotFound }

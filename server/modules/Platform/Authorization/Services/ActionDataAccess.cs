using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>只消费当前正式计划；浏览器预检结果与自报数据动作均不是执行凭据。</summary>
public sealed class ActionDataAccess(CanonicalModulePlan plan, IHttpContextAccessor http,
    IDataScopeObjectAccess access, IDataScopeObjectDecisionProvider decisions) : IActionDataAccess
{
    /// <inheritdoc/>
    public async Task<bool> IsAllowedAsync(string execution, string resourceKey, string objectId, CancellationToken cancellationToken = default)
    {
        var context = http.HttpContext ?? throw new ActionDataAccessException(400, "ActionDataAccess.ContextRequired");
        if (context.User.Identity?.IsAuthenticated != true) throw new ActionDataAccessException(401, "ActionDataAccess.SubjectRequired");
        var ids = context.Request.Headers["X-NewP-Data-Declaration"];
        var positions = context.Request.Headers["X-NewP-Feature"];
        var owners = context.GetEndpoint()?.Metadata.GetOrderedMetadata<EndpointModuleAttribute>();
        var featureRules = context.GetEndpoint()?.Metadata.GetOrderedMetadata<RequireFeatureAttribute>();
        var requirement = ids.Count == 1 ? plan.ActionDataAccessCatalog.SingleOrDefault(item => item.Id == ids[0]) : null;
        if (requirement is null || owners?.Count != 1 || featureRules?.Count != 1 || requirement.ModuleId != owners[0].ModuleId
            || requirement.Execution != execution || !requirement.Resources.Contains(resourceKey, StringComparer.Ordinal))
            throw new ActionDataAccessException(400, "ActionDataAccess.DeclarationInvalid");
        if (positions.Count != 1 || !requirement.FeaturePositions.Contains(positions[0]!, StringComparer.Ordinal))
            throw new ActionDataAccessException(403, "ActionDataAccess.FeatureMismatch");
        if (!await access.IsReadableAsync(resourceKey, objectId, cancellationToken)) return false;
        foreach (var action in requirement.Actions)
            if (action != "read" && !await decisions.IsAllowedAsync(resourceKey, objectId, action, cancellationToken)) return false;
        return true;
    }
}

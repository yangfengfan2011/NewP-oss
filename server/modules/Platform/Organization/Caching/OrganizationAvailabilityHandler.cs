using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Persistence.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Organization.Caching;

/// <summary>组织端点在原有判权之外检查自身数据可用性，LocalAdmin 也不能绕过。</summary>
public sealed class OrganizationAvailabilityHandler(OrganizationCache cache) : AuthorizationHandler<FeatureAuthorizationRequirement>
{
    /// <inheritdoc/>
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, FeatureAuthorizationRequirement requirement)
    {
        if (context.Resource is not HttpContext http || context.User.Identity?.IsAuthenticated != true) return;
        if (http.GetEndpoint()?.Metadata.GetMetadata<EndpointModuleAttribute>()?.ModuleId != "Organization") return;
        try { await cache.ReadAsync(http.RequestAborted); }
        catch (PlatformCacheUnavailableException)
        {
            context.Fail(new AuthorizationFailureReason(this, PermissionGateFailureCodes.DecisionUnavailable));
        }
    }
}

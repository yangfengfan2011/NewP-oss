using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Authorization.Http;

/// <summary>
/// 把 功能 policy 名编译为功能位置 requirement。
/// </summary>
public sealed class PermissionAuthorizationPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
        : base(options)
    {
    }

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(RequireFeatureAttribute.Prefix, StringComparison.Ordinal))
            return Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
                .AddRequirements(new FeatureAuthorizationRequirement(policyName[RequireFeatureAttribute.Prefix.Length..])).Build());

        return base.GetPolicyAsync(policyName);
    }
}

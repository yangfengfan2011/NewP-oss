using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Authorization.Http;

/// <summary>
/// 把 RequirePermission policy 名编译为单一 PermissionGate requirement。
/// </summary>
public sealed class PermissionAuthorizationPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
        : base(options)
    {
    }

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            var permissionCode = policyName[RequirePermissionAttribute.PolicyPrefix.Length..];
            if (!string.IsNullOrWhiteSpace(permissionCode))
            {
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionAuthorizationRequirement(permissionCode))
                    .Build();
                return Task.FromResult<AuthorizationPolicy?>(policy);
            }
        }

        return base.GetPolicyAsync(policyName);
    }
}

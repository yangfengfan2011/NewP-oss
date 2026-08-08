using ICP.Platform.Authorization.Http;
using Microsoft.AspNetCore.Builder;

namespace ICP.Platform.Authorization.Extensions;

public static class PermissionEndpointConventionBuilderExtensions
{
    /// <summary>
    /// 为 minimal API endpoint 附加与 Controller 相同的 PermissionGate requirement。
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(
        this TBuilder builder,
        string permissionCode)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.RequireAuthorization(new RequirePermissionAttribute(permissionCode));
        return builder;
    }
}

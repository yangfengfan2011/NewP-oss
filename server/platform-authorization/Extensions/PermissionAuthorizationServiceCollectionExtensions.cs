using ICP.Platform.Authorization.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ICP.Platform.Authorization.Extensions;

public static class PermissionAuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// 注册功能位置判权与 ASP.NET Core handler；生产 decision source 由组合根提供。
    /// </summary>
    public static IServiceCollection AddPlatformAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorization();
        services.TryAddScoped<IFeatureDecisionSource, IndeterminateFeatureDecisionSource>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IAuthorizationHandler, FeatureAuthorizationHandler>());
        services.Replace(
            ServiceDescriptor.Singleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>());
        services.Replace(
            ServiceDescriptor.Singleton<IAuthorizationMiddlewareResultHandler, PermissionAuthorizationMiddlewareResultHandler>());
        return services;
    }
}

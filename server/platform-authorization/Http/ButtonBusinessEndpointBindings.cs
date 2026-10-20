using ICP.Platform.Authorization.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ICP.Platform.Authorization.Http;

/// <summary>启动覆盖与运行时共用真实端点的业务归属证明，不包含对象动作要求。</summary>
public static class ButtonBusinessEndpointBindings
{
    /// <summary>静态端点匹配原模块行为；资源端点只匹配已登记资源拥有者及其真实行为。</summary>
    public static bool Matches(Endpoint? endpoint, string moduleId, string businessBinding, DataScopeResource? resource = null)
    {
        if (endpoint is null) return false;
        var owners = endpoint.Metadata.GetOrderedMetadata<EndpointModuleAttribute>();
        var features = endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>();
        var dynamic = endpoint.Metadata.GetOrderedMetadata<ResourceFeatureBindingAttribute>();
        if (owners.Count != 1) return false;
        if (dynamic.Count == 0)
            return owners[0].ModuleId == moduleId && features.Count == 1 && features[0].BehaviorKey == businessBinding;
        return dynamic.Count == 1 && features.Count == 0 && resource is not null
            && endpoint.Metadata.GetOrderedMetadata<AuthenticatedOnlyAttribute>().Count == 1
            && endpoint is RouteEndpoint route
            && route.RoutePattern.Parameters.Any(parameter => parameter.Name == "resourceKey" && !parameter.IsOptional && !parameter.IsCatchAll)
            && route.RoutePattern.Parameters.Any(parameter => parameter.Name == "objectId" && !parameter.IsOptional && !parameter.IsCatchAll)
            && resource.ModuleId == moduleId && businessBinding == resource.ResourceKey + "." + dynamic[0].ActionKey;
    }
}

using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.AspNetCore.Routing;
namespace ICP.Platform.HostApi.Authorization;

/// <summary>监听前核对声明与真实执行端绑定；行为键只是实现选择，位置才是授权身份。</summary>
public static class FeatureEndpointBindings
{
    /// <summary>每个声明必须有本模块执行端，每个执行端必须对应活动声明。</summary>
    public static void Validate(IEnumerable<EndpointDataSource> sources, IReadOnlyList<FeaturePoint> points)
    {
        var declared = points.Select(point => (point.ModuleId, point.BehaviorKey)).ToHashSet();
        var consumed = new HashSet<(string ModuleId, string BehaviorKey)>();
        foreach (var endpoint in sources.SelectMany(source => source.Endpoints))
        {
            var bindings = endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>();
            if (bindings.Count == 0) continue;
            var owners = endpoint.Metadata.GetOrderedMetadata<EndpointModuleAttribute>();
            if (owners.Count != 1 || bindings.Count != 1
                || !declared.Contains((owners[0].ModuleId, bindings[0].BehaviorKey)))
                throw new InvalidOperationException($"功能执行端 {endpoint.DisplayName} 缺少唯一有效声明或模块归属不符。");
            consumed.Add((owners[0].ModuleId, bindings[0].BehaviorKey));
        }
        if (!declared.IsSubsetOf(consumed))
            throw new InvalidOperationException("功能声明缺少实际执行端绑定。");
    }
}

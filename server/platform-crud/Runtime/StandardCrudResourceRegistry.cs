using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;

namespace ICP.Platform.Crud.Runtime;

public sealed class StandardCrudResourceRegistry : IStandardCrudResourceRegistry
{
    private readonly IReadOnlyList<StandardCrudDescriptor> _descriptors;
    private readonly IReadOnlyDictionary<string, StandardCrudDescriptor> _byResourceKey;
    private readonly IReadOnlyDictionary<string, StandardCrudDescriptor> _byRoute;

    public StandardCrudResourceRegistry(IEnumerable<StandardCrudDescriptor> descriptors)
    {
        var descriptorList = descriptors.ToList();
        var byResourceKey = new Dictionary<string, StandardCrudDescriptor>(StringComparer.Ordinal);
        var byRoute = new Dictionary<string, StandardCrudDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptorList)
        {
            StandardCrudDescriptorValidator.Validate(descriptor);

            if (byResourceKey.ContainsKey(descriptor.ResourceKey))
            {
                throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 重复注册。");
            }

            var normalizedRoute = NormalizeRoute(descriptor.Route);
            if (byRoute.TryGetValue(normalizedRoute, out var existing))
            {
                throw new InvalidOperationException(
                    $"标准 CRUD 路由 {normalizedRoute} 被 {existing.ResourceKey} 与 {descriptor.ResourceKey} 重复声明。");
            }

            byResourceKey[descriptor.ResourceKey] = descriptor;
            byRoute[normalizedRoute] = descriptor;
        }

        _descriptors = descriptorList.AsReadOnly();
        _byResourceKey = byResourceKey;
        _byRoute = byRoute;
    }

    public IReadOnlyList<StandardCrudDescriptor> GetAll() => _descriptors;

    public StandardCrudDescriptor? FindByResourceKey(string resourceKey) =>
        _byResourceKey.TryGetValue(resourceKey, out var descriptor) ? descriptor : null;

    public StandardCrudDescriptor? FindByRoute(string route)
    {
        var normalizedRoute = NormalizeRoute(route);
        return _byRoute.TryGetValue(normalizedRoute, out var descriptor) ? descriptor : null;
    }

    private static string NormalizeRoute(string route)
    {
        var normalized = route.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("标准 CRUD 路由不能为空。");
        }

        return normalized;
    }
}

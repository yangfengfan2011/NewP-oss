using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Testing.Authorization;

// 共享测试只复用 runtime manifest 解析和规范授权集合展开，不拥有网络与场景选择。
internal static class RuntimeFeatureCatalogTestSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static FeaturePoint[] Parse(JsonElement manifest) => manifest
        .GetProperty("modules")
        .EnumerateArray()
        .SelectMany(module => module.GetProperty("features").Deserialize<FeaturePoint[]>(JsonOptions)
            ?? throw new InvalidOperationException("Runtime feature catalog was empty."))
        .ToArray();

    internal static string[] ExpandGrantSet(IEnumerable<FeaturePoint> selected) => selected
        .SelectMany(point => point.EntryDependencies.Append(point.Position))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}

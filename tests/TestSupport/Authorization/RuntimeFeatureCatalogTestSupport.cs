using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Testing.Authorization;

// 共享测试只复用 runtime manifest 解析和规范授权集合展开，不拥有网络与场景选择。
internal static class RuntimeFeatureCatalogTestSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // 从真实 authoring 补齐被测入口的祖先；不伪造跨模块功能或给测试主体补授权限。
    internal static IReadOnlyList<FeaturePoint> CompileWithParents(IEnumerable<FeatureDeclaration> input)
    {
        var declarations = input.ToList();
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "ICP-Platform.sln"))) repository = repository.Parent;
        var modules = new Dictionary<string, IReadOnlyList<FeatureDeclaration>>(StringComparer.Ordinal);
        for (var index = 0; index < declarations.Count; index++)
        {
            var feature = declarations[index];
            if (feature.ParentKey is null) continue;
            var parentModule = feature.ParentModuleId ?? feature.ModuleId;
            if (declarations.Any(item => item.ModuleId == parentModule && (item.ReferenceKey ?? item.Key) == feature.ParentKey)) continue;
            if (!modules.TryGetValue(parentModule, out var candidates))
            {
                var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository!.FullName, parentModule);
                var capsule = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(Path.Combine(root, "newp.plugin.json")));
                var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
                    .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path)));
                modules[parentModule] = candidates = StandardFeatureMetadataCompiler.Read(parentModule, documents, FeatureCatalogCompiler.Read(capsule, parentModule), true);
            }
            declarations.Add(candidates.Single(item => (item.ReferenceKey ?? item.Key) == feature.ParentKey));
        }
        return FeatureCatalogCompiler.Compile(declarations);
    }

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

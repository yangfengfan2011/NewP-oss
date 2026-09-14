using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Acceptance;

/// <summary>验收仅从正式运行目录选择位置，不从旧权限代码推导授权或拼接位置字符串。</summary>
internal sealed class AcceptanceFeatureCatalog(IReadOnlyList<FeaturePoint> points)
{
    internal static async Task<AcceptanceFeatureCatalog> LoadAsync(HttpClient client, CancellationToken token)
    {
        using var manifest = await client.GetFromJsonAsync<JsonDocument>("/plugins/metadata-runtime-manifest.json", token)
            ?? throw new InvalidOperationException("Runtime metadata manifest was empty.");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return new(manifest.RootElement.GetProperty("modules").EnumerateArray()
            .SelectMany(module => module.GetProperty("features").Deserialize<FeaturePoint[]>(options)
                ?? throw new InvalidOperationException("Runtime feature catalog was empty.")).ToArray());
    }

    internal FeaturePoint Behavior(string module, string behavior) => Require(point =>
        point.ModuleId == module && point.BehaviorKey == behavior, $"{module} behavior {behavior}");

    internal FeaturePoint OrganizationTab(string key)
    {
        var navigation = Require(point => point.ModuleId == "Organization" && point.Kind == "navigation"
            && point.Key == "organization.structure", "Organization management navigation");
        return Require(point => point.ModuleId == "Organization" && point.Kind == "tab"
            && point.Key == key && point.ParentPosition == navigation.Position, $"Organization tab {key}");
    }

    internal FeaturePoint Action(FeaturePoint entry, string key, string scope, string? staticScope = null) => Require(point =>
        point.ModuleId == entry.ModuleId && point.Kind == "action" && point.ParentPosition == entry.Position
        && point.Key == key && point.ActionScope == scope && point.StaticScope == staticScope,
        $"{entry.Position} action {scope}/{key}");

    internal string[] GrantSet(params FeaturePoint[] selected) => selected
        .SelectMany(point => point.EntryDependencies.Append(point.Position)).Distinct(StringComparer.Ordinal).ToArray();

    private FeaturePoint Require(Func<FeaturePoint, bool> predicate, string description)
    {
        var matches = points.Where(predicate).ToArray();
        return matches.Length == 1 ? matches[0]
            : throw new InvalidOperationException($"Expected one registered {description}; found {matches.Length}.");
    }
}

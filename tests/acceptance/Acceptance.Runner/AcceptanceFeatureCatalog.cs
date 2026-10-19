using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Testing.Authorization;

namespace ICP.Platform.Acceptance;

/// <summary>验收仅从正式运行目录选择位置，不从旧权限代码推导授权或拼接位置字符串。</summary>
internal sealed class AcceptanceFeatureCatalog(IReadOnlyList<FeaturePoint> points, IReadOnlyList<ActionDataAccessRequirement>? declarations = null)
{
    internal static async Task<AcceptanceFeatureCatalog> LoadAsync(HttpClient client, CancellationToken token)
    {
        using var manifest = await client.GetFromJsonAsync<JsonDocument>("/plugins/metadata-runtime-manifest.json", token)
            ?? throw new InvalidOperationException("Runtime metadata manifest was empty.");
        return new(RuntimeFeatureCatalogTestSupport.Parse(manifest.RootElement),
            manifest.RootElement.GetProperty("modules").EnumerateArray().SelectMany(module => module.GetProperty("dataAccess").EnumerateArray())
                .Select(item => item.Deserialize<ActionDataAccessRequirement>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!).ToArray());
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

    internal FeaturePoint NodeTab(FeaturePoint tree, string nodeType, string key) => Require(point =>
        point.ModuleId == tree.ModuleId && point.Kind == "tab" && point.ParentPosition == tree.Position
        && point.StaticScope == nodeType && point.Key == key, $"{tree.Position} node {nodeType} tab {key}");

    internal string[] GrantSet(params FeaturePoint[] selected) =>
        RuntimeFeatureCatalogTestSupport.ExpandGrantSet(selected);

    internal string Declaration(FeaturePoint point) => (declarations ?? []).Single(declaration =>
        declaration.ModuleId == point.ModuleId && declaration.FeaturePositions.Contains(point.Position)).Id;

    // 同一表单位置同时包含写入和候选读取来源；准备请求按真实执行选择，不借位置猜测。
    internal string Declaration(FeaturePoint point, string execution, string provider, string resourceKey) =>
        (declarations ?? []).Single(declaration => declaration.ModuleId == point.ModuleId
            && declaration.Execution == execution && declaration.Provider == provider
            && declaration.Resources.Contains(resourceKey) && declaration.FeaturePositions.Contains(point.Position)).Id;

    // 同一功能位置可承载多个真实读取 View；准备请求同时核对实际 View、surface 和 action。
    internal string DeclarationAtView(FeaturePoint point, string viewKey, string surface, string actionKey) =>
        (declarations ?? []).Single(declaration => declaration.ModuleId == point.ModuleId
            && declaration.ViewKey == viewKey && declaration.Surface == surface && declaration.ActionKey == actionKey
            && declaration.FeaturePositions.Contains(point.Position)).Id;

    // 位置来自正式目录，View 仍由实际准备消费入口明确给出。
    internal string ReadDeclaration(string moduleId, string viewKey, string featurePosition) =>
        DeclarationAtView(Require(point => point.ModuleId == moduleId && point.Position == featurePosition,
            $"{moduleId} feature {featurePosition}"), viewKey, "read", "");

    private FeaturePoint Require(Func<FeaturePoint, bool> predicate, string description)
    {
        var matches = points.Where(predicate).ToArray();
        return matches.Length == 1 ? matches[0]
            : throw new InvalidOperationException($"Expected one registered {description}; found {matches.Length}.");
    }
}

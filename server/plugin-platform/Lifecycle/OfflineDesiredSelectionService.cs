using System.Text.Json;
using ICP.Platform.PluginPlatform.ComponentBundles;

namespace ICP.Platform.PluginPlatform.Lifecycle;

/// <summary>整包选版的完整差异，包含独立选版与包内版本两个明确输入。</summary>
public sealed record BundleVersionDifference(string ModuleId, string SelectedVersion, string PackageVersion);

/// <summary>调用方对一个版本差异的显式裁决，可继续保留已安装的独立版本。</summary>
public sealed record BundleVersionDecision(string ModuleId, string Version);

/// <summary>只规划离线期望选择；库存接纳、原子保存和重启校验仍由既有 owner 执行。</summary>
public static class OfflineDesiredSelectionService
{
    /// <summary>列出全部版本差异，不比较版本高低，也不修改任何状态。</summary>
    public static IReadOnlyList<BundleVersionDifference> DescribeDifferences(ComponentBundleManifest bundle,
        IReadOnlyList<PluginDesiredModuleState> recordedDesired)
    {
        var selected = recordedDesired.ToDictionary(item => item.ModuleId, StringComparer.OrdinalIgnoreCase);
        return bundle.Components.Where(member => selected.TryGetValue(member.ModuleId, out var previous) && previous.Version != member.Version)
            .Select(member => new BundleVersionDifference(member.ModuleId, selected[member.ModuleId].Version, member.Version))
            .OrderBy(item => item.ModuleId, StringComparer.Ordinal).ToArray();
    }

    /// <summary>全部差异明确裁决后合并整包选择；启用根及新成员，保留已有成员自身启停和所有无关选择。</summary>
    public static IReadOnlyList<PluginDesiredModuleState> SelectBundle(ComponentBundleManifest bundle,
        IReadOnlyList<PluginDesiredModuleState> recordedDesired, bool enableNewMembers,
        IReadOnlyList<BundleVersionDecision> decisions)
    {
        var differences = DescribeDifferences(bundle, recordedDesired);
        var chosen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var decision in decisions)
        {
            if (string.IsNullOrWhiteSpace(decision.Version) || !chosen.TryAdd(decision.ModuleId, decision.Version)
                || !differences.Any(item => string.Equals(item.ModuleId, decision.ModuleId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("PLUGIN_BUNDLE_VERSION_DECISION_INVALID: duplicate or unrelated decision.");
        }
        if (differences.Any(item => !chosen.ContainsKey(item.ModuleId)))
            throw new InvalidDataException("PLUGIN_BUNDLE_VERSION_CHOICE_REQUIRED: " + JsonSerializer.Serialize(differences, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var desired = recordedDesired.ToDictionary(item => item.ModuleId, StringComparer.OrdinalIgnoreCase);
        foreach (var member in bundle.Components)
        {
            desired.TryGetValue(member.ModuleId, out var previous);
            desired[member.ModuleId] = new(member.ModuleId, chosen.GetValueOrDefault(member.ModuleId) ?? member.Version,
                enableNewMembers && member.ModuleId == bundle.RootModuleId || (previous?.Enabled ?? enableNewMembers));
        }
        return desired.Values.OrderBy(item => item.ModuleId, StringComparer.Ordinal).ToArray();
    }
}

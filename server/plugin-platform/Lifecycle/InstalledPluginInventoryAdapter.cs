using ICP.Platform.Kernel.PluginPackages;

namespace ICP.Platform.PluginPlatform.Lifecycle;

/// <summary>
/// 把冻结期 legacy installed records 投影为 Plugin Platform 只读清单。
/// </summary>
public static class InstalledPluginInventoryAdapter
{
    public static IReadOnlyList<InstalledPluginInventoryItem> Create(
        InstalledPluginStateStore stateStore)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        var installRoot = stateStore.InstallRoot;

        return stateStore.LoadAll()
            .Select(record => new InstalledPluginInventoryItem(
                record.ModuleId,
                record.InstalledVersions
                    .Where(static version => !string.IsNullOrWhiteSpace(version))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                record.CurrentEnabledVersion,
                record.InstalledVersions
                    .Where(static version => !string.IsNullOrWhiteSpace(version))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        static version => version,
                        version => Path.GetFullPath(Path.Combine(
                            installRoot,
                            "packages",
                            record.ModuleId,
                            version)),
                        StringComparer.OrdinalIgnoreCase)))
            .OrderBy(static item => item.ModuleId, StringComparer.Ordinal)
            .ToArray();
    }
}

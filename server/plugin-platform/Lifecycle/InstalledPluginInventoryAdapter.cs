using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.PluginPlatform.ComponentBundles;

namespace ICP.Platform.PluginPlatform.Lifecycle;

/// <summary>
/// 把冻结期 legacy installed records 投影为 Plugin Platform 只读清单。
/// </summary>
public static class InstalledPluginInventoryAdapter
{
    // 正式入口先在租约内恢复；保留 legacy 显式停用，其他读取者也不能绕过文件事务屏障。
    public static IReadOnlyList<InstalledPluginInventoryItem> Create(
        InstalledPluginStateStore stateStore)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        var installRoot = stateStore.InstallRoot;
        NativeComponentInventory.EnsureComplete(installRoot);

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
                        StringComparer.OrdinalIgnoreCase),
                record.IsDisabled ? record.Version : null))
            .OrderBy(static item => item.ModuleId, StringComparer.Ordinal)
            .ToArray();
    }
}

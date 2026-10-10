namespace ICP.Platform.PluginPlatform.Lifecycle;

/// <summary>
/// 只管理 desired/active/LKG 状态转移；实际激活只允许由冷启动闸门提交。
/// </summary>
public sealed class PluginLifecycleManager
{
    private readonly PluginLifecycleStateStore _stateStore;

    public PluginLifecycleManager(PluginLifecycleStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public ResolvedPluginLifecycleState Resolve(IReadOnlyList<InstalledPluginInventoryItem> inventory) =>
        _stateStore.Resolve(inventory);

    /// <summary>一次原子保存完整离线期望集合；完整组合与依赖由下一次 Host 启动验证。</summary>
    public PluginLifecycleCommandResult RequestDesired(IReadOnlyList<InstalledPluginInventoryItem> inventory,
        IReadOnlyList<PluginDesiredModuleState> selections)
    {
        var current = Resolve(inventory);
        IReadOnlyList<PluginDesiredModuleState> desired = [];
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in selections)
        {
            var installed = FindInventoryItem(inventory, selection.ModuleId);
            if (!ids.Add(selection.ModuleId) || installed is null || !installed.InstalledVersions.Contains(selection.Version, StringComparer.Ordinal))
                return PluginLifecycleCommandResult.Failed(selection.ModuleId, selection.Version, "PLUGIN_SELECTION_INVALID: duplicate or unavailable installed identity.");
            desired = UpsertDesired(desired, selection with { ModuleId = installed.ModuleId });
        }
        _stateStore.SaveDesired(current, desired);
        return PluginLifecycleCommandResult.PendingRestart("candidate", null);
    }

    public PluginLifecycleCommandResult RequestEnable(
        IReadOnlyList<InstalledPluginInventoryItem> inventory,
        string moduleId,
        string version)
    {
        var installed = FindInventoryItem(inventory, moduleId);
        if (installed is null)
        {
            return PluginLifecycleCommandResult.Failed(
                moduleId,
                version,
                $"Plugin '{moduleId}' is not installed.");
        }

        var installedVersion = installed.InstalledVersions.FirstOrDefault(item =>
            string.Equals(item, version, StringComparison.OrdinalIgnoreCase));
        if (installedVersion is null)
        {
            return PluginLifecycleCommandResult.Failed(
                installed.ModuleId,
                version,
                $"Plugin '{installed.ModuleId}' version '{version}' is not installed.");
        }

        var current = Resolve(inventory);
        var desired = UpsertDesired(
            current.DesiredModules,
            new PluginDesiredModuleState(installed.ModuleId, installedVersion, Enabled: true));
        _stateStore.SaveDesired(current, desired);
        return PluginLifecycleCommandResult.PendingRestart(installed.ModuleId, installedVersion);
    }

    public PluginLifecycleCommandResult RequestDisable(
        IReadOnlyList<InstalledPluginInventoryItem> inventory,
        string moduleId)
    {
        var installed = FindInventoryItem(inventory, moduleId);
        if (installed is null)
        {
            return PluginLifecycleCommandResult.Failed(
                moduleId,
                null,
                $"Plugin '{moduleId}' is not installed.");
        }

        var current = Resolve(inventory);
        var existingDesired = current.DesiredModules.FirstOrDefault(item =>
            string.Equals(item.ModuleId, installed.ModuleId, StringComparison.OrdinalIgnoreCase));
        var preservedVersion = existingDesired?.Version
            ?? FindActiveVersion(current.ActiveSnapshot, installed.ModuleId)
            ?? installed.InstalledVersions.LastOrDefault();
        if (string.IsNullOrWhiteSpace(preservedVersion))
        {
            return PluginLifecycleCommandResult.Failed(
                installed.ModuleId,
                null,
                $"Plugin '{installed.ModuleId}' has no installed version to preserve while disabled.");
        }

        var desired = UpsertDesired(
            current.DesiredModules,
            new PluginDesiredModuleState(installed.ModuleId, preservedVersion, Enabled: false));
        _stateStore.SaveDesired(current, desired);
        return PluginLifecycleCommandResult.PendingRestart(installed.ModuleId, preservedVersion);
    }

    public PluginLifecycleCommandResult RequestRollback(
        IReadOnlyList<InstalledPluginInventoryItem> inventory,
        string moduleId)
    {
        var installed = FindInventoryItem(inventory, moduleId);
        if (installed is null)
        {
            return PluginLifecycleCommandResult.Failed(
                moduleId,
                null,
                $"Plugin '{moduleId}' is not installed.");
        }

        var current = Resolve(inventory);
        var lkgVersion = FindActiveVersion(current.LastKnownGoodSnapshot, installed.ModuleId);
        if (string.IsNullOrWhiteSpace(lkgVersion))
        {
            return PluginLifecycleCommandResult.Failed(
                installed.ModuleId,
                null,
                $"Plugin '{installed.ModuleId}' is not present in the last-known-good snapshot.");
        }

        var installedVersion = installed.InstalledVersions.FirstOrDefault(version =>
            string.Equals(version, lkgVersion, StringComparison.OrdinalIgnoreCase));
        if (installedVersion is null)
        {
            return PluginLifecycleCommandResult.Failed(
                installed.ModuleId,
                lkgVersion,
                $"Plugin '{installed.ModuleId}' last-known-good version '{lkgVersion}' is not installed.");
        }

        var desired = UpsertDesired(
            current.DesiredModules,
            new PluginDesiredModuleState(installed.ModuleId, installedVersion, Enabled: true));
        _stateStore.SaveDesired(current, desired);
        return PluginLifecycleCommandResult.PendingRestart(installed.ModuleId, installedVersion);
    }

    public void CommitActive(
        IReadOnlyList<InstalledPluginInventoryItem> inventory,
        PluginActiveSnapshot candidate)
    {
        var current = Resolve(inventory);
        _stateStore.CommitActive(current, candidate);
    }

    private static InstalledPluginInventoryItem? FindInventoryItem(
        IReadOnlyList<InstalledPluginInventoryItem> inventory,
        string moduleId)
    {
        return inventory.FirstOrDefault(item =>
            string.Equals(item.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindActiveVersion(PluginActiveSnapshot? snapshot, string moduleId)
    {
        return snapshot?.Modules.FirstOrDefault(item =>
            string.Equals(item.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase))?.Version;
    }

    private static IReadOnlyList<PluginDesiredModuleState> UpsertDesired(
        IReadOnlyList<PluginDesiredModuleState> current,
        PluginDesiredModuleState replacement)
    {
        return current
            .Where(item => !string.Equals(
                item.ModuleId,
                replacement.ModuleId,
                StringComparison.OrdinalIgnoreCase))
            .Append(replacement)
            .OrderBy(static item => item.ModuleId, StringComparer.Ordinal)
            .ToArray();
    }
}

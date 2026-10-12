using ICP.Platform.PluginPlatform.Activation;

namespace ICP.Platform.PluginPlatform.Lifecycle;

/// <summary>
/// 只管理 desired/active/LKG 状态转移；实际激活只允许由冷启动闸门提交。
/// </summary>
public sealed class PluginLifecycleManager
{
    private readonly PluginLifecycleStateStore _stateStore;

    internal IPluginActivationDeployment Deployment => _stateStore.Deployment;

    public PluginLifecycleManager(PluginLifecycleStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public ResolvedPluginLifecycleState Resolve(IReadOnlyList<InstalledPluginInventoryItem> inventory) =>
        _stateStore.Resolve(inventory);

    /// <summary>读取实际保存的离线选择；未选定库存的默认停用项不表示已有运维决定。</summary>
    public IReadOnlyList<PluginDesiredModuleState> ReadRecordedDesired(IReadOnlyList<InstalledPluginInventoryItem> inventory) =>
        _stateStore.ReadRecordedDesired(inventory);

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

    /// <summary>保存指定组件的启用选择，保持其他已记录选择且不持久化库存默认项。</summary>
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
            ReadRecordedDesired(inventory),
            new PluginDesiredModuleState(installed.ModuleId, installedVersion, Enabled: true));
        _stateStore.SaveDesired(current, desired);
        return PluginLifecycleCommandResult.PendingRestart(installed.ModuleId, installedVersion);
    }

    /// <summary>保存指定组件的停用选择，保留其版本与其他已记录选择。</summary>
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
            ReadRecordedDesired(inventory),
            new PluginDesiredModuleState(installed.ModuleId, preservedVersion, Enabled: false));
        _stateStore.SaveDesired(current, desired);
        return PluginLifecycleCommandResult.PendingRestart(installed.ModuleId, preservedVersion);
    }

    /// <summary>请求已安装的 LKG 版本，不立即改变活动集合或未选库存。</summary>
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
            ReadRecordedDesired(inventory),
            new PluginDesiredModuleState(installed.ModuleId, installedVersion, Enabled: true));
        _stateStore.SaveDesired(current, desired);
        return PluginLifecycleCommandResult.PendingRestart(installed.ModuleId, installedVersion);
    }

    /// <summary>提交已通过启动闸门的快照，保存真实选择而不提升默认库存项。</summary>
    public void CommitActive(
        IReadOnlyList<InstalledPluginInventoryItem> inventory,
        PluginActiveSnapshot candidate)
    {
        var current = Resolve(inventory);
        _stateStore.CommitActive(current, candidate, ReadRecordedDesired(inventory));
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

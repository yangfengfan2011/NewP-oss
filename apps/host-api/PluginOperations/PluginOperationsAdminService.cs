using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.PluginPlatform.Lifecycle;

namespace ICP.Platform.HostApi.PluginOperations;

/// <summary>
/// 从同一份 desired / active / LKG 状态生成运维视图；legacy CurrentEnabledVersion 仅作为首次迁移输入。
/// </summary>
public class PluginOperationsAdminService
{
    private readonly InstalledPluginStateStore _stateStore;
    private readonly PluginLifecycleManager _lifecycleManager;

    public PluginOperationsAdminService(InstalledPluginStateStore stateStore)
        : this(
            stateStore,
            new PluginLifecycleManager(new PluginLifecycleStateStore(stateStore.InstallRoot)))
    {
    }

    public PluginOperationsAdminService(
        InstalledPluginStateStore stateStore,
        PluginLifecycleManager lifecycleManager)
    {
        _stateStore = stateStore;
        _lifecycleManager = lifecycleManager;
    }

    public IReadOnlyList<PluginModuleStatusDto> GetAllModules()
    {
        var inventory = InstalledPluginInventoryAdapter.Create(_stateStore);
        var state = _lifecycleManager.Resolve(inventory);
        return inventory.Select(item => MapToStatus(item, state)).ToArray();
    }

    public PluginModuleStatusDto? GetModule(string moduleId) =>
        GetAllModules().FirstOrDefault(item =>
            string.Equals(item.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));

    private static PluginModuleStatusDto MapToStatus(
        InstalledPluginInventoryItem item,
        ResolvedPluginLifecycleState state)
    {
        var desired = state.DesiredModules.FirstOrDefault(candidate =>
            string.Equals(candidate.ModuleId, item.ModuleId, StringComparison.OrdinalIgnoreCase));
        var activeVersion = state.ActiveSnapshot?.Modules.FirstOrDefault(candidate =>
            string.Equals(candidate.ModuleId, item.ModuleId, StringComparison.OrdinalIgnoreCase))?.Version;
        var lastKnownGoodVersion = state.LastKnownGoodSnapshot?.Modules.FirstOrDefault(candidate =>
            string.Equals(candidate.ModuleId, item.ModuleId, StringComparison.OrdinalIgnoreCase))?.Version;
        var restartRequired = desired is not null && (desired.Enabled
            ? !string.Equals(desired.Version, activeVersion, StringComparison.OrdinalIgnoreCase)
            : activeVersion is not null);
        var status = restartRequired
            ? "PendingRestart"
            : activeVersion is not null
                ? "Enabled"
                : lastKnownGoodVersion is not null
                    ? "Disabled"
                    : "Installed";

        return new PluginModuleStatusDto
        {
            ModuleId = item.ModuleId,
            DisplayName = item.ModuleId,
            Status = status,
            InstalledVersions = item.InstalledVersions,
            DesiredVersion = desired?.Version,
            DesiredEnabled = desired?.Enabled ?? false,
            ActiveVersion = activeVersion,
            LastKnownGoodVersion = lastKnownGoodVersion,
            RestartRequired = restartRequired,
            // 一期兼容字段明确映射 active，而不是 legacy CurrentEnabledVersion。
            CurrentEnabledVersion = activeVersion,
            CanDisable = desired?.Enabled is true,
            CanRollback = lastKnownGoodVersion is not null
                && (desired?.Enabled is false
                    || !string.Equals(lastKnownGoodVersion, desired?.Version, StringComparison.OrdinalIgnoreCase)),
            CanEnable = item.InstalledVersions.Count > 0,
            CanUninstall = desired?.Enabled is false && activeVersion is null,
            LastOperation = null,
            LastError = null
        };
    }
}

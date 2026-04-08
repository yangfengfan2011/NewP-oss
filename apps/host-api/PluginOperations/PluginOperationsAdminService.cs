using ICP.Platform.Kernel.PluginPackages;

namespace ICP.Platform.HostApi.PluginOperations;

/// <summary>
/// 插件运维查询服务：封装从 InstalledPluginStateStore 读取记录并映射为 PluginModuleStatusDto 的逻辑。
/// </summary>
public class PluginOperationsAdminService
{
    private readonly InstalledPluginStateStore _stateStore;

    public PluginOperationsAdminService(InstalledPluginStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public IReadOnlyList<PluginModuleStatusDto> GetAllModules()
    {
        var allRecords = _stateStore.LoadAll();
        return allRecords.Select(MapToStatus).ToList();
    }

    public PluginModuleStatusDto? GetModule(string moduleId)
    {
        if (!_stateStore.TryLoad(moduleId, out var record, out _) || record is null)
        {
            return null;
        }

        return MapToStatus(record);
    }

    private static PluginModuleStatusDto MapToStatus(InstalledPluginRecord record)
    {
        var currentVersion = record.CurrentEnabledVersion;
        var hasEnabled = !string.IsNullOrWhiteSpace(currentVersion);
        var canRollback = false;

        if (hasEnabled)
        {
            var idx = record.InstalledVersions.FindIndex(
                v => string.Equals(v, currentVersion, StringComparison.OrdinalIgnoreCase));
            canRollback = idx > 0;
        }

        return new PluginModuleStatusDto
        {
            ModuleId = record.ModuleId,
            DisplayName = record.ModuleId,
            Status = hasEnabled ? "Enabled" : "Installed",
            InstalledVersions = record.InstalledVersions
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly(),
            CurrentEnabledVersion = hasEnabled ? currentVersion : null,
            CanDisable = hasEnabled,
            CanRollback = canRollback,
            CanEnable = record.InstalledVersions.Count > 0,
            LastOperation = null,
            LastError = null
        };
    }
}

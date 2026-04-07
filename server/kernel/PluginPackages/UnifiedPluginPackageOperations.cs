namespace ICP.Platform.Kernel.PluginPackages;

public class UnifiedPluginPackageOperations
{
    private readonly InstalledPluginStateStore _stateStore;

    public UnifiedPluginPackageOperations(InstalledPluginStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public UnifiedPluginLifecycleResult Enable(string moduleId, string version)
    {
        if (!_stateStore.TryLoad(moduleId, out var record, out var loadError))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, version, loadError ?? "Failed to load plugin state.");
        }

        if (record is null)
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, version, $"Plugin '{moduleId}' is not installed.");
        }

        if (!record.InstalledVersions.Contains(version, StringComparer.OrdinalIgnoreCase))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, version, $"Plugin '{moduleId}' version '{version}' is not installed.");
        }

        record.CurrentEnabledVersion = record.InstalledVersions
            .First(installedVersion => string.Equals(installedVersion, version, StringComparison.OrdinalIgnoreCase));

        if (!_stateStore.TrySave(record, out var saveError))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, version, saveError ?? "Failed to save plugin state.");
        }

        return UnifiedPluginLifecycleResult.Enabled(moduleId, record.CurrentEnabledVersion);
    }

    public UnifiedPluginLifecycleResult Disable(string moduleId)
    {
        if (!_stateStore.TryLoad(moduleId, out var record, out var loadError))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, null, loadError ?? "Failed to load plugin state.");
        }

        if (record is null)
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, null, $"Plugin '{moduleId}' is not installed.");
        }

        record.CurrentEnabledVersion = null;

        if (!_stateStore.TrySave(record, out var saveError))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, null, saveError ?? "Failed to save plugin state.");
        }

        return UnifiedPluginLifecycleResult.Disabled(moduleId);
    }

    public UnifiedPluginLifecycleResult Rollback(string moduleId)
    {
        if (!_stateStore.TryLoad(moduleId, out var record, out var loadError))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, null, loadError ?? "Failed to load plugin state.");
        }

        if (record is null)
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, null, $"Plugin '{moduleId}' is not installed.");
        }

        var currentVersion = record.CurrentEnabledVersion;
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, null, $"Plugin '{moduleId}' is disabled and cannot be rolled back.");
        }

        var currentIndex = record.InstalledVersions.FindIndex(version => string.Equals(version, currentVersion, StringComparison.OrdinalIgnoreCase));
        if (currentIndex <= 0)
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, currentVersion, $"Plugin '{moduleId}' has no previous installed version to roll back to.");
        }

        record.CurrentEnabledVersion = record.InstalledVersions[currentIndex - 1];

        if (!_stateStore.TrySave(record, out var saveError))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, record.CurrentEnabledVersion, saveError ?? "Failed to save plugin state.");
        }

        return UnifiedPluginLifecycleResult.Enabled(moduleId, record.CurrentEnabledVersion);
    }
}

public class UnifiedPluginLifecycleResult
{
    public string ModuleId { get; init; } = string.Empty;
    public string? Version { get; init; }
    public string Status { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;

    public static UnifiedPluginLifecycleResult Enabled(string moduleId, string? version) =>
        new()
        {
            ModuleId = moduleId,
            Version = version,
            Status = "Enabled"
        };

    public static UnifiedPluginLifecycleResult Disabled(string moduleId) =>
        new()
        {
            ModuleId = moduleId,
            Status = "Disabled"
        };

    public static UnifiedPluginLifecycleResult Failed(string moduleId, string? version, string errorMessage) =>
        new()
        {
            ModuleId = moduleId,
            Version = version,
            Status = "Failed",
            ErrorMessage = errorMessage
        };
}

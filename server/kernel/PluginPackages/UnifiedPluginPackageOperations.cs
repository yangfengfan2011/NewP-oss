using System.Text.Json;

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

        if (!TryValidateEnableDependencies(moduleId, version, out var dependencyError))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, version, dependencyError ?? $"Plugin '{moduleId}' version '{version}' cannot be enabled.");
        }

        record.CurrentEnabledVersion = record.InstalledVersions
            .First(installedVersion => string.Equals(installedVersion, version, StringComparison.OrdinalIgnoreCase));
        record.IsDisabled = false;

        if (!_stateStore.TrySave(record, out var saveError))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, version, saveError ?? "Failed to save plugin state.");
        }

        if (!_stateStore.TryPruneOldVersions(
            record,
            InstalledPluginStateStore.DefaultRetainedVersionCount,
            out var prunedRecord,
            out var pruneError))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, version, pruneError ?? "Failed to prune old plugin versions.");
        }

        return UnifiedPluginLifecycleResult.Enabled(moduleId, prunedRecord.CurrentEnabledVersion);
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
        record.IsDisabled = true;

        if (!_stateStore.TrySave(record, out var saveError))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, null, saveError ?? "Failed to save plugin state.");
        }

        return UnifiedPluginLifecycleResult.Disabled(moduleId);
    }

    public UnifiedPluginLifecycleResult Uninstall(string moduleId)
    {
        if (!_stateStore.TryLoad(moduleId, out var record, out var loadError))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, null, loadError ?? "Failed to load plugin state.");
        }

        if (record is null)
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, null, $"Plugin '{moduleId}' is not installed.");
        }

        var lifecycleState = InstalledPluginLifecycleState.FromRecord(record);
        if (string.Equals(lifecycleState, InstalledPluginLifecycleState.Enabled, StringComparison.OrdinalIgnoreCase))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, null, $"Plugin '{moduleId}' is enabled and must be disabled before uninstall.");
        }

        var warningMessage = BuildUninstallDependencyWarning(moduleId);

        if (!_stateStore.TryDeleteModule(moduleId, out var deleteError))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, null, deleteError ?? "Failed to delete plugin state.");
        }

        return UnifiedPluginLifecycleResult.Uninstalled(moduleId, warningMessage);
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
            if (!TryRestoreRollbackTarget(record, out var rollbackError))
            {
                return UnifiedPluginLifecycleResult.Failed(moduleId, currentVersion, rollbackError ?? $"Plugin '{moduleId}' has no previous installed version to roll back to.");
            }

            currentIndex = record.InstalledVersions.FindIndex(version => string.Equals(version, currentVersion, StringComparison.OrdinalIgnoreCase));
            if (currentIndex <= 0)
            {
                return UnifiedPluginLifecycleResult.Failed(moduleId, currentVersion, $"Plugin '{moduleId}' has no previous installed version to roll back to.");
            }
        }

        record.CurrentEnabledVersion = record.InstalledVersions[currentIndex - 1];
        record.IsDisabled = false;

        if (!_stateStore.TrySave(record, out var saveError))
        {
            return UnifiedPluginLifecycleResult.Failed(moduleId, record.CurrentEnabledVersion, saveError ?? "Failed to save plugin state.");
        }

        return UnifiedPluginLifecycleResult.Enabled(moduleId, record.CurrentEnabledVersion);
    }

    private bool TryRestoreRollbackTarget(InstalledPluginRecord record, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(record.CurrentEnabledVersion))
        {
            return false;
        }

        if (!_stateStore.TryGetPreviousArchivedVersion(record.ModuleId, record.CurrentEnabledVersion, out var previousVersion, out var lookupError))
        {
            errorMessage = lookupError;
            return false;
        }

        if (string.IsNullOrWhiteSpace(previousVersion))
        {
            return false;
        }

        if (!_stateStore.TryRestoreArchivedVersion(record.ModuleId, previousVersion, out var restoreError))
        {
            errorMessage = restoreError;
            return false;
        }

        if (!record.InstalledVersions.Contains(previousVersion, StringComparer.OrdinalIgnoreCase))
        {
            record.InstalledVersions.Insert(0, previousVersion);
        }

        return true;
    }

    private bool TryValidateEnableDependencies(string moduleId, string version, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            var packageRoot = _stateStore.GetPackageRoot(moduleId, version);
            if (!_stateStore.TryLoadPackageDependencies(packageRoot, out var dependencyModuleIds, out var loadError))
            {
                errorMessage = $"Plugin '{moduleId}' version '{version}' cannot be enabled: {loadError}";
                return false;
            }

            if (dependencyModuleIds.Count == 0)
            {
                return true;
            }

            var installedRecords = _stateStore.LoadAll()
                .ToDictionary(record => record.ModuleId, StringComparer.OrdinalIgnoreCase);
            var enabledRecords = _stateStore.LoadAllEnabled()
                .ToDictionary(record => record.ModuleId, StringComparer.OrdinalIgnoreCase);

            var missingDependencies = new List<string>();
            var disabledDependencies = new List<string>();

            foreach (var dependencyModuleId in dependencyModuleIds)
            {
                if (!installedRecords.ContainsKey(dependencyModuleId))
                {
                    missingDependencies.Add($"{dependencyModuleId} (not installed)");
                    continue;
                }

                if (!enabledRecords.ContainsKey(dependencyModuleId))
                {
                    disabledDependencies.Add(dependencyModuleId);
                }
            }

            if (missingDependencies.Count == 0 && disabledDependencies.Count == 0)
            {
                return true;
            }

            var details = new List<string>();
            if (missingDependencies.Count > 0)
            {
                details.Add($"missing: {string.Join(", ", missingDependencies)}");
            }

            if (disabledDependencies.Count > 0)
            {
                details.Add($"installed but not enabled: {string.Join(", ", disabledDependencies)}");
            }

            errorMessage = $"Plugin '{moduleId}' version '{version}' cannot be enabled until dependencies are ready ({string.Join("; ", details)}).";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            errorMessage = $"Plugin '{moduleId}' version '{version}' cannot be enabled: {ex.Message}";
            return false;
        }
    }

    private string? BuildUninstallDependencyWarning(string moduleId)
    {
        try
        {
            var installedRecords = _stateStore.LoadAll();
            var dependentModuleIds = new List<string>();

            foreach (var installedRecord in installedRecords)
            {
                if (string.Equals(installedRecord.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!_stateStore.TryLoadPackageDependencies(installedRecord.PackageRoot, out var dependencyModuleIds, out _))
                {
                    continue;
                }

                if (dependencyModuleIds.Contains(moduleId, StringComparer.OrdinalIgnoreCase))
                {
                    dependentModuleIds.Add(installedRecord.ModuleId);
                }
            }

            if (dependentModuleIds.Count == 0)
            {
                return null;
            }

            return $"Uninstalled with warning: other installed plugins still depend on '{moduleId}': {string.Join(", ", dependentModuleIds.Distinct(StringComparer.OrdinalIgnoreCase))}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return $"Uninstalled with warning: dependency impact could not be fully evaluated ({ex.Message}).";
        }
    }
}

public class UnifiedPluginLifecycleResult
{
    public string ModuleId { get; init; } = string.Empty;
    public string? Version { get; init; }
    public string Status { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public string WarningMessage { get; init; } = string.Empty;
    public string Message => string.Equals(Status, "Failed", StringComparison.OrdinalIgnoreCase)
        ? ErrorMessage
        : WarningMessage;

    public static UnifiedPluginLifecycleResult Enabled(string moduleId, string? version, string? warningMessage = null) =>
        new()
        {
            ModuleId = moduleId,
            Version = version,
            Status = "Enabled",
            WarningMessage = warningMessage ?? string.Empty
        };

    public static UnifiedPluginLifecycleResult Disabled(string moduleId) =>
        new()
        {
            ModuleId = moduleId,
            Status = "Disabled"
        };

    public static UnifiedPluginLifecycleResult Uninstalled(string moduleId, string? warningMessage = null) =>
        new()
        {
            ModuleId = moduleId,
            Status = "Uninstalled",
            WarningMessage = warningMessage ?? string.Empty
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

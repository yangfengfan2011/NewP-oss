using System.Text.Json;

namespace ICP.Platform.Kernel.PluginPackages;

public class UnifiedPluginPackageInstaller
{
    private static readonly char[] InvalidPathSegmentChars = Path.GetInvalidFileNameChars()
        .Concat(Path.GetInvalidPathChars())
        .Append(Path.DirectorySeparatorChar)
        .Append(Path.AltDirectorySeparatorChar)
        .Distinct()
        .ToArray();

    private readonly InstalledPluginStateStore _stateStore;
    private readonly UnifiedPluginPackageScanner _scanner;
    private readonly Action<string, string> _restoreInstallDirectory;

    public UnifiedPluginPackageInstaller(InstalledPluginStateStore stateStore)
        : this(stateStore, new UnifiedPluginPackageScanner())
    {
    }

    public UnifiedPluginPackageInstaller(InstalledPluginStateStore stateStore, UnifiedPluginPackageScanner scanner)
        : this(stateStore, scanner, RestoreInstallDirectory)
    {
    }

    internal UnifiedPluginPackageInstaller(
        InstalledPluginStateStore stateStore,
        UnifiedPluginPackageScanner scanner,
        Action<string, string> restoreInstallDirectory)
    {
        _stateStore = stateStore;
        _scanner = scanner;
        _restoreInstallDirectory = restoreInstallDirectory;
    }

    public UnifiedPluginInstallResult Install(string packageRoot, string installRoot)
    {
        var resolvedInstallRoot = Path.GetFullPath(installRoot);
        var scanResult = _scanner.ScanPackage(packageRoot);
        if (!scanResult.IsSuccess || scanResult.Manifest is null)
        {
            return UnifiedPluginInstallResult.Failed(
                $"Failed to install plugin package from '{packageRoot}': {scanResult.ErrorMessage}");
        }

        if (!TryValidatePathSegment(scanResult.Manifest.ModuleId, "ModuleId", out var moduleIdError))
        {
            return UnifiedPluginInstallResult.Failed(moduleIdError!);
        }

        if (!TryValidatePathSegment(scanResult.Manifest.Version, "Version", out var versionError))
        {
            return UnifiedPluginInstallResult.Failed(versionError!);
        }

        var packagesRoot = Path.Combine(resolvedInstallRoot, "packages");
        var versionedTarget = Path.Combine(packagesRoot, scanResult.Manifest.ModuleId, scanResult.Manifest.Version);
        var backupTarget = versionedTarget + ".backup";
        var hasExistingInstall = Directory.Exists(versionedTarget);
        var backupCreated = false;

        try
        {
            CleanupInstallDirectory(backupTarget);

            if (hasExistingInstall)
            {
                Directory.Move(versionedTarget, backupTarget);
                backupCreated = true;
            }

            Directory.CreateDirectory(versionedTarget);
            CopyDirectory(packageRoot, versionedTarget);

            if (!_stateStore.TryLoad(scanResult.Manifest.ModuleId, out var existingRecord, out var loadError))
            {
                throw new IOException(loadError);
            }

            var installedVersions = existingRecord?.InstalledVersions
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();

            if (!installedVersions.Contains(scanResult.Manifest.Version, StringComparer.OrdinalIgnoreCase))
            {
                installedVersions.Add(scanResult.Manifest.Version);
            }

            var installedRecord = new InstalledPluginRecord
            {
                ModuleId = scanResult.Manifest.ModuleId,
                Version = scanResult.Manifest.Version,
                Status = InstalledPluginLifecycleState.Installed,
                PackageRoot = versionedTarget,
                CurrentEnabledVersion = existingRecord?.CurrentEnabledVersion,
                IsDisabled = existingRecord?.IsDisabled ?? false,
                InstalledVersions = installedVersions
            };

            if (!_stateStore.TrySave(installedRecord, out var saveError))
            {
                throw new IOException(saveError);
            }

            if (!_stateStore.TryPruneOldVersions(
                installedRecord,
                InstalledPluginStateStore.DefaultRetainedVersionCount,
                out _,
                out var pruneError))
            {
                throw new IOException(pruneError);
            }

            CleanupInstallDirectory(backupTarget);
            var warningMessage = BuildDependencyPrecheckWarning(versionedTarget);
            return UnifiedPluginInstallResult.Installed(scanResult.Manifest.ModuleId, scanResult.Manifest.Version, warningMessage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CleanupInstallDirectory(versionedTarget);
            if (backupCreated)
            {
                try
                {
                    _restoreInstallDirectory(backupTarget, versionedTarget);
                }
                catch (Exception restoreEx) when (restoreEx is IOException or UnauthorizedAccessException)
                {
                    return UnifiedPluginInstallResult.Failed(
                        $"Failed to install plugin package '{scanResult.Manifest.ModuleId}@{scanResult.Manifest.Version}': {ex.Message} Restore failed: {restoreEx.Message}");
                }
            }

            return UnifiedPluginInstallResult.Failed($"Failed to install plugin package '{scanResult.Manifest.ModuleId}@{scanResult.Manifest.Version}': {ex.Message}");
        }
    }

    private string? BuildDependencyPrecheckWarning(string packageRoot)
    {
        try
        {
            if (!_stateStore.TryLoadPackageDependencies(packageRoot, out var dependencyModuleIds, out var loadError))
            {
                return $"Dependency precheck warning: {loadError}";
            }

            if (dependencyModuleIds.Count == 0)
            {
                return null;
            }

            var installedRecords = _stateStore.LoadAll()
                .ToDictionary(record => record.ModuleId, StringComparer.OrdinalIgnoreCase);
            var enabledRecords = _stateStore.LoadAllEnabled()
                .ToDictionary(record => record.ModuleId, StringComparer.OrdinalIgnoreCase);

            var unresolvedDependencies = new List<string>();
            foreach (var dependencyModuleId in dependencyModuleIds)
            {
                if (!installedRecords.ContainsKey(dependencyModuleId))
                {
                    unresolvedDependencies.Add($"{dependencyModuleId} (not installed)");
                    continue;
                }

                if (!enabledRecords.ContainsKey(dependencyModuleId))
                {
                    unresolvedDependencies.Add($"{dependencyModuleId} (installed but disabled)");
                }
            }

            if (unresolvedDependencies.Count == 0)
            {
                return null;
            }

            return $"Dependency precheck warning: some dependencies are not ready: {string.Join(", ", unresolvedDependencies)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return $"Dependency precheck warning: unable to verify dependencies ({ex.Message}).";
        }
    }

    private static bool TryValidatePathSegment(string value, string fieldName, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            errorMessage = $"Plugin manifest {fieldName} must not be empty.";
            return false;
        }

        if (value is "." or ".." || value.Contains("..", StringComparison.Ordinal) || value.IndexOfAny(InvalidPathSegmentChars) >= 0)
        {
            errorMessage = $"Plugin manifest {fieldName} contains unsafe path characters.";
            return false;
        }

        return true;
    }

    private static void CleanupInstallDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void RestoreInstallDirectory(string backupPath, string destinationPath)
    {
        if (!Directory.Exists(backupPath))
        {
            return;
        }

        if (Directory.Exists(destinationPath))
        {
            Directory.Delete(destinationPath, recursive: true);
        }

        Directory.Move(backupPath, destinationPath);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }
}

public class UnifiedPluginInstallResult
{
    public string ModuleId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public string WarningMessage { get; init; } = string.Empty;
    public string Message => string.Equals(Status, "Failed", StringComparison.OrdinalIgnoreCase)
        ? ErrorMessage
        : WarningMessage;

    public static UnifiedPluginInstallResult Installed(string moduleId, string version, string? warningMessage = null) =>
        new()
        {
            ModuleId = moduleId,
            Version = version,
            Status = "Installed",
            WarningMessage = warningMessage ?? string.Empty
        };

    public static UnifiedPluginInstallResult Failed(string errorMessage) =>
        new()
        {
            Status = "Failed",
            ErrorMessage = errorMessage
        };
}

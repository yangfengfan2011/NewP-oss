using System.Text;
using System.Text.Json;
using ICP.Platform.Kernel.PluginDiscovery;

namespace ICP.Platform.Kernel.PluginPackages;

// InstalledPluginStateStore 同时维护两类受控状态：
// 1) 安装态的权威记录；
// 2) 被裁剪版本的归档区，作为回滚和恢复的最后出口。
public class InstalledPluginStateStore
{
    internal const int DefaultRetainedVersionCount = 2;
    private const string PrunedVersionsRootName = ".pruned";

    private static readonly char[] InvalidPathSegmentChars = Path.GetInvalidFileNameChars()
        .Concat(Path.GetInvalidPathChars())
        .Append(Path.DirectorySeparatorChar)
        .Append(Path.AltDirectorySeparatorChar)
        .Distinct()
        .ToArray();
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _stateRoot;
    private readonly string _installRoot;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };
    private readonly Action<string, string, string?> _replaceFile;
    private readonly Action<string, string> _moveFile;

    public InstalledPluginStateStore(string installRoot)
        : this(
            installRoot,
            static (sourcePath, destinationPath, _) => File.Move(sourcePath, destinationPath, overwrite: true),
            File.Move)
    {
    }

    internal InstalledPluginStateStore(
        string installRoot,
        Action<string, string, string?> replaceFile,
        Action<string, string> moveFile)
    {
        _installRoot = Path.GetFullPath(installRoot);
        _stateRoot = Path.Combine(_installRoot, "state");
        Directory.CreateDirectory(_stateRoot);
        _replaceFile = replaceFile;
        _moveFile = moveFile;
    }

    public bool TryLoad(string moduleId, out InstalledPluginRecord? record, out string? errorMessage)
    {
        record = null;
        errorMessage = null;

        try
        {
            if (!TryGetStatePath(moduleId, out var path, out errorMessage))
            {
                return false;
            }

            if (!File.Exists(path))
            {
                return true;
            }

            record = JsonSerializer.Deserialize<InstalledPluginRecord>(File.ReadAllText(path));
            ValidateRecord(record, moduleId);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            errorMessage = $"Failed to load installed plugin state for '{moduleId}': {ex.Message}";
            record = null;
            return false;
        }
    }

    public bool TrySave(InstalledPluginRecord record, out string? errorMessage)
    {
        errorMessage = null;
        string? tempPath = null;

        try
        {
            Directory.CreateDirectory(_stateRoot);
            ValidateRecord(record, record.ModuleId);

            if (!TryGetStatePath(record.ModuleId, out var path, out errorMessage))
            {
                return false;
            }

            // 先写同目录临时文件，再以同卷覆盖移动提交，避免 File.Replace 在 Windows 上偶发
            // ERROR_UNABLE_TO_REMOVE_REPLACED，也避免崩溃时留下半截 JSON。
            var json = JsonSerializer.Serialize(record, _jsonOptions);
            tempPath = Path.Combine(_stateRoot, $"{record.ModuleId}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath, json + Environment.NewLine, new UTF8Encoding(false));

            if (File.Exists(path))
            {
                _replaceFile(tempPath, path, null);
            }
            else
            {
                _moveFile(tempPath, path);
            }

            tempPath = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            errorMessage = $"Failed to save installed plugin state for '{record.ModuleId}': {ex.Message}";
            return false;
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
    }

    public bool TryDeleteModule(string moduleId, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            if (!TryGetStatePath(moduleId, out var statePath, out errorMessage))
            {
                return false;
            }

            if (!TryGetModulePackagesPath(moduleId, out var modulePackagesPath, out errorMessage))
            {
                return false;
            }

            // 删除要同时清掉状态、当前安装包和归档包，避免“状态没了但文件还在”的悬挂残留。
            if (File.Exists(statePath))
            {
                File.Delete(statePath);
            }

            if (Directory.Exists(modulePackagesPath))
            {
                Directory.Delete(modulePackagesPath, recursive: true);
            }

            if (TryGetArchivedModulePath(moduleId, out var archivedModulePath, out _)
                && Directory.Exists(archivedModulePath))
            {
                Directory.Delete(archivedModulePath, recursive: true);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            errorMessage = $"Failed to delete installed plugin state for '{moduleId}': {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 加载所有已安装模块的记录（包括已禁用的模块）。
    /// </summary>
    public IReadOnlyList<InstalledPluginRecord> LoadAll()
    {
        Directory.CreateDirectory(_stateRoot);
        var records = new List<InstalledPluginRecord>();

        // 这里返回的是仍处于 Installed 状态的记录；已归档的旧版本不算在当前运行时视图内。
        foreach (var stateFilePath in Directory.GetFiles(_stateRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            var moduleId = Path.GetFileNameWithoutExtension(stateFilePath);
            if (!TryLoad(moduleId, out var record, out var errorMessage))
            {
                throw new IOException(errorMessage);
            }

            if (record is null)
            {
                continue;
            }

            if (!string.Equals(record.Status, InstalledPluginLifecycleState.Installed, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            records.Add(record);
        }

        return records;
    }

    public IReadOnlyList<InstalledPluginRecord> LoadAllEnabled()
    {
        Directory.CreateDirectory(_stateRoot);
        var enabledRecords = new List<InstalledPluginRecord>();

        // Enabled 视图是运行时投影：只有状态有效且能重建出当前启用版本的记录才算可用。
        foreach (var stateFilePath in Directory.GetFiles(_stateRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            var moduleId = Path.GetFileNameWithoutExtension(stateFilePath);
            if (!TryLoad(moduleId, out var record, out var errorMessage))
            {
                throw new IOException(errorMessage);
            }

            if (record is null)
            {
                continue;
            }

            if (!string.Equals(record.Status, InstalledPluginLifecycleState.Installed, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryCreateEnabledRecord(record, out var enabledRecord))
            {
                continue;
            }

            enabledRecords.Add(enabledRecord);
        }

        return enabledRecords;
    }

    internal bool TryLoadPackageDependencies(string packageRoot, out IReadOnlyList<string> dependencyModuleIds, out string? errorMessage)
    {
        dependencyModuleIds = Array.Empty<string>();
        errorMessage = null;

        try
        {
            // 依赖读取只用于安装后的预检提示，不改变安装结果；读取失败会降级为告警。
            var fullPackageRoot = Path.GetFullPath(packageRoot);
            var pluginJsonPath = Path.Combine(fullPackageRoot, "plugin.json");
            if (!File.Exists(pluginJsonPath))
            {
                errorMessage = $"Plugin manifest not found at '{pluginJsonPath}'.";
                return false;
            }

            var packageManifest = JsonSerializer.Deserialize<UnifiedPluginPackageManifest>(File.ReadAllText(pluginJsonPath), ManifestJsonOptions)
                ?? throw new JsonException("plugin.json deserialized to null.");

            if (packageManifest.Artifacts is null || string.IsNullOrWhiteSpace(packageManifest.Artifacts.Backend))
            {
                throw new JsonException("Plugin manifest Artifacts.Backend must not be empty.");
            }

            var backendManifestPath = ResolveContainedFilePath(fullPackageRoot, packageManifest.Artifacts.Backend, "Artifacts.Backend");
            var backendManifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(backendManifestPath), ManifestJsonOptions)
                ?? throw new JsonException("Backend manifest deserialized to null.");

            var dependencies = backendManifest.Dependencies?
                .Select(dependencyId => dependencyId?.Trim())
                .Where(dependencyId => !string.IsNullOrWhiteSpace(dependencyId))
                .Select(dependencyId => dependencyId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            dependencyModuleIds = dependencies ?? new List<string>();

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            errorMessage = $"Failed to load plugin dependencies for '{packageRoot}': {ex.Message}";
            dependencyModuleIds = Array.Empty<string>();
            return false;
        }
    }

    internal bool TryCreateEnabledRecord(InstalledPluginRecord record, out InstalledPluginRecord enabledRecord)
    {
        enabledRecord = null!;

        if (string.IsNullOrWhiteSpace(record.CurrentEnabledVersion))
        {
            return false;
        }

        var enabledVersion = record.CurrentEnabledVersion.Trim();
        if (!record.InstalledVersions.Contains(enabledVersion, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // 当前启用版本必须同时满足“记录里声明过”和“本地包版本仍存在”，否则不重建虚假的运行时视图。
        enabledRecord = new InstalledPluginRecord
        {
            ModuleId = record.ModuleId,
            Version = enabledVersion,
            Status = record.Status,
            PackageRoot = GetPackageRoot(record.ModuleId, enabledVersion),
            CurrentEnabledVersion = enabledVersion,
            IsDisabled = false,
            InstalledVersions = record.InstalledVersions
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        return true;
    }

    internal string GetPackageRoot(string moduleId, string version)
    {
        return Path.GetFullPath(Path.Combine(_installRoot, "packages", moduleId, version));
    }

    internal bool TryPruneOldVersions(
        InstalledPluginRecord record,
        int retainCount,
        out InstalledPluginRecord updatedRecord,
        out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            ValidateRecord(record, record.ModuleId);

            var installedVersions = record.InstalledVersions
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (installedVersions.Count <= retainCount)
            {
                updatedRecord = CloneRecord(record, installedVersions, record.Version);
                return true;
            }

            var retainedVersions = DetermineRetainedVersions(record, installedVersions, retainCount);
            var removedVersions = installedVersions
                .Where(version => !retainedVersions.Contains(version, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (removedVersions.Count == 0)
            {
                updatedRecord = CloneRecord(record, installedVersions, record.Version);
                return true;
            }

            // 裁剪不是简单删文件，而是先收敛状态，再把移除的版本挪到 .pruned 作为回滚保留。
            // 这样运行时视图和磁盘内容始终保持同一套“已生效版本”边界。
            var latestRetainedVersion = retainedVersions[^1];
            updatedRecord = CloneRecord(record, retainedVersions, latestRetainedVersion);

            if (!TrySave(updatedRecord, out errorMessage))
            {
                updatedRecord = null!;
                return false;
            }

            foreach (var removedVersion in removedVersions)
            {
                CleanupVersionArtifacts(record.ModuleId, removedVersion);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            updatedRecord = null!;
            errorMessage = $"Failed to prune installed plugin versions for '{record.ModuleId}': {ex.Message}";
            return false;
        }
    }

    private bool TryGetModulePackagesPath(string moduleId, out string path, out string? errorMessage)
    {
        errorMessage = null;
        path = string.Empty;

        if (!TryValidateModuleId(moduleId, out errorMessage))
        {
            return false;
        }

        var packagesRoot = Path.GetFullPath(Path.Combine(_installRoot, "packages"));
        var candidatePath = Path.GetFullPath(Path.Combine(packagesRoot, moduleId));
        if (!candidatePath.StartsWith(packagesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = $"Installed plugin package moduleId '{moduleId}' resolves to an unsafe packages path.";
            return false;
        }

        path = candidatePath;
        return true;
    }

    private IReadOnlyList<string> DetermineRetainedVersions(InstalledPluginRecord record, IReadOnlyList<string> installedVersions, int retainCount)
    {
        var retainedVersions = installedVersions
            .Skip(Math.Max(0, installedVersions.Count - retainCount))
            .ToList();

        foreach (var protectedVersion in GetProtectedVersions(record, installedVersions))
        {
            if (retainedVersions.Contains(protectedVersion, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            retainedVersions.Add(protectedVersion);
        }

        return installedVersions
            .Where(version => retainedVersions.Contains(version, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static IReadOnlyList<string> GetProtectedVersions(InstalledPluginRecord record, IReadOnlyList<string> installedVersions)
    {
        var protectedVersions = new List<string>();
        if (string.IsNullOrWhiteSpace(record.CurrentEnabledVersion))
        {
            return protectedVersions;
        }

        var currentEnabledVersion = installedVersions.FirstOrDefault(
            version => string.Equals(version, record.CurrentEnabledVersion, StringComparison.OrdinalIgnoreCase));
        if (currentEnabledVersion is null)
        {
            return protectedVersions;
        }

        protectedVersions.Add(currentEnabledVersion);

        var currentIndex = -1;
        for (var i = 0; i < installedVersions.Count; i++)
        {
            if (string.Equals(installedVersions[i], currentEnabledVersion, StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = i;
                break;
            }
        }
        if (currentIndex > 0)
        {
            protectedVersions.Add(installedVersions[currentIndex - 1]);
        }

        return protectedVersions;
    }

    private InstalledPluginRecord CloneRecord(InstalledPluginRecord record, IReadOnlyList<string> installedVersions, string currentVersion)
    {
        return new InstalledPluginRecord
        {
            ModuleId = record.ModuleId,
            Version = currentVersion,
            Status = record.Status,
            PackageRoot = GetPackageRoot(record.ModuleId, currentVersion),
            CurrentEnabledVersion = record.CurrentEnabledVersion,
            IsDisabled = record.IsDisabled,
            InstalledVersions = installedVersions.ToList()
        };
    }

    private void CleanupVersionArtifacts(string moduleId, string version)
    {
        try
        {
            // 被裁剪的版本不直接硬删，先进入归档区，给恢复旧版本留一条受控通道。
            ArchiveDirectoryIfExists(GetPackageRoot(moduleId, version), GetArchivedPackageRoot(moduleId, version));
        }
        catch
        {
        }

        try
        {
            ArchiveDirectoryIfExists(
                Path.Combine(_installRoot, "public", moduleId, version),
                GetArchivedPublicRoot(moduleId, version));
        }
        catch
        {
        }
    }

    private bool TryGetArchivedModulePath(string moduleId, out string path, out string? errorMessage)
    {
        errorMessage = null;
        path = string.Empty;

        if (!TryValidateModuleId(moduleId, out errorMessage))
        {
            return false;
        }

        var archivedRoot = Path.GetFullPath(Path.Combine(_installRoot, PrunedVersionsRootName, "packages"));
        var candidatePath = Path.GetFullPath(Path.Combine(archivedRoot, moduleId));
        if (!candidatePath.StartsWith(archivedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = $"Archived plugin package moduleId '{moduleId}' resolves to an unsafe path.";
            return false;
        }

        path = candidatePath;
        return true;
    }

    private string GetArchivedPackageRoot(string moduleId, string version)
    {
        return Path.GetFullPath(Path.Combine(_installRoot, PrunedVersionsRootName, "packages", moduleId, version));
    }

    private string GetArchivedPublicRoot(string moduleId, string version)
    {
        return Path.GetFullPath(Path.Combine(_installRoot, PrunedVersionsRootName, "public", moduleId, version));
    }

    private static void ArchiveDirectoryIfExists(string sourcePath, string archivePath)
    {
        if (!Directory.Exists(sourcePath))
        {
            return;
        }

        if (Directory.Exists(archivePath))
        {
            Directory.Delete(archivePath, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        Directory.Move(sourcePath, archivePath);
    }

    private static void RestoreDirectory(string archivePath, string destinationPath)
    {
        if (!Directory.Exists(archivePath))
        {
            return;
        }

        if (Directory.Exists(destinationPath))
        {
            Directory.Delete(destinationPath, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        Directory.Move(archivePath, destinationPath);
    }

    internal bool TryRestoreArchivedVersion(string moduleId, string version, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            // 恢复只允许从归档区回迁，不能从任意外部路径拼回安装树，避免把越界目录重新引入。
            var archivedPackageRoot = GetArchivedPackageRoot(moduleId, version);
            if (!Directory.Exists(archivedPackageRoot))
            {
                errorMessage = $"Archived plugin package version '{moduleId}@{version}' was not found.";
                return false;
            }

            RestoreDirectory(archivedPackageRoot, GetPackageRoot(moduleId, version));
            RestoreDirectory(GetArchivedPublicRoot(moduleId, version), Path.Combine(_installRoot, "public", moduleId, version));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            errorMessage = $"Failed to restore archived plugin package version '{moduleId}@{version}': {ex.Message}";
            return false;
        }
    }

    internal bool TryGetPreviousArchivedVersion(string moduleId, string currentEnabledVersion, out string? previousVersion, out string? errorMessage)
    {
        previousVersion = null;
        errorMessage = null;

        try
        {
            // 这里只找比当前启用版本更老的归档版本，用于回滚时选择最近的可用前一版。
            if (!TryGetArchivedModulePath(moduleId, out var archivedModulePath, out errorMessage))
            {
                return false;
            }

            if (!Directory.Exists(archivedModulePath))
            {
                return false;
            }

            if (!TryParseVersion(currentEnabledVersion, out var currentVersion))
            {
                errorMessage = $"Current enabled version '{currentEnabledVersion}' is not a valid version string.";
                return false;
            }

            Version? bestCandidate = null;
            string? bestCandidateText = null;

            foreach (var archivedVersionDirectory in Directory.GetDirectories(archivedModulePath, "*", SearchOption.TopDirectoryOnly))
            {
                var archivedVersionText = Path.GetFileName(archivedVersionDirectory);
                if (!TryParseVersion(archivedVersionText, out var archivedVersion))
                {
                    continue;
                }

                if (archivedVersion >= currentVersion)
                {
                    continue;
                }

                if (bestCandidate is null || archivedVersion > bestCandidate)
                {
                    bestCandidate = archivedVersion;
                    bestCandidateText = archivedVersionText;
                }
            }

            previousVersion = bestCandidateText;
            return previousVersion is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            errorMessage = $"Failed to inspect archived plugin versions for '{moduleId}': {ex.Message}";
            return false;
        }
    }

    private static bool TryParseVersion(string versionText, out Version version)
    {
        version = new Version(0, 0);
        if (!Version.TryParse(versionText, out var parsedVersion))
        {
            return false;
        }

        version = parsedVersion;
        return true;
    }

    private bool TryGetStatePath(string moduleId, out string path, out string? errorMessage)
    {
        errorMessage = null;
        path = string.Empty;

        if (!TryValidateModuleId(moduleId, out errorMessage))
        {
            return false;
        }

        var candidatePath = Path.GetFullPath(Path.Combine(_stateRoot, $"{moduleId}.json"));
        if (!candidatePath.StartsWith(_stateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = $"Installed plugin state moduleId '{moduleId}' resolves to an unsafe state path.";
            return false;
        }

        path = candidatePath;
        return true;
    }

    private void ValidateRecord(InstalledPluginRecord? record, string requestedModuleId)
    {
        if (record is null)
        {
            throw new JsonException("Installed plugin state is corrupt: payload deserialized to null.");
        }

        // 状态文件一旦无法自证一致，就按损坏处理，而不是尝试猜测修复。
        if (string.IsNullOrWhiteSpace(record.ModuleId))
        {
            throw new JsonException("Installed plugin state is corrupt: ModuleId must not be empty.");
        }

        if (!string.Equals(record.ModuleId, requestedModuleId, StringComparison.Ordinal))
        {
            throw new JsonException($"Installed plugin state is corrupt: ModuleId '{record.ModuleId}' does not match requested module '{requestedModuleId}'.");
        }

        if (string.IsNullOrWhiteSpace(record.Version))
        {
            throw new JsonException("Installed plugin state is corrupt: Version must not be empty.");
        }

        ValidateSafePathSegment(record.Version, "Version");

        if (!string.IsNullOrWhiteSpace(record.CurrentEnabledVersion))
        {
            var enabledVersion = record.CurrentEnabledVersion.Trim();
            ValidateSafePathSegment(enabledVersion, "CurrentEnabledVersion");

            if (!record.InstalledVersions.Contains(enabledVersion, StringComparer.OrdinalIgnoreCase))
            {
                throw new JsonException("Installed plugin state is corrupt: CurrentEnabledVersion must exist in InstalledVersions.");
            }

            if (record.IsDisabled)
            {
                throw new JsonException("Installed plugin state is corrupt: IsDisabled cannot be true when CurrentEnabledVersion is set.");
            }
        }

        if (string.IsNullOrWhiteSpace(record.Status))
        {
            throw new JsonException("Installed plugin state is corrupt: Status must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(record.PackageRoot))
        {
            throw new JsonException("Installed plugin state is corrupt: PackageRoot must not be empty.");
        }

        ValidatePackageRoot(record.PackageRoot, record.ModuleId, record.Version);

        if (record.InstalledVersions is null)
        {
            throw new JsonException("Installed plugin state is corrupt: InstalledVersions must not be null.");
        }

        if (record.InstalledVersions.Any(version => string.IsNullOrWhiteSpace(version)))
        {
            throw new JsonException("Installed plugin state is corrupt: InstalledVersions must not contain empty values.");
        }

        foreach (var installedVersion in record.InstalledVersions)
        {
            ValidateSafePathSegment(installedVersion, "InstalledVersions");
        }
    }

    private static void ValidateSafePathSegment(string value, string fieldName)
    {
        if (value is "." or ".." || value.Contains("..", StringComparison.Ordinal) || value.IndexOfAny(InvalidPathSegmentChars) >= 0)
        {
            throw new JsonException($"Installed plugin state is corrupt: {fieldName} contains unsafe path characters.");
        }
    }

    private static string ResolveContainedFilePath(string rootPath, string? relativePath, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new JsonException($"Plugin manifest {propertyName} must not be empty.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new JsonException($"Plugin manifest {propertyName} resolves to an unsafe path.");
        }

        // 状态和清单中的相对路径都必须收口在包根内；如果文件本身缺失，则直接视为坏包/坏状态。
        var fullRootPath = Path.GetFullPath(rootPath);
        var fullCandidatePath = Path.GetFullPath(Path.Combine(fullRootPath, relativePath));
        if (!fullCandidatePath.StartsWith(fullRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullCandidatePath, fullRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException($"Plugin manifest {propertyName} resolves to an unsafe path.");
        }

        if (!File.Exists(fullCandidatePath))
        {
            throw new IOException($"Required file not found for {propertyName}: {relativePath}");
        }

        return fullCandidatePath;
    }

    private void ValidatePackageRoot(string packageRoot, string moduleId, string version)
    {
        if (!Path.IsPathRooted(packageRoot))
        {
            throw new JsonException("Installed plugin state is corrupt: PackageRoot must be an absolute path.");
        }

        string fullPackageRoot;
        try
        {
            fullPackageRoot = Path.GetFullPath(packageRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new JsonException("Installed plugin state is corrupt: PackageRoot is invalid.", ex);
        }

        // PackageRoot 必须精确指向 installRoot/packages/{moduleId}/{version}，否则说明状态和磁盘已经失配。
        var packagesRoot = Path.GetFullPath(Path.Combine(_installRoot, "packages"));
        if (!fullPackageRoot.StartsWith(packagesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException("Installed plugin state is corrupt: PackageRoot resolves outside the install packages root.");
        }

        var expectedPackageRoot = Path.GetFullPath(Path.Combine(packagesRoot, moduleId, version));
        if (!string.Equals(fullPackageRoot, expectedPackageRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException("Installed plugin state is corrupt: PackageRoot does not match the record's ModuleId and Version.");
        }
    }

    private static bool TryValidateModuleId(string moduleId, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(moduleId))
        {
            errorMessage = "Installed plugin state moduleId must not be empty.";
            return false;
        }

        if (moduleId is "." or ".." || moduleId.Contains("..", StringComparison.Ordinal) || moduleId.IndexOfAny(InvalidPathSegmentChars) >= 0)
        {
            errorMessage = $"Installed plugin state moduleId '{moduleId}' contains unsafe path characters.";
            return false;
        }

        return true;
    }
}

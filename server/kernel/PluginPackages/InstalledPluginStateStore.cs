using System.Text;
using System.Text.Json;

namespace ICP.Platform.Kernel.PluginPackages;

public class InstalledPluginStateStore
{
    private static readonly char[] InvalidPathSegmentChars = Path.GetInvalidFileNameChars()
        .Concat(Path.GetInvalidPathChars())
        .Append(Path.DirectorySeparatorChar)
        .Append(Path.AltDirectorySeparatorChar)
        .Distinct()
        .ToArray();

    private readonly string _stateRoot;
    private readonly string _installRoot;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };
    private readonly Action<string, string, string?> _replaceFile;
    private readonly Action<string, string> _moveFile;

    public InstalledPluginStateStore(string installRoot)
        : this(installRoot, File.Replace, File.Move)
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

    /// <summary>
    /// 加载所有已安装模块的记录（包括已禁用的模块）。
    /// </summary>
    public IReadOnlyList<InstalledPluginRecord> LoadAll()
    {
        Directory.CreateDirectory(_stateRoot);
        var records = new List<InstalledPluginRecord>();

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

            if (!string.Equals(record.Status, "Installed", StringComparison.OrdinalIgnoreCase))
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

            if (!string.Equals(record.Status, "Installed", StringComparison.OrdinalIgnoreCase))
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

        enabledRecord = new InstalledPluginRecord
        {
            ModuleId = record.ModuleId,
            Version = enabledVersion,
            Status = record.Status,
            PackageRoot = GetPackageRoot(record.ModuleId, enabledVersion),
            CurrentEnabledVersion = enabledVersion,
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

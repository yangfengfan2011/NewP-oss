using ICP.Platform.Kernel.PluginPackages;

namespace ICP.Platform.PluginPlatform.StandardData;

public sealed record StandardDataPackageInstallResult(
    bool IsSuccess,
    string? ModuleId,
    string? Version,
    string Message);

/// <summary>
/// 把已验证的 standard-data 包原子安装到版本目录；安装只改变 installed inventory，
/// 不改变 desired、active、LKG，也不回写 legacy CurrentEnabledVersion。
/// </summary>
public sealed class StandardDataPackageInstaller
{
    private static readonly HashSet<string> TransportFiles =
        new(["package.zip", "upload-record.json"], StringComparer.OrdinalIgnoreCase);
    private readonly string _installRoot;
    private readonly InstalledPluginStateStore _stateStore;
    private readonly StandardDataPackageInspector _inspector;

    public StandardDataPackageInstaller(
        string installRoot,
        InstalledPluginStateStore stateStore,
        StandardDataPackageInspector inspector)
    {
        _installRoot = Path.GetFullPath(installRoot);
        _stateStore = stateStore;
        _inspector = inspector;
    }

    public StandardDataPackageInstallResult Install(string sourcePackageRoot)
    {
        var inspection = _inspector.Inspect(sourcePackageRoot);
        if (!inspection.IsSuccess)
        {
            var diagnostic = inspection.Diagnostics[0];
            return new(false, diagnostic.ModuleId, diagnostic.Version, $"{diagnostic.Code}: {diagnostic.Message}");
        }

        var manifest = inspection.Manifest!;
        if (!IsSafeSegment(manifest.ModuleId) || !IsSafeSegment(manifest.Version))
        {
            return new(false, manifest.ModuleId, manifest.Version, "插件标识或版本不能映射到安全安装路径。");
        }

        if (!_stateStore.TryLoad(manifest.ModuleId, out var previousRecord, out var loadError))
        {
            return new(false, manifest.ModuleId, manifest.Version, loadError ?? "无法读取插件安装状态。");
        }

        var moduleRoot = Path.Combine(_installRoot, "packages", manifest.ModuleId);
        var destinationRoot = Path.Combine(moduleRoot, manifest.Version);
        var stagingRoot = Path.Combine(moduleRoot, $".{manifest.Version}.install-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(moduleRoot, $".{manifest.Version}.backup-{Guid.NewGuid():N}");
        var destinationBackedUp = false;

        try
        {
            Directory.CreateDirectory(moduleRoot);
            CopyPackage(sourcePackageRoot, stagingRoot);
            if (Directory.Exists(destinationRoot))
            {
                Directory.Move(destinationRoot, backupRoot);
                destinationBackedUp = true;
            }

            Directory.Move(stagingRoot, destinationRoot);
            var installedVersions = (previousRecord?.InstalledVersions ?? [])
                .Where(static version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!installedVersions.Contains(manifest.Version, StringComparer.OrdinalIgnoreCase))
            {
                installedVersions.Add(manifest.Version);
            }

            var nextRecord = new InstalledPluginRecord
            {
                ModuleId = manifest.ModuleId,
                Version = manifest.Version,
                Status = InstalledPluginLifecycleState.Installed,
                PackageRoot = destinationRoot,
                CurrentEnabledVersion = previousRecord?.CurrentEnabledVersion,
                IsDisabled = previousRecord?.IsDisabled ?? true,
                InstalledVersions = installedVersions
            };
            if (!_stateStore.TrySave(nextRecord, out var saveError))
            {
                RestoreDestination(destinationRoot, backupRoot, destinationBackedUp);
                return new(false, manifest.ModuleId, manifest.Version, saveError ?? "无法保存插件安装状态。");
            }

            if (destinationBackedUp && Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot, recursive: true);
            }

            return new(true, manifest.ModuleId, manifest.Version, "Installed");
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or ArgumentException
                                           or NotSupportedException)
        {
            RestoreDestination(destinationRoot, backupRoot, destinationBackedUp);
            return new(false, manifest.ModuleId, manifest.Version, exception.Message);
        }
        finally
        {
            TryDelete(stagingRoot);
            TryDelete(backupRoot);
        }
    }

    private static bool IsSafeSegment(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !Path.IsPathRooted(value)
        && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !value.Contains(Path.DirectorySeparatorChar)
        && !value.Contains(Path.AltDirectorySeparatorChar)
        && value is not "." and not "..";

    private static void CopyPackage(string sourceRoot, string destinationRoot)
    {
        var resolvedSourceRoot = Path.GetFullPath(sourceRoot);
        Directory.CreateDirectory(destinationRoot);
        foreach (var file in Directory.GetFiles(resolvedSourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(resolvedSourceRoot, file);
            if (!relativePath.Contains(Path.DirectorySeparatorChar)
                && !relativePath.Contains(Path.AltDirectorySeparatorChar)
                && TransportFiles.Contains(relativePath))
            {
                continue;
            }

            var destinationPath = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: false);
        }
    }

    private static void RestoreDestination(string destinationRoot, string backupRoot, bool destinationBackedUp)
    {
        TryDelete(destinationRoot);
        if (destinationBackedUp && Directory.Exists(backupRoot))
        {
            Directory.Move(backupRoot, destinationRoot);
        }
    }

    private static void TryDelete(string path)
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
}

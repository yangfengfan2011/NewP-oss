using ICP.Platform.Kernel.PluginPackages;

namespace ICP.Platform.HostApi.PluginOperations;

/// <summary>
/// 插件生命周期运维服务：封装 install / enable / disable / uninstall / rollback 操作。
/// install 仅从已验证的 upload 区域读取，不自动启用版本。
/// enable / disable / uninstall / rollback 委托 UnifiedPluginPackageOperations 并刷新 RuntimeViewMaterializer。
/// </summary>
public class PluginOperationsLifecycleService
{
    private readonly InstalledPluginStateStore _stateStore;
    private readonly UnifiedPluginPackageInstaller _installer;
    private readonly UnifiedPluginPackageOperations _operations;
    private readonly RuntimeViewMaterializer _materializer;
    private readonly PluginUploadService _uploadService;
    private readonly string _installRoot;

    public PluginOperationsLifecycleService(
        InstalledPluginStateStore stateStore,
        UnifiedPluginPackageInstaller installer,
        UnifiedPluginPackageOperations operations,
        RuntimeViewMaterializer materializer,
        PluginUploadService uploadService,
        string installRoot)
    {
        _stateStore = stateStore;
        _installer = installer;
        _operations = operations;
        _materializer = materializer;
        _uploadService = uploadService;
        _installRoot = Path.GetFullPath(installRoot);
    }

    /// <summary>
    /// 从已验证的 upload 区域安装插件包。
    /// 仅当 uploadId 对应的记录 IsValid == true 时才执行安装。
    /// 安装后不自动启用该版本。
    /// </summary>
    public PluginOperationResultDto Install(string uploadId)
    {
        var uploadInfo = _uploadService.GetUploadInfo(uploadId);
        if (uploadInfo is null)
        {
            return PluginOperationResultDto.Failed("找不到指定的上传记录，请先上传并验证插件包。");
        }

        if (!uploadInfo.IsValid)
        {
            return PluginOperationResultDto.Failed("该上传包未通过验证，无法安装。");
        }

        var uploadPackageDir = Path.Combine(_installRoot, "uploads", uploadId);
        var installResult = _installer.Install(uploadPackageDir, _installRoot);

        if (installResult.Status != "Installed")
        {
            return PluginOperationResultDto.Failed($"安装失败：{installResult.ErrorMessage}");
        }

        return PluginOperationResultDto.Success(
            installResult.ModuleId,
            installResult.Version,
            "Installed",
            GetSuccessMessage(installResult));
    }

    /// <summary>
    /// 启用指定模块的指定版本，并刷新 runtime/public 投影。
    /// </summary>
    public PluginOperationResultDto Enable(string moduleId, string version)
    {
        return ExecuteWithMaterializerRecovery(
            moduleId,
            backupModuleFiles: true,
            () => _operations.Enable(moduleId, version),
            result => PluginOperationResultDto.Success(
                result.ModuleId,
                result.Version,
                "Enabled",
                GetSuccessMessage(result)));
    }

    /// <summary>
    /// 禁用指定模块，并刷新 runtime/public 投影。
    /// </summary>
    public PluginOperationResultDto Disable(string moduleId)
    {
        return ExecuteWithMaterializerRecovery(
            moduleId,
            backupModuleFiles: false,
            () => _operations.Disable(moduleId),
            result => PluginOperationResultDto.Success(
                result.ModuleId,
                null,
                "Disabled",
                GetSuccessMessage(result)));
    }

    /// <summary>
    /// 卸载指定模块的所有已安装版本，并刷新 runtime/public 投影。
    /// </summary>
    public PluginOperationResultDto Uninstall(string moduleId)
    {
        return ExecuteWithMaterializerRecovery(
            moduleId,
            backupModuleFiles: true,
            () => _operations.Uninstall(moduleId),
            result => PluginOperationResultDto.Success(
                result.ModuleId,
                null,
                "Uninstalled",
                GetSuccessMessage(result)));
    }

    /// <summary>
    /// 回滚指定模块到上一个已安装版本，并刷新 runtime/public 投影。
    /// </summary>
    public PluginOperationResultDto Rollback(string moduleId)
    {
        if (!_stateStore.TryLoad(moduleId, out var previousRecord, out var loadError))
        {
            return PluginOperationResultDto.Failed(loadError ?? $"Failed to load plugin state for '{moduleId}'.");
        }

        var result = _operations.Rollback(moduleId);
        if (result.Status == "Failed")
        {
            return PluginOperationResultDto.Failed(result.ErrorMessage);
        }

        try
        {
            RefreshMaterializer();
            return PluginOperationResultDto.Success(result.ModuleId, result.Version, "Enabled");
        }
        catch (Exception ex)
        {
            if (previousRecord is not null && !_stateStore.TrySave(previousRecord, out var restoreStateError))
            {
                return PluginOperationResultDto.Failed(
                    $"{ex.Message} Failed to restore previous plugin state: {restoreStateError}");
            }

            try
            {
                RefreshMaterializer();
            }
            catch (Exception restoreProjectionEx)
            {
                return PluginOperationResultDto.Failed(
                    $"{ex.Message} Failed to restore previous runtime projection: {restoreProjectionEx.Message}");
            }

            return PluginOperationResultDto.Failed($"{ex.Message} Plugin state was restored to the previous version.");
        }
    }

    private void RefreshMaterializer()
    {
        _materializer.Materialize(_installRoot);
    }

    private PluginOperationResultDto ExecuteWithMaterializerRecovery(
        string moduleId,
        bool backupModuleFiles,
        Func<UnifiedPluginLifecycleResult> operation,
        Func<UnifiedPluginLifecycleResult, PluginOperationResultDto> buildSuccessResult)
    {
        if (!_stateStore.TryLoad(moduleId, out var previousRecord, out var loadError))
        {
            return PluginOperationResultDto.Failed(loadError ?? $"Failed to load plugin state for '{moduleId}'.");
        }

        string? backupError = null;
        using var fileBackup = backupModuleFiles
            ? ModuleFilesystemBackup.Create(_installRoot, moduleId, out backupError)
            : ModuleFilesystemBackup.Empty();

        if (!string.IsNullOrWhiteSpace(backupError))
        {
            return PluginOperationResultDto.Failed(backupError!);
        }

        var result = operation();
        if (result.Status == "Failed")
        {
            return PluginOperationResultDto.Failed(result.ErrorMessage);
        }

        try
        {
            RefreshMaterializer();
            return buildSuccessResult(result);
        }
        catch (Exception ex)
        {
            return RecoverFromMaterializerFailure(moduleId, previousRecord, fileBackup, ex);
        }
    }

    private PluginOperationResultDto RecoverFromMaterializerFailure(
        string moduleId,
        InstalledPluginRecord? previousRecord,
        ModuleFilesystemBackup fileBackup,
        Exception materializeError)
    {
        if (!RestorePreviousState(moduleId, previousRecord, out var restoreStateError))
        {
            return PluginOperationResultDto.Failed(
                $"{materializeError.Message} Failed to restore previous plugin state: {restoreStateError}");
        }

        if (!fileBackup.Restore(out var restoreFileError))
        {
            return PluginOperationResultDto.Failed(
                $"{materializeError.Message} Failed to restore previous plugin files: {restoreFileError}");
        }

        try
        {
            RefreshMaterializer();
        }
        catch (Exception restoreProjectionEx)
        {
            return PluginOperationResultDto.Failed(
                $"{materializeError.Message} Failed to restore previous runtime projection: {restoreProjectionEx.Message}");
        }

        return PluginOperationResultDto.Failed($"{materializeError.Message} Plugin state was restored to the previous version.");
    }

    private bool RestorePreviousState(string moduleId, InstalledPluginRecord? previousRecord, out string? errorMessage)
    {
        if (previousRecord is null)
        {
            return _stateStore.TryDeleteModule(moduleId, out errorMessage);
        }

        return _stateStore.TrySave(CloneRecord(previousRecord), out errorMessage);
    }

    private static InstalledPluginRecord CloneRecord(InstalledPluginRecord record)
    {
        return new InstalledPluginRecord
        {
            ModuleId = record.ModuleId,
            Version = record.Version,
            Status = record.Status,
            PackageRoot = record.PackageRoot,
            CurrentEnabledVersion = record.CurrentEnabledVersion,
            IsDisabled = record.IsDisabled,
            InstalledVersions = [.. record.InstalledVersions]
        };
    }

    private static string GetSuccessMessage(object result)
    {
        return GetResultString(result, "Message", "WarningMessage");
    }

    private static string GetResultString(object result, params string[] propertyNames)
    {
        var type = result.GetType();

        foreach (var propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName);
            if (property?.PropertyType == typeof(string))
            {
                return (string?)property.GetValue(result) ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private sealed class ModuleFilesystemBackup : IDisposable
    {
        private static readonly string[] RelativePaths =
        [
            "packages",
            Path.Combine(".pruned", "packages"),
            Path.Combine(".pruned", "public")
        ];

        private readonly string _installRoot;
        private readonly string _backupRoot;
        private readonly IReadOnlyList<BackupEntry> _entries;

        private ModuleFilesystemBackup(string installRoot, string backupRoot, IReadOnlyList<BackupEntry> entries)
        {
            _installRoot = installRoot;
            _backupRoot = backupRoot;
            _entries = entries;
        }

        public static ModuleFilesystemBackup Create(string installRoot, string moduleId, out string? errorMessage)
        {
            errorMessage = null;
            var backupRoot = Path.Combine(Path.GetTempPath(), "plugin_ops_backup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupRoot);

            try
            {
                var entries = new List<BackupEntry>();

                foreach (var relativeRoot in RelativePaths)
                {
                    var sourcePath = Path.Combine(installRoot, relativeRoot, moduleId);
                    var backupPath = Path.Combine(backupRoot, relativeRoot, moduleId);
                    var exists = Directory.Exists(sourcePath);
                    if (exists)
                    {
                        CopyDirectory(sourcePath, backupPath);
                    }

                    entries.Add(new BackupEntry(sourcePath, backupPath, exists));
                }

                return new ModuleFilesystemBackup(installRoot, backupRoot, entries);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                errorMessage = $"Failed to snapshot plugin files for '{moduleId}': {ex.Message}";
                try
                {
                    if (Directory.Exists(backupRoot))
                    {
                        Directory.Delete(backupRoot, recursive: true);
                    }
                }
                catch
                {
                }

                return Empty();
            }
        }

        public static ModuleFilesystemBackup Empty()
        {
            return new ModuleFilesystemBackup(
                string.Empty,
                string.Empty,
                Array.Empty<BackupEntry>());
        }

        public bool Restore(out string? errorMessage)
        {
            errorMessage = null;

            try
            {
                foreach (var entry in _entries)
                {
                    if (Directory.Exists(entry.DestinationPath))
                    {
                        Directory.Delete(entry.DestinationPath, recursive: true);
                    }

                    if (!entry.Existed)
                    {
                        continue;
                    }

                    CopyDirectory(entry.BackupPath, entry.DestinationPath);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public void Dispose()
        {
            if (string.IsNullOrWhiteSpace(_backupRoot))
            {
                return;
            }

            try
            {
                if (Directory.Exists(_backupRoot))
                {
                    Directory.Delete(_backupRoot, recursive: true);
                }
            }
            catch
            {
            }
        }

        private static void CopyDirectory(string sourcePath, string destinationPath)
        {
            var sourceDirectory = new DirectoryInfo(sourcePath);
            Directory.CreateDirectory(destinationPath);

            foreach (var file in sourceDirectory.GetFiles())
            {
                file.CopyTo(Path.Combine(destinationPath, file.Name), overwrite: true);
            }

            foreach (var directory in sourceDirectory.GetDirectories())
            {
                CopyDirectory(directory.FullName, Path.Combine(destinationPath, directory.Name));
            }
        }

        private sealed record BackupEntry(string DestinationPath, string BackupPath, bool Existed);
    }
}

/// <summary>
/// 插件操作结果 DTO：可复用于后续 HTTP 层返回。
/// </summary>
public class PluginOperationResultDto
{
    /// <summary>模块标识。</summary>
    public string ModuleId { get; init; } = string.Empty;

    /// <summary>涉及的版本号；禁用操作时为 null。</summary>
    public string? Version { get; init; }

    /// <summary>操作状态：Installed / Enabled / Disabled / Uninstalled / Failed。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>操作消息，成功时可携带 warning/message，失败时包含错误描述。</summary>
    public string Message { get; init; } = string.Empty;

    public static PluginOperationResultDto Success(string moduleId, string? version, string status, string message = "") =>
        new() { ModuleId = moduleId, Version = version, Status = status, Message = message };

    public static PluginOperationResultDto Failed(string message) =>
        new() { Status = "Failed", Message = message };
}

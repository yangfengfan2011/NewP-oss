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
        var result = _operations.Enable(moduleId, version);
        if (result.Status == "Failed")
        {
            return PluginOperationResultDto.Failed(result.ErrorMessage);
        }

        RefreshMaterializer();
        return PluginOperationResultDto.Success(result.ModuleId, result.Version, "Enabled");
    }

    /// <summary>
    /// 禁用指定模块，并刷新 runtime/public 投影。
    /// </summary>
    public PluginOperationResultDto Disable(string moduleId)
    {
        var result = _operations.Disable(moduleId);
        if (result.Status == "Failed")
        {
            return PluginOperationResultDto.Failed(result.ErrorMessage);
        }

        RefreshMaterializer();
        return PluginOperationResultDto.Success(result.ModuleId, null, "Disabled");
    }

    /// <summary>
    /// 卸载指定模块的所有已安装版本，并刷新 runtime/public 投影。
    /// </summary>
    public PluginOperationResultDto Uninstall(string moduleId)
    {
        var result = _operations.Uninstall(moduleId);
        if (result.Status == "Failed")
        {
            return PluginOperationResultDto.Failed(result.ErrorMessage);
        }

        RefreshMaterializer();
        return PluginOperationResultDto.Success(
            result.ModuleId,
            null,
            "Uninstalled",
            GetSuccessMessage(result));
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

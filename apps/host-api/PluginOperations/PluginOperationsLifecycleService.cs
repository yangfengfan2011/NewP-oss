using System.Text.Json;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.PluginPlatform.StandardData;

namespace ICP.Platform.HostApi.PluginOperations;

/// <summary>
/// 插件生命周期运维服务。安装只增加已安装清单；enable / disable / rollback 只更新 desired，
/// 活动快照只能由下一次 Host 冷启动闸门提交。
/// </summary>
public class PluginOperationsLifecycleService
{
    private readonly InstalledPluginStateStore _stateStore;
    private readonly UnifiedPluginPackageInstaller _legacyInstaller;
    private readonly StandardDataPackageInstaller _standardDataInstaller;
    private readonly PluginLifecycleManager _lifecycleManager;
    private readonly PluginUploadService _uploadService;
    private readonly string _installRoot;

    // 写入期望状态与回滚选择复用同一确认读取，不能创建无策略的旁路管理器。
    public PluginOperationsLifecycleService(
        InstalledPluginStateStore stateStore,
        UnifiedPluginPackageInstaller legacyInstaller,
        StandardDataPackageInstaller standardDataInstaller,
        PluginLifecycleManager lifecycleManager,
        PluginUploadService uploadService,
        string installRoot)
    {
        _stateStore = stateStore;
        _legacyInstaller = legacyInstaller;
        _standardDataInstaller = standardDataInstaller;
        _lifecycleManager = lifecycleManager;
        _uploadService = uploadService;
        _installRoot = Path.GetFullPath(installRoot);
    }

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

        var uploadPackageRoot = Path.Combine(_installRoot, "uploads", uploadId);
        if (IsStandardDataPackage(uploadPackageRoot))
        {
            var standardResult = _standardDataInstaller.Install(uploadPackageRoot);
            return standardResult.IsSuccess
                ? PluginOperationResultDto.Success(
                    standardResult.ModuleId!,
                    standardResult.Version,
                    "Installed",
                    "安装完成；当前活动快照未改变。")
                : PluginOperationResultDto.Failed($"安装失败：{standardResult.Message}");
        }

        var legacyResult = _legacyInstaller.Install(uploadPackageRoot, _installRoot);
        return legacyResult.Status == "Installed"
            ? PluginOperationResultDto.Success(
                legacyResult.ModuleId,
                legacyResult.Version,
                "Installed",
                "安装完成；当前活动快照未改变。")
            : PluginOperationResultDto.Failed($"安装失败：{GetResultString(legacyResult, "ErrorMessage")}");
    }

    public PluginOperationResultDto Enable(string moduleId, string version) =>
        MapCommand(_lifecycleManager.RequestEnable(Inventory(), moduleId, version));

    public PluginOperationResultDto Disable(string moduleId) =>
        MapCommand(_lifecycleManager.RequestDisable(Inventory(), moduleId));

    public PluginOperationResultDto Rollback(string moduleId) =>
        MapCommand(_lifecycleManager.RequestRollback(Inventory(), moduleId));

    public PluginOperationResultDto Uninstall(string moduleId)
    {
        var inventory = Inventory();
        var item = inventory.FirstOrDefault(candidate =>
            string.Equals(candidate.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return PluginOperationResultDto.Failed($"Plugin '{moduleId}' is not installed.");
        }

        var state = _lifecycleManager.Resolve(inventory);
        var desired = state.DesiredModules.FirstOrDefault(candidate =>
            string.Equals(candidate.ModuleId, item.ModuleId, StringComparison.OrdinalIgnoreCase));
        var isActive = state.ActiveSnapshot?.Modules.Any(candidate =>
            string.Equals(candidate.ModuleId, item.ModuleId, StringComparison.OrdinalIgnoreCase)) is true;
        if (desired?.Enabled is not false || isActive)
        {
            return PluginOperationResultDto.Failed(
                "卸载前必须先禁用模块并完成一次成功重启；数据与迁移账本不会被删除。");
        }

        return _stateStore.TryDeleteModule(item.ModuleId, out var deleteError)
            ? PluginOperationResultDto.Success(
                item.ModuleId,
                null,
                "Uninstalled",
                "插件包已卸载；业务数据与迁移账本已保留。")
            : PluginOperationResultDto.Failed(deleteError ?? "插件包卸载失败。");
    }

    private IReadOnlyList<InstalledPluginInventoryItem> Inventory() =>
        InstalledPluginInventoryAdapter.Create(_stateStore);

    private static PluginOperationResultDto MapCommand(PluginLifecycleCommandResult result) =>
        result.IsSuccess
            ? PluginOperationResultDto.Success(
                result.ModuleId,
                result.Version,
                result.Status,
                result.Message)
            : PluginOperationResultDto.Failed(result.Message);

    private static string GetResultString(object result, string propertyName)
    {
        var property = result.GetType().GetProperty(propertyName);
        return property?.GetValue(result)?.ToString() ?? string.Empty;
    }

    private static bool IsStandardDataPackage(string packageRoot)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(packageRoot, "plugin.json")));
            return document.RootElement.TryGetProperty("profile", out var profile)
                && string.Equals(
                    profile.GetString(),
                    PlatformApiReleaseIdentity.StandardDataProfile,
                    StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return false;
        }
    }

}

/// <summary>
/// 插件操作结果 DTO：命令只表达 desired 已更新，不能把 PendingRestart 误报为活动成功。
/// </summary>
public class PluginOperationResultDto
{
    public string ModuleId { get; init; } = string.Empty;

    public string? Version { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public static PluginOperationResultDto Success(string moduleId, string? version, string status, string message = "") =>
        new() { ModuleId = moduleId, Version = version, Status = status, Message = message };

    public static PluginOperationResultDto Failed(string message) =>
        new() { Status = "Failed", Message = message };
}

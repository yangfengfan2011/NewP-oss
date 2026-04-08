namespace ICP.Platform.HostApi.PluginOperations;

/// <summary>
/// 插件模块状态 DTO：供服务层返回、控制器层消费。
/// </summary>
public class PluginModuleStatusDto
{
    public string ModuleId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public IReadOnlyList<string> InstalledVersions { get; init; } = Array.Empty<string>();
    public string? CurrentEnabledVersion { get; init; }
    public bool CanDisable { get; init; }
    public bool CanRollback { get; init; }
    public bool CanEnable { get; init; }
    public string? LastOperation { get; init; }
    public string? LastError { get; init; }
}

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
    public string? DesiredVersion { get; init; }
    public bool DesiredEnabled { get; init; }
    public string? ActiveVersion { get; init; }
    public string? LastKnownGoodVersion { get; init; }
    public bool RestartRequired { get; init; }
    /// <summary>一期只读兼容字段，值等于 ActiveVersion；后续删除。</summary>
    public string? CurrentEnabledVersion { get; init; }
    public bool CanDisable { get; init; }
    public bool CanRollback { get; init; }
    public bool CanEnable { get; init; }
    public bool CanUninstall { get; init; }
    public string? LastOperation { get; init; }
    public string? LastError { get; init; }
}

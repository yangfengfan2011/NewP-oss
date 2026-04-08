namespace ICP.Platform.HostApi.PluginOperations;

/// <summary>
/// 启用插件请求体：指定要启用的版本号。
/// </summary>
public class PluginEnableRequest
{
    public string Version { get; set; } = string.Empty;
}

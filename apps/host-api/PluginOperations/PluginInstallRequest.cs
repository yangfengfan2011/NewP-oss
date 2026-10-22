namespace ICP.Platform.HostApi.PluginOperations;

/// <summary>
/// 安装插件请求体：需要已验证的 uploadId。
/// </summary>
public class PluginInstallRequest
{
    public string UploadId { get; set; } = string.Empty;
}

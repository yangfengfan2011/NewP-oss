namespace ICP.Platform.HostApi.PluginOperations;

/// <summary>
/// 上传验证结果 DTO：描述一次 zip 包上传 + 扫描验证的结果。
/// 供控制器返回、前端展示、以及后续 install-by-uploadId 流程使用。
/// </summary>
public class PluginUploadVerifyResultDto
{
    /// <summary>本次上传的唯一标识，用于后续按 uploadId 安装。</summary>
    public string UploadId { get; init; } = string.Empty;

    /// <summary>从 plugin.json 中解析出的模块标识。验证失败时为空字符串。</summary>
    public string ModuleId { get; init; } = string.Empty;

    /// <summary>从 plugin.json 中解析出的显示名称。验证失败时为空字符串。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>从 plugin.json 中解析出的版本号。验证失败时为空字符串。</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>zip 包是否通过了 UnifiedPluginPackageScanner 的全部校验。</summary>
    public bool IsValid { get; init; }

    /// <summary>校验过程中产生的错误信息列表；通过时为空列表。</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

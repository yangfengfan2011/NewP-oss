namespace ICP.Platform.Plugin.Contracts;

/// <summary>
/// Platform API 构建、打包和兼容性检查返回的稳定诊断。
/// </summary>
public sealed record PlatformApiDiagnostic(
    string Code,
    string Stage,
    string Message,
    string? ModuleId = null,
    string? Version = null,
    string? Profile = null,
    string? SourcePath = null,
    string? JsonPointer = null,
    string? BuiltWith = null,
    string? SupportedRelease = null,
    string? ResolvedPackageId = null,
    string? ResolvedPackageVersion = null,
    string? ResolvedPackageHash = null,
    string? Replacement = null);

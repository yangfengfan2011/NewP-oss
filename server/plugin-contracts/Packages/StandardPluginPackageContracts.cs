namespace ICP.Platform.Plugin.Contracts;

/// <summary>
/// standard-data.v1 `.npp` 根 transport；不包含任意运行时入口。
/// </summary>
public sealed record StandardPluginPackageManifest(
    int SchemaVersion,
    string Profile,
    string ModuleId,
    string DisplayName,
    string Version,
    string? Description,
    PlatformApiAttestation Compatibility,
    StandardPluginPackageArtifacts Artifacts,
    StandardPersistenceBlueprint Persistence,
    StandardPluginPackageChecksums Checksums);

/// <summary>
/// `.npp` 内由同一 Blueprint 派生的声明式产物。
/// </summary>
public sealed record StandardPluginPackageArtifacts(
    string Blueprint,
    string StandardProjection,
    string Explain);

/// <summary>
/// `.npp` 文件完整性清单位置。
/// </summary>
public sealed record StandardPluginPackageChecksums(string Sha256Manifest);

/// <summary>
/// 除 checksum manifest 本身之外的完整包文件 SHA-256。
/// </summary>
public sealed record StandardPluginChecksumManifest(
    string SchemaVersion,
    IReadOnlyDictionary<string, string> Files);

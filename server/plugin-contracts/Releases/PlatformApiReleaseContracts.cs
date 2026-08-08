namespace ICP.Platform.Plugin.Contracts;

/// <summary>
/// release manifest 中单个发布物的内容证明。
/// </summary>
public sealed record PlatformApiArtifactAttestation(
    string Kind,
    string PackageId,
    string Version,
    string ReleaseDigest,
    string Path,
    string Sha256);

/// <summary>
/// 四个锁步发布物与 canonical schemas 的原子 release manifest。
/// </summary>
public sealed record PlatformApiReleaseManifest(
    string SchemaVersion,
    string ReleaseVersion,
    string ReleaseDigest,
    string SourceCommit,
    IReadOnlyList<PlatformApiArtifactAttestation> Artifacts,
    IReadOnlyDictionary<string, string> CanonicalSchemas);

/// <summary>
/// Host 支持目录中的一个不可变 Platform API release。
/// </summary>
public sealed record PlatformApiReleaseReference(string Version, string ReleaseDigest);

/// <summary>
/// Host 在任何插件代码、数据库连接或 DDL 前使用的 Platform API 支持目录。
/// </summary>
public sealed record PlatformApiHostCatalog(
    string CurrentRelease,
    IReadOnlyList<string> SupportedProfiles,
    IReadOnlyList<PlatformApiReleaseReference> Releases);

/// <summary>
/// 插件包由真实锁文件和 release manifest 生成的构建证明。
/// </summary>
public sealed record PlatformApiAttestation(
    string BuiltWith,
    string MinInclusive,
    string MaxExclusive,
    string ReleaseDigest,
    string Profile,
    string PersistenceContract,
    IReadOnlyList<string> PersistenceProviders);

/// <summary>
/// Platform API preflight 的兼容性裁决。
/// </summary>
public sealed record PlatformApiCompatibilityResult(
    bool IsCompatible,
    IReadOnlyList<PlatformApiDiagnostic> Diagnostics);

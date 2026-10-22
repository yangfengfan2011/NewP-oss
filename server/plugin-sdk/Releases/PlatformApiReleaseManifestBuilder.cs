using ICP.Platform.Plugin.Contracts;

namespace ICP.Platform.Plugin.Sdk;

/// <summary>
/// 构建 release manifest 时使用的已解析发布物描述。
/// </summary>
public sealed record PlatformApiArtifactDescriptor(
    string Kind,
    string PackageId,
    string Version,
    string ReleaseDigest,
    string Path,
    string Sha256);

/// <summary>
/// release manifest 构建输入。
/// </summary>
public sealed record PlatformApiReleaseBuildRequest(
    string SourceCommit,
    IReadOnlyList<PlatformApiArtifactDescriptor> Artifacts,
    IReadOnlyDictionary<string, string>? CanonicalSchemas = null);

/// <summary>
/// release manifest 构建结果；失败时绝不返回部分 manifest。
/// </summary>
public sealed record PlatformApiReleaseBuildResult(
    PlatformApiReleaseManifest? Manifest,
    IReadOnlyList<PlatformApiDiagnostic> Diagnostics)
{
    public bool IsSuccess => Manifest is not null && Diagnostics.Count == 0;
}

/// <summary>
/// 校验四个锁步发布物并产生原子 release manifest。
/// </summary>
public sealed class PlatformApiReleaseManifestBuilder
{
    /// <summary>
    /// 验证发布物集合、精确版本和 release digest 后构建 manifest。
    /// </summary>
    public PlatformApiReleaseBuildResult Build(PlatformApiReleaseBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var byKind = request.Artifacts
            .GroupBy(static artifact => artifact.Kind, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var hasKindMismatch = PlatformApiArtifactKinds.Required.Any(kind =>
                !byKind.TryGetValue(kind, out var matches) || matches.Length != 1)
            || byKind.Keys.Except(PlatformApiArtifactKinds.Required, StringComparer.Ordinal).Any();
        var hasIdentityMismatch = request.Artifacts.Any(artifact =>
            !PlatformApiArtifactKinds.Required.Contains(artifact.Kind, StringComparer.Ordinal)
            || !string.Equals(artifact.PackageId, PlatformApiArtifactKinds.PackageIdFor(artifact.Kind), StringComparison.Ordinal)
            || !string.Equals(artifact.Version, PlatformApiReleaseIdentity.Version, StringComparison.Ordinal)
            || !string.Equals(artifact.ReleaseDigest, PlatformApiReleaseIdentity.ReleaseDigest, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(artifact.Path)
            || !PlatformApiReleaseIntegrity.IsSha256(artifact.Sha256));
        var hasReleaseMetadataMismatch = string.IsNullOrWhiteSpace(request.SourceCommit)
            || !PlatformApiReleaseIntegrity.HasRequiredCanonicalSchema(request.CanonicalSchemas);

        if (hasKindMismatch || hasIdentityMismatch || hasReleaseMetadataMismatch)
        {
            return Failure("The Platform API release must contain one source commit, canonical schema hash and four exact lockstep artifacts.");
        }

        var ordered = PlatformApiArtifactKinds.Required
            .Select(kind => byKind[kind][0])
            .Select(static artifact => new PlatformApiArtifactAttestation(
                artifact.Kind,
                artifact.PackageId,
                artifact.Version,
                artifact.ReleaseDigest,
                artifact.Path,
                artifact.Sha256))
            .ToArray();
        var manifest = new PlatformApiReleaseManifest(
            "1.0",
            PlatformApiReleaseIdentity.Version,
            PlatformApiReleaseIdentity.ReleaseDigest,
            request.SourceCommit,
            ordered,
            new SortedDictionary<string, string>(
                request.CanonicalSchemas!.ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal),
                StringComparer.Ordinal));
        return new PlatformApiReleaseBuildResult(manifest, Array.Empty<PlatformApiDiagnostic>());
    }

    private static PlatformApiReleaseBuildResult Failure(string message) =>
        new(
            null,
            [
                new PlatformApiDiagnostic(
                    PlatformApiDiagnosticCodes.PlatformApiReleaseSkew,
                    "release",
                    message,
                    SupportedRelease: PlatformApiReleaseIdentity.Version)
            ]);
}

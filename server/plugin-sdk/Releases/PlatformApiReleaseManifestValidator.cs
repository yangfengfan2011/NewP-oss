using ICP.Platform.Plugin.Contracts;

namespace ICP.Platform.Plugin.Sdk;

/// <summary>
/// 验证 release manifest 是否完整描述四个锁步发布物。
/// </summary>
public sealed class PlatformApiReleaseManifestValidator
{
    /// <summary>
    /// 返回稳定排序的 release manifest 诊断。
    /// </summary>
    public IReadOnlyList<PlatformApiDiagnostic> Validate(PlatformApiReleaseManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var valid = string.Equals(manifest.SchemaVersion, "1.0", StringComparison.Ordinal)
            && string.Equals(manifest.ReleaseVersion, PlatformApiReleaseIdentity.Version, StringComparison.Ordinal)
            && string.Equals(manifest.ReleaseDigest, PlatformApiReleaseIdentity.ReleaseDigest, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(manifest.SourceCommit)
            && PlatformApiReleaseIntegrity.HasRequiredCanonicalSchema(manifest.CanonicalSchemas)
            && manifest.Artifacts.Count == PlatformApiArtifactKinds.Required.Count
            && PlatformApiArtifactKinds.Required.All(kind => manifest.Artifacts.Count(artifact =>
                string.Equals(artifact.Kind, kind, StringComparison.Ordinal)
                && string.Equals(artifact.PackageId, PlatformApiArtifactKinds.PackageIdFor(kind), StringComparison.Ordinal)
                && string.Equals(artifact.Version, manifest.ReleaseVersion, StringComparison.Ordinal)
                && string.Equals(artifact.ReleaseDigest, manifest.ReleaseDigest, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(artifact.Path)
                && PlatformApiReleaseIntegrity.IsSha256(artifact.Sha256)) == 1);
        return valid
            ? Array.Empty<PlatformApiDiagnostic>()
            :
            [
                new PlatformApiDiagnostic(
                    PlatformApiDiagnosticCodes.PlatformApiReleaseSkew,
                    "release",
                    "Release manifest must contain one source commit, canonical schema hash and four exact lockstep artifacts.",
                    SupportedRelease: PlatformApiReleaseIdentity.Version)
            ];
    }
}

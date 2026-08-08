using ICP.Platform.Plugin.Contracts;

namespace ICP.Platform.Plugin.Sdk;

/// <summary>
/// 在插件代码、连接数据库和 DDL 前裁决 Standard Data Profile 兼容性。
/// </summary>
public sealed class PlatformApiCompatibilityEvaluator
{
    /// <summary>
    /// 使用 Host 的嵌入 release catalog 校验插件 attestation。
    /// </summary>
    public PlatformApiCompatibilityResult Evaluate(
        PlatformApiAttestation attestation,
        PlatformApiHostCatalog hostCatalog)
    {
        ArgumentNullException.ThrowIfNull(attestation);
        ArgumentNullException.ThrowIfNull(hostCatalog);

        if (!hostCatalog.SupportedProfiles.Contains(attestation.Profile, StringComparer.Ordinal))
        {
            return Failure(
                PlatformApiDiagnosticCodes.PluginProfileUnsupported,
                attestation,
                hostCatalog,
                $"Plugin profile '{attestation.Profile}' is not supported.");
        }

        if (!string.Equals(attestation.PersistenceContract, PlatformApiReleaseIdentity.PersistenceContract, StringComparison.Ordinal)
            || attestation.PersistenceProviders.Count != 1
            || !string.Equals(attestation.PersistenceProviders[0], PlatformApiReleaseIdentity.SqlServerProvider, StringComparison.Ordinal))
        {
            return Failure(
                PlatformApiDiagnosticCodes.PersistenceContractUnsupported,
                attestation,
                hostCatalog,
                "The standard-data.v1 profile requires the SQL Server ef-migrations.v1 contract.");
        }

        if (!TryVersion(attestation.BuiltWith, out var builtWith)
            || !TryVersion(attestation.MinInclusive, out var minInclusive)
            || !TryVersion(attestation.MaxExclusive, out var maxExclusive)
            || !TryVersion(hostCatalog.CurrentRelease, out var current)
            || builtWith.Major != current.Major
            || builtWith > current
            || current < minInclusive
            || current >= maxExclusive)
        {
            return Failure(
                PlatformApiDiagnosticCodes.PlatformApiIncompatible,
                attestation,
                hostCatalog,
                $"Plugin built with '{attestation.BuiltWith}' is incompatible with Host release '{hostCatalog.CurrentRelease}'.");
        }

        var release = hostCatalog.Releases.SingleOrDefault(candidate =>
            string.Equals(candidate.Version, attestation.BuiltWith, StringComparison.Ordinal));
        if (release is null)
        {
            return Failure(
                PlatformApiDiagnosticCodes.PlatformApiIncompatible,
                attestation,
                hostCatalog,
                $"Host release catalog does not contain '{attestation.BuiltWith}'.");
        }

        if (!string.Equals(release.ReleaseDigest, attestation.ReleaseDigest, StringComparison.Ordinal))
        {
            return Failure(
                PlatformApiDiagnosticCodes.PlatformApiAttestationMismatch,
                attestation,
                hostCatalog,
                "Plugin release digest does not match the Host release catalog.");
        }

        return new PlatformApiCompatibilityResult(true, Array.Empty<PlatformApiDiagnostic>());
    }

    private static bool TryVersion(string value, out Version version) =>
        Version.TryParse(value, out version!);

    private static PlatformApiCompatibilityResult Failure(
        string code,
        PlatformApiAttestation attestation,
        PlatformApiHostCatalog hostCatalog,
        string message) =>
        new(
            false,
            [
                new PlatformApiDiagnostic(
                    code,
                    "preflight",
                    message,
                    Profile: attestation.Profile,
                    BuiltWith: attestation.BuiltWith,
                    SupportedRelease: hostCatalog.CurrentRelease)
            ]);
}

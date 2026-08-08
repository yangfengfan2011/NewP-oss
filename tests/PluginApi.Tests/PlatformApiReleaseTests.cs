using ICP.Platform.Plugin.Contracts;
using ICP.Platform.Plugin.Sdk;

namespace ICP.Platform.PluginApi.Tests;

public sealed class PlatformApiReleaseTests
{
    [Fact]
    public void ContractsAssembly_ReferencesOnlyBclAssemblies()
    {
        var references = typeof(PlatformApiReleaseIdentity).Assembly.GetReferencedAssemblies();

        Assert.NotEmpty(references);
        Assert.All(references, static reference =>
            Assert.True(
                reference.Name?.StartsWith("System.", StringComparison.Ordinal) == true
                || string.Equals(reference.Name, "System", StringComparison.Ordinal)
                || string.Equals(reference.Name, "netstandard", StringComparison.Ordinal)
                || string.Equals(reference.Name, "mscorlib", StringComparison.Ordinal),
                $"Contracts referenced non-BCL assembly '{reference.Name}'."));
    }

    [Fact]
    public void Build_FourLockstepArtifacts_ProducesReleaseManifest()
    {
        var artifacts = PlatformApiArtifactKinds.Required
            .Select((kind, index) => new PlatformApiArtifactDescriptor(
                kind,
                PlatformApiArtifactKinds.PackageIdFor(kind),
                PlatformApiReleaseIdentity.Version,
                PlatformApiReleaseIdentity.ReleaseDigest,
                $"feed/artifact-{index}",
                $"sha256:{new string((char)('a' + index), 64)}"))
            .ToArray();

        var result = new PlatformApiReleaseManifestBuilder().Build(
            new PlatformApiReleaseBuildRequest("commit-123", artifacts, CanonicalSchemas()));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Manifest);
        Assert.Equal(PlatformApiReleaseIdentity.Version, result.Manifest.ReleaseVersion);
        Assert.Equal(PlatformApiReleaseIdentity.ReleaseDigest, result.Manifest.ReleaseDigest);
        Assert.Equal(PlatformApiArtifactKinds.Required, result.Manifest.Artifacts.Select(static artifact => artifact.Kind));
    }

    [Fact]
    public void Build_CanonicalSchemaIsMissing_ReturnsReleaseSkew()
    {
        var artifacts = PlatformApiArtifactKinds.Required
            .Select((kind, index) => new PlatformApiArtifactDescriptor(
                kind,
                PlatformApiArtifactKinds.PackageIdFor(kind),
                PlatformApiReleaseIdentity.Version,
                PlatformApiReleaseIdentity.ReleaseDigest,
                $"feed/artifact-{index}",
                $"sha256:{new string((char)('a' + index), 64)}"))
            .ToArray();

        var result = new PlatformApiReleaseManifestBuilder().Build(
            new PlatformApiReleaseBuildRequest("commit-123", artifacts));

        Assert.False(result.IsSuccess);
        Assert.Equal(PlatformApiDiagnosticCodes.PlatformApiReleaseSkew, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Build_ArtifactVersionSkew_ReturnsStableDiagnostic()
    {
        var artifacts = PlatformApiArtifactKinds.Required
            .Select(kind => new PlatformApiArtifactDescriptor(
                kind,
                PlatformApiArtifactKinds.PackageIdFor(kind),
                kind == PlatformApiArtifactKinds.FrontendSdk ? "1.1.0" : PlatformApiReleaseIdentity.Version,
                PlatformApiReleaseIdentity.ReleaseDigest,
                $"feed/{kind}",
                $"sha256:{new string('a', 64)}"))
            .ToArray();

        var result = new PlatformApiReleaseManifestBuilder().Build(
            new PlatformApiReleaseBuildRequest("commit-123", artifacts, CanonicalSchemas()));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Manifest);
        Assert.Equal(PlatformApiDiagnosticCodes.PlatformApiReleaseSkew, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Evaluate_OldPackageOnNewHost_PreservesSameMajorCompatibility()
    {
        var catalog = HostCatalog(
            "1.1.0",
            new PlatformApiReleaseReference("1.0.0", PlatformApiReleaseIdentity.ReleaseDigest),
            new PlatformApiReleaseReference("1.1.0", $"sha256:{new string('b', 64)}"));
        var attestation = Attestation("1.0.0", PlatformApiReleaseIdentity.ReleaseDigest);

        var result = new PlatformApiCompatibilityEvaluator().Evaluate(attestation, catalog);

        Assert.True(result.IsCompatible);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Evaluate_NewerPackageOnOldHost_FailsBeforeActivation()
    {
        var catalog = HostCatalog(
            "1.0.0",
            new PlatformApiReleaseReference("1.0.0", PlatformApiReleaseIdentity.ReleaseDigest));
        var attestation = Attestation("1.1.0", $"sha256:{new string('b', 64)}");

        var result = new PlatformApiCompatibilityEvaluator().Evaluate(attestation, catalog);

        Assert.False(result.IsCompatible);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(PlatformApiDiagnosticCodes.PlatformApiIncompatible, diagnostic.Code);
        Assert.Equal("preflight", diagnostic.Stage);
    }

    [Fact]
    public void Evaluate_ReleaseDigestTampered_ReturnsAttestationMismatch()
    {
        var catalog = HostCatalog(
            "1.0.0",
            new PlatformApiReleaseReference("1.0.0", PlatformApiReleaseIdentity.ReleaseDigest));
        var attestation = Attestation("1.0.0", $"sha256:{new string('f', 64)}");

        var result = new PlatformApiCompatibilityEvaluator().Evaluate(attestation, catalog);

        Assert.False(result.IsCompatible);
        Assert.Equal(
            PlatformApiDiagnosticCodes.PlatformApiAttestationMismatch,
            Assert.Single(result.Diagnostics).Code);
    }

    private static PlatformApiAttestation Attestation(string builtWith, string releaseDigest) =>
        new(
            builtWith,
            builtWith,
            "2.0.0",
            releaseDigest,
            PlatformApiReleaseIdentity.StandardDataProfile,
            PlatformApiReleaseIdentity.PersistenceContract,
            [PlatformApiReleaseIdentity.SqlServerProvider]);

    private static PlatformApiHostCatalog HostCatalog(
        string currentRelease,
        params PlatformApiReleaseReference[] releases) =>
        new(currentRelease, [PlatformApiReleaseIdentity.StandardDataProfile], releases);

    private static IReadOnlyDictionary<string, string> CanonicalSchemas() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["standard-plugin-blueprint.schema.json"] = $"sha256:{new string('c', 64)}"
        };
}

using ICP.Platform.Persistence.Planning;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class MigrationManifestIntegrityTests
{
    [Fact]
    public void ComputeCanonicalContentDigest_NormalizesLineEndings()
    {
        var lf = MigrationManifestIntegrity.ComputeCanonicalContentDigest("first\nsecond\n");

        Assert.Equal(lf, MigrationManifestIntegrity.ComputeCanonicalContentDigest("first\r\nsecond\r\n"));
        Assert.Equal(lf, MigrationManifestIntegrity.ComputeCanonicalContentDigest("first\rsecond\r"));
    }

    [Fact]
    public void ComputeBundleChecksum_ReturnsLowercaseSha256()
    {
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            MigrationManifestIntegrity.ComputeBundleChecksum("abc"u8));
    }

    [Fact]
    public void Validate_ValidManifest_ReturnsCanonicalSequenceOrder()
    {
        var manifest = CreateManifest() with
        {
            ModelDigest = Digest('b'),
            Migrations =
            [
                Step("000002", "migration-two", Digest('1'), Digest('2'), Digest('a'), Digest('b')),
                Step("000001", "migration-one", null, Digest('1'), Digest('0'), Digest('a'))
            ]
        };

        var migrations = MigrationManifestIntegrity.Validate(
            "Example",
            PersistenceProvider.SqlServer,
            manifest,
            "bundle"u8,
            "manifest.json",
            Digest('b'));

        Assert.Equal(["migration-one", "migration-two"], migrations.Select(static migration => migration.MigrationId));
    }

    [Theory]
    [InlineData("owner", "PERSISTENCE_MIGRATION_OWNER_MISMATCH")]
    [InlineData("provider", "PERSISTENCE_MIGRATION_OWNER_MISMATCH")]
    [InlineData("bundle", "PERSISTENCE_BUNDLE_CHECKSUM_MISMATCH")]
    [InlineData("model", "PERSISTENCE_MODEL_MISMATCH")]
    [InlineData("sequence", "PERSISTENCE_MIGRATION_CHAIN_INVALID")]
    [InlineData("previous", "PERSISTENCE_MIGRATION_CHAIN_INVALID")]
    [InlineData("from", "PERSISTENCE_MIGRATION_CHAIN_INVALID")]
    [InlineData("to", "PERSISTENCE_MODEL_MISMATCH")]
    [InlineData("compatibility", "PERSISTENCE_MIGRATION_CHAIN_INVALID")]
    public void Validate_InvalidManifest_PreservesStableFailureCode(string invalidField, string expectedCode)
    {
        var manifest = CreateManifest();
        var expectedModule = "Example";
        var expectedProvider = PersistenceProvider.SqlServer;
        var assemblyBytes = "bundle"u8.ToArray();
        var expectedModelDigest = Digest('a');

        switch (invalidField)
        {
            case "owner": manifest = manifest with { ModuleId = "Other" }; break;
            case "provider": manifest = manifest with { Provider = "Oracle" }; break;
            case "bundle": manifest = manifest with { BundleChecksum = Digest('f') }; break;
            case "model": manifest = manifest with { ModelDigest = Digest('f') }; break;
            case "sequence": manifest = WithFirstStep(manifest, step => step with { Sequence = "000002" }); break;
            case "previous": manifest = WithFirstStep(manifest, step => step with { PreviousContentDigest = Digest('f') }); break;
            case "from": manifest = WithFirstStep(manifest, step => step with { FromModelDigest = Digest('f') }); break;
            case "to": manifest = WithFirstStep(manifest, step => step with { ToModelDigest = Digest('f') }); break;
            case "compatibility": manifest = WithFirstStep(manifest, step => step with { Compatibility = "unknown" }); break;
        }

        var exception = Assert.Throws<MigrationManifestValidationException>(() =>
            MigrationManifestIntegrity.Validate(
                expectedModule,
                expectedProvider,
                manifest,
                assemblyBytes,
                "manifest.json",
                expectedModelDigest));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public void ValidateMigrationTypes_WhenDeclaredTypeIsMissing_ReportsMigrationId()
    {
        var exception = Assert.Throws<MigrationManifestValidationException>(() =>
            MigrationManifestIntegrity.ValidateMigrationTypes(
                ["migration-one", "migration-two"],
                ["migration-one"]));

        Assert.Equal(PersistenceMigrationFailureCodes.MigrationMissing, exception.Code);
        Assert.Equal("migration-two", exception.CanonicalKey);
    }

    private static MigrationManifest CreateManifest()
    {
        var assemblyBytes = "bundle"u8;
        return new MigrationManifest(
            "1.0",
            "SqlServer",
            "Example",
            Digest('a'),
            MigrationManifestIntegrity.ComputeBundleChecksum(assemblyBytes),
            [Step("000001", "migration-one", null, Digest('1'), Digest('0'), Digest('a'))]);
    }

    private static MigrationManifest WithFirstStep(
        MigrationManifest manifest,
        Func<MigrationManifestStep, MigrationManifestStep> update) =>
        manifest with { Migrations = [update(manifest.Migrations[0])] };

    private static MigrationManifestStep Step(
        string sequence,
        string migrationId,
        string? previousContentDigest,
        string contentDigest,
        string fromModelDigest,
        string toModelDigest) =>
        new(sequence, migrationId, previousContentDigest, contentDigest, fromModelDigest, toModelDigest, "safe-expand");

    private static string Digest(char value) => new(value, 64);
}

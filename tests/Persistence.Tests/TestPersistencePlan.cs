using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.Planning;

namespace ICP.Platform.Persistence.Tests;

internal static class TestPersistencePlan
{
    public static CompiledPersistencePlan Create(PersistenceProvider provider = PersistenceProvider.SqlServer)
    {
        var models = PlatformPersistenceModelCatalog.AdaptPlatformCore(
            PlatformCoreLegacyModelSource.GetDefinitions());
        var digest = PersistenceModelDigest.Compute(models);
        var frontier = provider switch
        {
            PersistenceProvider.SqlServer => PlatformCoreMigrationFrontiers.SqlServerLegacyBaseline,
            PersistenceProvider.Oracle => "20260428145243_AlignModelMetadataEntityKeys",
            PersistenceProvider.MySql => "20260428145227_AlignModelMetadataEntityKeys",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
        var legacy = new ModuleMigrationStream(
            "platform.core",
            provider,
            "platform.core:legacy-baseline",
            "platform.core:legacy-baseline",
            digest,
            digest,
            frontier,
            IsLegacyBaseline: true,
            []);
        return new CompiledPersistencePlan(
            "test-candidate",
            digest,
            digest,
            provider,
            digest,
            Array.Empty<byte>(),
            models,
            [legacy]);
    }
}

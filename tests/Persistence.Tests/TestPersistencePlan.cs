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
        return new CompiledPersistencePlan(
            "test-candidate",
            digest,
            digest,
            provider,
            digest,
            Array.Empty<byte>(),
            models,
            Array.Empty<ModuleMigrationStream>());
    }
}

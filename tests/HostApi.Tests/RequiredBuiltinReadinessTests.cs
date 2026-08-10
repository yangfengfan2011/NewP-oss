using ICP.Platform.HostApi.Readiness;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.Planning;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class RequiredBuiltinReadinessTests
{
    [Fact]
    public async Task Contributor_IsReadyOnlyWhenRequiredBuiltinsModelsAndMigrationExist()
    {
        var contributor = new RequiredBuiltinReadinessContributor(ModulePlan("Identity", "Authentication"), PersistencePlan());

        var result = await contributor.CheckAsync(CancellationToken.None);

        Assert.True(contributor.IsRequired);
        Assert.Equal("identity-foundation", contributor.Name);
        Assert.Equal(ReadinessState.Ready, result.State);
    }

    [Fact]
    public async Task Contributor_WhenIdentityIsMissing_ReturnsStableNotReadySignal()
    {
        var contributor = new RequiredBuiltinReadinessContributor(ModulePlan("Authentication"), PersistencePlan());

        var result = await contributor.CheckAsync(CancellationToken.None);

        Assert.Equal(ReadinessState.Failed, result.State);
        Assert.Equal(RequiredBuiltinReadinessFailureCodes.ModuleMissing, result.FailureCode);
        Assert.DoesNotContain("path", result.Diagnostic ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Contributor_WhenIdentityIsDuplicated_ReturnsStableNotReadySignal()
    {
        var contributor = new RequiredBuiltinReadinessContributor(
            ModulePlan("Identity", "Identity", "Authentication"),
            PersistencePlan());

        var result = await contributor.CheckAsync(CancellationToken.None);

        Assert.Equal(ReadinessState.Failed, result.State);
        Assert.Equal(RequiredBuiltinReadinessFailureCodes.ModuleDuplicate, result.FailureCode);
    }

    [Fact]
    public async Task Contributor_WhenRequiredModelOrMigrationIsMissing_ReturnsStableNotReadySignal()
    {
        var modelsMissing = new RequiredBuiltinReadinessContributor(
            ModulePlan("Identity", "Authentication"),
            PersistencePlan(includeAuthenticationModel: false));
        var migrationMissing = new RequiredBuiltinReadinessContributor(
            ModulePlan("Identity", "Authentication"),
            PersistencePlan(includeMigration: false));
        var migrationOutdated = new RequiredBuiltinReadinessContributor(
            ModulePlan("Identity", "Authentication"),
            PersistencePlan(migrationFrontier: "20260808054041_AlignOrganizationSchemaDrift"));

        var modelResult = await modelsMissing.CheckAsync(CancellationToken.None);
        var migrationResult = await migrationMissing.CheckAsync(CancellationToken.None);
        var outdatedResult = await migrationOutdated.CheckAsync(CancellationToken.None);

        Assert.Equal(RequiredBuiltinReadinessFailureCodes.ModelMissing, modelResult.FailureCode);
        Assert.Equal(RequiredBuiltinReadinessFailureCodes.MigrationMissing, migrationResult.FailureCode);
        Assert.Equal(RequiredBuiltinReadinessFailureCodes.MigrationMissing, outdatedResult.FailureCode);
    }

    private static CanonicalModulePlan ModulePlan(params string[] moduleIds) =>
        new(
            "candidate",
            "1.0",
            "plan",
            "snapshot",
            [],
            moduleIds.Select(Module).ToArray());

    private static CanonicalModulePlanModule Module(string moduleId) =>
        new(
            moduleId,
            "1.0.0",
            moduleId,
            null,
            moduleId,
            $"../../{moduleId}Module.dll",
            $"{moduleId.ToLowerInvariant()}.backend",
            null,
            null,
            "legacy-module",
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            new ModulePlanProvenance("built-in", moduleId, "/"));

    private static CompiledPersistencePlan PersistencePlan(
        bool includeAuthenticationModel = true,
        bool includeMigration = true,
        string migrationFrontier = PlatformCoreMigrationFrontiers.SqlServerIdentityFoundation)
    {
        var models = new List<ModelDefinition>
        {
            Model("Identity", "User"),
            Model("Identity", "LocalCredential"),
            Model("Identity", "IdentitySecurityEvent")
        };
        if (includeAuthenticationModel)
        {
            models.Add(Model("Authentication", "AuthenticationSession"));
        }

        return new CompiledPersistencePlan(
            "candidate",
            "snapshot",
            "plan",
            PersistenceProvider.SqlServer,
            "digest",
            [],
            models,
            includeMigration
                ?
                [
                    new ModuleMigrationStream(
                        "platform.core",
                        PersistenceProvider.SqlServer,
                        "migration.dll",
                        "legacy",
                        "checksum",
                        "model",
                        migrationFrontier,
                        true,
                        [])
                ]
                : []);
    }

    private static ModelDefinition Model(string owner, string name) =>
        new(name, name, ModelKind.Entity, [], [], OwnerModuleId: owner);
}

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
        var identityMigrationMissing = new RequiredBuiltinReadinessContributor(
            ModulePlan("Identity", "Authentication"),
            PersistencePlan(includeIdentityMigration: false));
        var authenticationMigrationMissing = new RequiredBuiltinReadinessContributor(
            ModulePlan("Identity", "Authentication"),
            PersistencePlan(includeAuthenticationMigration: false));
        var migrationOutdated = new RequiredBuiltinReadinessContributor(
            ModulePlan("Identity", "Authentication"),
            PersistencePlan(identityMigrationFrontier: "20260810000000_Outdated"));

        var modelResult = await modelsMissing.CheckAsync(CancellationToken.None);
        var identityMigrationResult = await identityMigrationMissing.CheckAsync(CancellationToken.None);
        var authenticationMigrationResult = await authenticationMigrationMissing.CheckAsync(CancellationToken.None);
        var outdatedResult = await migrationOutdated.CheckAsync(CancellationToken.None);

        Assert.Equal(RequiredBuiltinReadinessFailureCodes.ModelMissing, modelResult.FailureCode);
        Assert.Equal(RequiredBuiltinReadinessFailureCodes.MigrationMissing, identityMigrationResult.FailureCode);
        Assert.Equal(RequiredBuiltinReadinessFailureCodes.MigrationMissing, authenticationMigrationResult.FailureCode);
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
        bool includeIdentityMigration = true,
        bool includeAuthenticationMigration = true,
        string identityMigrationFrontier = PlatformCoreMigrationFrontiers.IdentitySqlServerFoundation)
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

        var streams = new List<ModuleMigrationStream>();
        if (includeIdentityMigration)
        {
            streams.Add(new ModuleMigrationStream(
                "Identity",
                PersistenceProvider.SqlServer,
                "identity-migration.dll",
                "identity-manifest.json",
                "checksum",
                "model",
                identityMigrationFrontier,
                false,
                []));
        }

        if (includeAuthenticationMigration)
        {
            streams.Add(new ModuleMigrationStream(
                "Authentication",
                PersistenceProvider.SqlServer,
                "authentication-migration.dll",
                "authentication-manifest.json",
                "checksum",
                "model",
                PlatformCoreMigrationFrontiers.AuthenticationSqlServerFoundation,
                false,
                []));
        }

        return new CompiledPersistencePlan(
            "candidate",
            "snapshot",
            "plan",
            PersistenceProvider.SqlServer,
            "digest",
            [],
            models,
            streams);
    }

    private static ModelDefinition Model(string owner, string name) =>
        new(name, name, ModelKind.Entity, [], [], OwnerModuleId: owner);
}

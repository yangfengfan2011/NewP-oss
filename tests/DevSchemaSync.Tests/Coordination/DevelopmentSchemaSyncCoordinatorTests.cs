using ICP.Platform.Persistence.DevSync.Coordination;
using ICP.Platform.Persistence.DevSync.Execution;
using ICP.Platform.Persistence.DevSync.Planning;
using Xunit;

namespace ICP.Platform.DevSchemaSync.Tests.Coordination;

public class DevelopmentSchemaSyncCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_WhenPlanContainsConfirmOperations_ReturnsBlockedResult()
    {
        var confirmCalled = false;
        var applyCalled = false;
        var coordinator = DevelopmentSchemaSyncCoordinator.CreateForTests(
            inventory: ModelSourceInventory.Success(["Role"]),
            signaturesMatch: true,
            plan: new SchemaPlan(
                new SchemaPlanSummary(1, 1, 0, 1, 0),
                ["Role"],
                [new SchemaOperation("DropColumn", "Confirm", "Role", "Description", null, "drop Description", ["ALTER TABLE [Role] DROP COLUMN [Description]"])],
                []),
            applyConfirmation: _ =>
            {
                confirmCalled = true;
                return true;
            },
            applyAsync: (_, _) =>
            {
                applyCalled = true;
                return Task.FromResult(new ApplyResult(true, false, 1, 1));
            });

        var result = await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.False(result.StartupAllowed);
        Assert.Equal("ConfirmOperationsDetected", result.FailureReason);
        Assert.False(confirmCalled);
        Assert.False(applyCalled);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBaseDtoSignaturesMismatch_ReturnsBlockedResult()
    {
        var coordinator = DevelopmentSchemaSyncCoordinator.CreateForTests(
            inventory: ModelSourceInventory.Success(["Role"]),
            signaturesMatch: false,
            plan: new SchemaPlan(
                new SchemaPlanSummary(1, 0, 0, 0, 0),
                ["Role"],
                [],
                []));

        var result = await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.False(result.StartupAllowed);
        Assert.Equal("BaseDtoSignaturesMismatch", result.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_WhenApplyIsCancelled_ReturnsBlockedResult()
    {
        var coordinator = DevelopmentSchemaSyncCoordinator.CreateForTests(
            inventory: ModelSourceInventory.Success(["Role"]),
            signaturesMatch: true,
            plan: new SchemaPlan(
                new SchemaPlanSummary(1, 1, 1, 0, 0),
                ["Role"],
                [new SchemaOperation("AddColumn", "Safe", "Role", "Description", null, "add Description", ["ALTER TABLE [Role] ADD [Description] nvarchar(max) NULL;"])],
                []),
            applyConfirmation: static _ => true,
            applyResult: new ApplyResult(false, true, 0, 0));

        var result = await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.False(result.StartupAllowed);
        Assert.Equal("ApplyCancelled", result.FailureReason);
        Assert.Equal(4, result.ExitCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenApplyRequestedWithoutExplicitConfirmation_ReturnsCancelledAndDoesNotExecute()
    {
        var applyCalled = false;
        var coordinator = DevelopmentSchemaSyncCoordinator.CreateForTests(
            inventory: ModelSourceInventory.Success(["Role"]),
            signaturesMatch: true,
            plan: new SchemaPlan(
                new SchemaPlanSummary(1, 1, 1, 0, 0),
                ["Role"],
                [new SchemaOperation("AddColumn", "Safe", "Role", "Description", null, "add Description", ["ALTER TABLE [Role] ADD [Description] nvarchar(max) NULL;"])],
                []),
            applyAsync: (_, _) =>
            {
                applyCalled = true;
                return Task.FromResult(new ApplyResult(true, false, 1, 1));
            });

        var result = await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.False(result.StartupAllowed);
        Assert.Equal("ApplyCancelled", result.FailureReason);
        Assert.Equal(4, result.ExitCode);
        Assert.False(applyCalled);
    }

    [Fact]
    public async Task ExecuteAsync_WhenApplyReturnsFailed_ReturnsBlockedResult()
    {
        var coordinator = DevelopmentSchemaSyncCoordinator.CreateForTests(
            inventory: ModelSourceInventory.Success(["Role"]),
            signaturesMatch: true,
            plan: new SchemaPlan(
                new SchemaPlanSummary(1, 1, 1, 0, 0),
                ["Role"],
                [new SchemaOperation("AddColumn", "Safe", "Role", "Description", null, "add Description", ["ALTER TABLE [Role] ADD [Description] nvarchar(max) NULL;"])],
                []),
            applyConfirmation: static _ => true,
            applyResult: new ApplyResult(false, false, 0, 0));

        var result = await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.False(result.StartupAllowed);
        Assert.Equal("ApplyFailed", result.FailureReason);
        Assert.Equal(4, result.ExitCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenApplyThrows_ReturnsBlockedResult()
    {
        var coordinator = DevelopmentSchemaSyncCoordinator.CreateForTests(
            inventory: ModelSourceInventory.Success(["Role"]),
            signaturesMatch: true,
            plan: new SchemaPlan(
                new SchemaPlanSummary(1, 1, 1, 0, 0),
                ["Role"],
                [new SchemaOperation("AddColumn", "Safe", "Role", "Description", null, "add Description", ["ALTER TABLE [Role] ADD [Description] nvarchar(max) NULL;"])],
                []),
            applyConfirmation: static _ => true,
            applyException: new InvalidOperationException("apply failed"));

        var result = await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.False(result.StartupAllowed);
        Assert.Equal("ApplyFailed", result.FailureReason);
        Assert.Equal(4, result.ExitCode);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Contains("apply failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PrepareAsync_WhenBuildPlanThrows_ReturnsStableDiagnosticsEnvelope()
    {
        var coordinator = DevelopmentSchemaSyncCoordinator.CreateForTests(
            inventory: ModelSourceInventory.Success(["Role"]),
            signaturesMatch: true,
            plan: new SchemaPlan(
                new SchemaPlanSummary(1, 0, 0, 0, 0),
                ["Role"],
                [],
                []),
            buildPlanException: new InvalidOperationException("metadata path invalid"));

        var result = await coordinator.PrepareAsync(CancellationToken.None);

        Assert.False(result.StartupAllowed);
        Assert.Equal("PrepareFailed", result.FailureReason);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Contains("InvalidOperationException", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Contains("metadata path invalid", StringComparison.Ordinal));
        Assert.Contains("metadata path invalid", result.BuildMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_WhenCatalogModelHasNoMetadataFile_ReturnsMissingModel()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"dev-sync-inventory-{Guid.NewGuid():N}");
        var modelDirectory = Path.Combine(repositoryRoot, "modules", "Authorization", "metadata", "models");
        Directory.CreateDirectory(modelDirectory);
        await File.WriteAllTextAsync(Path.Combine(modelDirectory, "Role.model.json"), "{}", CancellationToken.None);

        try
        {
            var inventory = ModelSourceInventory.CreateForTests(["Role", "Permission"], repositoryRoot);

            var result = await inventory.CheckAsync(CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(["Permission"], result.MissingModels);
            Assert.Equal(["Role"], result.LoadedModels);
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CheckAsync_WhenModelFileIsOutsideMetadataModelsDirectory_ReturnsMissingModel()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"dev-sync-inventory-{Guid.NewGuid():N}");
        var nonSourceDirectory = Path.Combine(repositoryRoot, "modules", "Authorization", "metadata", "archive");
        Directory.CreateDirectory(nonSourceDirectory);
        await File.WriteAllTextAsync(Path.Combine(nonSourceDirectory, "Role.model.json"), "{}", CancellationToken.None);

        try
        {
            var inventory = ModelSourceInventory.CreateForTests(["Role"], repositoryRoot);

            var result = await inventory.CheckAsync(CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(["Role"], result.MissingModels);
            Assert.Empty(result.LoadedModels);
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CheckAsync_WhenModelFileIsInNestedMetadataModelsDirectory_ReturnsMissingModel()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"dev-sync-inventory-{Guid.NewGuid():N}");
        var nestedSourceDirectory = Path.Combine(repositoryRoot, "modules", "Authorization", "archive", "metadata", "models");
        Directory.CreateDirectory(nestedSourceDirectory);
        await File.WriteAllTextAsync(Path.Combine(nestedSourceDirectory, "Role.model.json"), "{}", CancellationToken.None);

        try
        {
            var inventory = ModelSourceInventory.CreateForTests(["Role"], repositoryRoot);

            var result = await inventory.CheckAsync(CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(["Role"], result.MissingModels);
            Assert.Empty(result.LoadedModels);
        }
        finally
        {
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Verify_WhenActualSignatureDiffers_ReturnsMismatchedDtoNames()
    {
        var verifier = new BaseDtoSignatureVerifier();

        var success = verifier.Verify(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RoleDto"] = "sha256:expected"
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RoleDto"] = "sha256:actual"
            },
            out var mismatches);

        Assert.False(success);
        Assert.Equal(["RoleDto"], mismatches);
    }

    [Fact]
    public void VerifyGeneratedArtifacts_WhenProviderAndFileMatchButModelProjectionDiffers_ReturnsMismatch()
    {
        var verifier = new BaseDtoSignatureVerifier();

        var result = verifier.VerifyGeneratedArtifactsForTests(
            expectedFromModelProjection: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RoleDto"] = "sha256:current-model"
            },
            providerSignatures: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RoleDto"] = "sha256:old-generated"
            },
            generatedFileSignatures: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RoleDto"] = "sha256:old-generated"
            });

        Assert.False(result.Success);
        Assert.Equal(["RoleDto"], result.Mismatches);
    }

    [Fact]
    public void VerifyGeneratedArtifacts_WhenGeneratedDtoIsNotInModelProjectionOrProvider_ReturnsMismatch()
    {
        var verifier = new BaseDtoSignatureVerifier();

        var result = verifier.VerifyGeneratedArtifactsForTests(
            expectedFromModelProjection: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RoleDto"] = "sha256:role"
            },
            providerSignatures: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RoleDto"] = "sha256:role"
            },
            generatedFileSignatures: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RoleDto"] = "sha256:role",
                ["StaleDto"] = "sha256:stale"
            });

        Assert.False(result.Success);
        Assert.Equal(["StaleDto"], result.Mismatches);
    }
}

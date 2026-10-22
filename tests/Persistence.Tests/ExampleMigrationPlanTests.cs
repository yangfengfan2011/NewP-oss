using System.Text.Json;
using ICP.Platform.Modules.Example.Migrations.SqlServer.Migrations;
using ICP.Platform.Modules.Example.Persistence.Descriptors;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.Planning;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class ExampleMigrationPlanTests
{
    [Fact]
    public void ExampleModelMetadataProvider_ProjectsOwnedAuthoringShapeAndPinnedDigest()
    {
        var definitions = new ExampleModelMetadataProvider().GetDefinitions()
            .Select(static definition => definition with
            {
                OwnerModuleId = "Example",
                SchemaName = "dbo"
            })
            .ToArray();

        var model = Assert.Single(definitions);
        Assert.Equal("ExampleRecord", model.Name);
        Assert.Equal("ExampleRecord", model.TableName);
        Assert.Equal(["CreatedAt", "CreatedBy", "ECODE", "EID", "ENAME", "RecordSource", "ReviewerUserEID", "UpdatedAt", "UpdatedBy"],
            model.Fields.Select(static field => field.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            "55aabe69f5f70166a7c153061443c74ebc26a8363762a389e227db6e05c22dfa",
            PersistenceModelDigest.Compute(definitions));
    }

    [Fact]
    public void CurrentSqlServerPlan_ContainsExampleOwnedModelAndMigrationStream()
    {
        var plan = TestPersistencePlan.CreateCurrentSqlServer();

        var model = Assert.Single(
            plan.Models,
            static candidate => candidate.OwnerModuleId == "Example" && candidate.Name == "ExampleRecord");
        Assert.Equal("ExampleRecord", model.TableName);
        Assert.Equal(["CreatedAt", "CreatedBy", "ECODE", "EID", "ENAME", "RecordSource", "ReviewerUserEID", "UpdatedAt", "UpdatedBy"],
            model.Fields.Select(static field => field.Name).Order(StringComparer.Ordinal));

        var stream = Assert.Single(plan.Streams, static candidate => candidate.ModuleId == "Example");
        Assert.False(stream.IsPlatformStream);
        Assert.Equal(PersistenceProvider.SqlServer, stream.Provider);
        Assert.Equal("55aabe69f5f70166a7c153061443c74ebc26a8363762a389e227db6e05c22dfa", stream.ModelDigest);
        Assert.Collection(
            stream.Migrations,
            migration =>
            {
                Assert.Equal("000001", migration.Sequence);
                Assert.EndsWith("_Example_000001_Foundation", migration.MigrationId, StringComparison.Ordinal);
                Assert.Equal(new string('0', 64), migration.FromModelDigest);
                Assert.Equal("4c30a1bbfe2e587016b0bb1a84d3115c497a5b70fecf21e6bcd2ded5702a46a8", migration.ToModelDigest);
                Assert.Equal("safe-expand", migration.Compatibility);
            },
            migration =>
            {
                Assert.Equal("000002", migration.Sequence);
                Assert.EndsWith("_Example_000002_ReviewerUserEID", migration.MigrationId, StringComparison.Ordinal);
                Assert.Equal("4c30a1bbfe2e587016b0bb1a84d3115c497a5b70fecf21e6bcd2ded5702a46a8", migration.FromModelDigest);
                Assert.Equal("a5464035b4271ab8c1495edba7bfab00625a9ebb65978c459f05efd0758c4d9c", migration.ToModelDigest);
                Assert.Equal("safe-expand", migration.Compatibility);
            },
            migration =>
            {
                Assert.Equal("000003", migration.Sequence);
                Assert.EndsWith("_Example_000003_RecordSourceNullable", migration.MigrationId, StringComparison.Ordinal);
                Assert.Equal("a5464035b4271ab8c1495edba7bfab00625a9ebb65978c459f05efd0758c4d9c", migration.FromModelDigest);
                Assert.Equal("05f1fa870d2f602d37cddd4c79d3e3931730c8d505223c0a35c57a7b662cc0cf", migration.ToModelDigest);
                Assert.Equal("safe-expand", migration.Compatibility);
            },
            migration =>
            {
                Assert.Equal("000004", migration.Sequence);
                Assert.EndsWith("_Example_000004_RecordSourceBackfill", migration.MigrationId, StringComparison.Ordinal);
                Assert.Equal("05f1fa870d2f602d37cddd4c79d3e3931730c8d505223c0a35c57a7b662cc0cf", migration.FromModelDigest);
                Assert.Equal(migration.FromModelDigest, migration.ToModelDigest);
                Assert.Equal("data-backfill", migration.Compatibility);
            },
            migration =>
            {
                Assert.Equal("000005", migration.Sequence);
                Assert.EndsWith("_Example_000005_RecordSourceRequired", migration.MigrationId, StringComparison.Ordinal);
                Assert.Equal("05f1fa870d2f602d37cddd4c79d3e3931730c8d505223c0a35c57a7b662cc0cf", migration.FromModelDigest);
                Assert.Equal(stream.ModelDigest, migration.ToModelDigest);
                Assert.Equal("contract", migration.Compatibility);
            });
    }

    [Fact]
    public void ExampleSqlServerManifest_DeclaresDeterministicLinearExpansion()
    {
        var repositoryRoot = FindRepositoryRoot();
        var manifestPath = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repositoryRoot, "Example"),
            "migrations",
            "Example.SqlServerMigrations.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var migrations = root.GetProperty("migrations").EnumerateArray().ToArray();
        Assert.Equal(5, migrations.Length);
        var foundation = migrations[0];
        var reviewerUser = migrations[1];
        var recordSourceNullable = migrations[2];
        var recordSourceBackfill = migrations[3];
        var recordSourceRequired = migrations[4];

        Assert.Equal("Example", root.GetProperty("moduleId").GetString());
        Assert.Equal("55aabe69f5f70166a7c153061443c74ebc26a8363762a389e227db6e05c22dfa",
            root.GetProperty("modelDigest").GetString());
        Assert.Equal(new string('0', 64), foundation.GetProperty("fromModelDigest").GetString());
        Assert.Equal("4c30a1bbfe2e587016b0bb1a84d3115c497a5b70fecf21e6bcd2ded5702a46a8", foundation.GetProperty("toModelDigest").GetString());
        Assert.Equal("safe-expand", foundation.GetProperty("compatibility").GetString());
        Assert.Equal(JsonValueKind.Null, foundation.GetProperty("previousContentDigest").ValueKind);
        Assert.Equal("4c30a1bbfe2e587016b0bb1a84d3115c497a5b70fecf21e6bcd2ded5702a46a8", reviewerUser.GetProperty("fromModelDigest").GetString());
        Assert.Equal("a5464035b4271ab8c1495edba7bfab00625a9ebb65978c459f05efd0758c4d9c", reviewerUser.GetProperty("toModelDigest").GetString());
        Assert.Equal("safe-expand", reviewerUser.GetProperty("compatibility").GetString());
        Assert.Equal(
            foundation.GetProperty("contentDigest").GetString(),
            reviewerUser.GetProperty("previousContentDigest").GetString());
        Assert.Equal(reviewerUser.GetProperty("toModelDigest").GetString(), recordSourceNullable.GetProperty("fromModelDigest").GetString());
        Assert.Equal("05f1fa870d2f602d37cddd4c79d3e3931730c8d505223c0a35c57a7b662cc0cf", recordSourceNullable.GetProperty("toModelDigest").GetString());
        Assert.Equal("safe-expand", recordSourceNullable.GetProperty("compatibility").GetString());
        Assert.Equal(reviewerUser.GetProperty("contentDigest").GetString(), recordSourceNullable.GetProperty("previousContentDigest").GetString());
        Assert.Equal(recordSourceNullable.GetProperty("toModelDigest").GetString(), recordSourceBackfill.GetProperty("fromModelDigest").GetString());
        Assert.Equal(recordSourceBackfill.GetProperty("fromModelDigest").GetString(), recordSourceBackfill.GetProperty("toModelDigest").GetString());
        Assert.Equal("data-backfill", recordSourceBackfill.GetProperty("compatibility").GetString());
        Assert.Equal(recordSourceNullable.GetProperty("contentDigest").GetString(), recordSourceBackfill.GetProperty("previousContentDigest").GetString());
        Assert.Equal(recordSourceBackfill.GetProperty("toModelDigest").GetString(), recordSourceRequired.GetProperty("fromModelDigest").GetString());
        Assert.Equal(root.GetProperty("modelDigest").GetString(), recordSourceRequired.GetProperty("toModelDigest").GetString());
        Assert.Equal("contract", recordSourceRequired.GetProperty("compatibility").GetString());
        Assert.Equal(recordSourceBackfill.GetProperty("contentDigest").GetString(), recordSourceRequired.GetProperty("previousContentDigest").GetString());
        Assert.Equal(
            root.GetProperty("bundleChecksum").GetString(),
            MigrationManifestIntegrity.ComputeBundleChecksum(File.ReadAllBytes(
                typeof(Example_000005_RecordSourceRequired).Assembly.Location)));

        var foundationPath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Example",
            "Migrations.SqlServer",
            "Migrations",
            "20260827110703_Example_000001_Foundation.cs");
        var foundationSource = File.ReadAllText(foundationPath);
        Assert.Equal(
            foundation.GetProperty("contentDigest").GetString(),
            MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(foundationPath)));
        Assert.Equal(1, Count(foundationSource, "migrationBuilder.CreateTable"));
        Assert.Contains("name: \"ExampleRecord\"", foundationSource, StringComparison.Ordinal);

        var reviewerUserPath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Example",
            "Migrations.SqlServer",
            "Migrations",
            "20260827120035_Example_000002_ReviewerUserEID.cs");
        var reviewerUserSource = File.ReadAllText(reviewerUserPath);
        Assert.Equal(
            reviewerUser.GetProperty("contentDigest").GetString(),
            MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(reviewerUserPath)));
        Assert.Equal(1, Count(reviewerUserSource, "migrationBuilder.AddColumn"));
        Assert.Contains("ReviewerUserEID", reviewerUserSource, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValue", reviewerUserSource, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValueSql", reviewerUserSource, StringComparison.Ordinal);
        Assert.DoesNotContain("migrationBuilder.Sql", reviewerUserSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlOperation", reviewerUserSource, StringComparison.Ordinal);

        var recordSourceNullablePath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Example",
            "Migrations.SqlServer",
            "Migrations",
            "20260828082341_Example_000003_RecordSourceNullable.cs");
        var recordSourceNullableSource = File.ReadAllText(recordSourceNullablePath);
        Assert.Equal(
            recordSourceNullable.GetProperty("contentDigest").GetString(),
            MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(recordSourceNullablePath)));
        Assert.Contains("migrationBuilder.AddColumn", recordSourceNullableSource, StringComparison.Ordinal);
        Assert.Contains("nullable: true", recordSourceNullableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValue", recordSourceNullableSource, StringComparison.Ordinal);

        var recordSourceBackfillPath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Example",
            "Migrations.SqlServer",
            "Migrations",
            "20260828082358_Example_000004_RecordSourceBackfill.cs");
        var recordSourceBackfillSource = File.ReadAllText(recordSourceBackfillPath);
        Assert.Equal(
            recordSourceBackfill.GetProperty("contentDigest").GetString(),
            MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(recordSourceBackfillPath)));
        Assert.Contains("migrationBuilder.BackfillRequiredField", recordSourceBackfillSource, StringComparison.Ordinal);
        Assert.Contains("\"Legacy\"", recordSourceBackfillSource, StringComparison.Ordinal);
        Assert.DoesNotContain("migrationBuilder.Sql", recordSourceBackfillSource, StringComparison.Ordinal);

        var recordSourceRequiredPath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Example",
            "Migrations.SqlServer",
            "Migrations",
            "20260828082459_Example_000005_RecordSourceRequired.cs");
        var recordSourceRequiredSource = File.ReadAllText(recordSourceRequiredPath);
        Assert.Equal(
            recordSourceRequired.GetProperty("contentDigest").GetString(),
            MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(recordSourceRequiredPath)));
        Assert.Contains("migrationBuilder.AlterColumn", recordSourceRequiredSource, StringComparison.Ordinal);
        Assert.Contains("nullable: false", recordSourceRequiredSource, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValue", recordSourceRequiredSource, StringComparison.Ordinal);

        var snapshotPath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Example",
            "Migrations.SqlServer",
            "Migrations",
            "EfPlatformDbContextModelSnapshot.cs");
        var snapshotSource = File.ReadAllText(snapshotPath);
        Assert.Contains("Property<string>(\"RecordSource\")", snapshotSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HasDefaultValue", snapshotSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ExampleDesignTimeFactory_ContainsNoCommittedSqlPassword()
    {
        var factoryPath = Path.Combine(
            FindRepositoryRoot(),
            "server",
            "modules",
            "Example",
            "Migrations.SqlServer",
            "ExampleDesignTimeDbContextFactory.cs");
        var source = File.ReadAllText(factoryPath);

        Assert.DoesNotContain("Password=", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(localdb)", source, StringComparison.OrdinalIgnoreCase);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ICP-Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}

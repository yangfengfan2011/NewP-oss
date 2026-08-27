using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICP.Platform.Modules.Example.Migrations.SqlServer.Migrations;
using ICP.Platform.Modules.Example.Persistence.Descriptors;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.Planning;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

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
        Assert.Equal(["CreatedAt", "CreatedBy", "ECODE", "EID", "ENAME", "ReviewerUserEID", "UpdatedAt", "UpdatedBy"],
            model.Fields.Select(static field => field.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            "a5464035b4271ab8c1495edba7bfab00625a9ebb65978c459f05efd0758c4d9c",
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
        Assert.Equal(["CreatedAt", "CreatedBy", "ECODE", "EID", "ENAME", "ReviewerUserEID", "UpdatedAt", "UpdatedBy"],
            model.Fields.Select(static field => field.Name).Order(StringComparer.Ordinal));

        var stream = Assert.Single(plan.Streams, static candidate => candidate.ModuleId == "Example");
        Assert.False(stream.IsPlatformStream);
        Assert.Equal(PersistenceProvider.SqlServer, stream.Provider);
        Assert.Equal("a5464035b4271ab8c1495edba7bfab00625a9ebb65978c459f05efd0758c4d9c", stream.ModelDigest);
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
                Assert.Equal(stream.ModelDigest, migration.ToModelDigest);
                Assert.Equal("safe-expand", migration.Compatibility);
            });
    }

    [Fact]
    public void ExampleSqlServerManifest_DeclaresDeterministicLinearExpansion()
    {
        var repositoryRoot = FindRepositoryRoot();
        var manifestPath = Path.Combine(
            repositoryRoot,
            "modules",
            "Example",
            "migrations",
            "Example.SqlServerMigrations.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var migrations = root.GetProperty("migrations").EnumerateArray().ToArray();
        Assert.Equal(2, migrations.Length);
        var foundation = migrations[0];
        var reviewerUser = migrations[1];

        Assert.Equal("Example", root.GetProperty("moduleId").GetString());
        Assert.Equal("a5464035b4271ab8c1495edba7bfab00625a9ebb65978c459f05efd0758c4d9c",
            root.GetProperty("modelDigest").GetString());
        Assert.Equal(new string('0', 64), foundation.GetProperty("fromModelDigest").GetString());
        Assert.Equal("4c30a1bbfe2e587016b0bb1a84d3115c497a5b70fecf21e6bcd2ded5702a46a8", foundation.GetProperty("toModelDigest").GetString());
        Assert.Equal("safe-expand", foundation.GetProperty("compatibility").GetString());
        Assert.Equal(JsonValueKind.Null, foundation.GetProperty("previousContentDigest").ValueKind);
        Assert.Equal("4c30a1bbfe2e587016b0bb1a84d3115c497a5b70fecf21e6bcd2ded5702a46a8", reviewerUser.GetProperty("fromModelDigest").GetString());
        Assert.Equal(root.GetProperty("modelDigest").GetString(), reviewerUser.GetProperty("toModelDigest").GetString());
        Assert.Equal("safe-expand", reviewerUser.GetProperty("compatibility").GetString());
        Assert.Equal(
            foundation.GetProperty("contentDigest").GetString(),
            reviewerUser.GetProperty("previousContentDigest").GetString());
        Assert.Equal(
            root.GetProperty("bundleChecksum").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(
                typeof(Example_000002_ReviewerUserEID).Assembly.Location))));

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
            Convert.ToHexStringLower(SHA256.HashData(ReadCanonicalSourceBytes(foundationPath))));
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
            Convert.ToHexStringLower(SHA256.HashData(ReadCanonicalSourceBytes(reviewerUserPath))));
        Assert.Equal(1, Count(reviewerUserSource, "migrationBuilder.AddColumn"));
        Assert.Contains("ReviewerUserEID", reviewerUserSource, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValue", reviewerUserSource, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValueSql", reviewerUserSource, StringComparison.Ordinal);
        Assert.DoesNotContain("migrationBuilder.Sql", reviewerUserSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlOperation", reviewerUserSource, StringComparison.Ordinal);
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

    private static byte[] ReadCanonicalSourceBytes(string sourcePath)
    {
        var source = File.ReadAllText(sourcePath)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return Encoding.UTF8.GetBytes(source);
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

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
        Assert.Equal(["CreatedAt", "CreatedBy", "ECODE", "EID", "ENAME", "UpdatedAt", "UpdatedBy"],
            model.Fields.Select(static field => field.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            "4c30a1bbfe2e587016b0bb1a84d3115c497a5b70fecf21e6bcd2ded5702a46a8",
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
        Assert.Equal(["CreatedAt", "CreatedBy", "ECODE", "EID", "ENAME", "UpdatedAt", "UpdatedBy"],
            model.Fields.Select(static field => field.Name).Order(StringComparer.Ordinal));

        var stream = Assert.Single(plan.Streams, static candidate => candidate.ModuleId == "Example");
        Assert.False(stream.IsPlatformStream);
        Assert.Equal(PersistenceProvider.SqlServer, stream.Provider);
        var migration = Assert.Single(stream.Migrations);
        Assert.Equal("000001", migration.Sequence);
        Assert.EndsWith("_Example_000001_Foundation", migration.MigrationId, StringComparison.Ordinal);
        Assert.Equal(new string('0', 64), migration.FromModelDigest);
        Assert.Equal(stream.ModelDigest, migration.ToModelDigest);
        Assert.Equal("safe-expand", migration.Compatibility);
    }

    [Fact]
    public void ExampleSqlServerManifest_DeclaresDeterministicFoundationFromZero()
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
        var migration = Assert.Single(root.GetProperty("migrations").EnumerateArray());

        Assert.Equal("Example", root.GetProperty("moduleId").GetString());
        Assert.Equal("4c30a1bbfe2e587016b0bb1a84d3115c497a5b70fecf21e6bcd2ded5702a46a8",
            root.GetProperty("modelDigest").GetString());
        Assert.Equal(new string('0', 64), migration.GetProperty("fromModelDigest").GetString());
        Assert.Equal("safe-expand", migration.GetProperty("compatibility").GetString());
        Assert.Equal(root.GetProperty("modelDigest").GetString(), migration.GetProperty("toModelDigest").GetString());
        Assert.Equal(JsonValueKind.Null, migration.GetProperty("previousContentDigest").ValueKind);
        Assert.Equal(
            root.GetProperty("bundleChecksum").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(
                typeof(Example_000001_Foundation).Assembly.Location))));

        var migrationDirectory = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Example",
            "Migrations.SqlServer",
            "Migrations");
        var sourcePath = Assert.Single(Directory.GetFiles(migrationDirectory, "*_Foundation.cs"));
        var source = File.ReadAllText(sourcePath);
        Assert.Equal(
            migration.GetProperty("contentDigest").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(ReadCanonicalSourceBytes(sourcePath))));
        Assert.Equal(1, Count(source, "migrationBuilder.CreateTable"));
        Assert.Contains("name: \"ExampleRecord\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValue", source, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValueSql", source, StringComparison.Ordinal);
        Assert.DoesNotContain("migrationBuilder.Sql", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlOperation", source, StringComparison.Ordinal);
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

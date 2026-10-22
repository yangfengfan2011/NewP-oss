using Xunit;

namespace ICP.Platform.Persistence.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class SqlServerMigrationContractTests
{
    [Fact]
    public void CurrentInitialMigrations_ContainNoDefaultsOrRawDataOperations()
    {
        var repositoryRoot = FindRepositoryRoot();
        var migrationPaths = new[]
        {
            Path.Combine(repositoryRoot, "server", "platform-persistence-efcore.migrations.sqlserver", "Migrations", "20260812144154_PlatformFoundation.cs"),
            Path.Combine(repositoryRoot, "server", "modules", "Platform", "Organization", "Migrations.SqlServer", "Migrations", "20260812144235_Organization_000001_Foundation.cs")
        };

        foreach (var migrationPath in migrationPaths)
        {
            var source = File.ReadAllText(migrationPath);
            Assert.DoesNotContain("defaultValue", source, StringComparison.Ordinal);
            Assert.DoesNotContain("defaultValueSql", source, StringComparison.Ordinal);
            Assert.DoesNotContain("migrationBuilder.Sql", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SqlOperation", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DesignTimeFactory_DoesNotContainCommittedSqlPassword()
    {
        var factoryPath = Path.Combine(
            FindRepositoryRoot(),
            "server",
            "platform-persistence-efcore.migrations.sqlserver",
            "SqlServerDesignTimeDbContextFactory.cs");
        var source = File.ReadAllText(factoryPath);

        Assert.DoesNotContain("Password=", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(localdb)", source, StringComparison.OrdinalIgnoreCase);
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

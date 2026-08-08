using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class SqlServerMigrationContractTests
{
    [Fact]
    public void LatestMigration_RenamesProtocolFieldsAndBackfillsPersonNameBeforeDrop()
    {
        var repositoryRoot = FindRepositoryRoot();
        var migrationPath = Path.Combine(
            repositoryRoot,
            "server",
            "platform-persistence-efcore.migrations.sqlserver",
            "Migrations",
            "20260808054041_AlignOrganizationSchemaDrift.cs");
        var source = File.ReadAllText(migrationPath);

        Assert.Contains("UPDATE [Person] SET [ENAME] = [Name]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("computedColumnSql:", source, StringComparison.Ordinal);
        Assert.Contains("newName: \"ECODE\"", source, StringComparison.Ordinal);
        Assert.Contains("newName: \"ENAME\"", source, StringComparison.Ordinal);

        var backfill = source.IndexOf("UPDATE [Person] SET [ENAME] = [Name]", StringComparison.Ordinal);
        var dropLegacyPersonName = source.IndexOf("migrationBuilder.DropColumn(", backfill, StringComparison.Ordinal);
        Assert.True(backfill >= 0 && dropLegacyPersonName > backfill);
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

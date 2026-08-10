using ICP.Platform.Persistence.Composition;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class IdentityFoundationMigrationTests
{
    [Fact]
    public void LegacyDesignModel_IncludesAllRequiredIdentityFoundationModels()
    {
        var definitions = PlatformCoreLegacyModelSource.GetDefinitions();

        Assert.Contains(definitions, static model => model is { OwnerModuleId: "Identity", Name: "User" });
        Assert.Contains(definitions, static model => model is { OwnerModuleId: "Identity", Name: "LocalCredential" });
        Assert.Contains(definitions, static model => model is { OwnerModuleId: "Identity", Name: "IdentitySecurityEvent" });
        Assert.Contains(definitions, static model => model is { OwnerModuleId: "Authentication", Name: "AuthenticationSession" });

        var adapted = PlatformPersistenceModelCatalog.AdaptPlatformCore(definitions);
        Assert.All(
            adapted.SelectMany(static model => model.ForeignKeys),
            static foreignKey => Assert.StartsWith("platform.core/", foreignKey.PrincipalModelKey, StringComparison.Ordinal));
    }

    [Fact]
    public void SqlServerMigrationDesignModel_PreservesFrozenLegacyShapeAndKeepsNewAuditsStrict()
    {
        var adapted = PlatformPersistenceModelCatalog.AdaptSqlServerMigrationBaseline(
            PlatformCoreLegacyModelSource.GetDefinitions());

        var legacyModel = Assert.Single(adapted, static model => model.Name == "Company");
        Assert.Equal(string.Empty, legacyModel.OwnerModuleId);
        Assert.Equal(string.Empty, legacyModel.SchemaName);
        Assert.False(Assert.Single(legacyModel.Fields, static field => field.Name == "CreatedBy").Required);
        Assert.False(Assert.Single(legacyModel.Fields, static field => field.Name == "UpdatedBy").Required);

        var newModel = Assert.Single(adapted, static model => model.Name == "User");
        Assert.Equal("dbo", newModel.SchemaName);
        Assert.All(
            newModel.Fields.Where(static field => field.IsAuditField),
            static field => Assert.True(field.Required));
        Assert.All(
            adapted.SelectMany(static model => model.ForeignKeys),
            static foreignKey => Assert.StartsWith("platform.core/", foreignKey.PrincipalModelKey, StringComparison.Ordinal));
    }

    [Fact]
    public void SqlServerMigration_CreatesRequiredTablesIndexesForeignKeysAndNonNullAuditColumns()
    {
        var migrationDirectory = Path.Combine(
            FindRepositoryRoot(),
            "server",
            "platform-persistence-efcore.migrations.sqlserver",
            "Migrations");
        var migrationPath = Assert.Single(Directory.GetFiles(migrationDirectory, "*_AddIdentityFoundation.cs"));
        var source = File.ReadAllText(migrationPath);

        foreach (var table in new[] { "User", "LocalCredential", "IdentitySecurityEvent", "AuthenticationSession" })
        {
            Assert.Contains($"name: \"{table}\"", source, StringComparison.Ordinal);
        }

        Assert.Contains("name: \"UX_User_NormalizedECODE\"", source, StringComparison.Ordinal);
        Assert.Contains("name: \"UX_LocalCredential_UserEID\"", source, StringComparison.Ordinal);
        Assert.Contains("name: \"UX_AuthenticationSession_TokenHash\"", source, StringComparison.Ordinal);
        Assert.Contains("name: \"FK_LocalCredential_User\"", source, StringComparison.Ordinal);
        Assert.Contains("name: \"FK_AuthenticationSession_User\"", source, StringComparison.Ordinal);
        Assert.Contains("RevokedAt = table.Column<DateTime>(type: \"datetime2\", nullable: true)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RenameTable", source, StringComparison.Ordinal);
        Assert.Equal(4, Count(source, "CreatedBy = table.Column<string>(type: \"nvarchar(128)\", maxLength: 128, nullable: false)"));
        Assert.Equal(4, Count(source, "UpdatedBy = table.Column<string>(type: \"nvarchar(128)\", maxLength: 128, nullable: false)"));
        Assert.Equal(4, Count(source, "CreatedAt = table.Column<DateTime>(type: \"datetime2\", nullable: false)"));
        Assert.Equal(4, Count(source, "UpdatedAt = table.Column<DateTime>(type: \"datetime2\", nullable: false)"));
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

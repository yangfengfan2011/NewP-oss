using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICP.Platform.Modules.Authentication.Migrations.SqlServer.Migrations;
using ICP.Platform.Modules.Identity.Migrations.SqlServer.Migrations;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.Planning;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class IdentityFoundationMigrationTests
{
    [Fact]
    public void SqlServerLegacyFrontier_RemainsFrozenBeforeOrganizationOwnedMigrations()
    {
        Assert.Equal(
            "20260808054041_AlignOrganizationSchemaDrift",
            PlatformCoreMigrationFrontiers.SqlServerLegacyBaseline);
    }

    [Fact]
    public void OrganizationMigrationModelDigests_ArePinnedToOwnedMetadataStages()
    {
        var definitions = new OrganizationModelMetadataProvider().GetDefinitions()
            .Select(static definition => definition with { OwnerModuleId = "Organization", SchemaName = "dbo" })
            .ToArray();
        var finalDigest = PersistenceModelDigest.Compute(definitions);
        var intermediateDigest = PersistenceModelDigest.Compute(definitions.Select(static definition =>
            definition.Name == "Person"
                ? definition with { Fields = definition.Fields.Where(static field => field.Name != "Revision").ToArray() }
                : definition));

        Assert.Equal("0aa9f2e2956c6f4e094224d65b65ca182c980278cfcf95ff368d9f4f67b9de58", finalDigest);
        Assert.Equal("967cfb3295a645bf6d85d90a9e25523297e75171176732263f437fd27cb3e6b3", intermediateDigest);
    }

    [Fact]
    public void LegacyDesignModel_ExcludesModuleOwnedIdentityFoundationModels()
    {
        var definitions = PlatformCoreLegacyModelSource.GetDefinitions();

        Assert.DoesNotContain(definitions, static model => model.OwnerModuleId is "Identity" or "Authentication");

        var adapted = PlatformPersistenceModelCatalog.AdaptPlatformCore(definitions);
        Assert.All(
            adapted.SelectMany(static model => model.ForeignKeys),
            static foreignKey => Assert.StartsWith("platform.core/", foreignKey.PrincipalModelKey, StringComparison.Ordinal));
    }

    [Fact]
    public void SqlServerMigrationDesignModel_PreservesFrozenLegacyShape()
    {
        var adapted = PlatformPersistenceModelCatalog.AdaptSqlServerMigrationBaseline(
            PlatformCoreLegacyModelSource.GetDefinitions());

        var legacyModel = Assert.Single(adapted, static model => model.Name == "Company");
        Assert.Equal(string.Empty, legacyModel.OwnerModuleId);
        Assert.Equal(string.Empty, legacyModel.SchemaName);
        Assert.False(Assert.Single(legacyModel.Fields, static field => field.Name == "CreatedBy").Required);
        Assert.False(Assert.Single(legacyModel.Fields, static field => field.Name == "UpdatedBy").Required);

        Assert.All(
            adapted.SelectMany(static model => model.ForeignKeys),
            static foreignKey => Assert.StartsWith("platform.core/", foreignKey.PrincipalModelKey, StringComparison.Ordinal));
    }

    [Fact]
    public void SqlServerMigration_CreatesRequiredTablesIndexesForeignKeysAndNonNullAuditColumns()
    {
        var repositoryRoot = FindRepositoryRoot();
        var identityPath = Assert.Single(Directory.GetFiles(
            Path.Combine(repositoryRoot, "server", "modules", "Identity", "Migrations.SqlServer", "Migrations"),
            "*_Foundation.cs"));
        var authenticationPath = Assert.Single(Directory.GetFiles(
            Path.Combine(repositoryRoot, "server", "modules", "Authentication", "Migrations.SqlServer", "Migrations"),
            "*_Foundation.cs"));
        var source = File.ReadAllText(identityPath) + File.ReadAllText(authenticationPath);

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

    [Fact]
    public void SqlServerMigrationManifests_PinBuiltBundleAndSourceDigests()
    {
        var repositoryRoot = FindRepositoryRoot();
        AssertManifest(
            repositoryRoot,
            "Identity",
            "Identity.SqlServerMigrations.json",
            typeof(IdentityFoundation).Assembly.Location,
            "20260810033908_Identity_000001_Foundation.cs");
        AssertManifest(
            repositoryRoot,
            "Authentication",
            "Authentication.SqlServerMigrations.json",
            typeof(AuthenticationFoundation).Assembly.Location,
            "20260810033909_Authentication_000001_Foundation.cs");
        AssertOrganizationManifest(repositoryRoot);
    }

    [Fact]
    public void SqlServerMigrationProjects_MapAbsoluteSourcePathsToStableVirtualRoots()
    {
        var repositoryRoot = FindRepositoryRoot();

        AssertPathMap(
            repositoryRoot,
            "Identity",
            "$(MSBuildProjectDirectory)=/_/Identity.SqlServerMigrations");
        AssertPathMap(
            repositoryRoot,
            "Authentication",
            "$(MSBuildProjectDirectory)=/_/Authentication.SqlServerMigrations");
        AssertPathMap(
            repositoryRoot,
            "Organization",
            "$(MSBuildProjectDirectory)=/_/Organization.SqlServerMigrations");
        AssertSourceRevisionIsExcluded(repositoryRoot, "Identity");
        AssertSourceRevisionIsExcluded(repositoryRoot, "Authentication");
        AssertSourceRevisionIsExcluded(repositoryRoot, "Organization");
        Assert.True(File.Exists(Path.Combine(
            repositoryRoot,
            "tests",
            "integration",
            "verify-migration-bundle-determinism.ps1")));
    }

    [Fact]
    public void OrganizationMigrations_AreOwnedByModuleAndUseBackupOnlyRollback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreDirectory = Path.Combine(repositoryRoot, "server", "platform-persistence-efcore.migrations.sqlserver", "Migrations");
        var organizationDirectory = Path.Combine(repositoryRoot, "server", "modules", "Organization", "Migrations.SqlServer", "Migrations");

        Assert.Empty(Directory.GetFiles(coreDirectory, "*_Organization_*.cs"));
        var sources = Directory.GetFiles(organizationDirectory, "*.cs").OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, sources.Length);
        var source = string.Join('\n', sources.Select(File.ReadAllText));
        Assert.Contains("DropTable(name: \"PersonUserLink\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", source, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, Count(source, "protected override void Down"));
        Assert.DoesNotContain("DropColumn", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateTable", source, StringComparison.Ordinal);
    }

    private static void AssertOrganizationManifest(string repositoryRoot)
    {
        var manifestPath = Path.Combine(repositoryRoot, "modules", "Organization", "migrations", "Organization.SqlServerMigrations.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var migrations = root.GetProperty("migrations").EnumerateArray().ToArray();
        Assert.Equal(2, migrations.Length);
        Assert.Equal(
            root.GetProperty("bundleChecksum").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(OrganizationPermanentUserBinding).Assembly.Location))));

        var sourceNames = new[]
        {
            "20260810102141_Organization_000001_PermanentUserBinding.cs",
            "20260810111523_Organization_000002_PersonLifecycleRevision.cs"
        };
        for (var index = 0; index < migrations.Length; index++)
        {
            var sourcePath = Path.Combine(
                repositoryRoot,
                "server",
                "modules",
                "Organization",
                "Migrations.SqlServer",
                "Migrations",
                sourceNames[index]);
            Assert.Equal(
                migrations[index].GetProperty("contentDigest").GetString(),
                Convert.ToHexStringLower(SHA256.HashData(ReadCanonicalSourceBytes(sourcePath))));
        }

        Assert.Equal(
            migrations[0].GetProperty("contentDigest").GetString(),
            migrations[1].GetProperty("previousContentDigest").GetString());
        Assert.Equal(
            migrations[0].GetProperty("toModelDigest").GetString(),
            migrations[1].GetProperty("fromModelDigest").GetString());
        Assert.Equal(root.GetProperty("modelDigest").GetString(), migrations[1].GetProperty("toModelDigest").GetString());
    }

    private static void AssertManifest(
        string repositoryRoot,
        string moduleId,
        string manifestName,
        string assemblyPath,
        string migrationSourceName)
    {
        var manifestPath = Path.Combine(repositoryRoot, "modules", moduleId, "migrations", manifestName);
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var migration = Assert.Single(root.GetProperty("migrations").EnumerateArray());
        var sourcePath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            moduleId,
            "Migrations.SqlServer",
            "Migrations",
            migrationSourceName);

        Assert.Equal(
            root.GetProperty("bundleChecksum").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assemblyPath))));
        Assert.Equal(
            migration.GetProperty("contentDigest").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(ReadCanonicalSourceBytes(sourcePath))));
    }

    private static void AssertPathMap(string repositoryRoot, string moduleId, string expectedPathMap)
    {
        var projectPath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            moduleId,
            "Migrations.SqlServer",
            $"{moduleId}.SqlServerMigrations.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.Contains($"<PathMap>{expectedPathMap}</PathMap>", project, StringComparison.Ordinal);
    }

    private static void AssertSourceRevisionIsExcluded(string repositoryRoot, string moduleId)
    {
        var projectPath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            moduleId,
            "Migrations.SqlServer",
            $"{moduleId}.SqlServerMigrations.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.Contains(
            "<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>",
            project,
            StringComparison.Ordinal);
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

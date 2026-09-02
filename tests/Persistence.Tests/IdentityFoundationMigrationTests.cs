using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICP.Platform.Modules.Authentication.Migrations.SqlServer.Migrations;
using ICP.Platform.Modules.Identity.Migrations.SqlServer.Migrations;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Migrations.SqlServer.Migrations;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Persistence.ProviderTargets;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class IdentityFoundationMigrationTests
{
    [Fact]
    public void SqlServerPlatformFoundationMigration_StartsFromZeroAndExcludesOrganization()
    {
        var repositoryRoot = FindRepositoryRoot();
        var migrationDirectory = Path.Combine(
            repositoryRoot,
            "server",
            "platform-persistence-efcore.migrations.sqlserver",
            "Migrations");
        var migrationPath = Assert.Single(Directory.GetFiles(
            migrationDirectory,
            "*_PlatformFoundation.cs"));
        var source = File.ReadAllText(migrationPath);

        foreach (var table in new[] { "ConfigItem", "ValueListItem", "Role", "Permission", "RolePermission", "UserRole" })
        {
            Assert.Contains($"name: \"{table}\"", source, StringComparison.Ordinal);
        }

        foreach (var table in new[] { "Company", "Department", "Person", "Position" })
        {
            Assert.DoesNotContain($"name: \"{table}\"", source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("defaultValue", source, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValueSql", source, StringComparison.Ordinal);
        Assert.DoesNotContain("migrationBuilder.Sql", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformFoundationModelDigest_IsPinnedToAuthorizationAndConfigCenter()
    {
        var definitions = PlatformFoundationModelSource.GetDefinitions()
            .Select(static definition => definition with
            {
                OwnerModuleId = definition.OwnerModuleId,
                SchemaName = "dbo"
            })
            .ToArray();

        Assert.Equal(
            "038748ee556ade48658e64abc43230578eec7d4b9a7aab3429034bc53d7cf666",
            PersistenceModelDigest.Compute(definitions));
    }

    [Fact]
    public void SqlServerPlatformFoundationFrontier_IsCurrentEmptyDatabaseMigration()
    {
        Assert.Equal(
            "20260812144154_PlatformFoundation",
            PlatformCoreMigrationFrontiers.SqlServerPlatformFoundation);
    }

    [Fact]
    public void OrganizationMigrationModelDigests_ArePinnedToOwnedMetadataStages()
    {
        var definitions = new OrganizationModelMetadataProvider().GetDefinitions()
            .Select(static definition => definition with { OwnerModuleId = "Organization", SchemaName = "dbo" })
            .ToArray();
        var finalDigest = PersistenceModelDigest.Compute(definitions);
        Assert.Equal("56c4c80c1bb8d15cba760f463ce19dacb9cacd997db6206f4fa5770306c55621", finalDigest);
    }

    [Fact]
    public void PlatformFoundationDesignModel_ContainsOnlyAuthorizationAndConfigCenter()
    {
        var definitions = PlatformFoundationModelSource.GetDefinitions();

        Assert.Equal(
            ["ConfigItem", "Permission", "Role", "RolePermission", "UserRole", "ValueListItem"],
            definitions.Select(static model => model.Name).Order(StringComparer.Ordinal));

        var adapted = PlatformPersistenceModelCatalog.AdaptPlatformCore(definitions);
        Assert.All(
            adapted.SelectMany(static model => model.ForeignKeys),
            static foreignKey => Assert.StartsWith("platform.core/", foreignKey.PrincipalModelKey, StringComparison.Ordinal));
    }

    [Fact]
    public void SqlServerMigrationDesignModel_UsesCurrentFoundationShape()
    {
        var adapted = PlatformPersistenceModelCatalog.AdaptSqlServerPlatformFoundation(
            PlatformFoundationModelSource.GetDefinitions());

        var model = Assert.Single(adapted, static model => model.Name == "Role");
        Assert.Equal(string.Empty, model.OwnerModuleId);
        Assert.Equal(string.Empty, model.SchemaName);
        Assert.True(Assert.Single(model.Fields, static field => field.Name == "CreatedBy").Required);
        Assert.True(Assert.Single(model.Fields, static field => field.Name == "UpdatedBy").Required);

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

        foreach (var table in new[] { "IdentityAccount", "LocalCredential", "IdentitySecurityEvent", "AuthenticationSession" })
        {
            Assert.Contains($"name: \"{table}\"", source, StringComparison.Ordinal);
        }

        Assert.Contains("name: \"UX_IdentityAccount_NormalizedECODE\"", source, StringComparison.Ordinal);
        Assert.Contains("name: \"UX_LocalCredential_AccountEID\"", source, StringComparison.Ordinal);
        Assert.Contains("name: \"UX_AuthenticationSession_TokenHash\"", source, StringComparison.Ordinal);
        Assert.Contains("TimeZoneId = table.Column<string>(type: \"nvarchar(256)\", maxLength: 256, nullable: false)", source, StringComparison.Ordinal);
        Assert.Contains("name: \"FK_LocalCredential_IdentityAccount\"", source, StringComparison.Ordinal);
        Assert.Contains("name: \"FK_AuthenticationSession_IdentityAccount\"", source, StringComparison.Ordinal);
        Assert.Contains("RevokedAt = table.Column<DateTime>(type: \"datetime2\", nullable: true)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AlterColumn", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RenameTable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValue", source, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValueSql", source, StringComparison.Ordinal);
        Assert.DoesNotContain("migrationBuilder.Sql", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlOperation", source, StringComparison.Ordinal);
        Assert.Equal(4, Count(source, "CreatedBy = table.Column<string>(type: \"nvarchar(128)\", maxLength: 128, nullable: false)"));
        Assert.Equal(4, Count(source, "UpdatedBy = table.Column<string>(type: \"nvarchar(128)\", maxLength: 128, nullable: false)"));
        Assert.Equal(4, Count(source, "CreatedAt = table.Column<DateTime>(type: \"datetime2\", nullable: false)"));
        Assert.Equal(4, Count(source, "UpdatedAt = table.Column<DateTime>(type: \"datetime2\", nullable: false)"));
    }

    [Fact]
    public void SqlServerMigrationManifests_PinBuiltBundleAndSourceDigests()
    {
        var repositoryRoot = FindRepositoryRoot();
        AssertPlatformFoundationManifest(repositoryRoot);
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
            "20260828151500_Authentication_000001_Foundation.cs");
        AssertOrganizationManifest(repositoryRoot);
    }

    [Fact]
    public void SqlServerMigrationProjects_MapAbsoluteSourcePathsToStableVirtualRoots()
    {
        var repositoryRoot = FindRepositoryRoot();

        AssertPlatformFoundationPathMap(repositoryRoot);
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

    private static void AssertPlatformFoundationPathMap(string repositoryRoot)
    {
        var projectPath = Path.Combine(
            repositoryRoot,
            "server",
            "platform-persistence-efcore.migrations.sqlserver",
            "PlatformPersistence.EfCore.SqlServerMigrations.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.Contains(
            "$(MSBuildProjectDirectory)=/_/PlatformPersistence.EfCore.SqlServerMigrations",
            project,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OrganizationMigration_StartsFromZeroAndCreatesCurrentSchemaWithoutLegacyCutover()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreDirectory = Path.Combine(repositoryRoot, "server", "platform-persistence-efcore.migrations.sqlserver", "Migrations");
        var organizationDirectory = Path.Combine(repositoryRoot, "server", "modules", "Organization", "Migrations.SqlServer", "Migrations");

        Assert.Empty(Directory.GetFiles(coreDirectory, "*_Organization_*.cs"));
        var sourcePath = Assert.Single(Directory.GetFiles(organizationDirectory, "*_Foundation.cs"));
        var source = File.ReadAllText(sourcePath);
        foreach (var table in new[] { "Company", "Department", "Person", "Position" })
        {
            Assert.Contains($"name: \"{table}\"", source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("PersonUserLink", source, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValue", source, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValueSql", source, StringComparison.Ordinal);
        Assert.DoesNotContain("migrationBuilder.Sql", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlOperation", source, StringComparison.Ordinal);
        Assert.Equal(1, Count(source, "protected override void Down"));
        Assert.DoesNotContain("DropColumn", source, StringComparison.Ordinal);
        Assert.Equal(4, Count(source, "migrationBuilder.CreateTable"));
    }

    [Fact]
    public void OrganizationPositionDirectoryMigration_ChangesOnlyTheOwnedPositionShape()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Organization",
            "Migrations.SqlServer",
            "Migrations",
            "20260902052903_Organization_000002_GlobalPositionDirectory.cs");
        var source = File.ReadAllText(sourcePath);
        var up = source[..source.IndexOf("protected override void Down", StringComparison.Ordinal)];

        foreach (var column in new[] { "CompanyId", "DepartmentId", "ECODE", "EmploymentStatus", "SortOrder" })
        {
            Assert.Contains($"name: \"{column}\"", up, StringComparison.Ordinal);
        }

        Assert.Contains("name: \"Description\"", up, StringComparison.Ordinal);
        Assert.Contains("type: \"nvarchar(256)\"", up, StringComparison.Ordinal);
        Assert.Contains("nullable: true", up, StringComparison.Ordinal);
        Assert.Contains("name: \"UX_Position_ENAME\"", up, StringComparison.Ordinal);
        Assert.Contains("unique: true", up, StringComparison.Ordinal);
        Assert.DoesNotContain("Person", up, StringComparison.Ordinal);
        Assert.DoesNotContain("AddForeignKey", up, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValue", up, StringComparison.Ordinal);
        Assert.DoesNotContain("migrationBuilder.Sql", up, StringComparison.Ordinal);
    }

    [Fact]
    public void OrganizationPositionDirectoryMigration_PinsDroppedProviderObjects()
    {
        var migration = new Organization_000002_GlobalPositionDirectory();
        var droppedColumns = migration.UpOperations
            .OfType<DropColumnOperation>()
            .ToDictionary(static operation => operation.Name, StringComparer.Ordinal);
        var droppedIndexes = migration.UpOperations
            .OfType<DropIndexOperation>()
            .ToDictionary(static operation => operation.Name, StringComparer.Ordinal);

        Assert.Equal(5, droppedColumns.Count);
        Assert.Equal(2, droppedIndexes.Count);
        Assert.Equal(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["CompanyId"] = 1,
                ["DepartmentId"] = 3,
                ["ECODE"] = 3,
                ["EmploymentStatus"] = 4,
                ["SortOrder"] = 5
            },
            droppedColumns.ToDictionary(
                static entry => entry.Key,
                static entry => ReadFromDescriptor(entry.Value).Ordinal!.Value,
                StringComparer.Ordinal));
        Assert.All(droppedColumns, static entry =>
        {
            var descriptor = ReadFromDescriptor(entry.Value);
            Assert.Equal("Organization", descriptor.OwnerModuleId);
            Assert.Equal(entry.Key, descriptor.Column!.Name);
            Assert.False(descriptor.Column.IsNullable);
        });
        Assert.All(droppedIndexes, static entry =>
        {
            var descriptor = ReadFromDescriptor(entry.Value);
            Assert.Equal("Organization", descriptor.OwnerModuleId);
            Assert.Equal(entry.Key, descriptor.Index!.Name);
            Assert.False(descriptor.Index.IsUnique);
        });
    }

    [Fact]
    public void OrganizationPositionDirectoryMigration_DownRequiresVerifiedBackupRestore()
    {
        var migration = new Organization_000002_GlobalPositionDirectory();

        var exception = Assert.Throws<NotSupportedException>(() => _ = migration.DownOperations);

        Assert.Contains("backup-restore-only", exception.Message, StringComparison.Ordinal);
        Assert.Contains("verified database backup", exception.Message, StringComparison.Ordinal);

        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Organization",
            "Migrations.SqlServer",
            "Migrations",
            "20260902052903_Organization_000002_GlobalPositionDirectory.cs");
        var source = File.ReadAllText(sourcePath);
        var down = source[source.IndexOf("protected override void Down", StringComparison.Ordinal)..];
        Assert.DoesNotContain("AddColumn", down, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValue", down, StringComparison.Ordinal);
    }

    [Fact]
    public void OrganizationPositionReferenceMigration_AddsOnlyNullableRestrictForeignKey()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Organization",
            "Migrations.SqlServer",
            "Migrations",
            "20260902063356_Organization_000003_PositionReference.cs");
        var source = File.ReadAllText(sourcePath);
        var up = source[..source.IndexOf("protected override void Down", StringComparison.Ordinal)];

        Assert.Contains("name: \"IX_Person_PositionId\"", up, StringComparison.Ordinal);
        Assert.Contains("name: \"FK_Person_Position\"", up, StringComparison.Ordinal);
        Assert.Contains("table: \"Person\"", up, StringComparison.Ordinal);
        Assert.Contains("column: \"PositionId\"", up, StringComparison.Ordinal);
        Assert.Contains("principalTable: \"Position\"", up, StringComparison.Ordinal);
        Assert.Contains("principalColumn: \"EID\"", up, StringComparison.Ordinal);
        Assert.Contains("onDelete: ReferentialAction.Restrict", up, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateData", up, StringComparison.Ordinal);
        Assert.DoesNotContain("migrationBuilder.Sql", up, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValue", up, StringComparison.Ordinal);

        var snapshotPath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Organization",
            "Migrations.SqlServer",
            "Migrations",
            "EfPlatformDbContextModelSnapshot.cs");
        var snapshot = File.ReadAllText(snapshotPath);
        Assert.Contains(".HasForeignKey(\"PositionId\")", snapshot, StringComparison.Ordinal);
        Assert.Contains(".OnDelete(DeleteBehavior.Restrict)", snapshot, StringComparison.Ordinal);
        Assert.Contains(".HasConstraintName(\"FK_Person_Position\")", snapshot, StringComparison.Ordinal);
    }

    private static void AssertOrganizationManifest(string repositoryRoot)
    {
        var manifestPath = Path.Combine(repositoryRoot, "modules", "Organization", "migrations", "Organization.SqlServerMigrations.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var migrations = root.GetProperty("migrations").EnumerateArray().ToArray();
        Assert.Equal(3, migrations.Length);
        var foundation = migrations[0];
        var positionDirectory = migrations[1];
        var positionReference = migrations[2];
        Assert.False(root.TryGetProperty("legacyCutover", out _));
        Assert.Equal("20260812144235_Organization_000001_Foundation", foundation.GetProperty("migrationId").GetString());
        Assert.Equal(new string('0', 64), foundation.GetProperty("fromModelDigest").GetString());
        Assert.Equal("safe-expand", foundation.GetProperty("compatibility").GetString());
        Assert.Equal("24a56838c9395ea3c1afa3dfa3cc8549afda2151e2d8515b6f08617d0679425f", foundation.GetProperty("toModelDigest").GetString());
        Assert.Equal(JsonValueKind.Null, foundation.GetProperty("previousContentDigest").ValueKind);
        Assert.Equal("20260902052903_Organization_000002_GlobalPositionDirectory", positionDirectory.GetProperty("migrationId").GetString());
        Assert.Equal(foundation.GetProperty("contentDigest").GetString(), positionDirectory.GetProperty("previousContentDigest").GetString());
        Assert.Equal(foundation.GetProperty("toModelDigest").GetString(), positionDirectory.GetProperty("fromModelDigest").GetString());
        Assert.Equal("56c4c80c1bb8d15cba760f463ce19dacb9cacd997db6206f4fa5770306c55621", positionDirectory.GetProperty("toModelDigest").GetString());
        Assert.Equal("backup-restore-only", positionDirectory.GetProperty("compatibility").GetString());
        Assert.Equal("20260902063356_Organization_000003_PositionReference", positionReference.GetProperty("migrationId").GetString());
        Assert.Equal(positionDirectory.GetProperty("contentDigest").GetString(), positionReference.GetProperty("previousContentDigest").GetString());
        Assert.Equal(positionDirectory.GetProperty("toModelDigest").GetString(), positionReference.GetProperty("fromModelDigest").GetString());
        Assert.Equal(root.GetProperty("modelDigest").GetString(), positionReference.GetProperty("toModelDigest").GetString());
        Assert.Equal("safe-expand", positionReference.GetProperty("compatibility").GetString());
        Assert.Equal(
            root.GetProperty("bundleChecksum").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Organization_000001_Foundation).Assembly.Location))));

        var foundationSourcePath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Organization",
            "Migrations.SqlServer",
            "Migrations",
            "20260812144235_Organization_000001_Foundation.cs");
        Assert.Equal(
            foundation.GetProperty("contentDigest").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(ReadCanonicalSourceBytes(foundationSourcePath))));
        var positionSourcePath = Path.Combine(
            Path.GetDirectoryName(foundationSourcePath)!,
            "20260902052903_Organization_000002_GlobalPositionDirectory.cs");
        Assert.Equal(
            positionDirectory.GetProperty("contentDigest").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(ReadCanonicalSourceBytes(positionSourcePath))));
        var positionReferenceSourcePath = Path.Combine(
            Path.GetDirectoryName(foundationSourcePath)!,
            "20260902063356_Organization_000003_PositionReference.cs");
        Assert.Equal(
            positionReference.GetProperty("contentDigest").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(ReadCanonicalSourceBytes(positionReferenceSourcePath))));
    }

    private static void AssertPlatformFoundationManifest(string repositoryRoot)
    {
        var manifestPath = Path.Combine(
            repositoryRoot,
            "server",
            "platform-persistence-efcore.migrations.sqlserver",
            "PlatformFoundation.SqlServerMigrations.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var migration = Assert.Single(root.GetProperty("migrations").EnumerateArray());
        var sourcePath = Path.Combine(
            repositoryRoot,
            "server",
            "platform-persistence-efcore.migrations.sqlserver",
            "Migrations",
            "20260812144154_PlatformFoundation.cs");

        Assert.Equal("platform.foundation", root.GetProperty("moduleId").GetString());
        Assert.Equal(new string('0', 64), migration.GetProperty("fromModelDigest").GetString());
        Assert.Equal(root.GetProperty("modelDigest").GetString(), migration.GetProperty("toModelDigest").GetString());
        Assert.Equal(
            root.GetProperty("bundleChecksum").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(
                typeof(ICP.Platform.Persistence.EfCore.SqlServerMigrations.Migrations.PlatformFoundation).Assembly.Location))));
        Assert.Equal(
            migration.GetProperty("contentDigest").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(ReadCanonicalSourceBytes(sourcePath))));
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

    private static ProviderTransitionFromDescriptor ReadFromDescriptor(MigrationOperation operation)
    {
        var encoded = Assert.IsType<string>(operation[
            ProviderTransitionAnnotationNames.FromDescriptorV1]);
        return ProviderTransitionDescriptorCodec.Decode(encoded);
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

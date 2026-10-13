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
            "56554470f57d6620d947482f760982f3f6c2473739c2c66696019fefc5aa21a1",
            PersistenceModelDigest.Compute(definitions));
        Assert.Equal("038748ee556ade48658e64abc43230578eec7d4b9a7aab3429034bc53d7cf666",
            PersistenceModelDigest.Compute(FrozenFoundation()));
    }

    [Fact]
    public void SqlServerPlatformFoundationFrontier_IsCurrentEmptyDatabaseMigration()
    {
        Assert.Equal(
            "20260923033732_AddDataScopeObjectGrants",
            PlatformCoreMigrationFrontiers.SqlServerPlatformFoundation);
    }

    [Fact]
    public void OrganizationMigrationModelDigests_ArePinnedToOwnedMetadataStages()
    {
        var definitions = new OrganizationModelMetadataProvider().GetDefinitions()
            .Select(static definition => definition with { OwnerModuleId = "Organization", SchemaName = "dbo" })
            .ToArray();
        var finalDigest = PersistenceModelDigest.Compute(definitions);
        Assert.Equal("6b1b4c75c7d11a0d512023bbd8923c80db7fc3d989f4cb8f93f8433f8e2c29c1", finalDigest);
    }

    [Fact]
    public void OrganizationMigrationManifestModelDigest_MatchesCurrentCompiledModel()
    {
        var definitions = new OrganizationModelMetadataProvider().GetDefinitions()
            .Select(static definition => definition with { OwnerModuleId = "Organization", SchemaName = "dbo" })
            .ToArray();
        var actualDigest = PersistenceModelDigest.Compute(definitions);
        var manifestPath = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(FindRepositoryRoot(), "Organization"),
            "migrations",
            "Organization.SqlServerMigrations.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var expectedDigest = manifest.RootElement.GetProperty("modelDigest").GetString();

        Assert.True(
            string.Equals(expectedDigest, actualDigest, StringComparison.Ordinal),
            $"Organization manifest digest {expectedDigest} does not match compiled model digest {actualDigest}.");
    }

    [Fact]
    public void PlatformFoundationDesignModel_ContainsOnlyAuthorizationAndConfigCenter()
    {
        var definitions = PlatformFoundationModelSource.GetDefinitions();

        Assert.Equal(
            ["ConfigItem", "DataScopeObjectAction", "DataScopeObjectSubject", "DataScopeRuleNode", "DataScopeRuleSubject", "FunctionPoint", "Permission", "Role", "RoleFunctionGrant", "RolePermission", "UserRole", "ValueListItem"],
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
            Path.Combine(repositoryRoot, "server", "modules", "Platform", "Identity", "Migrations.SqlServer", "Migrations"),
            "*_Foundation.cs"));
        var authenticationPath = Assert.Single(Directory.GetFiles(
            Path.Combine(repositoryRoot, "server", "modules", "Platform", "Authentication", "Migrations.SqlServer", "Migrations"),
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
        var organizationDirectory = Path.Combine(repositoryRoot, "server", "modules", "Platform", "Organization", "Migrations.SqlServer", "Migrations");

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
            "Platform",
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
            "Platform",
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
            "Platform",
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
            "Platform",
            "Organization",
            "Migrations.SqlServer",
            "Migrations",
            "EfPlatformDbContextModelSnapshot.cs");
        var snapshot = File.ReadAllText(snapshotPath);
        Assert.Contains(".HasForeignKey(\"PositionId\")", snapshot, StringComparison.Ordinal);
        Assert.Contains(".OnDelete(DeleteBehavior.Restrict)", snapshot, StringComparison.Ordinal);
        Assert.Contains(".HasConstraintName(\"FK_Person_Position\")", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void OrganizationPersonRevisionRemovalMigration_DropsOwnedColumnAndRequiresBackupRestoreForDown()
    {
        var migration = new Organization_000005_RemovePersonRevision();
        var drop = Assert.IsType<DropColumnOperation>(Assert.Single(migration.UpOperations));

        Assert.Equal("dbo", drop.Schema);
        Assert.Equal("Person", drop.Table);
        Assert.Equal("Revision", drop.Name);
        var descriptor = ReadFromDescriptor(drop);
        Assert.Equal("Organization", descriptor.OwnerModuleId);
        Assert.Equal(17, descriptor.Ordinal);
        Assert.NotNull(descriptor.Column);
        Assert.Equal("Revision", descriptor.Column.Name);
        Assert.Equal("bigint", descriptor.Column.StoreType);
        Assert.Equal(typeof(long).FullName, descriptor.Column.ClrType);
        Assert.False(descriptor.Column.IsNullable);

        var exception = Assert.Throws<NotSupportedException>(() => _ = migration.DownOperations);
        Assert.Contains("backup-restore-only", exception.Message, StringComparison.Ordinal);
        Assert.Contains("verified database backup", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OrganizationCompanyStandardCrudMigration_AddsNameUniquenessAndRestrictReferences()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Platform",
            "Organization",
            "Migrations.SqlServer",
            "Migrations",
            "20260904033918_Organization_000006_CompanyStandardCrud.cs");
        var source = File.ReadAllText(sourcePath);
        var up = source[..source.IndexOf("protected override void Down", StringComparison.Ordinal)];
        var down = source[source.IndexOf("protected override void Down", StringComparison.Ordinal)..];

        Assert.Contains("name: \"UX_Company_ENAME\"", up, StringComparison.Ordinal);
        Assert.Contains("unique: true", up, StringComparison.Ordinal);
        Assert.Contains("name: \"FK_Department_Company\"", up, StringComparison.Ordinal);
        Assert.Contains("name: \"FK_Person_Company\"", up, StringComparison.Ordinal);
        Assert.Equal(2, Count(up, "onDelete: ReferentialAction.Restrict"));
        Assert.DoesNotContain("UpdateData", up, StringComparison.Ordinal);
        Assert.DoesNotContain("migrationBuilder.Sql", up, StringComparison.Ordinal);
        Assert.Contains("DropForeignKey", down, StringComparison.Ordinal);
        Assert.Contains("DropIndex", down, StringComparison.Ordinal);
    }

    [Fact]
    public void OrganizationRootDepartmentStandardCrudMigration_AddsScopedUniquenessAndCompositeRestrictReference()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Platform",
            "Organization",
            "Migrations.SqlServer",
            "Migrations",
            "20260904041209_Organization_000007_RootDepartmentStandardCrud.cs");
        var source = File.ReadAllText(sourcePath);
        var up = source[..source.IndexOf("protected override void Down", StringComparison.Ordinal)];

        Assert.Contains("name: \"UX_Department_Company_ECODE\"", up, StringComparison.Ordinal);
        Assert.Contains("name: \"UX_Department_Company_ENAME\"", up, StringComparison.Ordinal);
        Assert.Contains("name: \"AK_Department_CompanyId_EID\"", up, StringComparison.Ordinal);
        Assert.Contains("name: \"FK_Person_Company_Department\"", up, StringComparison.Ordinal);
        Assert.Contains("columns: new[] { \"CompanyId\", \"DepartmentId\" }", up, StringComparison.Ordinal);
        Assert.Contains("principalColumns: new[] { \"CompanyId\", \"EID\" }", up, StringComparison.Ordinal);
        Assert.Contains("onDelete: ReferentialAction.Restrict", up, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateData", up, StringComparison.Ordinal);
        Assert.DoesNotContain("migrationBuilder.Sql", up, StringComparison.Ordinal);
        Assert.DoesNotContain("defaultValue", up, StringComparison.Ordinal);

        var migration = new Organization_000007_RootDepartmentStandardCrud();
        var droppedIndexes = migration.UpOperations
            .OfType<DropIndexOperation>()
            .ToDictionary(static operation => operation.Name, StringComparer.Ordinal);
        Assert.Equal(2, droppedIndexes.Count);
        Assert.Equal(
            new[] { "CompanyId" },
            ReadFromDescriptor(droppedIndexes["IX_Person_CompanyId"]).Index!.Columns);
        Assert.Equal(
            new[] { "CompanyId", "ParentDepartmentId", "ECODE" },
            ReadFromDescriptor(droppedIndexes["IX_Department_Company_Parent_ECODE"]).Index!.Columns);
        Assert.All(droppedIndexes, static entry =>
        {
            var descriptor = ReadFromDescriptor(entry.Value);
            Assert.Equal("Organization", descriptor.OwnerModuleId);
            Assert.Equal(entry.Key, descriptor.Index!.Name);
            Assert.False(descriptor.Index.IsUnique);
        });
    }

    [Fact]
    public void OrganizationDepartmentHierarchyMigration_AddsUnfilteredSiblingUniquenessAndCompositeParentRestrictReference()
    {
        var migration = new Organization_000008_DepartmentHierarchy();

        var siblingIndex = Assert.Single(
            migration.UpOperations.OfType<CreateIndexOperation>(),
            static operation => operation.Name == "UX_Department_Company_Parent_ENAME");
        Assert.Equal(new[] { "CompanyId", "ParentDepartmentId", "ENAME" }, siblingIndex.Columns);
        Assert.True(siblingIndex.IsUnique);
        Assert.Null(siblingIndex.Filter);

        var parentForeignKey = Assert.Single(migration.UpOperations.OfType<AddForeignKeyOperation>());
        Assert.Equal("FK_Department_Company_ParentDepartment", parentForeignKey.Name);
        Assert.Equal(new[] { "CompanyId", "ParentDepartmentId" }, parentForeignKey.Columns);
        Assert.Equal(new[] { "CompanyId", "EID" }, parentForeignKey.PrincipalColumns);
        Assert.Equal(Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict, parentForeignKey.OnDelete);

        var droppedIndex = Assert.Single(migration.UpOperations.OfType<DropIndexOperation>());
        var descriptor = ReadFromDescriptor(droppedIndex);
        Assert.Equal("Organization", descriptor.OwnerModuleId);
        Assert.Equal("UX_Department_Company_ENAME", descriptor.Index!.Name);
        Assert.Equal(new[] { "CompanyId", "ENAME" }, descriptor.Index.Columns);
        Assert.True(descriptor.Index.IsUnique);
    }

    private static void AssertOrganizationManifest(string repositoryRoot)
    {
        var manifestPath = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repositoryRoot, "Organization"), "migrations", "Organization.SqlServerMigrations.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var migrations = root.GetProperty("migrations").EnumerateArray().ToArray();
        Assert.Equal(8, migrations.Length);
        var foundation = migrations[0];
        var positionDirectory = migrations[1];
        var positionReference = migrations[2];
        var personnelTemporalFields = migrations[3];
        var personRevisionRemoval = migrations[4];
        var companyStandardCrud = migrations[5];
        var rootDepartmentStandardCrud = migrations[6];
        var departmentHierarchy = migrations[7];
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
        Assert.Equal(positionDirectory.GetProperty("toModelDigest").GetString(), positionReference.GetProperty("toModelDigest").GetString());
        Assert.Equal("safe-expand", positionReference.GetProperty("compatibility").GetString());
        Assert.Equal("20260902110155_Organization_000004_PersonnelTemporalFields", personnelTemporalFields.GetProperty("migrationId").GetString());
        Assert.Equal(positionReference.GetProperty("contentDigest").GetString(), personnelTemporalFields.GetProperty("previousContentDigest").GetString());
        Assert.Equal(positionReference.GetProperty("toModelDigest").GetString(), personnelTemporalFields.GetProperty("fromModelDigest").GetString());
        Assert.Equal("b3d160af02f7c6226cd462a79b3bef1e93e1cf7083380d76564273f0e636fe59", personnelTemporalFields.GetProperty("toModelDigest").GetString());
        Assert.Equal("safe-expand", personnelTemporalFields.GetProperty("compatibility").GetString());
        Assert.Equal("20260902135016_Organization_000005_RemovePersonRevision", personRevisionRemoval.GetProperty("migrationId").GetString());
        Assert.Equal(personnelTemporalFields.GetProperty("contentDigest").GetString(), personRevisionRemoval.GetProperty("previousContentDigest").GetString());
        Assert.Equal(personnelTemporalFields.GetProperty("toModelDigest").GetString(), personRevisionRemoval.GetProperty("fromModelDigest").GetString());
        Assert.Equal("579c6974b008e4695332d34269f1fc5f4d0ed386257015096079776c0d0f4973", personRevisionRemoval.GetProperty("toModelDigest").GetString());
        Assert.Equal("backup-restore-only", personRevisionRemoval.GetProperty("compatibility").GetString());
        Assert.Equal("20260904033918_Organization_000006_CompanyStandardCrud", companyStandardCrud.GetProperty("migrationId").GetString());
        Assert.Equal(personRevisionRemoval.GetProperty("contentDigest").GetString(), companyStandardCrud.GetProperty("previousContentDigest").GetString());
        Assert.Equal(personRevisionRemoval.GetProperty("toModelDigest").GetString(), companyStandardCrud.GetProperty("fromModelDigest").GetString());
        Assert.Equal("backup-restore-only", companyStandardCrud.GetProperty("compatibility").GetString());
        Assert.Equal("20260904041209_Organization_000007_RootDepartmentStandardCrud", rootDepartmentStandardCrud.GetProperty("migrationId").GetString());
        Assert.Equal(companyStandardCrud.GetProperty("contentDigest").GetString(), rootDepartmentStandardCrud.GetProperty("previousContentDigest").GetString());
        Assert.Equal(companyStandardCrud.GetProperty("toModelDigest").GetString(), rootDepartmentStandardCrud.GetProperty("fromModelDigest").GetString());
        Assert.Equal("808adb10d897941053c08a141480758caa957b6b1b999517bf54a9d1d3f5b2d5", rootDepartmentStandardCrud.GetProperty("toModelDigest").GetString());
        Assert.Equal("backup-restore-only", rootDepartmentStandardCrud.GetProperty("compatibility").GetString());
        Assert.Equal("20260904044325_Organization_000008_DepartmentHierarchy", departmentHierarchy.GetProperty("migrationId").GetString());
        Assert.Equal(rootDepartmentStandardCrud.GetProperty("contentDigest").GetString(), departmentHierarchy.GetProperty("previousContentDigest").GetString());
        Assert.Equal(rootDepartmentStandardCrud.GetProperty("toModelDigest").GetString(), departmentHierarchy.GetProperty("fromModelDigest").GetString());
        Assert.Equal(root.GetProperty("modelDigest").GetString(), departmentHierarchy.GetProperty("toModelDigest").GetString());
        Assert.Equal("backup-restore-only", departmentHierarchy.GetProperty("compatibility").GetString());
        Assert.Equal(
            root.GetProperty("bundleChecksum").GetString(),
            MigrationManifestIntegrity.ComputeBundleChecksum(
                File.ReadAllBytes(typeof(Organization_000001_Foundation).Assembly.Location)));

        var foundationSourcePath = Path.Combine(
            repositoryRoot,
            "server",
            "modules",
            "Platform",
            "Organization",
            "Migrations.SqlServer",
            "Migrations",
            "20260812144235_Organization_000001_Foundation.cs");
        Assert.Equal(
            foundation.GetProperty("contentDigest").GetString(),
            MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(foundationSourcePath)));
        var positionSourcePath = Path.Combine(
            Path.GetDirectoryName(foundationSourcePath)!,
            "20260902052903_Organization_000002_GlobalPositionDirectory.cs");
        Assert.Equal(
            positionDirectory.GetProperty("contentDigest").GetString(),
            MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(positionSourcePath)));
        var positionReferenceSourcePath = Path.Combine(
            Path.GetDirectoryName(foundationSourcePath)!,
            "20260902063356_Organization_000003_PositionReference.cs");
        Assert.Equal(
            positionReference.GetProperty("contentDigest").GetString(),
            MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(positionReferenceSourcePath)));
        var personnelTemporalSourcePath = Path.Combine(
            Path.GetDirectoryName(foundationSourcePath)!,
            "20260902110155_Organization_000004_PersonnelTemporalFields.cs");
        Assert.Equal(
            personnelTemporalFields.GetProperty("contentDigest").GetString(),
            MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(personnelTemporalSourcePath)));
        var personRevisionRemovalSourcePath = Path.Combine(
            Path.GetDirectoryName(foundationSourcePath)!,
            "20260902135016_Organization_000005_RemovePersonRevision.cs");
        Assert.Equal(
            personRevisionRemoval.GetProperty("contentDigest").GetString(),
            MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(personRevisionRemovalSourcePath)));
        var companyStandardCrudSourcePath = Path.Combine(
            Path.GetDirectoryName(foundationSourcePath)!,
            "20260904033918_Organization_000006_CompanyStandardCrud.cs");
        Assert.Equal(
            companyStandardCrud.GetProperty("contentDigest").GetString(),
            MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(companyStandardCrudSourcePath)));
        var rootDepartmentStandardCrudSourcePath = Path.Combine(
            Path.GetDirectoryName(foundationSourcePath)!,
            "20260904041209_Organization_000007_RootDepartmentStandardCrud.cs");
        Assert.Equal(
            rootDepartmentStandardCrud.GetProperty("contentDigest").GetString(),
            MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(rootDepartmentStandardCrudSourcePath)));
        var departmentHierarchySourcePath = Path.Combine(
            Path.GetDirectoryName(foundationSourcePath)!,
            "20260904044325_Organization_000008_DepartmentHierarchy.cs");
        Assert.Equal(
            departmentHierarchy.GetProperty("contentDigest").GetString(),
            MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(departmentHierarchySourcePath)));
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
        var migrations = root.GetProperty("migrations").EnumerateArray().ToArray();
        Assert.Equal(
            ["20260812144154_PlatformFoundation", "20260914114935_AddFunctionalAuthorization",
                "20260922094146_AddDataScopeRules", "20260922105706_AddDataScopeSubjectOptions",
                "20260923033732_AddDataScopeObjectGrants"],
            migrations.Select(static migration => migration.GetProperty("migrationId").GetString()));

        Assert.Equal("platform.foundation", root.GetProperty("moduleId").GetString());
        Assert.Equal(new string('0', 64), migrations[0].GetProperty("fromModelDigest").GetString());
        Assert.Equal(root.GetProperty("modelDigest").GetString(), migrations[^1].GetProperty("toModelDigest").GetString());
        for (var index = 0; index < migrations.Length; index++)
        {
            var migration = migrations[index];
            if (index > 0)
            {
                Assert.Equal(migrations[index - 1].GetProperty("toModelDigest").GetString(),
                    migration.GetProperty("fromModelDigest").GetString());
                Assert.Equal(migrations[index - 1].GetProperty("contentDigest").GetString(),
                    migration.GetProperty("previousContentDigest").GetString());
            }

            var sourcePath = Path.Combine(repositoryRoot, "server",
                "platform-persistence-efcore.migrations.sqlserver", "Migrations",
                migration.GetProperty("migrationId").GetString() + ".cs");
            Assert.Equal(migration.GetProperty("contentDigest").GetString(),
                MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(sourcePath)));
        }
        Assert.Equal(
            root.GetProperty("bundleChecksum").GetString(),
            MigrationManifestIntegrity.ComputeBundleChecksum(File.ReadAllBytes(
                typeof(ICP.Platform.Persistence.EfCore.SqlServerMigrations.Migrations.PlatformFoundation).Assembly.Location)));
    }

    private static void AssertManifest(
        string repositoryRoot,
        string moduleId,
        string manifestName,
        string assemblyPath,
        string migrationSourceName)
    {
        var manifestPath = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repositoryRoot, moduleId), "migrations", manifestName);
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var migration = Assert.Single(root.GetProperty("migrations").EnumerateArray());
        var sourcePath = Path.Combine(
            ICP.Platform.Kernel.ModulePlanning.ComponentSourceCatalog.ResolveBackendRoot(repositoryRoot, moduleId),
            "Migrations.SqlServer",
            "Migrations",
            migrationSourceName);

        Assert.Equal(
            root.GetProperty("bundleChecksum").GetString(),
            MigrationManifestIntegrity.ComputeBundleChecksum(File.ReadAllBytes(assemblyPath)));
        Assert.Equal(
            migration.GetProperty("contentDigest").GetString(),
            MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(sourcePath)));
    }

    private static void AssertPathMap(string repositoryRoot, string moduleId, string expectedPathMap)
    {
        var projectPath = Path.Combine(
            ICP.Platform.Kernel.ModulePlanning.ComponentSourceCatalog.ResolveBackendRoot(repositoryRoot, moduleId),
            "Migrations.SqlServer",
            $"{moduleId}.SqlServerMigrations.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.Contains($"<PathMap>{expectedPathMap}</PathMap>", project, StringComparison.Ordinal);
    }

    private static void AssertSourceRevisionIsExcluded(string repositoryRoot, string moduleId)
    {
        var projectPath = Path.Combine(
            ICP.Platform.Kernel.ModulePlanning.ComponentSourceCatalog.ResolveBackendRoot(repositoryRoot, moduleId),
            "Migrations.SqlServer",
            $"{moduleId}.SqlServerMigrations.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.Contains(
            "<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>",
            project,
            StringComparison.Ordinal);
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

    private static IEnumerable<ICP.Platform.Modeling.Definitions.ModelDefinition> FrozenFoundation()
    {
        var root = FindRepositoryRoot();
        var map = new ICP.Platform.Modeling.Metadata.JsonStorageTypeMapLoader().LoadFromFile(Path.Combine(root, "server", "platform-modeling", "metadata", "storage-types.json"));
        foreach (var owner in new[] { "Authorization", "ConfigCenter" })
        foreach (var path in Directory.GetFiles(Path.Combine(root, "tests", "Acceptance.HistoricalMigrations", "Authoring", "Platform.Foundation", owner), "*.model.json"))
        {
            var document = new ICP.Platform.Modeling.Metadata.JsonModelMetadataDocumentLoader().Load(File.ReadAllBytes(path), Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root, owner), "metadata", "models", Path.GetFileName(path)));
            new ICP.Platform.Modeling.Validation.ModelMetadataValidator().Validate(document, map);
            yield return new ICP.Platform.Modeling.Projection.ModelMetadataProjector().Project(document, map, "SqlServer") with { OwnerModuleId = owner, SchemaName = "dbo" };
        }
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

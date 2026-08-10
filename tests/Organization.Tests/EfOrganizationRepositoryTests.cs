using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class EfOrganizationRepositoryTests
{
    [Fact]
    public async Task EfCompanyRepository_CanCreateAndUpdateCompany()
    {
        using var dbContext = CreateDbContext();
        var repository = new EfCompanyRepository(dbContext);

        var company = new Company
        {
            Id = "Company_11111111111111111111111111111111",
            Code = "ICP",
            Name = "研发中心",
            SortOrder = 1
        };

        await repository.CreateAsync(company);
        company.Name = "研发中心（更新）";
        company.SortOrder = 2;
        company.IsEnabled = false;
        company.UpdatedAt = DateTime.UtcNow;
        await repository.UpdateAsync(company);

        var saved = await repository.GetByIdAsync(company.Id);

        Assert.NotNull(saved);
        Assert.Equal("ICP", saved!.Code);
        Assert.Equal("研发中心（更新）", saved.Name);
        Assert.False(saved.IsEnabled);
        Assert.Equal(2, saved.SortOrder);
    }

    [Fact]
    public async Task EfDepartmentRepository_ExistsSiblingCodeAsync_RespectsParentScope()
    {
        using var dbContext = CreateDbContext();
        var repository = new EfDepartmentRepository(dbContext);
        var companyId = "Company_11111111111111111111111111111111";
        var rootId = "Department_22222222222222222222222222222222";

        await repository.CreateAsync(new Department
        {
            Id = rootId,
            CompanyId = companyId,
            Code = "ROOT",
            Name = "根部门"
        });

        await repository.CreateAsync(new Department
        {
            Id = "Department_33333333333333333333333333333333",
            CompanyId = companyId,
            ParentDepartmentId = rootId,
            Code = "ENG",
            Name = "工程部"
        });

        var duplicateUnderSameParent = await repository.ExistsSiblingCodeAsync(companyId, rootId, "ENG");
        var sameCodeAtRoot = await repository.ExistsSiblingCodeAsync(companyId, null, "ENG");

        Assert.True(duplicateUnderSameParent);
        Assert.False(sameCodeAtRoot);
    }

    [Fact]
    public async Task EfPositionRepository_CanCreateAndLoadPosition()
    {
        using var dbContext = CreateDbContext();
        var repository = new EfPositionRepository(dbContext);
        var departmentId = "Department_22222222222222222222222222222222";

        var position = new Position
        {
            Id = "Position_33333333333333333333333333333333",
            CompanyId = "Company_11111111111111111111111111111111",
            DepartmentId = departmentId,
            Code = "LEAD",
            Name = "组长",
            EmploymentStatus = EmploymentStatus.FullTime,
            SortOrder = 5
        };

        await repository.CreateAsync(position);

        var saved = await repository.GetByIdAsync(position.Id);
        var positions = await repository.GetByDepartmentIdAsync(departmentId);

        Assert.NotNull(saved);
        Assert.Equal("LEAD", saved!.Code);
        Assert.Single(positions);
        Assert.Equal(position.Id, positions[0].Id);
    }

    [Fact]
    public async Task EfOrganizationRepositories_GetByIdsAsync_ReturnsIndexedEntities()
    {
        using var dbContext = CreateDbContext();
        var companyRepository = new EfCompanyRepository(dbContext);
        var departmentRepository = new EfDepartmentRepository(dbContext);
        var positionRepository = new EfPositionRepository(dbContext);

        var company = new Company
        {
            Id = "Company_44444444444444444444444444444444",
            Code = "ROOT",
            Name = "总公司"
        };
        var department = new Department
        {
            Id = "Department_55555555555555555555555555555555",
            CompanyId = company.Id,
            Code = "RD",
            Name = "研发部"
        };
        var position = new Position
        {
            Id = "Position_66666666666666666666666666666666",
            CompanyId = company.Id,
            DepartmentId = department.Id,
            Code = "DEV",
            Name = "工程师"
        };

        await companyRepository.CreateAsync(company);
        await departmentRepository.CreateAsync(department);
        await positionRepository.CreateAsync(position);

        var companies = await companyRepository.GetByIdsAsync([company.Id, "Company_missing"]);
        var departments = await departmentRepository.GetByIdsAsync([department.Id, "Department_missing"]);
        var positions = await positionRepository.GetByIdsAsync([position.Id, "Position_missing"]);

        Assert.Equal("总公司", companies[company.Id].Name);
        Assert.Equal("研发部", departments[department.Id].Name);
        Assert.Equal("工程师", positions[position.Id].Name);
    }

    [Fact]
    public void OrganizationModule_ConfigureServices_RegistersRepositoriesAndServices()
    {
        var services = new ServiceCollection();
        var module = new OrganizationModule();

        module.ConfigureServices(services);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ICompanyRepository) &&
            descriptor.ImplementationType == typeof(EfCompanyRepository));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IDepartmentRepository) &&
            descriptor.ImplementationType == typeof(EfDepartmentRepository));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPositionRepository) &&
            descriptor.ImplementationType == typeof(EfPositionRepository));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ICompanyService) &&
            descriptor.ImplementationType == typeof(CompanyService));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IDepartmentService) &&
            descriptor.ImplementationType == typeof(DepartmentService));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPositionService) &&
            descriptor.ImplementationType == typeof(PositionService));
    }

    [Fact]
    public void PlatformPersistenceModelCatalog_IncludesProviderProjectedOrganizationDefinitions()
    {
        var referencedAssemblies = typeof(PlatformPersistenceModelCatalog).Assembly.GetReferencedAssemblies();
        var definitions = PlatformPersistenceModelCatalog.AdaptPlatformCore(PlatformCoreLegacyModelSource.GetDefinitions());

        Assert.Contains(referencedAssemblies, assembly => assembly.Name == "OrganizationModule");
        Assert.Contains(definitions, definition => definition.Name == "Company");
        Assert.Contains(definitions, definition => definition.Name == "Department");
        Assert.Contains(definitions, definition => definition.Name == "Position");
        Assert.Contains(definitions, definition => definition.Name == "Person");
        Assert.DoesNotContain(definitions, definition => definition.Name == "PersonUserLink");
    }

    [Fact]
    public void MigrationSnapshots_UsePascalCaseTableAndColumnNames_ForOrganizationModels()
    {
        var repositoryRoot = GetRepositoryRoot();
        var sqlServerSnapshot = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "server",
            "platform-persistence-efcore.migrations.sqlserver",
            "Migrations",
            "EfPlatformDbContextModelSnapshot.cs"));
        var mySqlSnapshot = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "server",
            "platform-persistence-efcore.migrations.mysql",
            "Migrations",
            "EfPlatformDbContextModelSnapshot.cs"));
        var oracleSnapshot = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "server",
            "platform-persistence-efcore.migrations.oracle",
            "Migrations",
            "EfPlatformDbContextModelSnapshot.cs"));

        foreach (var snapshot in new[] { sqlServerSnapshot, mySqlSnapshot, oracleSnapshot })
        {
            Assert.Contains("b.ToTable(\"Company\"", snapshot, StringComparison.Ordinal);
            Assert.Contains("b.ToTable(\"Department\"", snapshot, StringComparison.Ordinal);
            Assert.Contains("b.ToTable(\"Position\"", snapshot, StringComparison.Ordinal);
            Assert.Contains("b.ToTable(\"Person\"", snapshot, StringComparison.Ordinal);
            Assert.Contains(".HasColumnName(\"CompanyId\")", snapshot, StringComparison.Ordinal);
            Assert.Contains(".HasColumnName(\"EmploymentStatus\")", snapshot, StringComparison.Ordinal);
            AssertModelPrimaryKey(snapshot, "Company", "EID");
            AssertModelPrimaryKey(snapshot, "Department", "EID");
            AssertModelPrimaryKey(snapshot, "Position", "EID");
            AssertModelPrimaryKey(snapshot, "Person", "EID");
        }

        var sqlServerPerson = ExtractPersonSection(sqlServerSnapshot);
        Assert.Contains("modelBuilder.Entity(\"PersonUserLink\"", sqlServerSnapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("b.Property<string>(\"UserEID\")", sqlServerPerson, StringComparison.Ordinal);
        Assert.DoesNotContain("b.Property<long>(\"Revision\")", sqlServerPerson, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationSnapshots_MatchNullablePersonFields_ForOrganizationModels()
    {
        var repositoryRoot = GetRepositoryRoot();
        var sqlServerSnapshot = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "server",
            "platform-persistence-efcore.migrations.sqlserver",
            "Migrations",
            "EfPlatformDbContextModelSnapshot.cs"));
        var mySqlSnapshot = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "server",
            "platform-persistence-efcore.migrations.mysql",
            "Migrations",
            "EfPlatformDbContextModelSnapshot.cs"));
        var oracleSnapshot = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "server",
            "platform-persistence-efcore.migrations.oracle",
            "Migrations",
            "EfPlatformDbContextModelSnapshot.cs"));

        foreach (var snapshot in new[] { sqlServerSnapshot, mySqlSnapshot, oracleSnapshot })
        {
            var personSection = ExtractPersonSection(snapshot);
            Assert.DoesNotContain(
                "b.Property<string>(\"CompanyId\")\r\n                        .IsRequired()",
                personSection,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "b.Property<string>(\"DepartmentId\")\r\n                        .IsRequired()",
                personSection,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "b.Property<string>(\"EmploymentStatus\")\r\n                        .IsRequired()",
                personSection,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MigrationFiles_BackfillPersonUserLinkEidBeforePrimaryKeySwitch()
    {
        var repositoryRoot = GetRepositoryRoot();
        var sqlServerMigration = ReadMigration(
            repositoryRoot,
            "platform-persistence-efcore.migrations.sqlserver",
            "AlignModelMetadataEntityKeys");
        var mySqlMigration = ReadMigration(
            repositoryRoot,
            "platform-persistence-efcore.migrations.mysql",
            "AlignModelMetadataEntityKeys");
        var oracleMigration = ReadMigration(
            repositoryRoot,
            "platform-persistence-efcore.migrations.oracle",
            "AlignModelMetadataEntityKeys");

        AssertPersonUserLinkBackfillPrecedesKeySwitch(sqlServerMigration, "SET [EID] = [PersonId]");
        AssertPersonUserLinkBackfillPrecedesKeySwitch(mySqlMigration, "SET `EID` = `PersonId`");
        AssertPersonUserLinkBackfillPrecedesKeySwitch(oracleMigration, @"SET \""EID\"" = \""PersonId\""");
    }

    [Fact]
    public void PermanentBindingMigration_DropsLegacyTableWithoutReadingOrMigratingRows()
    {
        var migration = ReadMigration(
            GetRepositoryRoot(),
            Path.Combine("modules", "Organization", "Migrations.SqlServer"),
            "Organization_000001_PermanentUserBinding");

        Assert.Contains("DropTable(name: \"PersonUserLink\")", migration, StringComparison.Ordinal);
        Assert.Contains("name: \"UserEID\"", migration, StringComparison.Ordinal);
        Assert.Contains("name: \"UX_Person_UserEID\"", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", migration, StringComparison.OrdinalIgnoreCase);
        var down = migration[migration.IndexOf("protected override void Down", StringComparison.Ordinal)..];
        Assert.DoesNotContain("CreateTable", down, StringComparison.Ordinal);
    }

    private static EfPlatformDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new EfPlatformDbContext(options, OrganizationModelDefinitions.All);
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "server")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }

    private static string ExtractPersonSection(string snapshot)
    {
        return ExtractModelSection(snapshot, "Person");
    }

    private static void AssertModelPrimaryKey(string snapshot, string modelName, string primaryKey)
    {
        var section = ExtractModelSection(snapshot, modelName);

        Assert.Contains($"b.HasKey(\"{primaryKey}\")", section, StringComparison.Ordinal);
        Assert.DoesNotContain("b.HasKey(\"Id\")", section, StringComparison.Ordinal);
        Assert.DoesNotContain("b.HasKey(\"PersonId\")", section, StringComparison.Ordinal);
    }

    private static string ExtractModelSection(string snapshot, string modelName)
    {
        var marker = $"modelBuilder.Entity(\"{modelName}\", b =>";
        const string nextMarker = "modelBuilder.Entity(\"";

        var start = snapshot.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"快照中缺少 {modelName} 实体定义。");

        var next = snapshot.IndexOf(nextMarker, start + marker.Length, StringComparison.Ordinal);
        return next > start ? snapshot[start..next] : snapshot[start..];
    }

    private static string ReadMigration(string repositoryRoot, string migrationProject, string migrationName)
    {
        var migrationDirectory = Path.Combine(repositoryRoot, "server", migrationProject, "Migrations");
        var migrationPath = Directory
            .GetFiles(migrationDirectory, $"*_{migrationName}.cs")
            .Single(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal));

        return File.ReadAllText(migrationPath);
    }

    private static void AssertPersonUserLinkBackfillPrecedesKeySwitch(string migration, string backfillSql)
    {
        var backfillIndex = migration.IndexOf(backfillSql, StringComparison.Ordinal);
        var primaryKeyIndex = migration.IndexOf(
            "AddPrimaryKey(\r\n                name: \"PK_PersonUserLink\"",
            StringComparison.Ordinal);
        var uniqueIndexIndex = migration.IndexOf(
            "CreateIndex(\r\n                name: \"UX_PersonUserLink_EID\"",
            StringComparison.Ordinal);

        Assert.True(backfillIndex >= 0, "迁移缺少 PersonUserLink.EID 历史数据回填。");
        Assert.True(primaryKeyIndex > backfillIndex, "PersonUserLink.EID 必须先回填，再切换主键。");
        Assert.True(uniqueIndexIndex > backfillIndex, "PersonUserLink.EID 必须先回填，再创建唯一索引。");
    }
}

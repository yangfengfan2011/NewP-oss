using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Crud.Tests.Support;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudEfRepositoryTests
{
    [Fact]
    public async Task OptionsAsync_UsesConfiguredLabelFields()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("Person").Add(Row("person-1", "P001", "张三", true));
        await dbContext.SaveChangesAsync();

        var repository = new EfStandardCrudRepository(dbContext, new PlatformObjectIdGenerator());
        var options = await repository.OptionsAsync(Descriptor(), new StandardCrudListQuery());

        var option = Assert.Single(options);
        Assert.Equal("person-1", option.Value);
        Assert.Equal("P001 张三", option.Label);
    }

    [Fact]
    public async Task ExistsAsync_WhenMatchingRowExists_ReturnsTrue()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("Person").AddRange(
            Row("person-1", "P001", "张三", true),
            Row("person-2", "P002", "李四", true));
        await dbContext.SaveChangesAsync();

        var repository = new EfStandardCrudRepository(dbContext, new PlatformObjectIdGenerator());

        var exists = await repository.ExistsAsync(
            Descriptor(),
            UniqueConstraint(),
            new Dictionary<string, object?> { ["PersonNo"] = "p001" });

        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_WhenExcludedIdMatchesOnlyRow_ReturnsFalse()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("Person").Add(Row("person-1", "P001", "张三", true));
        await dbContext.SaveChangesAsync();

        var repository = new EfStandardCrudRepository(dbContext, new PlatformObjectIdGenerator());

        var exists = await repository.ExistsAsync(
            Descriptor(),
            UniqueConstraint(),
            new Dictionary<string, object?> { ["PersonNo"] = "P001" },
            excludedId: "person-1");

        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsAsync_WhenConstraintTrimsStoredValue_ReturnsTrue()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("Person").Add(Row("person-1", " P001 ", "张三", true));
        await dbContext.SaveChangesAsync();

        var repository = new EfStandardCrudRepository(dbContext, new PlatformObjectIdGenerator());

        var exists = await repository.ExistsAsync(
            Descriptor(),
            UniqueConstraint(),
            new Dictionary<string, object?> { ["PersonNo"] = "P001" });

        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_WhenConstraintIsCaseSensitive_RespectsCase()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("Person").Add(Row("person-1", "P001", "张三", true));
        await dbContext.SaveChangesAsync();

        var repository = new EfStandardCrudRepository(dbContext, new PlatformObjectIdGenerator());

        var exists = await repository.ExistsAsync(
            Descriptor(),
            UniqueConstraint(caseInsensitive: false),
            new Dictionary<string, object?> { ["PersonNo"] = "p001" });

        Assert.False(exists);
    }

    [Fact]
    public async Task CreateAsync_WhenIdIsMissing_GeneratesModelNamePrefixedEid()
    {
        await using var dbContext = BuildDbContext();
        var repository = new EfStandardCrudRepository(
            dbContext,
            new FixedObjectIdGenerator("Person_00000000000000000000000000000001"));

        var created = await repository.CreateAsync(
            Descriptor(),
            new Dictionary<string, object?>
            {
                ["PersonNo"] = "P900",
                ["ENAME"] = "协议人员",
                ["IsEnabled"] = true
            });

        Assert.Equal("Person_00000000000000000000000000000001", created["EID"]);
    }

    [Fact]
    public void PlatformObjectIdGenerator_NewId_UsesModelNamePrefixAndLowerHex()
    {
        var id = new PlatformObjectIdGenerator().NewId("Person");

        Assert.Matches("^Person_[0-9a-f]{32}$", id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("organization.person")]
    [InlineData("Person/User")]
    public void PlatformObjectIdGenerator_NewId_RejectsInvalidObjectName(string objectName)
    {
        var error = Assert.Throws<ArgumentException>(() => new PlatformObjectIdGenerator().NewId(objectName));

        Assert.Contains("对象名称必须匹配", error.Message, StringComparison.Ordinal);
    }

    private static EfPlatformDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EfPlatformDbContext(options, [PlatformCrudModelDefinitions.Person()]);
    }

    private static Dictionary<string, object?> Row(string id, string personNo, string name, bool isEnabled) => new(StringComparer.Ordinal)
    {
        ["EID"] = id,
        ["PersonNo"] = personNo,
        ["ENAME"] = name,
        ["CompanyId"] = "company-1",
        ["DepartmentId"] = "department-1",
        ["EmploymentStatus"] = "FullTime",
        ["IsEnabled"] = isEnabled,
        ["CreatedAt"] = DateTime.UtcNow
    };

    private static StandardCrudDescriptor Descriptor() => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        EnabledField = "IsEnabled",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "EmploymentStatus", "IsEnabled"],
            DetailFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "EmploymentStatus", "IsEnabled", "CreatedAt"],
            CreateFields: ["PersonNo", "ENAME", "CompanyId", "DepartmentId", "EmploymentStatus", "IsEnabled"],
            UpdateFields: ["ENAME", "CompanyId", "DepartmentId", "EmploymentStatus", "IsEnabled"]),
        OptionLabelFields = ["PersonNo", "ENAME"]
    };

    private static StandardCrudUniqueConstraint UniqueConstraint(bool caseInsensitive = true) => new()
    {
        Key = "personNo",
        Fields = ["PersonNo"],
        CaseInsensitive = caseInsensitive,
        TrimWhitespace = true,
        ErrorCode = "Organization.Person.PersonNoDuplicated",
        Message = "人员工号必须唯一。",
        ReasonKey = "organization.person.personNoDuplicated"
    };

    private sealed class FixedObjectIdGenerator : IPlatformObjectIdGenerator
    {
        private readonly string _id;

        public FixedObjectIdGenerator(string id)
        {
            _id = id;
        }

        public string NewId(string objectName) => _id;
    }
}

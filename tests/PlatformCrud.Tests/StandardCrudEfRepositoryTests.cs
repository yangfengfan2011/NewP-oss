using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Crud.Tests.Support;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudEfRepositoryTests
{
    [Fact]
    public async Task ListAsync_AppliesConditionsAndSorting()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("Person").AddRange(
            Row("person-1", "P001", "张三", true),
            Row("person-2", "P002", "李四", false));
        await dbContext.SaveChangesAsync();

        var repository = new EfStandardCrudRepository(dbContext);
        var result = await repository.ListAsync(
            Descriptor(),
            new StandardCrudListQuery(Page: 1, PageSize: 20, SortBy: "PersonNo", SortDirection: "desc"),
            [new StandardCrudQueryCondition("IsEnabled", "=", true)]);

        Assert.Single(result.Items);
        Assert.Equal("P001", result.Items[0]["PersonNo"]);
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task ListAsync_AppliesExplicitOrConditionGroup()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("Person").AddRange(
            Row("person-1", "P001", "张三", true),
            Row("person-2", "P002", "李四", true),
            Row("person-3", "X003", "王五", true));
        await dbContext.SaveChangesAsync();

        var repository = new EfStandardCrudRepository(dbContext);
        var result = await repository.ListAsync(
            Descriptor(),
            new StandardCrudListQuery(Page: 1, PageSize: 20, SortBy: "PersonNo"),
            [
                new StandardCrudQueryCondition("PersonNo", "contains", "P", GroupKey: "keyword", GroupOperator: "or"),
                new StandardCrudQueryCondition("ENAME", "contains", "五", GroupKey: "keyword", GroupOperator: "or")
            ]);

        Assert.Equal(3, result.Total);
        Assert.Collection(
            result.Items,
            row => Assert.Equal("P001", row["PersonNo"]),
            row => Assert.Equal("P002", row["PersonNo"]),
            row => Assert.Equal("X003", row["PersonNo"]));
    }

    [Fact]
    public async Task ListAsync_WhenConditionGroupMixesOperators_ThrowsStableMessage()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("Person").Add(Row("person-1", "P001", "张三", true));
        await dbContext.SaveChangesAsync();

        var repository = new EfStandardCrudRepository(dbContext);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ListAsync(
            Descriptor(),
            new StandardCrudListQuery(),
            [
                new StandardCrudQueryCondition("PersonNo", "contains", "P", GroupKey: "keyword", GroupOperator: "or"),
                new StandardCrudQueryCondition("ENAME", "contains", "张", GroupKey: "keyword")
            ]));

        Assert.Equal("标准 CRUD 查询条件组 keyword 不能混用组合操作符。", ex.Message);
    }

    [Fact]
    public async Task OptionsAsync_UsesConfiguredLabelFields()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("Person").Add(Row("person-1", "P001", "张三", true));
        await dbContext.SaveChangesAsync();

        var repository = new EfStandardCrudRepository(dbContext);
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

        var repository = new EfStandardCrudRepository(dbContext);

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

        var repository = new EfStandardCrudRepository(dbContext);

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

        var repository = new EfStandardCrudRepository(dbContext);

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

        var repository = new EfStandardCrudRepository(dbContext);

        var exists = await repository.ExistsAsync(
            Descriptor(),
            UniqueConstraint(caseInsensitive: false),
            new Dictionary<string, object?> { ["PersonNo"] = "p001" });

        Assert.False(exists);
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
}

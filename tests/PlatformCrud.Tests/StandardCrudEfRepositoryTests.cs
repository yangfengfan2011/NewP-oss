using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Crud.Tests.Support;
using ICP.Platform.Persistence.EfCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudEfRepositoryTests
{
    [Fact]
    public async Task OptionsAsync_UsesConfiguredLabelFields()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("platform.core/Person").Add(Row("person-1", "P001", "张三", true));
        await dbContext.SaveChangesAsync();

        var repository = new EfStandardCrudRepository(dbContext, new PlatformObjectIdGenerator());
        var options = await repository.OptionsAsync(Descriptor(), new StandardCrudListQuery());

        var option = Assert.Single(options);
        Assert.Equal("person-1", option.Value);
        Assert.Equal("P001 张三", option.Label);
    }

    [Fact]
    public async Task LookupAsync_AppliesKeywordStablePagingAndResolvesCurrentValues()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("platform.core/Person").AddRange(
            Row("person-3", "P003", "Alpha Three", true),
            Row("person-1", "P001", "Alpha One", true),
            Row("person-2", "P002", "Alpha Two", true),
            Row("person-4", "P004", "Other", false));
        await dbContext.SaveChangesAsync();

        var repository = new EfStandardCrudRepository(dbContext, new PlatformObjectIdGenerator());

        var result = await repository.LookupAsync(
            Descriptor(),
            new StandardCrudLookupRequest(
                Keyword: "alpha",
                Page: 2,
                PageSize: 2,
                CurrentValueIds: ["person-4"]));

        Assert.Equal(["person-3"], result.Items.Select(item => item.Value));
        Assert.Equal(["person-4"], result.CurrentItems.Select(item => item.Value));
        Assert.Equal("P004 Other（已停用）", Assert.Single(result.CurrentItems).Label);
        Assert.True(Assert.Single(result.CurrentItems).Disabled);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task LookupAsync_ExecutesKeywordAndPagingOnRelationalProvider()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = BuildSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Set<Dictionary<string, object?>>("platform.core/Person").AddRange(
            Row("person-2", "P002", "Alpha Two", true),
            Row("person-1", "P001", "Alpha One", true),
            Row("person-3", "P003", "Other", true));
        await dbContext.SaveChangesAsync();

        var repository = new EfStandardCrudRepository(dbContext, new PlatformObjectIdGenerator());
        var result = await repository.LookupAsync(
            Descriptor(),
            new StandardCrudLookupRequest(Keyword: "alpha", Page: 1, PageSize: 1));

        Assert.Equal(["person-1"], result.Items.Select(item => item.Value));
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task ExistsAsync_WhenMatchingRowExists_ReturnsTrue()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("platform.core/Person").AddRange(
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
        dbContext.Set<Dictionary<string, object?>>("platform.core/Person").Add(Row("person-1", "P001", "张三", true));
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
        dbContext.Set<Dictionary<string, object?>>("platform.core/Person").Add(Row("person-1", " P001 ", "张三", true));
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
        dbContext.Set<Dictionary<string, object?>>("platform.core/Person").Add(Row("person-1", "P001", "张三", true));
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
    public async Task CreateAsync_WhenRequiredLongFieldUsesClrDefault_ProjectsTheManagedValue()
    {
        await using var dbContext = BuildDbContext();
        var repository = new EfStandardCrudRepository(dbContext, new PlatformObjectIdGenerator());
        var descriptor = Descriptor();

        var created = await repository.CreateAsync(
            descriptor,
            new Dictionary<string, object?>
            {
                ["PersonNo"] = "P901",
                ["ENAME"] = "Revision 人员",
                ["IsEnabled"] = true
            });

        Assert.Equal(0L, Assert.IsType<long>(created["Revision"]));
    }

    [Fact]
    public async Task UpdateAsync_WhenDateTimeValueIsRoundTripText_ConvertsToMappedClrType()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("platform.core/Person").Add(Row("person-1", "P001", "张三", true));
        await dbContext.SaveChangesAsync();
        var repository = new EfStandardCrudRepository(dbContext, new PlatformObjectIdGenerator());
        var expected = new DateTime(2026, 8, 9, 2, 3, 4, DateTimeKind.Utc);

        var updated = await repository.UpdateAsync(
            Descriptor(),
            "person-1",
            new Dictionary<string, object?> { ["EffectiveAt"] = expected.ToString("O") });

        Assert.NotNull(updated);
        Assert.Equal(expected, Assert.IsType<DateTime>(updated["EffectiveAt"]));
    }

    [Fact]
    public async Task UpdateAsync_WhenNullableDateTimeValueIsBlank_StoresNull()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("platform.core/Person").Add(Row("person-1", "P001", "张三", true));
        await dbContext.SaveChangesAsync();
        var repository = new EfStandardCrudRepository(dbContext, new PlatformObjectIdGenerator());

        await repository.UpdateAsync(
            Descriptor(),
            "person-1",
            new Dictionary<string, object?> { ["EffectiveAt"] = string.Empty });

        var stored = await dbContext.Set<Dictionary<string, object?>>("platform.core/Person").SingleAsync();
        Assert.Null(stored["EffectiveAt"]);
    }

    [Fact]
    public async Task UpdateAsync_WithRevisionCondition_RejectsStaleAndIncrementsFreshRevision()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = BuildSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Set<Dictionary<string, object?>>("platform.core/Person").Add(Row("person-1", "P001", "服务器姓名", true));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        ICP.Platform.Crud.Abstractions.IStandardCrudRepository repository =
            new EfStandardCrudRepository(dbContext, new PlatformObjectIdGenerator());
        var descriptor = Descriptor(concurrencyRevisionField: "Revision");

        var stale = await repository.UpdateAsync(
            descriptor,
            "person-1",
            new Dictionary<string, object?> { ["ENAME"] = "过期草稿" },
            expectedRevision: 3L);
        var afterStale = await repository.GetByIdAsync(descriptor, "person-1");
        var fresh = await repository.UpdateAsync(
            descriptor,
            "person-1",
            new Dictionary<string, object?> { ["ENAME"] = "新姓名" },
            expectedRevision: 0L);

        Assert.Null(stale);
        Assert.Equal("服务器姓名", afterStale!["ENAME"]);
        Assert.NotNull(fresh);
        Assert.Equal("新姓名", fresh["ENAME"]);
        Assert.Equal(1L, fresh["Revision"]);
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

    private static EfPlatformDbContext BuildSqliteDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlite(connection)
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

    private static StandardCrudDescriptor Descriptor(string? concurrencyRevisionField = null) => new()
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
            DetailFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "EmploymentStatus", "IsEnabled", "Revision", "EffectiveAt", "CreatedAt"],
            CreateFields: ["PersonNo", "ENAME", "CompanyId", "DepartmentId", "EmploymentStatus", "IsEnabled"],
            UpdateFields: ["ENAME", "CompanyId", "DepartmentId", "EmploymentStatus", "IsEnabled"]),
        OptionLabelFields = ["PersonNo", "ENAME"],
        ConcurrencyRevisionField = concurrencyRevisionField
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

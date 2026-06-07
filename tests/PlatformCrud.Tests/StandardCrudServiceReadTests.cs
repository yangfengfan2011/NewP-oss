using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.Tests.Support;
using ICP.Platform.Query.Descriptors;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudServiceReadTests
{
    [Fact]
    public async Task ListAsync_AppliesQueryCustomizerAndDisplayValueResolver()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["CompanyId"] = "company-1", ["DepartmentId"] = "department-1" },
            new Dictionary<string, object?> { ["EID"] = "person-2", ["PersonNo"] = "P002", ["ENAME"] = "李四", ["CompanyId"] = "company-2", ["DepartmentId"] = "department-2" }
        ]);
        var service = new StandardCrudService(
            repository,
            [new KeywordQueryCustomizer()],
            [new PersonDisplayResolver()],
            [],
            [],
            [],
            []);

        var result = await service.ListAsync(Descriptor(), new StandardCrudListQuery(Filters: new Dictionary<string, string?> { ["keyword"] = "P001" }));

        var row = Assert.Single(result.Items);
        Assert.Equal("公司一", row["CompanyName"]);
        Assert.True(row.ContainsKey("DepartmentName"));
        Assert.True(row.ContainsKey("PositionName"));
        Assert.Equal("部门一", row["DepartmentName"]);
        Assert.Null(row["PositionName"]);
    }

    [Fact]
    public async Task GetDetailAsync_WhenSummaryProviderThrows_ReturnsDependencyUnavailable()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三" }
        ]);
        var service = new StandardCrudService(repository, [], [], [new FailingSummaryProvider()], [], [], []);

        var detail = await service.GetDetailAsync(Descriptor(summaryKeys: ["userLink"]), "person-1");

        Assert.NotNull(detail);
        Assert.Equal("dependencyUnavailable", detail!.Summaries["userLink"].Status);
    }

    [Fact]
    public async Task OptionsAsync_ReturnsRepositoryOptions()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三" }
        ]);
        var service = new StandardCrudService(repository, [], [], [], [], [], []);

        var options = await service.OptionsAsync(Descriptor(), new StandardCrudListQuery());

        var option = Assert.Single(options);
        Assert.Equal("person-1", option.Value);
        Assert.Equal("P001 张三", option.Label);
    }

    private static StandardCrudDescriptor Descriptor(IReadOnlyList<string>? summaryKeys = null) => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId"],
            DetailFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId"],
            CreateFields: ["PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId"],
            UpdateFields: ["ENAME", "CompanyId", "DepartmentId", "PositionId"]),
        OptionLabelFields = ["PersonNo", "ENAME"],
        DisplayValueFields =
        [
            new PlatformDisplayValueField("CompanyName", "CompanyId", "Company", "EID", "ENAME"),
            new PlatformDisplayValueField("DepartmentName", "DepartmentId", "Department", "EID", "ENAME"),
            new PlatformDisplayValueField("PositionName", "PositionId", "Position", "EID", "ENAME")
        ],
        SummaryBlockKeys = summaryKeys ?? []
    };

    private sealed class KeywordQueryCustomizer : IStandardCrudQueryCustomizer
    {
        public string ResourceKey => "organization.person";

        public IReadOnlyList<StandardCrudQueryCondition> BuildConditions(StandardCrudDescriptor descriptor, StandardCrudListQuery query)
        {
            return query.Filters?.TryGetValue("keyword", out var keyword) == true && !string.IsNullOrWhiteSpace(keyword)
                ? [new StandardCrudQueryCondition("PersonNo", "contains", keyword)]
                : [];
        }
    }

    private sealed class PersonDisplayResolver : IStandardCrudDisplayValueResolver
    {
        public string ResourceKey => "organization.person";

        public Task<IReadOnlyDictionary<string, object?>> ResolveAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyDictionary<string, object?> row,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>
            {
                ["CompanyName"] = row.TryGetValue("CompanyId", out var companyId) && Equals(companyId, "company-1") ? "公司一" : string.Empty,
                ["DepartmentName"] = row.TryGetValue("DepartmentId", out var departmentId) && Equals(departmentId, "department-1") ? "部门一" : string.Empty,
                ["PositionName"] = row.TryGetValue("PositionId", out var positionId) ? positionId : null
            });
        }
    }

    private sealed class FailingSummaryProvider : IStandardCrudSummaryProvider
    {
        public string ResourceKey => "organization.person";
        public string Key => "userLink";

        public Task<StandardCrudSummaryBlock> BuildAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyDictionary<string, object?> detail,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("summary dependency unavailable");
        }
    }
}

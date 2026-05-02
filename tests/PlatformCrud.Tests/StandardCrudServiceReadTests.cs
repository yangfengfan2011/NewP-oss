using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.Tests.Support;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudServiceReadTests
{
    [Fact]
    public async Task ListAsync_AppliesQueryCustomizerAndDisplayValueResolver()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["CompanyId"] = "company-1" },
            new Dictionary<string, object?> { ["EID"] = "person-2", ["PersonNo"] = "P002", ["ENAME"] = "李四", ["CompanyId"] = "company-2" }
        ]);
        var service = new StandardCrudService(
            repository,
            [new KeywordQueryCustomizer()],
            [new CompanyDisplayResolver()],
            [],
            [],
            [],
            []);

        var result = await service.ListAsync(Descriptor(), new StandardCrudListQuery(Filters: new Dictionary<string, string?> { ["keyword"] = "P001" }));

        var row = Assert.Single(result.Items);
        Assert.Equal("公司一", row["CompanyName"]);
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
            ListFields: ["EID", "PersonNo", "ENAME", "CompanyId"],
            DetailFields: ["EID", "PersonNo", "ENAME", "CompanyId"],
            CreateFields: ["PersonNo", "ENAME", "CompanyId"],
            UpdateFields: ["ENAME", "CompanyId"]),
        OptionLabelFields = ["PersonNo", "ENAME"],
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

    private sealed class CompanyDisplayResolver : IStandardCrudDisplayValueResolver
    {
        public string ResourceKey => "organization.person";

        public Task<IReadOnlyDictionary<string, object?>> ResolveAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyDictionary<string, object?> row,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>
            {
                ["CompanyName"] = row.TryGetValue("CompanyId", out var value) && Equals(value, "company-1") ? "公司一" : string.Empty
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

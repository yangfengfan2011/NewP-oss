using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.Tests.Support;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudServiceReadTests
{
    [Fact]
    public async Task ListAsync_WhenRawFieldRequested_AddsDisplayCompanionWithoutDroppingRawField()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["CompanyId"] = "company-1", ["DepartmentId"] = "department-1" },
            new Dictionary<string, object?> { ["EID"] = "person-2", ["PersonNo"] = "P002", ["ENAME"] = "李四", ["CompanyId"] = "company-2", ["DepartmentId"] = "department-2" }
        ]);
        var executor = new RecordingPlatformQueryExecutor(new PlatformQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["PersonNo"] = "P001",
                ["CompanyId"] = "company-1",
                ["EmploymentStatus"] = "FullTime"
            }
        ],
        Total: 1,
        Page: 1,
        PageSize: 20,
        SortBy: "PersonNo",
        SortDirection: "asc",
        ResponseFields: ["EID", "PersonNo", "CompanyId", "CompanyId_DisplayValue"],
        RequestedDisplayFields:
        [
            new PlatformDisplayValueField("CompanyId_DisplayValue", "CompanyId", "Company", "EID", "ENAME")
        ]));
        var service = new StandardCrudService(
            repository,
            executor,
            [new KeywordQueryCustomizer()],
            [new PersonDisplayResolver()],
            [],
            [],
            [],
            []);

        var result = await service.ListAsync(
            Descriptor(),
            new StandardCrudListQuery(
                SortBy: "PersonNo",
                SortDirection: "asc",
                Filters: new Dictionary<string, string?> { ["keyword"] = "P001" },
                Fields: ["EID", "PersonNo", "CompanyId"]));

        Assert.Equal(["EID", "PersonNo", "CompanyId", "CompanyId_DisplayValue"], executor.LastRequest!.Fields);
        Assert.Equal("PersonNo", executor.LastRequest.SortBy);
        var condition = Assert.Single(executor.LastRequest.Conditions!);
        Assert.Equal("PersonNo", condition.Field);
        var row = Assert.Single(result.Items);
        Assert.Equal("company-1", row["CompanyId"]);
        Assert.Equal("公司一", row["CompanyId_DisplayValue"]);
        Assert.Equal(["EID", "PersonNo", "CompanyId", "CompanyId_DisplayValue"], row.Keys);
    }

    [Fact]
    public async Task ListAsync_WhenCanonicalDisplayCompanionRequested_AddsRawFieldBeforeDisplayField()
    {
        var executor = DisplayValueQueryExecutor();
        var service = new StandardCrudService(
            new InMemoryStandardCrudRepository([]),
            executor,
            [],
            [new PersonDisplayResolver()],
            [],
            [],
            [],
            []);

        var result = await service.ListAsync(
            Descriptor(),
            new StandardCrudListQuery(Fields: ["CompanyId_DisplayValue"]));

        Assert.Equal(["CompanyId", "CompanyId_DisplayValue"], executor.LastRequest!.Fields);
        var row = Assert.Single(result.Items);
        Assert.Equal("company-1", row["CompanyId"]);
        Assert.Equal("公司一", row["CompanyId_DisplayValue"]);
    }

    [Theory]
    [InlineData("DepartmentId", "department-1")]
    [InlineData("PositionId", "position-1")]
    public async Task ListAsync_WhenLegacyRelationRawFieldRequested_DoesNotAddLegacyDisplayField(
        string rawField,
        string rawValue)
    {
        var executor = new RecordingPlatformQueryExecutor(new PlatformQueryResult(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", [rawField] = rawValue }
        ],
        1,
        1,
        20,
        null,
        null,
        [rawField],
        []));
        var descriptor = Descriptor(displayValueFields:
        [
            new PlatformDisplayValueField("CompanyId_DisplayValue", "CompanyId", "Company", "EID", "ENAME"),
            new PlatformDisplayValueField("DepartmentName", "DepartmentId", "Department", "EID", "ENAME"),
            new PlatformDisplayValueField("PositionName", "PositionId", "Position", "EID", "ENAME")
        ]);
        var service = new StandardCrudService(
            new InMemoryStandardCrudRepository([]),
            executor,
            [],
            [],
            [],
            [],
            [],
            []);

        var result = await service.ListAsync(
            descriptor,
            new StandardCrudListQuery(Fields: [rawField]));

        Assert.Equal([rawField], executor.LastRequest!.Fields);
        var row = Assert.Single(result.Items);
        Assert.Equal(rawValue, row[rawField]);
    }

    [Fact]
    public async Task GetDetailAsync_WhenSummaryProviderThrows_ReturnsDependencyUnavailable()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三" }
        ]);
        var service = new StandardCrudService(
            repository,
            EmptyQueryExecutor(),
            [],
            [new PersonDisplayResolver()],
            [new FailingSummaryProvider()],
            [],
            [],
            []);

        var detail = await service.GetDetailAsync(Descriptor(summaryKeys: ["userLink"]), "person-1");

        Assert.NotNull(detail);
        Assert.Equal("dependencyUnavailable", detail!.Summaries["userLink"].Status);
    }

    [Fact]
    public async Task GetDetailAsync_PreservesRawFieldAndAddsDisplayCompanion()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["PersonNo"] = "P001",
                ["ENAME"] = "张三",
                ["CompanyId"] = "company-1"
            }
        ]);
        var service = new StandardCrudService(
            repository,
            EmptyQueryExecutor(),
            [],
            [new PersonDisplayResolver()],
            [],
            [],
            [],
            []);

        var detail = await service.GetDetailAsync(Descriptor(), "person-1");

        Assert.NotNull(detail);
        Assert.Equal("company-1", detail!.Fields["CompanyId"]);
        Assert.Equal("公司一", detail.Fields["CompanyId_DisplayValue"]);
    }

    [Fact]
    public async Task OptionsAsync_ReturnsRepositoryOptions()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三" }
        ]);
        var service = new StandardCrudService(repository, EmptyQueryExecutor(), [], [], [], [], [], []);

        var options = await service.OptionsAsync(Descriptor(), new StandardCrudListQuery());

        var option = Assert.Single(options);
        Assert.Equal("person-1", option.Value);
        Assert.Equal("P001 张三", option.Label);
    }

    [Fact]
    public async Task ListAsync_WhenDisplayResolverOverwritesRawField_ThrowsStableMessage()
    {
        var executor = DisplayValueQueryExecutor();
        var service = new StandardCrudService(
            new InMemoryStandardCrudRepository([]),
            executor,
            [],
            [new RawOverwritingDisplayResolver()],
            [],
            [],
            [],
            []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListAsync(
            Descriptor(),
            new StandardCrudListQuery(Fields: ["CompanyId"])));

        Assert.Equal(
            "标准 CRUD 资源 organization.person 的显示值解析器尝试覆盖原始字段 CompanyId。",
            ex.Message);
    }

    [Fact]
    public async Task ListAsync_WhenDisplayResolverReturnsUnrequestedField_ThrowsStableMessage()
    {
        var service = new StandardCrudService(
            new InMemoryStandardCrudRepository([]),
            DisplayValueQueryExecutor(),
            [],
            [new ExtraFieldDisplayResolver()],
            [],
            [],
            [],
            []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListAsync(
            Descriptor(),
            new StandardCrudListQuery(Fields: ["CompanyId"])));

        Assert.Equal(
            "标准 CRUD 资源 organization.person 的显示值解析器返回未请求字段 CompanyName。",
            ex.Message);
    }

    [Fact]
    public async Task ListAsync_WhenDisplayResolverReturnsDifferentRowCount_ThrowsStableMessage()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["CompanyId"] = "company-1" }
        ]);
        var executor = new RecordingPlatformQueryExecutor(new PlatformQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["PersonNo"] = "P001",
                ["CompanyId"] = "company-1"
            }
        ],
        Total: 1,
        Page: 1,
        PageSize: 20,
        SortBy: "PersonNo",
        SortDirection: "asc",
        ResponseFields: ["EID", "PersonNo", "CompanyId", "CompanyId_DisplayValue"],
        RequestedDisplayFields:
        [
            new PlatformDisplayValueField("CompanyId_DisplayValue", "CompanyId", "Company", "EID", "ENAME")
        ]));
        var service = new StandardCrudService(
            repository,
            executor,
            [],
            [new BrokenPersonDisplayResolver()],
            [],
            [],
            [],
            []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListAsync(
            Descriptor(),
            new StandardCrudListQuery(Fields: ["EID", "PersonNo", "CompanyId"])));

        Assert.Equal("标准 CRUD 资源 organization.person 的显示值解析器返回行数 0，预期 1。", ex.Message);
    }

    [Fact]
    public async Task ListAsync_WhenDisplayResolverProvidesNoSupportedImplementation_ThrowsStableMessage()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["CompanyId"] = "company-1" }
        ]);
        var executor = new RecordingPlatformQueryExecutor(new PlatformQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["PersonNo"] = "P001",
                ["CompanyId"] = "company-1"
            }
        ],
        Total: 1,
        Page: 1,
        PageSize: 20,
        SortBy: "PersonNo",
        SortDirection: "asc",
        ResponseFields: ["EID", "PersonNo", "CompanyId", "CompanyId_DisplayValue"],
        RequestedDisplayFields:
        [
            new PlatformDisplayValueField("CompanyId_DisplayValue", "CompanyId", "Company", "EID", "ENAME")
        ]));
        var service = new StandardCrudService(
            repository,
            executor,
            [],
            [new MissingDisplayResolver()],
            [],
            [],
            [],
            []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListAsync(
            Descriptor(),
            new StandardCrudListQuery(Fields: ["EID", "PersonNo", "CompanyId"])));

        Assert.Equal(
            "标准 CRUD 资源 organization.person 的显示值解析器未实现批量 ResolveListAsync，也未提供兼容的单行 ResolveAsync。",
            ex.Message);
    }

    [Fact]
    public async Task ListAsync_WhenDisplayFieldsRequestedWithoutResolver_ThrowsStableMessage()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["CompanyId"] = "company-1" }
        ]);
        var executor = new RecordingPlatformQueryExecutor(new PlatformQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["PersonNo"] = "P001",
                ["CompanyId"] = "company-1"
            }
        ],
        Total: 1,
        Page: 1,
        PageSize: 20,
        SortBy: "PersonNo",
        SortDirection: "asc",
        ResponseFields: ["EID", "PersonNo", "CompanyId", "CompanyId_DisplayValue"],
        RequestedDisplayFields:
        [
            new PlatformDisplayValueField("CompanyId_DisplayValue", "CompanyId", "Company", "EID", "ENAME")
        ]));
        var service = new StandardCrudService(
            repository,
            executor,
            [],
            [],
            [],
            [],
            [],
            []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListAsync(
            Descriptor(),
            new StandardCrudListQuery(Fields: ["EID", "PersonNo", "CompanyId"])));

        Assert.Equal("标准 CRUD 资源 organization.person 请求了显示值字段，但未注册显示值解析器。", ex.Message);
    }

    [Fact]
    public async Task ListAsync_WhenDisplayFieldsRequestedWithoutResolverAndNoRows_ThrowsStableMessage()
    {
        var repository = new InMemoryStandardCrudRepository([]);
        var executor = new RecordingPlatformQueryExecutor(new PlatformQueryResult(
            [],
            Total: 0,
            Page: 1,
            PageSize: 20,
            SortBy: "PersonNo",
            SortDirection: "asc",
            ResponseFields: ["EID", "PersonNo", "CompanyId", "CompanyId_DisplayValue"],
            RequestedDisplayFields:
            [
                new PlatformDisplayValueField("CompanyId_DisplayValue", "CompanyId", "Company", "EID", "ENAME")
            ]));
        var service = new StandardCrudService(
            repository,
            executor,
            [],
            [],
            [],
            [],
            [],
            []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListAsync(
            Descriptor(),
            new StandardCrudListQuery(Fields: ["EID", "PersonNo", "CompanyId"])));

        Assert.Equal("标准 CRUD 资源 organization.person 请求了显示值字段，但未注册显示值解析器。", ex.Message);
    }

    private static StandardCrudDescriptor Descriptor(
        IReadOnlyList<string>? summaryKeys = null,
        IReadOnlyList<PlatformDisplayValueField>? displayValueFields = null) => new()
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
        DisplayValueFields = displayValueFields ??
            [new PlatformDisplayValueField("CompanyId_DisplayValue", "CompanyId", "Company", "EID", "ENAME")],
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

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
            IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<IReadOnlyDictionary<string, object?>> result = rows.Select(row =>
                (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
                {
                    ["CompanyId_DisplayValue"] = row.TryGetValue("CompanyId", out var companyId) && Equals(companyId, "company-1")
                        ? "公司一"
                        : string.Empty
                }).ToList();

            return Task.FromResult(result);
        }
    }

    private sealed class BrokenPersonDisplayResolver : IStandardCrudDisplayValueResolver
    {
        public string ResourceKey => "organization.person";

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
            IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>([]);
    }

    private sealed class RawOverwritingDisplayResolver : IStandardCrudDisplayValueResolver
    {
        public string ResourceKey => "organization.person";

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
            IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(
            [
                new Dictionary<string, object?> { ["CompanyId"] = "tampered-company" }
            ]);
    }

    private sealed class ExtraFieldDisplayResolver : IStandardCrudDisplayValueResolver
    {
        public string ResourceKey => "organization.person";

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
            IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(
            [
                new Dictionary<string, object?> { ["CompanyName"] = "公司一" }
            ]);
    }

    private sealed class MissingDisplayResolver : IStandardCrudDisplayValueResolver
    {
        public string ResourceKey => "organization.person";
    }

    private sealed class RecordingPlatformQueryExecutor : IPlatformQueryExecutor
    {
        private readonly PlatformQueryResult _result;

        public RecordingPlatformQueryExecutor(PlatformQueryResult result)
        {
            _result = result;
        }

        public PlatformQueryRequest? LastRequest { get; private set; }

        public Task<PlatformQueryResult> QueryAsync(
            PlatformQueryDescriptor descriptor,
            PlatformQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    private static RecordingPlatformQueryExecutor EmptyQueryExecutor() =>
        new(new PlatformQueryResult([], 0, 1, 20, null, null, [], []));

    private static RecordingPlatformQueryExecutor DisplayValueQueryExecutor() =>
        new(new PlatformQueryResult(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["CompanyId"] = "company-1" }
        ],
        1,
        1,
        20,
        null,
        null,
        ["CompanyId", "CompanyId_DisplayValue"],
        [new PlatformDisplayValueField("CompanyId_DisplayValue", "CompanyId", "Company", "EID", "ENAME")]));

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

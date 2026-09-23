using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.Tests.Support;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Presentation;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudServiceReadTests
{
    [Fact]
    public async Task GetDetailAsync_AuditUsersShareOneDirectoryBatchAndPreserveRaw()
    {
        var repository = new InMemoryStandardCrudRepository(
            [new Dictionary<string, object?> { ["EID"] = "person-1", ["CreatedBy"] = "actor-1", ["UpdatedBy"] = "actor-1" }]);
        var directory = new RecordingUserDirectory();
        var service = new StandardCrudService(
            repository,
            EmptyQueryExecutor(),
            new StandardCrudDefaultWriter(repository, [], []),
            [],
            [],
            [],
            userDisplayNameDirectory: directory);
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person", ModuleId = "Organization", ModelName = "Person",
            Route = "api/organization/persons", IdField = "EID", DisplayField = "EID",
            FieldSet = new StandardCrudFieldSet(["EID"], ["EID", "CreatedBy", "UpdatedBy"], [], []),
            DisplayProjectionPlan = new ModelDisplayProjectionPlan("Person", new[] { "CreatedBy", "UpdatedBy" }
                .Select(field => (FieldDisplayProjectionPlan)new LookupFieldDisplayProjectionPlan(field, field + "_DisplayValue",
                    LookupDisplayProjectionKind.User, "IdentityAccount", "EID", "ENAME") { IsEntityAuditField = true }).ToArray())
        };

        var detail = await service.GetDetailAsync(descriptor, "person-1");

        Assert.Equal(1, directory.Calls);
        Assert.Equal("actor-1", Assert.Single(directory.Ids!));
        Assert.Equal("actor-1", detail!.Fields["CreatedBy"]);
        Assert.Equal("Current name", detail.Fields["CreatedBy_DisplayValue"]);
        Assert.Equal("Current name", detail.Fields["UpdatedBy_DisplayValue"]);
    }

    private sealed class RecordingUserDirectory : IUserDisplayNameDirectory
    {
        public int Calls { get; private set; }
        public IReadOnlyCollection<string>? Ids { get; private set; }
        public Exception? Failure { get; set; }
        public IReadOnlyDictionary<string, string> Names { get; set; } = new Dictionary<string, string> { ["actor-1"] = "Current name" };
        public Task<IReadOnlyDictionary<string, string>> FindManyAsync(IReadOnlyCollection<string> accountIds, CancellationToken cancellationToken = default)
        {
            Calls++;
            Ids = accountIds;
            return Failure is null ? Task.FromResult(Names) : Task.FromException<IReadOnlyDictionary<string, string>>(Failure);
        }
    }

    [Fact]
    public async Task ListAsync_AuditBatchUsesIdsAcrossRowsAndReadsCurrentNamesOnEveryRequest()
    {
        var directory = new RecordingUserDirectory
        {
            Names = new Dictionary<string, string> { ["actor-2"] = "Disabled name", ["actor-1"] = "Original name" }
        };
        var fields = new[] { "CreatedBy", "UpdatedBy" };
        var displayFields = fields.Select(field => new PlatformDisplayValueField(field + "_DisplayValue", field, "IdentityAccount", "EID", "ENAME")).ToArray();
        var executor = new RecordingPlatformQueryExecutor(new PlatformQueryResult(
            [new Dictionary<string, object?> { ["EID"] = "1", ["CreatedBy"] = "actor-1", ["UpdatedBy"] = "actor-2" },
             new Dictionary<string, object?> { ["EID"] = "2", ["CreatedBy"] = "actor-2", ["UpdatedBy"] = "deleted" },
             new Dictionary<string, object?> { ["EID"] = "3", ["CreatedBy"] = "system", ["UpdatedBy"] = "actor-1" }],
            3, 1, 20, null, null, ["EID", .. fields, .. displayFields.Select(field => field.Field)], displayFields));
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.company", ModuleId = "Organization", ModelName = "Company", Route = "api/organization/companies",
            IdField = "EID", DisplayField = "EID", FieldSet = new StandardCrudFieldSet(["EID", .. fields], ["EID", .. fields], [], []),
            DisplayProjectionPlan = new ModelDisplayProjectionPlan("Company", fields.Select(field => (FieldDisplayProjectionPlan)
                new LookupFieldDisplayProjectionPlan(field, field + "_DisplayValue", LookupDisplayProjectionKind.User, "IdentityAccount", "EID", "ENAME")
                { IsEntityAuditField = true }).ToArray())
        };
        var writeRepository = new InMemoryStandardCrudRepository([]);
        var service = new StandardCrudService(
            writeRepository,
            executor,
            new StandardCrudDefaultWriter(writeRepository, [], []),
            [],
            [],
            [],
            userDisplayNameDirectory: directory,
            presentationContextAccessor: new FixedPresentationContextAccessor(new RequestPresentationContext("zh-CN", "Asia/Taipei")));

        var result = await service.ListAsync(descriptor, new StandardCrudListQuery());
        Assert.Equal(1, directory.Calls);
        Assert.Equal(new[] { "actor-1", "actor-2", "deleted" }, directory.Ids);
        Assert.Equal("Disabled name", result.Items[0]["UpdatedBy_DisplayValue"]);
        Assert.Equal("deleted", result.Items[1]["UpdatedBy_DisplayValue"]);
        Assert.Equal("系统", result.Items[2]["CreatedBy_DisplayValue"]);
        directory.Names = new Dictionary<string, string> { ["actor-1"] = "Renamed" };
        result = await service.ListAsync(descriptor, new StandardCrudListQuery());
        Assert.Equal(2, directory.Calls);
        Assert.Equal("Renamed", result.Items[0]["CreatedBy_DisplayValue"]);
        Assert.Equal("actor-1", result.Items[0]["CreatedBy"]);
        directory.Failure = new IOException("offline");
        result = await service.ListAsync(descriptor, new StandardCrudListQuery());
        Assert.Null(result.Items[0]["CreatedBy_DisplayValue"]);
        Assert.Equal("系统", result.Items[2]["CreatedBy_DisplayValue"]);
        directory.Failure = new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ListAsync(descriptor, new StandardCrudListQuery()));
        directory.Failure = new UserDisplayNameDirectoryContractException();
        await Assert.ThrowsAsync<UserDisplayNameDirectoryContractException>(() => service.ListAsync(descriptor, new StandardCrudListQuery()));
        directory.Failure = new ICP.Platform.Authorization.Contracts.DataScopeReadExecutionException(
            "identity.account", new IOException("authorization unavailable"));
        await Assert.ThrowsAsync<ICP.Platform.Authorization.Contracts.DataScopeReadExecutionException>(
            () => service.ListAsync(descriptor, new StandardCrudListQuery()));
        directory.Failure = null;
        directory.Names = new Dictionary<string, string> { ["unrequested-id"] = "Wrong record" };
        await Assert.ThrowsAsync<UserDisplayNameDirectoryContractException>(() => service.ListAsync(descriptor, new StandardCrudListQuery()));
    }

    [Theory]
    [InlineData(null, "en-US", null)]
    [InlineData("system", "en-US", "System")]
    [InlineData("system", "zh-CN", "系统")]
    public async Task GetDetailAsync_EmptyOrSystemActorsDoNotCallDirectory(string? actor, string locale, string? expected)
    {
        var directory = new RecordingUserDirectory { Failure = new InvalidOperationException("must not call") };
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "test.audit", ModuleId = "Organization", ModelName = "Company", Route = "api/companies",
            IdField = "EID", DisplayField = "EID", FieldSet = new StandardCrudFieldSet(["EID"], ["EID", "CreatedBy"], [], []),
            DisplayProjectionPlan = new ModelDisplayProjectionPlan("Company", [new LookupFieldDisplayProjectionPlan(
                "CreatedBy", "CreatedBy_DisplayValue", LookupDisplayProjectionKind.User, "IdentityAccount", "EID", "ENAME") { IsEntityAuditField = true }])
        };
        var writeRepository = new InMemoryStandardCrudRepository(
            [new Dictionary<string, object?> { ["EID"] = "1", ["CreatedBy"] = actor }]);
        var service = new StandardCrudService(
            writeRepository,
            EmptyQueryExecutor(),
            new StandardCrudDefaultWriter(writeRepository, [], []),
            [],
            [],
            [],
            userDisplayNameDirectory: directory,
            presentationContextAccessor: new FixedPresentationContextAccessor(new RequestPresentationContext(locale, "Asia/Taipei")));

        var detail = await service.GetDetailAsync(descriptor, "1");
        Assert.Equal(expected, detail!.Fields["CreatedBy_DisplayValue"]);
        Assert.Equal(0, directory.Calls);
        Assert.Null(await service.GetDetailAsync(descriptor, "missing"));
        Assert.Equal(0, directory.Calls);
    }

    [Fact]
    public async Task GetDetailAsync_AppliesTypedTemporalCompanionsWithoutModuleFormatting()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["BirthDate"] = new DateOnly(1990, 2, 3),
                ["EmploymentStartedAt"] = new DateTime(2030, 1, 2, 0, 4, 5, DateTimeKind.Utc)
            }
        ]);
        var executor = new TemporalDisplayProjectionExecutor(
            new FixedPresentationContextAccessor(new RequestPresentationContext("en-US", "Asia/Taipei")),
            new TemporalDisplayFormatter());
        var service = new StandardCrudService(
            repository,
            EmptyQueryExecutor(),
            new StandardCrudDefaultWriter(repository, [], []),
            [],
            [],
            [],
            temporalDisplayProjectionExecutor: executor);
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "EID",
            FieldSet = new StandardCrudFieldSet(
                ["EID", "BirthDate", "EmploymentStartedAt"],
                ["EID", "BirthDate", "EmploymentStartedAt"],
                [],
                []),
            DisplayProjectionPlan = new ModelDisplayProjectionPlan(
                "Person",
                [
                    new CalendarDateFieldDisplayProjectionPlan("BirthDate", "BirthDate_DisplayValue"),
                    new TemporalFieldDisplayProjectionPlan("EmploymentStartedAt", "EmploymentStartedAt_DisplayValue", TemporalDisplayFormat.DateTime)
                ])
        };

        var detail = await service.GetDetailAsync(descriptor, "person-1");

        Assert.Equal(new DateOnly(1990, 2, 3), detail!.Fields["BirthDate"]);
        Assert.Equal("2/3/1990", detail.Fields["BirthDate_DisplayValue"]);
        Assert.Equal(new DateTime(2030, 1, 2, 0, 4, 5, DateTimeKind.Utc), detail.Fields["EmploymentStartedAt"]);
        Assert.Equal("1/2/2030 8:04:05 AM", detail.Fields["EmploymentStartedAt_DisplayValue"]);
    }
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
            new StandardCrudDefaultWriter(repository, [], []),
            [new KeywordQueryCustomizer()],
            [new PersonDisplayResolver()],
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
        var writeRepository = new InMemoryStandardCrudRepository([]);
        var service = new StandardCrudService(
            writeRepository,
            executor,
            new StandardCrudDefaultWriter(writeRepository, [], []),
            [],
            [new PersonDisplayResolver()],
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
        var writeRepository = new InMemoryStandardCrudRepository([]);
        var service = new StandardCrudService(
            writeRepository,
            executor,
            new StandardCrudDefaultWriter(writeRepository, [], []),
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
            new StandardCrudDefaultWriter(repository, [], []),
            [],
            [new PersonDisplayResolver()],
            [new FailingSummaryProvider()]);

        var detail = await service.GetDetailAsync(Descriptor(summaryKeys: ["userLink"]), "person-1");

        Assert.NotNull(detail);
        Assert.Equal("dependencyUnavailable", detail!.Summaries["userLink"].Status);
    }

    [Fact]
    public async Task GetDetailAsync_WhenSummaryAuthorizationFails_PropagatesFailure()
    {
        var repository = new InMemoryStandardCrudRepository(
            [new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三" }]);
        var service = new StandardCrudService(repository, EmptyQueryExecutor(),
            new StandardCrudDefaultWriter(repository, [], []), [], [new PersonDisplayResolver()],
            [new FailingSummaryProvider(new ICP.Platform.Authorization.Contracts.DataScopeReadExecutionException(
                "identity.account", new IOException("authorization unavailable")))]);

        await Assert.ThrowsAsync<ICP.Platform.Authorization.Contracts.DataScopeReadExecutionException>(
            () => service.GetDetailAsync(Descriptor(summaryKeys: ["userLink"]), "person-1"));
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
            new StandardCrudDefaultWriter(repository, [], []),
            [],
            [new PersonDisplayResolver()],
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
        var service = new StandardCrudService(
            repository,
            EmptyQueryExecutor(),
            new StandardCrudDefaultWriter(repository, [], []),
            [],
            [],
            []);

        var options = await service.OptionsAsync(Descriptor(), new StandardCrudListQuery());

        var option = Assert.Single(options);
        Assert.Equal("person-1", option.Value);
        Assert.Equal("P001 张三", option.Label);
    }

    [Fact]
    public async Task ListAsync_WhenDisplayResolverOverwritesRawField_ThrowsStableMessage()
    {
        var executor = DisplayValueQueryExecutor();
        var writeRepository = new InMemoryStandardCrudRepository([]);
        var service = new StandardCrudService(
            writeRepository,
            executor,
            new StandardCrudDefaultWriter(writeRepository, [], []),
            [],
            [new RawOverwritingDisplayResolver()],
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
        var writeRepository = new InMemoryStandardCrudRepository([]);
        var service = new StandardCrudService(
            writeRepository,
            DisplayValueQueryExecutor(),
            new StandardCrudDefaultWriter(writeRepository, [], []),
            [],
            [new ExtraFieldDisplayResolver()],
            []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListAsync(
            Descriptor(),
            new StandardCrudListQuery(Fields: ["CompanyId"])));

        Assert.Equal(
            "标准 CRUD 资源 organization.person 的显示值解析器返回未请求字段 CompanyName。",
            ex.Message);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(true)]
    public async Task ListAsync_WhenDisplayResolverReturnsNonStringDisplayValue_ThrowsStableMessage(object invalidDisplayValue)
    {
        var writeRepository = new InMemoryStandardCrudRepository([]);
        var service = new StandardCrudService(
            writeRepository,
            DisplayValueQueryExecutor(),
            new StandardCrudDefaultWriter(writeRepository, [], []),
            [],
            [new FixedDisplayValueResolver(invalidDisplayValue)],
            []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListAsync(
            Descriptor(),
            new StandardCrudListQuery(Fields: ["CompanyId"])));

        Assert.Equal(
            "标准 CRUD 资源 organization.person 的显示值解析器返回字段 CompanyId_DisplayValue 的值必须是 string 或 null。",
            ex.Message);
    }

    [Theory]
    [InlineData("公司一")]
    [InlineData(null)]
    public async Task ListAsync_WhenDisplayResolverReturnsSupportedDisplayValue_PreservesRawField(object? displayValue)
    {
        var writeRepository = new InMemoryStandardCrudRepository([]);
        var service = new StandardCrudService(
            writeRepository,
            DisplayValueQueryExecutor(),
            new StandardCrudDefaultWriter(writeRepository, [], []),
            [],
            [new FixedDisplayValueResolver(displayValue)],
            []);

        var result = await service.ListAsync(
            Descriptor(),
            new StandardCrudListQuery(Fields: ["CompanyId"]));

        var row = Assert.Single(result.Items);
        Assert.Equal("company-1", row["CompanyId"]);
        Assert.Equal(displayValue, row["CompanyId_DisplayValue"]);
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
            new StandardCrudDefaultWriter(repository, [], []),
            [],
            [new BrokenPersonDisplayResolver()],
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
            new StandardCrudDefaultWriter(repository, [], []),
            [],
            [new MissingDisplayResolver()],
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
            new StandardCrudDefaultWriter(repository, [], []),
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
            new StandardCrudDefaultWriter(repository, [], []),
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

    private sealed class FixedDisplayValueResolver(object? displayValue) : IStandardCrudDisplayValueResolver
    {
        public string ResourceKey => "organization.person";

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
            IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(
            [
                new Dictionary<string, object?> { ["CompanyId_DisplayValue"] = displayValue }
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

    private sealed class FailingSummaryProvider(Exception? failure = null) : IStandardCrudSummaryProvider
    {
        public string ResourceKey => "organization.person";
        public string Key => "userLink";

        public Task<StandardCrudSummaryBlock> BuildAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyDictionary<string, object?> detail,
            CancellationToken cancellationToken = default)
        {
            throw failure ?? new InvalidOperationException("summary dependency unavailable");
        }
    }

    private sealed class FixedPresentationContextAccessor(RequestPresentationContext? context)
        : IRequestPresentationContextAccessor
    {
        public RequestPresentationContext? Current => context;
    }
}

using System.Text.Json;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Planning;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class PersonStandardCrudResourceTests
{
    [Fact]
    public void PersonDto_SerializesCalendarDateAsIsoDateAndKeepsTemporalCompanions()
    {
        var dto = new PersonDto
        {
            BirthDate = new DateOnly(2000, 2, 29),
            BirthDate_DisplayValue = "2000/2/29",
            EmploymentStartedAt = new DateTime(2030, 1, 2, 0, 4, 5, DateTimeKind.Utc),
            EmploymentStartedAt_DisplayValue = "2030/1/2 08:04:05"
        };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto));

        Assert.Equal("2000-02-29", json.RootElement.GetProperty("BirthDate").GetString());
        Assert.Equal("2000/2/29", json.RootElement.GetProperty("BirthDate_DisplayValue").GetString());
        Assert.Equal("2030-01-02T00:04:05Z", json.RootElement.GetProperty("EmploymentStartedAt").GetString());
        Assert.Equal("2030/1/2 08:04:05", json.RootElement.GetProperty("EmploymentStartedAt_DisplayValue").GetString());
    }

    [Fact]
    public void OrganizationModule_RegistersPersonStandardCrudDescriptor()
    {
        var services = new ServiceCollection();
        new OrganizationModule().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IStandardCrudResourceRegistry>();
        var descriptor = registry.FindByResourceKey("organization.person");

        Assert.NotNull(descriptor);
        Assert.Equal("api/organization/persons", descriptor!.Route);
        Assert.Equal(StandardCrudDeletePolicy.Conditional, descriptor.DeletePolicy);
    }

    [Fact]
    public void PersonCrudDescriptor_DeclaresRouteSummaryDisplayAndUniqueConstraint()
    {
        var descriptor = PersonCrudDescriptorFactory.Create();

        Assert.Equal("organization.person", descriptor.ResourceKey);
        Assert.Equal("api/organization/persons", descriptor.Route);
        Assert.Contains(descriptor.DisplayValueFields, field => field.Field == "CompanyId_DisplayValue");
        Assert.DoesNotContain(descriptor.DisplayValueFields, field => field.Field == "CompanyName");
        Assert.Contains(descriptor.DisplayValueFields, field => field.Field == "DepartmentName");
        Assert.Contains(descriptor.DisplayValueFields, field => field.Field == "PositionName");
        Assert.Contains("userBinding", descriptor.SummaryBlockKeys);
        Assert.Contains("authorizationSummary", descriptor.SummaryBlockKeys);
        Assert.Equal(
            ["PersonNo", "ENAME", "BirthDate", "EmploymentStatus", "EmploymentStartedAt", "EmploymentEndedAt", "IsEnabled"],
            descriptor.SortableFields);
        Assert.Equal("PersonNo", descriptor.DefaultSortField);
        Assert.Equal("asc", descriptor.DefaultSortDirection);
        var constraint = Assert.Single(descriptor.UniqueConstraints);
        Assert.Equal("personNo", constraint.Key);
        Assert.Equal(["PersonNo"], constraint.Fields);
    }

    [Fact]
    public void PersonCrudDescriptor_DoesNotDeclareRevisionConcurrency()
    {
        var descriptor = PersonCrudDescriptorFactory.Create();

        Assert.Null(descriptor.ConcurrencyRevisionField);
        Assert.DoesNotContain("Revision", descriptor.FieldSet.ListFields);
        Assert.DoesNotContain("Revision", descriptor.FieldSet.DetailFields);
        Assert.DoesNotContain(descriptor.FieldPolicies, policy => policy.Field == "Revision");
    }

    [Fact]
    public void PersonCrudDescriptor_ExposesAllEntityAuditFieldsToDetailResponses()
    {
        var descriptor = PersonCrudDescriptorFactory.Create();

        Assert.All(
            new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" },
            field => Assert.Contains(field, descriptor.FieldSet.DetailFields));
    }

    [Fact]
    public void PersonCrudDescriptor_ExposesNullablePersonnelTemporalFieldsAcrossReadWriteAndDisplayPlans()
    {
        var descriptor = PersonCrudDescriptorFactory.Create();
        var temporalFields = new[] { "BirthDate", "EmploymentStartedAt", "EmploymentEndedAt" };

        Assert.All(temporalFields, field =>
        {
            Assert.Contains(field, descriptor.FieldSet.ListFields);
            Assert.Contains(field, descriptor.FieldSet.DetailFields);
            Assert.Contains(field, descriptor.FieldSet.CreateFields);
            Assert.Contains(field, descriptor.FieldSet.UpdateFields);
            Assert.Contains(field, descriptor.SortableFields);
        });
        Assert.Contains(descriptor.DisplayProjectionPlan!.Fields, plan =>
            plan is CalendarDateFieldDisplayProjectionPlan { SourceField: "BirthDate" });
        Assert.Equal(
            2,
            descriptor.DisplayProjectionPlan.Fields.Count(plan => plan is TemporalFieldDisplayProjectionPlan
                && plan.SourceField is "EmploymentStartedAt" or "EmploymentEndedAt"));
    }

    [Fact]
    public void Descriptor_ShouldExposePersonDisplayValueProjectionFields()
    {
        var descriptor = PersonCrudDescriptorFactory.Create();

        Assert.Contains(descriptor.DisplayValueFields, field => field.Field == "DepartmentName");
        Assert.Contains(descriptor.DisplayValueFields, field => field.Field == "PositionName");
    }

    [Fact]
    public void PersonCrudDescriptor_DeclaresStructuredDisplayValueFields()
    {
        var descriptor = PersonCrudDescriptorFactory.Create();

        Assert.Contains(descriptor.DisplayValueFields, field =>
            field.Field == "CompanyId_DisplayValue" &&
            field.SourceField == "CompanyId" &&
            field.TargetModelName == "Company" &&
            field.TargetIdField == "EID" &&
            field.TargetDisplayField == "ENAME");
        Assert.DoesNotContain(descriptor.DisplayValueFields, field => field.Field == "CompanyName");
        Assert.Contains(descriptor.DisplayValueFields, field =>
            field.Field == "DepartmentName" &&
            field.SourceField == "DepartmentId" &&
            field.TargetModelName == "Department");
        Assert.Contains(descriptor.DisplayValueFields, field =>
            field.Field == "PositionName" &&
            field.SourceField == "PositionId" &&
            field.TargetModelName == "Position");
    }

    [Fact]
    public void PersonCrudDescriptor_UsesCompanyDisplayFieldProjectedFromRealModelMetadata()
    {
        var definitions = new OrganizationModelMetadataProvider().GetDefinitions();
        var person = Assert.Single(definitions, definition => definition.Name == "Person");
        var displayPlan = new ModelDisplayProjectionPlanner().Compile(person, definitions).Plan!;
        var projected = Assert.Single(
            new ModelDisplayValueFieldProjector().Project(displayPlan),
            field => field.SourceField == "CompanyId");

        var descriptor = PersonCrudDescriptorFactory.Create();
        var actual = Assert.Single(
            descriptor.DisplayValueFields,
            field => field.SourceField == "CompanyId");

        Assert.Equal(projected, actual);
        Assert.Contains(descriptor.DisplayValueFields, field => field.Field == "DepartmentName");
        Assert.Contains(descriptor.DisplayValueFields, field => field.Field == "PositionName");
    }

    [Fact]
    public void PersonCrudDescriptor_RequiresIdentityFieldsAndLeavesAffiliationOptional()
    {
        var descriptor = PersonCrudDescriptorFactory.Create();

        var required = descriptor.RequiredFields.ToDictionary(item => item.Field, StringComparer.Ordinal);

        Assert.Equal(StandardCrudRequiredFieldScope.CreateOnly, required["PersonNo"].Scope);
        Assert.Equal("Organization.Person.PersonNoRequired", required["PersonNo"].ErrorCode);
        Assert.Equal(StandardCrudRequiredFieldScope.CreateAndUpdate, required["ENAME"].Scope);
        Assert.Equal("Organization.Person.NameRequired", required["ENAME"].ErrorCode);
        Assert.Equal(StandardCrudRequiredFieldScope.CreateOnly, required["IsEnabled"].Scope);
        Assert.Equal("Organization.Person.EnabledRequired", required["IsEnabled"].ErrorCode);
        Assert.DoesNotContain("IsEnabled", descriptor.FieldSet.UpdateFields);
        Assert.Equal(StandardCrudRequiredFieldScope.CreateAndUpdate, required["EmploymentStatus"].Scope);
        Assert.Equal("Organization.Person.EmploymentStatusRequired", required["EmploymentStatus"].ErrorCode);
        Assert.DoesNotContain("CompanyId", required.Keys);
        Assert.DoesNotContain("DepartmentId", required.Keys);
        Assert.DoesNotContain("PositionId", required.Keys);
    }

    [Fact]
    public void PersonCrudQueryCustomizer_MapsPositionAndEnabledFilters()
    {
        var customizer = new PersonCrudQueryCustomizer();
        var descriptor = PersonCrudDescriptorFactory.Create();

        var conditions = customizer.BuildConditions(
            descriptor,
            new StandardCrudListQuery(Filters: new Dictionary<string, string?>
            {
                ["positionId"] = "Position_1",
                ["isEnabled"] = "false"
            }));

        Assert.Contains(conditions, condition =>
            condition.Field == "PositionId" && condition.Operator == "=" && Equals(condition.Value, "Position_1"));
        Assert.Contains(conditions, condition =>
            condition.Field == "IsEnabled" && condition.Operator == "=" && Equals(condition.Value, "false"));
    }

    [Fact]
    public void PersonCrudQueryCustomizer_MapsExplicitBirthDateBoundsToInclusiveRawFieldConditions()
    {
        var customizer = new PersonCrudQueryCustomizer();
        var descriptor = PersonCrudDescriptorFactory.Create();

        var conditions = customizer.BuildConditions(
            descriptor,
            new StandardCrudListQuery(Filters: new Dictionary<string, string?>
            {
                ["birthDateFrom"] = "2000-02-29",
                ["birthDateTo"] = "2010-12-31"
            }));

        Assert.Contains(conditions, condition =>
            condition.Field == "BirthDate" && condition.Operator == "gte" && Equals(condition.Value, "2000-02-29"));
        Assert.Contains(conditions, condition =>
            condition.Field == "BirthDate" && condition.Operator == "lte" && Equals(condition.Value, "2010-12-31"));
    }

    [Theory]
    [InlineData("2000-02-29", null, "gte", "2000-02-29")]
    [InlineData(null, "2010-12-31", "lte", "2010-12-31")]
    public void PersonCrudQueryCustomizer_SupportsEitherBirthDateBoundary(
        string? from,
        string? to,
        string expectedOperator,
        string expectedValue)
    {
        var conditions = new PersonCrudQueryCustomizer().BuildConditions(
            PersonCrudDescriptorFactory.Create(),
            new StandardCrudListQuery(Filters: new Dictionary<string, string?>
            {
                ["birthDateFrom"] = from,
                ["birthDateTo"] = to
            }));

        var condition = Assert.Single(conditions);
        Assert.Equal("BirthDate", condition.Field);
        Assert.Equal(expectedOperator, condition.Operator);
        Assert.Equal(expectedValue, condition.Value);
    }

    [Fact]
    public void PersonCrudQueryCustomizer_RejectsInvalidCalendarDateBoundary()
    {
        var error = Assert.Throws<PlatformQueryValidationException>(() =>
            new PersonCrudQueryCustomizer().BuildConditions(
                PersonCrudDescriptorFactory.Create(),
                new StandardCrudListQuery(Filters: new Dictionary<string, string?>
                {
                    ["birthDateFrom"] = "2001-02-29"
                })));

        Assert.Equal("PlatformQuery.InvalidConditionValue", error.Code);
    }

    [Fact]
    public void PersonCrudQueryCustomizer_RejectsReversedBirthDateRange()
    {
        var error = Assert.Throws<PlatformQueryValidationException>(() =>
            new PersonCrudQueryCustomizer().BuildConditions(
                PersonCrudDescriptorFactory.Create(),
                new StandardCrudListQuery(Filters: new Dictionary<string, string?>
                {
                    ["birthDateFrom"] = "2010-12-31",
                    ["birthDateTo"] = "2000-02-29"
                })));

        Assert.Equal("PlatformQuery.InvalidConditionRange", error.Code);
    }

    [Fact]
    public void LegacyPersonController_IsRemoved()
    {
        var legacyType = typeof(OrganizationModule).Assembly.GetType(
            "ICP.Platform.Modules.Organization.Controllers.Persons" + "Controller");

        Assert.Null(legacyType);
        Assert.NotNull(typeof(PersonUserBindingsController));
    }

    [Fact]
    public async Task PersonCrudDisplayValueResolver_ReturnsOnlyRequestedCompanyCompanionAndBatchesDistinctIds()
    {
        var companyDirectory = new FakeCompanyDirectory(
        [
            new Company { Id = "Company_1", Name = "总公司" }
        ]);
        var departmentDirectory = new FakeDepartmentDirectory([]);
        var positionRepository = new FakePositionRepository([]);
        var resolver = new PersonCrudDisplayValueResolver(
            companyDirectory,
            departmentDirectory,
            positionRepository);
        var descriptor = PersonCrudDescriptorFactory.Create();
        var companyDisplayField = Assert.Single(
            descriptor.DisplayValueFields,
            field => field.Field == "CompanyId_DisplayValue");
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            new Dictionary<string, object?> { ["CompanyId"] = "Company_1" },
            new Dictionary<string, object?> { ["CompanyId"] = "Company_1" },
            new Dictionary<string, object?> { ["CompanyId"] = "Company_missing" }
        ];

        var resolved = await resolver.ResolveListAsync(
            descriptor,
            rows,
            [companyDisplayField],
            CancellationToken.None);

        Assert.All(resolved, row =>
        {
            var entry = Assert.Single(row);
            Assert.Equal("CompanyId_DisplayValue", entry.Key);
        });
        Assert.Equal("总公司", resolved[0]["CompanyId_DisplayValue"]);
        Assert.Equal("总公司", resolved[1]["CompanyId_DisplayValue"]);
        Assert.Equal(string.Empty, resolved[2]["CompanyId_DisplayValue"]);
        Assert.Equal(1, companyDirectory.BatchCallCount);
        Assert.Equal(
            new[] { "Company_1", "Company_missing" },
            Assert.Single(companyDirectory.RequestedBatches));
        Assert.Equal(0, departmentDirectory.BatchCallCount);
        Assert.Equal(0, positionRepository.BatchCallCount);
    }

    [Fact]
    public async Task PersonCrudDisplayValueResolver_ResolvesRequestedDisplayFieldsInBatch()
    {
        var companyDirectory = new FakeCompanyDirectory(
        [
            new Company { Id = "Company_1", Name = "总公司" }
        ]);
        var departmentDirectory = new FakeDepartmentDirectory(
        [
            new Department { Id = "Department_1", Name = "研发部" }
        ]);
        var positionRepository = new FakePositionRepository(
        [
            new Position { Id = "Position_1", Name = "工程师", IsEnabled = true },
            new Position { Id = "Position_2", Name = "旧岗位", IsEnabled = false }
        ]);
        var resolver = new PersonCrudDisplayValueResolver(
            companyDirectory,
            departmentDirectory,
            positionRepository);

        var descriptor = PersonCrudDescriptorFactory.Create();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            new Dictionary<string, object?> { ["CompanyId"] = "Company_1", ["DepartmentId"] = "Department_1", ["PositionId"] = "Position_1" },
            new Dictionary<string, object?> { ["CompanyId"] = "Company_1", ["DepartmentId"] = "Department_1", ["PositionId"] = "Position_2" }
        ];

        var resolved = await resolver.ResolveListAsync(
            descriptor,
            rows,
            descriptor.DisplayValueFields,
            CancellationToken.None);

        Assert.Equal("总公司", resolved[0]["CompanyId_DisplayValue"]);
        Assert.DoesNotContain("CompanyName", resolved[0].Keys);
        Assert.Equal("研发部", resolved[1]["DepartmentName"]);
        Assert.Equal("工程师", resolved[0]["PositionName"]);
        Assert.Equal("旧岗位（已停用）", resolved[1]["PositionName"]);
        Assert.Equal(1, companyDirectory.BatchCallCount);
        Assert.Equal(1, departmentDirectory.BatchCallCount);
        Assert.Equal(1, positionRepository.BatchCallCount);
    }

    [Fact]
    public async Task PersonCrudDisplayValueResolver_MarksDisabledAffiliationWithoutHidingItsName()
    {
        var resolver = new PersonCrudDisplayValueResolver(
            new FakeCompanyDirectory([new Company { Id = "Company_1", Name = "旧公司", IsEnabled = false }]),
            new FakeDepartmentDirectory([new Department { Id = "Department_1", Name = "旧部门", IsEnabled = false }]),
            new FakePositionRepository([]));
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            new Dictionary<string, object?>
            {
                ["CompanyId"] = "Company_1",
                ["DepartmentId"] = "Department_1"
            }
        ];

        var result = await resolver.ResolveListAsync(
            PersonCrudDescriptorFactory.Create(),
            rows,
            PersonCrudDescriptorFactory.Create().DisplayValueFields,
            CancellationToken.None);

        Assert.Equal("旧公司（已停用）", result[0]["CompanyId_DisplayValue"]);
        Assert.Equal("旧部门（已停用）", result[0]["DepartmentName"]);
    }

    private sealed class FakeCompanyDirectory : ICompanyDirectory
    {
        private readonly IReadOnlyDictionary<string, Company> _companies;

        public FakeCompanyDirectory(IEnumerable<Company> companies)
        {
            _companies = companies.ToDictionary(company => company.Id, StringComparer.Ordinal);
        }

        public int BatchCallCount { get; private set; }

        public List<IReadOnlyCollection<string>> RequestedBatches { get; } = [];

        public Task<Company?> GetByIdAsync(string companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_companies.TryGetValue(companyId, out var company) ? company : null);

        public Task<IReadOnlyDictionary<string, Company>> GetByIdsAsync(
            IReadOnlyCollection<string> companyIds,
            CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            RequestedBatches.Add(companyIds.ToArray());
            return Task.FromResult<IReadOnlyDictionary<string, Company>>(
                _companies.Values
                    .Where(company => companyIds.Contains(company.Id))
                    .ToDictionary(company => company.Id, StringComparer.Ordinal));
        }

        public Task<Company?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OrganizationDirectoryPage<Company>> LookupAsync(
            OrganizationDirectoryLookupRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDepartmentDirectory : IDepartmentDirectory
    {
        private readonly IReadOnlyDictionary<string, Department> _departments;

        public FakeDepartmentDirectory(IEnumerable<Department> departments)
        {
            _departments = departments.ToDictionary(department => department.Id, StringComparer.Ordinal);
        }

        public int BatchCallCount { get; private set; }

        public Task<Department?> GetByIdAsync(string departmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_departments.TryGetValue(departmentId, out var department) ? department : null);

        public Task<IReadOnlyDictionary<string, Department>> GetByIdsAsync(
            IReadOnlyCollection<string> departmentIds,
            CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            return Task.FromResult<IReadOnlyDictionary<string, Department>>(
                _departments.Values
                    .Where(department => departmentIds.Contains(department.Id))
                    .ToDictionary(department => department.Id, StringComparer.Ordinal));
        }

        public Task<Department?> GetByCompanyAndCodeAsync(
            string companyId,
            string code,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OrganizationDirectoryPage<Department>> LookupAsync(
            string companyId,
            OrganizationDirectoryLookupRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakePositionRepository : IPositionDirectory
    {
        private readonly IReadOnlyDictionary<string, Position> _positions;

        public FakePositionRepository(IEnumerable<Position> positions)
        {
            _positions = positions.ToDictionary(position => position.Id, StringComparer.Ordinal);
        }

        public int BatchCallCount { get; private set; }

        public Task<Position?> GetByIdAsync(string positionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_positions.TryGetValue(positionId, out var position) ? position : null);

        public Task<IReadOnlyDictionary<string, Position>> GetByIdsAsync(
            IReadOnlyCollection<string> positionIds,
            CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            return Task.FromResult<IReadOnlyDictionary<string, Position>>(
                _positions.Values
                    .Where(position => positionIds.Contains(position.Id))
                    .ToDictionary(position => position.Id, StringComparer.Ordinal));
        }

    }
}

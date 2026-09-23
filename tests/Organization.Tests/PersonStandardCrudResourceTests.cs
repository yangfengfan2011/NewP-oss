using System.Text.Json;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Crud.Planning;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.Auditing;
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
    public async Task ConditionalDeleteExecutor_WhenCalledDirectly_RoutesToUnifiedObjectOperation()
    {
        var operations = new RecordingDeleteObjectOperations();
        var executor = new PersonCrudDeleteGuard(
            new UnusedBindingRepository(),
            null!,
            () => operations,
            new InactiveObjectOperationContext());

        var result = await executor.DeleteAsync(
            PersonCrudDescriptorFactory.Create(),
            "person-alias");

        Assert.True(result.Succeeded);
        Assert.Equal(1, operations.Executions);
        Assert.Equal("person-alias", operations.TargetId);
    }

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
        Assert.True(descriptor.RequiresCreationRecord);
        Assert.True(descriptor.RequiresUpdateRecord);
        Assert.Contains(services, service => service.ServiceType == typeof(IStandardCrudUpdateRecorder)
            && service.ImplementationType == typeof(PersonUpdateRecorder));
    }

    [Fact]
    public void PersonCrudDescriptor_DeclaresRouteSummaryDisplayAndUniqueConstraint()
    {
        var descriptor = OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create());

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
        var descriptor = OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create());

        Assert.Null(descriptor.ConcurrencyRevisionField);
        Assert.DoesNotContain("Revision", descriptor.FieldSet.ListFields);
        Assert.DoesNotContain("Revision", descriptor.FieldSet.DetailFields);
        Assert.DoesNotContain(descriptor.FieldPolicies, policy => policy.Field == "Revision");
    }

    [Fact]
    public void PersonCrudDescriptor_ExposesAllEntityAuditFieldsToDetailResponses()
    {
        var descriptor = OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create());

        Assert.All(
            new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" },
            field => Assert.Contains(field, descriptor.FieldSet.DetailFields));
    }

    [Fact]
    public void PersonCrudDescriptor_ExposesNullablePersonnelTemporalFieldsAcrossReadWriteAndDisplayPlans()
    {
        var descriptor = OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create());
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
        var descriptor = OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create());

        Assert.Contains(descriptor.DisplayValueFields, field => field.Field == "DepartmentName");
        Assert.Contains(descriptor.DisplayValueFields, field => field.Field == "PositionName");
    }

    [Fact]
    public void PersonCrudDescriptor_DeclaresStructuredDisplayValueFields()
    {
        var descriptor = OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create());

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

        var descriptor = OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create());
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
        var descriptor = OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create());

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
        var descriptor = OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create());

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
        var descriptor = OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create());

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
            OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create()),
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
                OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create()),
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
                OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create()),
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
    public async Task PersonCrudDisplayValueResolver_MasksUnreadableExistingTargetsWithoutDroppingRelations()
    {
        var query = new RecordingOrganizationQuery(
            companies: [new Company { Id = "Company_1", Name = "私有公司" }],
            departments: [new Department { Id = "Department_1", Name = "可见部门" }],
            positions: [new Position { Id = "Position_1", Name = "私有岗位" }]);
        var readable = new RecordingReadableIds("Department_1");
        var resolver = new PersonCrudDisplayValueResolver(query, readable);
        var rows = new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?>
            {
                ["CompanyId"] = "Company_1", ["DepartmentId"] = "Department_1", ["PositionId"] = "Position_1"
            },
            new Dictionary<string, object?> { ["CompanyId"] = "Company_missing" }
        };

        var result = await resolver.ResolveListAsync(
            OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create()), rows,
            OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create()).DisplayValueFields);

        Assert.Equal("***", result[0]["CompanyId_DisplayValue"]);
        Assert.Equal("可见部门", result[0]["DepartmentName"]);
        Assert.Equal("***", result[0]["PositionName"]);
        Assert.Equal(string.Empty, result[1]["CompanyId_DisplayValue"]);
        Assert.Equal(3, readable.Calls.Count);
        Assert.Contains(readable.Calls, call => call.ResourceKey == "organization.company" && call.Ids.SequenceEqual(["Company_1", "Company_missing"]));
        Assert.Contains(readable.Calls, call => call.ResourceKey == "organization.department" && call.Ids.SequenceEqual(["Department_1"]));
        Assert.Contains(readable.Calls, call => call.ResourceKey == "organization.position" && call.Ids.SequenceEqual(["Position_1"]));
    }

    private sealed class RecordingReadableIds(params string[] readableIds) : ICP.Platform.Authorization.Contracts.IDataScopeReadableIds
    {
        public List<(string ResourceKey, string[] Ids)> Calls { get; } = [];

        /// <inheritdoc/>
        public Task<IReadOnlySet<string>> GetReadableIdsAsync(string resourceKey, IReadOnlyCollection<string> objectIds,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((resourceKey, objectIds.ToArray()));
            return Task.FromResult<IReadOnlySet<string>>(readableIds.ToHashSet(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task PersonCrudDisplayValueResolver_ReturnsOnlyRequestedCompanyCompanionAndBatchesDistinctIds()
    {
        var query = new RecordingOrganizationQuery(companies: [new Company { Id = "Company_1", Name = "总公司" }]);
        var resolver = new PersonCrudDisplayValueResolver(query, new AllowAllReadableIds());
        var descriptor = OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create());
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
        Assert.Equal(1, query.Reads);
        Assert.Equal(new[] { "Company_1", "Company_missing" }, Assert.Single(query.Batches).Value);

    }

    [Fact]
    public async Task PersonCrudDisplayValueResolver_ResolvesRequestedDisplayFieldsInBatch()
    {
        var query = new RecordingOrganizationQuery(
            companies: [new Company { Id = "Company_1", Name = "总公司" }],
            departments: [new Department { Id = "Department_1", Name = "研发部" }],
            positions: [new Position { Id = "Position_1", Name = "工程师" }, new Position { Id = "Position_2", Name = "旧岗位", IsEnabled = false }]);
        var resolver = new PersonCrudDisplayValueResolver(query, new AllowAllReadableIds());

        var descriptor = OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create());
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
        Assert.Equal(1, query.Reads);
        Assert.Equal(3, query.Batches.Count);

    }

    [Fact]
    public async Task PersonCrudDisplayValueResolver_MarksDisabledAffiliationWithoutHidingItsName()
    {
        var resolver = new PersonCrudDisplayValueResolver(new RecordingOrganizationQuery(
            companies: [new Company { Id = "Company_1", Name = "旧公司", IsEnabled = false }],
            departments: [new Department { Id = "Department_1", Name = "旧部门", IsEnabled = false }]), new AllowAllReadableIds());
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            new Dictionary<string, object?>
            {
                ["CompanyId"] = "Company_1",
                ["DepartmentId"] = "Department_1"
            }
        ];

        var result = await resolver.ResolveListAsync(
            OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create()),
            rows,
            OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create()).DisplayValueFields,
            CancellationToken.None);

        Assert.Equal("旧公司（已停用）", result[0]["CompanyId_DisplayValue"]);
        Assert.Equal("旧部门（已停用）", result[0]["DepartmentName"]);
    }

    private sealed class RecordingDeleteObjectOperations : IObjectOperationExecutor
    {
        public int Executions { get; private set; }
        public string? TargetId { get; private set; }

        public bool CanExecute(string objectKey, ObjectOperationAction action) =>
            objectKey == "organization.person" && action == ObjectOperationAction.Delete;

        public Task<StandardCrudMutationResult> ExecuteAsync(
            string objectKey,
            ObjectOperationAction action,
            string? targetId,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            Executions++;
            TargetId = targetId;
            return Task.FromResult(StandardCrudMutationResult.Success());
        }
    }

    private sealed class InactiveObjectOperationContext : IObjectOperationExecutionContext
    {
        public bool IsExecuting(string objectKey, ObjectOperationAction action, string? targetId) => false;
    }

    private sealed class UnusedBindingRepository : IPersonUserBindingRepository
    {
        public ValueTask<PersonUserBindingState?> FindAsync(
            string personEid,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> TryBindAsync(
            string personEid,
            string accountEid,
            string reason,
            EntityAuditValues auditValues,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask SetEnablementAsync(
            string personEid,
            bool isEnabled,
            EntityAuditValues auditValues,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

}

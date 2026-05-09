using System.Collections;
using System.Reflection;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class PersonListContractTests
{
    private const string CompanyId = "Company_11111111111111111111111111111111";
    private const string DepartmentId = "Department_22222222222222222222222222222222";

    [Fact]
    public void PersonsController_DeclaresListAndDisableEndpoints()
    {
        var controllerType = typeof(PersonsController);

        var listAction = controllerType.GetMethod("GetList");
        var disableAction = controllerType.GetMethod("Disable");

        Assert.NotNull(listAction);
        Assert.NotNull(disableAction);
        Assert.Contains(listAction!.GetCustomAttributes<HttpGetAttribute>(), attribute => attribute.Template is null);
        Assert.Contains(disableAction!.GetCustomAttributes<HttpPostAttribute>(), attribute => attribute.Template == "{id}/disable");
    }

    [Fact]
    public void PersonsController_DeclaresOptionsAndDeleteEndpoints()
    {
        var controllerType = typeof(PersonsController);

        var optionsAction = controllerType.GetMethod("Options");
        var deleteAction = controllerType.GetMethod("Delete");

        Assert.NotNull(optionsAction);
        Assert.NotNull(deleteAction);
        Assert.Contains(optionsAction!.GetCustomAttributes<HttpGetAttribute>(), attribute => attribute.Template == "options");
        Assert.Contains(deleteAction!.GetCustomAttributes<HttpDeleteAttribute>(), attribute => attribute.Template == "{id}");
    }

    [Fact]
    public async Task PersonsController_GetList_PreservesCompatibilityListShapeWithoutTruncatingAtOneHundred()
    {
        var standardCrudService = new RecordingStandardCrudService([]);
        var controller = BuildController(standardCrudService);

        var response = await controller.GetList(
            keyword: null,
            companyId: null,
            departmentId: null,
            employmentStatus: null);

        Assert.IsType<OkObjectResult>(response.Result);
        Assert.Equal(int.MaxValue, standardCrudService.LastListQuery!.PageSize);
    }

    [Fact]
    public async Task PersonsController_GetList_ParsesEmploymentStatusIgnoringCase()
    {
        var standardCrudService = new RecordingStandardCrudService(
        [
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["EID"] = "Person_33333333333333333333333333333333",
                ["PersonNo"] = "P003",
                ["ENAME"] = "王五",
                ["CompanyName"] = "ICP研发中心",
                ["DepartmentName"] = "人力资源部",
                ["EmploymentStatus"] = "parttime",
                ["IsEnabled"] = true
            }
        ]);
        var controller = BuildController(standardCrudService);

        var response = await controller.GetList(
            keyword: null,
            companyId: null,
            departmentId: null,
            employmentStatus: null);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var item = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<PersonListItemDto>>(ok.Value));
        Assert.Equal(EmploymentStatus.PartTime, item.EmploymentStatus);
    }

    [Fact]
    public async Task PersonsController_GetList_ReturnsStringIdWithoutGuidParsing()
    {
        var standardCrudService = new RecordingStandardCrudService(
        [
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["EID"] = "Person_00000000000000000000000000000001",
                ["PersonNo"] = "P001",
                ["ENAME"] = "张三",
                ["CompanyName"] = "ICP研发中心",
                ["DepartmentName"] = "人力资源部",
                ["EmploymentStatus"] = "FullTime",
                ["IsEnabled"] = true
            }
        ]);
        var controller = BuildController(standardCrudService);

        var response = await controller.GetList(null, null, null, null);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var item = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<PersonListItemDto>>(ok.Value));
        Assert.Equal("Person_00000000000000000000000000000001", item.Id);
    }

    [Fact]
    public async Task PersonsController_Get_ParsesEmploymentStatusIgnoringCase()
    {
        var personId = "Person_44444444444444444444444444444444";
        var standardCrudService = new RecordingStandardCrudService([], new StandardCrudDetail
        {
            Fields = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["EID"] = personId,
                ["PersonNo"] = "P004",
                ["ENAME"] = "赵六",
                ["EmploymentStatus"] = "contractor",
                ["IsEnabled"] = true,
                ["CreatedAt"] = DateTime.UtcNow
            }
        });
        var controller = BuildController(standardCrudService);

        var response = await controller.Get(personId);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var detail = Assert.IsType<PersonDetailDto>(ok.Value);
        Assert.Equal(EmploymentStatus.Contractor, detail.EmploymentStatus);
    }

    [Fact]
    public async Task PersonsController_Create_DelegatesToStandardCrudEndpoint()
    {
        var standardCrudService = new RecordingStandardCrudService([], MutationDetail());
        var controller = BuildController(standardCrudService);

        var response = await controller.Create(new PersonDto
        {
            PersonNo = "P100",
            Name = "标准人员",
            CompanyId = CompanyId,
            DepartmentId = DepartmentId,
            EmploymentStatus = EmploymentStatus.FullTime,
            IsEnabled = true
        });

        Assert.Equal(nameof(IStandardCrudService.CreateAsync), standardCrudService.LastMutation);
        Assert.Equal("P100", standardCrudService.LastMutationValues!["PersonNo"]);
        Assert.Equal("标准人员", standardCrudService.LastMutationValues["ENAME"]);
        Assert.Equal(CompanyId, standardCrudService.LastMutationValues["CompanyId"]);
        Assert.IsType<CreatedAtActionResult>(response.Result);
    }

    [Fact]
    public async Task PersonsController_Update_DelegatesToStandardCrudEndpointWithoutPersonNo()
    {
        var personId = "Person_55555555555555555555555555555555";
        var standardCrudService = new RecordingStandardCrudService([], MutationDetail(personId));
        var controller = BuildController(standardCrudService);

        var response = await controller.Update(personId, new PersonDto
        {
            PersonNo = "SHOULD-NOT-BE-SENT",
            Name = "标准更新",
            CompanyId = CompanyId,
            DepartmentId = DepartmentId,
            EmploymentStatus = EmploymentStatus.FullTime,
            IsEnabled = true
        });

        Assert.Equal(nameof(IStandardCrudService.UpdateAsync), standardCrudService.LastMutation);
        Assert.False(standardCrudService.LastMutationValues!.ContainsKey("PersonNo"));
        Assert.Equal("标准更新", standardCrudService.LastMutationValues["ENAME"]);
        var ok = Assert.IsType<OkObjectResult>(response.Result);
        Assert.IsType<PersonDto>(ok.Value);
    }

    [Fact]
    public async Task PersonsController_Create_WhenStandardCrudReturnsConflict_UsesStandardError()
    {
        var standardCrudService = new RecordingStandardCrudService(
            [],
            mutationError: new StandardCrudError
            {
                StatusCode = 409,
                Code = "Organization.Person.PersonNoDuplicated",
                Message = "人员工号必须唯一。",
                ReasonKey = "organization.person.personNoDuplicated"
            });
        var controller = BuildController(standardCrudService);

        var response = await controller.Create(new PersonDto
        {
            PersonNo = "P100",
            Name = "重复人员",
            IsEnabled = true
        });

        var conflict = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(409, conflict.StatusCode);
        var error = Assert.IsType<StandardCrudError>(conflict.Value);
        Assert.Equal("Organization.Person.PersonNoDuplicated", error.Code);
    }

    [Fact]
    public async Task PersonsController_Create_WhenStandardCrudSucceedsWithoutDetail_ReturnsStableError()
    {
        var standardCrudService = new RecordingStandardCrudService([]);
        var controller = BuildController(standardCrudService);

        var response = await controller.Create(new PersonDto
        {
            PersonNo = "P100",
            Name = "缺失详情",
            IsEnabled = true
        });

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(500, result.StatusCode);
        var error = Assert.IsType<StandardCrudError>(result.Value);
        Assert.Equal("StandardCrud.MutationDetailMissing", error.Code);
    }

    [Fact]
    public async Task PersonsController_Update_WhenStandardCrudSucceedsWithoutDetail_ReturnsStableError()
    {
        var standardCrudService = new RecordingStandardCrudService([]);
        var controller = BuildController(standardCrudService);

        var response = await controller.Update("Person_55555555555555555555555555555555", new PersonDto
        {
            PersonNo = "SHOULD-NOT-BE-SENT",
            Name = "缺失详情",
            IsEnabled = true
        });

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(500, result.StatusCode);
        var error = Assert.IsType<StandardCrudError>(result.Value);
        Assert.Equal("StandardCrud.MutationDetailMissing", error.Code);
    }

    [Fact]
    public async Task PersonsController_Create_WithDuplicatePersonNo_UsesRealStandardCrudUniqueConstraint()
    {
        var controller = BuildControllerWithRealStandardCrudRows(
            StandardCrudPersonRow("Person_66666666666666666666666666666666", "P100", "已存在人员"));

        var response = await controller.Create(new PersonDto
        {
            PersonNo = " p100 ",
            Name = "重复人员",
            IsEnabled = true
        });

        var conflict = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(409, conflict.StatusCode);
        var error = Assert.IsType<StandardCrudError>(conflict.Value);
        Assert.Equal("Organization.Person.PersonNoDuplicated", error.Code);
    }

    [Fact]
    public async Task PersonsController_Update_WhenExistingPersonNoAlreadyConflicts_UsesRealStandardCrudUniqueConstraint()
    {
        var personId = "Person_77777777777777777777777777777777";
        var controller = BuildControllerWithRealStandardCrudRows(
            StandardCrudPersonRow(personId, "P100", "待更新人员"),
            StandardCrudPersonRow("Person_88888888888888888888888888888888", "p100", "冲突人员"));

        var response = await controller.Update(personId, new PersonDto
        {
            PersonNo = "SHOULD-NOT-BE-SENT",
            Name = "更新人员",
            IsEnabled = true
        });

        var conflict = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(409, conflict.StatusCode);
        var error = Assert.IsType<StandardCrudError>(conflict.Value);
        Assert.Equal("Organization.Person.PersonNoDuplicated", error.Code);
    }

    [Fact]
    public void IPersonService_DeclaresListAndDisableContracts()
    {
        var serviceType = typeof(IPersonService);

        Assert.NotNull(serviceType.GetMethod("GetListAsync"));
        Assert.NotNull(serviceType.GetMethod("DisableAsync"));
    }

    [Fact]
    public void IPersonRepository_DeclaresListContract()
    {
        var repositoryType = typeof(IPersonRepository);

        Assert.NotNull(repositoryType.GetMethod("GetListAsync"));
    }

    [Fact]
    public void PersonListItemDto_ExposesRequiredListColumns()
    {
        var dtoType = GetOrganizationAssembly().GetType("ICP.Platform.Modules.Organization.DTOs.PersonListItemDto");

        Assert.NotNull(dtoType);
        Assert.NotNull(dtoType!.GetProperty("Id"));
        Assert.NotNull(dtoType.GetProperty("PersonNo"));
        Assert.NotNull(dtoType.GetProperty("Name"));
        Assert.NotNull(dtoType.GetProperty("CompanyName"));
        Assert.NotNull(dtoType.GetProperty("DepartmentName"));
        Assert.NotNull(dtoType.GetProperty("EmploymentStatus"));
        Assert.NotNull(dtoType.GetProperty("IsEnabled"));
    }

    [Fact]
    public void PersonDetailDto_ExposesStatusizedSummaryBlocks()
    {
        var assembly = GetOrganizationAssembly();
        var detailType = assembly.GetType("ICP.Platform.Modules.Organization.DTOs.PersonDetailDto");
        var summaryType = assembly.GetType("ICP.Platform.Modules.Organization.DTOs.PersonSummaryBlockDto");

        Assert.NotNull(detailType);
        Assert.NotNull(summaryType);
        Assert.NotNull(detailType!.GetProperty("UserLinkSummary"));
        Assert.NotNull(detailType.GetProperty("AuthorizationSummary"));
        Assert.Equal(summaryType, detailType.GetProperty("UserLinkSummary")!.PropertyType);
        Assert.Equal(summaryType, detailType.GetProperty("AuthorizationSummary")!.PropertyType);
        Assert.NotNull(summaryType!.GetProperty("Status"));
        Assert.NotNull(summaryType.GetProperty("Data"));
    }

    [Fact]
    public void PersonCrudDescriptor_DeclaresQueryDisplaySummaryAndConditionalDelete()
    {
        var descriptor = PersonCrudDescriptorFactory.Create();

        Assert.Equal("organization.person", descriptor.ResourceKey);
        Assert.Equal("Person", descriptor.ModelName);
        Assert.Equal("api/organization/persons", descriptor.Route);
        Assert.Equal(StandardCrudDeletePolicy.Conditional, descriptor.DeletePolicy);
        Assert.Contains("CompanyName", descriptor.DisplayValueFields);
        Assert.Contains("DepartmentName", descriptor.DisplayValueFields);
        Assert.Contains("userLink", descriptor.SummaryBlockKeys);
        Assert.Contains("authorizationSummary", descriptor.SummaryBlockKeys);
    }

    [Fact]
    public void PersonCrudDescriptor_DeclaresPersonNoGlobalUniqueConstraint()
    {
        var descriptor = PersonCrudDescriptorFactory.Create();

        var constraint = Assert.Single(descriptor.UniqueConstraints);
        Assert.Equal("personNo", constraint.Key);
        Assert.Equal(["PersonNo"], constraint.Fields);
        Assert.Empty(constraint.ScopeFields);
        Assert.Equal("Organization.Person.PersonNoDuplicated", constraint.ErrorCode);
        Assert.Equal("人员工号必须唯一。", constraint.Message);
    }

    [Fact]
    public void PersonCrudQueryCustomizer_MapsKeywordToPersonNoAndName()
    {
        var customizer = new PersonCrudQueryCustomizer();
        var conditions = customizer.BuildConditions(
            PersonCrudDescriptorFactory.Create(),
            new StandardCrudListQuery(Filters: new Dictionary<string, string?> { ["keyword"] = "张" }));

        Assert.Contains(conditions, condition => condition.Field == "PersonNo" && condition.Operator == "contains");
        Assert.Contains(conditions, condition => condition.Field == "ENAME" && condition.Operator == "contains");
    }

    [Fact]
    public async Task PersonCrudDeleteGuard_WhenPersonHasUserLink_ReturnsConflict()
    {
        var personId = "Person_11111111111111111111111111111111";
        var guard = new PersonCrudDeleteGuard(new InMemoryPersonUserLinkRepositoryWithLink(personId));

        var error = await guard.CanDeleteAsync(PersonCrudDescriptorFactory.Create(), personId);

        Assert.NotNull(error);
        Assert.Equal("Organization.Person.DeleteBlockedByUserLink", error!.Code);
    }

    [Fact]
    public async Task GetListAsync_ProjectsRequiredFields()
    {
        dynamic service = BuildService();
        var created = await service.CreateAsync(new PersonDto
        {
            PersonNo = "P001",
            Name = "张三",
            CompanyId = CompanyId,
            DepartmentId = DepartmentId,
            EmploymentStatus = EmploymentStatus.FullTime,
            IsEnabled = true
        });

        var rows = await service.GetListAsync(
            keyword: "张",
            companyId: CompanyId,
            departmentId: DepartmentId,
            employmentStatus: EmploymentStatus.FullTime);

        var row = ((IEnumerable)rows).Cast<object>().Single();

        Assert.Equal("P001", row.GetType().GetProperty("PersonNo")!.GetValue(row));
        Assert.Equal("张三", row.GetType().GetProperty("Name")!.GetValue(row));
        Assert.Equal("ICP研发中心", row.GetType().GetProperty("CompanyName")!.GetValue(row));
        Assert.Equal("人力资源部", row.GetType().GetProperty("DepartmentName")!.GetValue(row));
        Assert.Equal(EmploymentStatus.FullTime, row.GetType().GetProperty("EmploymentStatus")!.GetValue(row));
        Assert.True((bool)row.GetType().GetProperty("IsEnabled")!.GetValue(row)!);
    }

    private static IPersonService BuildService() =>
        (IPersonService)Activator.CreateInstance(
            typeof(PersonService),
            new object[]
            {
                new InMemoryPersonRepository(),
                new InMemoryPersonUserLinkRepository(),
                new InMemoryCompanyRepository(),
                new InMemoryDepartmentRepository(),
                new FakeIdentityUserDirectory(),
                new FakeAuthorizationSummaryReader(),
                new PlatformObjectIdGenerator()
            })!;

    private static PersonsController BuildController(RecordingStandardCrudService standardCrudService) =>
        new(
            BuildService(),
            new FakeIdentityUserDirectory(),
            new StandardCrudEndpoint(standardCrudService));

    private static StandardCrudDetail MutationDetail(string? id = null) => new()
    {
        Fields = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["EID"] = id ?? "Person_55555555555555555555555555555555",
            ["PersonNo"] = "P100",
            ["ENAME"] = "标准人员",
            ["CompanyId"] = CompanyId,
            ["DepartmentId"] = DepartmentId,
            ["EmploymentStatus"] = "FullTime",
            ["IsEnabled"] = true,
            ["CreatedAt"] = DateTime.UtcNow
        }
    };

    private static PersonsController BuildControllerWithRealStandardCrudRows(params Dictionary<string, object?>[] rows)
    {
        var standardCrudService = new StandardCrudService(
            new ControllerStandardCrudRepository(rows),
            [],
            [],
            [],
            [new PersonCrudValidator()],
            [],
            []);

        return new PersonsController(
            BuildService(),
            new FakeIdentityUserDirectory(),
            new StandardCrudEndpoint(standardCrudService));
    }

    private static Dictionary<string, object?> StandardCrudPersonRow(string id, string personNo, string name) => new(StringComparer.Ordinal)
    {
        ["EID"] = id,
        ["PersonNo"] = personNo,
        ["ENAME"] = name,
        ["CompanyId"] = null,
        ["DepartmentId"] = null,
        ["PositionId"] = null,
        ["EmploymentStatus"] = "FullTime",
        ["IsEnabled"] = true,
        ["CreatedAt"] = DateTime.UtcNow,
        ["UpdatedAt"] = null
    };

    private static Assembly GetOrganizationAssembly() => typeof(PersonDto).Assembly;

    private sealed class RecordingStandardCrudService : IStandardCrudService
    {
        private readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> _items;
        private readonly StandardCrudDetail? _detail;
        private readonly StandardCrudError? _mutationError;

        public RecordingStandardCrudService(
            IReadOnlyList<IReadOnlyDictionary<string, object?>> items,
            StandardCrudDetail? detail = null,
            StandardCrudError? mutationError = null)
        {
            _items = items;
            _detail = detail;
            _mutationError = mutationError;
        }

        public StandardCrudListQuery? LastListQuery { get; private set; }
        public string? LastMutation { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastMutationValues { get; private set; }

        public Task<StandardCrudListResult> ListAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudListQuery query,
            CancellationToken cancellationToken = default)
        {
            LastListQuery = query;
            return Task.FromResult(new StandardCrudListResult(
                _items,
                _items.Count,
                query.Page,
                query.PageSize,
                query.SortBy,
                query.SortDirection));
        }

        public Task<StandardCrudDetail?> GetDetailAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_detail);

        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudListQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StandardCrudOptionItem>>([]);

        public Task<StandardCrudMutationResult> CreateAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            LastMutation = nameof(CreateAsync);
            LastMutationValues = new Dictionary<string, object?>(values, StringComparer.Ordinal);
            return Task.FromResult(_mutationError is null
                ? StandardCrudMutationResult.Success(_detail)
                : StandardCrudMutationResult.Failure(_mutationError));
        }

        public Task<StandardCrudMutationResult> UpdateAsync(
            StandardCrudDescriptor descriptor,
            string id,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            LastMutation = nameof(UpdateAsync);
            LastMutationValues = new Dictionary<string, object?>(values, StringComparer.Ordinal);
            return Task.FromResult(_mutationError is null
                ? StandardCrudMutationResult.Success(_detail)
                : StandardCrudMutationResult.Failure(_mutationError));
        }

        public Task<StandardCrudMutationResult> DisableAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StandardCrudMutationResult.Success(_detail));

        public Task<StandardCrudMutationResult> DeleteAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StandardCrudMutationResult.Success());
    }

    private sealed class ControllerStandardCrudRepository : IStandardCrudRepository
    {
        private readonly List<Dictionary<string, object?>> _rows;

        public ControllerStandardCrudRepository(IEnumerable<Dictionary<string, object?>> rows)
        {
            _rows = rows.Select(row => new Dictionary<string, object?>(row, StringComparer.Ordinal)).ToList();
        }

        public Task<StandardCrudListResult> ListAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudListQuery query,
            IReadOnlyList<StandardCrudQueryCondition> conditions,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new StandardCrudListResult([], 0, query.Page, query.PageSize, query.SortBy, query.SortDirection));

        public Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default)
        {
            var row = _rows.FirstOrDefault(item => string.Equals(item[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal));
            return Task.FromResult<IReadOnlyDictionary<string, object?>?>(row is null ? null : Project(row, descriptor.FieldSet.DetailFields));
        }

        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudListQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StandardCrudOptionItem>>([]);

        public Task<bool> ExistsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudUniqueConstraint constraint,
            IReadOnlyDictionary<string, object?> equalityValues,
            string? excludedId = null,
            CancellationToken cancellationToken = default)
        {
            var exists = _rows.Any(row =>
                (string.IsNullOrWhiteSpace(excludedId) ||
                    !string.Equals(row[descriptor.IdField]?.ToString(), excludedId, StringComparison.Ordinal)) &&
                equalityValues.All(pair =>
                    row.TryGetValue(pair.Key, out var value) &&
                    UniqueValuesEqual(value, pair.Value, constraint)));

            return Task.FromResult(exists);
        }

        public Task<IReadOnlyDictionary<string, object?>> CreateAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            var row = new Dictionary<string, object?>(values, StringComparer.Ordinal);
            row[descriptor.IdField] = $"{descriptor.ModelName}_{Guid.NewGuid():N}";
            row["CreatedAt"] = DateTime.UtcNow;
            _rows.Add(row);
            return Task.FromResult(Project(row, descriptor.FieldSet.DetailFields));
        }

        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
            StandardCrudDescriptor descriptor,
            string id,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            var row = _rows.FirstOrDefault(item => string.Equals(item[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal));
            if (row is null)
            {
                return Task.FromResult<IReadOnlyDictionary<string, object?>?>(null);
            }

            foreach (var pair in values)
            {
                row[pair.Key] = pair.Value;
            }

            return Task.FromResult<IReadOnlyDictionary<string, object?>?>(Project(row, descriptor.FieldSet.DetailFields));
        }

        public Task<bool> DeleteAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        private static IReadOnlyDictionary<string, object?> Project(IReadOnlyDictionary<string, object?> row, IReadOnlyList<string> fields)
        {
            var projected = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                projected[field] = row.TryGetValue(field, out var value) ? value : null;
            }

            return projected;
        }

        private static bool UniqueValuesEqual(object? left, object? right, StandardCrudUniqueConstraint constraint)
        {
            var normalizedLeft = NormalizeUniqueValue(left, constraint);
            var normalizedRight = NormalizeUniqueValue(right, constraint);
            if (normalizedLeft is null || normalizedRight is null)
            {
                return normalizedLeft is null && normalizedRight is null;
            }

            return normalizedLeft is string leftText && normalizedRight is string rightText
                ? string.Equals(
                    leftText,
                    rightText,
                    constraint.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                : Equals(normalizedLeft, normalizedRight);
        }

        private static object? NormalizeUniqueValue(object? value, StandardCrudUniqueConstraint constraint)
        {
            if (value is not string text)
            {
                return value;
            }

            return constraint.TrimWhitespace ? text.Trim() : text;
        }
    }

    private sealed class InMemoryPersonRepository : IPersonRepository
    {
        private readonly List<Person> _persons = [];

        public Task<Person?> GetByIdAsync(string personId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_persons.FirstOrDefault(person => person.Id == personId) is { } person ? Clone(person) : null);

        public Task<IReadOnlyList<Person>> GetListAsync(
            string? keyword,
            string? companyId,
            string? departmentId,
            EmploymentStatus? employmentStatus,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<Person> query = _persons;

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(person =>
                    person.PersonNo.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    person.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(companyId))
            {
                query = query.Where(person => person.CompanyId == companyId);
            }

            if (!string.IsNullOrWhiteSpace(departmentId))
            {
                query = query.Where(person => person.DepartmentId == departmentId);
            }

            if (employmentStatus.HasValue)
            {
                query = query.Where(person => person.EmploymentStatus == employmentStatus.Value);
            }

            return Task.FromResult<IReadOnlyList<Person>>(query.Select(Clone).ToList());
        }

        public Task<bool> ExistsPersonNoAsync(
            string? companyId,
            string personNo,
            string? excludedPersonId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_persons.Any(person =>
                person.CompanyId == companyId &&
                person.Id != excludedPersonId &&
                string.Equals(person.PersonNo, personNo, StringComparison.OrdinalIgnoreCase)));

        public Task CreateAsync(Person person, CancellationToken cancellationToken = default)
        {
            _persons.Add(Clone(person));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Person person, CancellationToken cancellationToken = default)
        {
            var index = _persons.FindIndex(existing => existing.Id == person.Id);
            if (index >= 0)
            {
                _persons[index] = Clone(person);
            }

            return Task.CompletedTask;
        }

        private static Person Clone(Person person) => new()
        {
            Id = person.Id,
            PersonNo = person.PersonNo,
            Name = person.Name,
            CompanyId = person.CompanyId,
            DepartmentId = person.DepartmentId,
            PositionId = person.PositionId,
            EmploymentStatus = person.EmploymentStatus,
            IsEnabled = person.IsEnabled,
            CreatedAt = person.CreatedAt,
            UpdatedAt = person.UpdatedAt
        };
    }

    private sealed class InMemoryPersonUserLinkRepository : IPersonUserLinkRepository
    {
        private readonly List<PersonUserLink> _links = [];

        public Task<PersonUserLink?> GetByPersonIdAsync(string personId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PersonUserLink?>(null);

        public Task<bool> ExistsByUserIdAsync(
            string userId,
            string? excludedPersonId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task UpsertAsync(PersonUserLink link, CancellationToken cancellationToken = default)
        {
            _links.Add(link);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryPersonUserLinkRepositoryWithLink : IPersonUserLinkRepository
    {
        private readonly string _personId;

        public InMemoryPersonUserLinkRepositoryWithLink(string personId)
        {
            _personId = personId;
        }

        public Task<PersonUserLink?> GetByPersonIdAsync(string personId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PersonUserLink?>(personId == _personId
                ? new PersonUserLink
                {
                    PersonId = personId,
                    UserId = "user-1",
                    LinkedAt = DateTime.UtcNow,
                    LinkedBy = "test"
                }
                : null);

        public Task<bool> ExistsByUserIdAsync(
            string userId,
            string? excludedPersonId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task UpsertAsync(PersonUserLink link, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class InMemoryCompanyRepository : ICompanyRepository
    {
        private readonly List<Company> _companies =
        [
            new Company
            {
                Id = CompanyId,
                Code = "ICP",
                Name = "ICP研发中心",
                IsEnabled = true,
                SortOrder = 1
            }
        ];

        public Task<List<Company>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_companies.Select(Clone).ToList());

        public Task<Company?> GetByIdAsync(string companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_companies.FirstOrDefault(company => company.Id == companyId) is { } company ? Clone(company) : null);

        public Task<Company?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(_companies.FirstOrDefault(company => string.Equals(company.Code, code, StringComparison.OrdinalIgnoreCase)) is { } company ? Clone(company) : null);

        public Task CreateAsync(Company company, CancellationToken cancellationToken = default)
        {
            _companies.Add(Clone(company));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Company company, CancellationToken cancellationToken = default)
        {
            var index = _companies.FindIndex(existing => existing.Id == company.Id);
            if (index >= 0)
            {
                _companies[index] = Clone(company);
            }

            return Task.CompletedTask;
        }

        private static Company Clone(Company company) => new()
        {
            Id = company.Id,
            Code = company.Code,
            Name = company.Name,
            IsEnabled = company.IsEnabled,
            SortOrder = company.SortOrder,
            CreatedAt = company.CreatedAt,
            UpdatedAt = company.UpdatedAt
        };
    }

    private sealed class InMemoryDepartmentRepository : IDepartmentRepository
    {
        private readonly List<Department> _departments =
        [
            new Department
            {
                Id = DepartmentId,
                CompanyId = CompanyId,
                Code = "HR",
                Name = "人力资源部",
                IsEnabled = true,
                SortOrder = 1
            }
        ];

        public Task<List<Department>> GetByCompanyIdAsync(string companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_departments.Where(department => department.CompanyId == companyId).Select(Clone).ToList());

        public Task<Department?> GetByIdAsync(string departmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_departments.FirstOrDefault(department => department.Id == departmentId) is { } department ? Clone(department) : null);

        public Task<bool> ExistsSiblingCodeAsync(
            string companyId,
            string? parentDepartmentId,
            string code,
            string? excludedDepartmentId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task CreateAsync(Department department, CancellationToken cancellationToken = default)
        {
            _departments.Add(Clone(department));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Department department, CancellationToken cancellationToken = default)
        {
            var index = _departments.FindIndex(existing => existing.Id == department.Id);
            if (index >= 0)
            {
                _departments[index] = Clone(department);
            }

            return Task.CompletedTask;
        }

        private static Department Clone(Department department) => new()
        {
            Id = department.Id,
            CompanyId = department.CompanyId,
            ParentDepartmentId = department.ParentDepartmentId,
            Code = department.Code,
            Name = department.Name,
            IsEnabled = department.IsEnabled,
            SortOrder = department.SortOrder,
            CreatedAt = department.CreatedAt,
            UpdatedAt = department.UpdatedAt
        };
    }

    private sealed class FakeIdentityUserDirectory : IIdentityUserDirectory
    {
        public Task EnsureUserExistsAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<IdentityUserOptionDto>> SearchAsync(
            string keyword,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IdentityUserOptionDto>>([]);
    }

    private sealed class FakeAuthorizationSummaryReader : IAuthorizationSummaryReader
    {
        public Task<IReadOnlyList<string>> GetRoleNamesAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}

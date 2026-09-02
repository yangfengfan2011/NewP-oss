using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class PersonCrudValidatorTests
{
    private const string CompanyId = "Company_11111111111111111111111111111111";
    private const string OtherCompanyId = "Company_22222222222222222222222222222222";
    private const string DepartmentId = "Department_11111111111111111111111111111111";
    private const string OtherDepartmentId = "Department_22222222222222222222222222222222";

    [Fact]
    public async Task ValidateCreateAsync_WithValidEnabledAffiliation_ReturnsNoError()
    {
        var validator = CreateValidator(
            [Company(CompanyId, enabled: true)],
            [Department(DepartmentId, CompanyId, enabled: true)]);

        var error = await validator.ValidateCreateAsync(
            PersonCrudDescriptorFactory.Create(),
            Values(CompanyId, DepartmentId),
            CancellationToken.None);

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateCreateAsync_WithoutAffiliation_ReturnsNoError()
    {
        var validator = CreateValidator([], []);

        var error = await validator.ValidateCreateAsync(
            PersonCrudDescriptorFactory.Create(),
            Values(null, null),
            CancellationToken.None);

        Assert.Null(error);
    }

    [Theory]
    [InlineData(null, DepartmentId, "Organization.Person.CompanyRequired", "CompanyId")]
    [InlineData(CompanyId, null, "Organization.Person.DepartmentRequired", "DepartmentId")]
    public async Task ValidateCreateAsync_WithPartialAffiliation_ReturnsStableFieldError(
        string? companyId,
        string? departmentId,
        string expectedCode,
        string expectedField)
    {
        var validator = CreateValidator(
            [Company(CompanyId, enabled: true)],
            [Department(DepartmentId, CompanyId, enabled: true)]);

        var error = await validator.ValidateCreateAsync(
            PersonCrudDescriptorFactory.Create(),
            Values(companyId, departmentId),
            CancellationToken.None);

        AssertError(error, expectedCode, expectedField);
    }

    [Theory]
    [InlineData("company-missing", "Organization.Person.CompanyNotFound", "CompanyId")]
    [InlineData("company-disabled", "Organization.Person.CompanyDisabled", "CompanyId")]
    [InlineData("department-missing", "Organization.Person.DepartmentNotFound", "DepartmentId")]
    [InlineData("department-disabled", "Organization.Person.DepartmentDisabled", "DepartmentId")]
    [InlineData("department-mismatch", "Organization.Person.DepartmentCompanyMismatch", "DepartmentId")]
    public async Task ValidateCreateAsync_WithInvalidAffiliation_ReturnsStableFieldError(
        string scenario,
        string expectedCode,
        string expectedField)
    {
        var companies = scenario switch
        {
            "company-missing" => Array.Empty<Company>(),
            "company-disabled" => [Company(CompanyId, enabled: false)],
            _ => [Company(CompanyId, enabled: true), Company(OtherCompanyId, enabled: true)]
        };
        var departments = scenario switch
        {
            "department-missing" => Array.Empty<Department>(),
            "department-disabled" => [Department(DepartmentId, CompanyId, enabled: false)],
            "department-mismatch" => [Department(DepartmentId, OtherCompanyId, enabled: true)],
            _ => [Department(DepartmentId, CompanyId, enabled: true)]
        };
        var validator = CreateValidator(companies, departments);

        var error = await validator.ValidateCreateAsync(
            PersonCrudDescriptorFactory.Create(),
            Values(CompanyId, DepartmentId),
            CancellationToken.None);

        AssertError(error, expectedCode, expectedField);
    }

    [Fact]
    public async Task ValidateUpdateAsync_MergesExistingAffiliationBeforeCheckingDepartmentCompany()
    {
        var existing = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["EID"] = "Person_1",
            ["CompanyId"] = CompanyId,
            ["DepartmentId"] = DepartmentId,
            ["EmploymentStatus"] = "FullTime",
            ["IsEnabled"] = true
        };
        var validator = CreateValidator(
            [Company(CompanyId, enabled: true), Company(OtherCompanyId, enabled: true)],
            [
                Department(DepartmentId, CompanyId, enabled: true),
                Department(OtherDepartmentId, OtherCompanyId, enabled: true)
            ],
            existing);

        var error = await validator.ValidateUpdateAsync(
            PersonCrudDescriptorFactory.Create(),
            "Person_1",
            new Dictionary<string, object?> { ["DepartmentId"] = OtherDepartmentId },
            CancellationToken.None);

        AssertError(error, "Organization.Person.DepartmentCompanyMismatch", "DepartmentId");
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenOnlyEnablementChanges_PreservesEmploymentStatusAndAffiliation()
    {
        var existing = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["EID"] = "Person_1",
            ["CompanyId"] = CompanyId,
            ["DepartmentId"] = DepartmentId,
            ["EmploymentStatus"] = "FullTime",
            ["IsEnabled"] = true
        };
        var repository = new FakeStandardCrudRepository(existing);
        var validator = CreateValidator(
            [Company(CompanyId, enabled: true)],
            [Department(DepartmentId, CompanyId, enabled: true)],
            existing,
            repository);

        var error = await validator.ValidateUpdateAsync(
            PersonCrudDescriptorFactory.Create(),
            "Person_1",
            new Dictionary<string, object?> { ["IsEnabled"] = false },
            CancellationToken.None);

        Assert.Null(error);
        Assert.Equal("FullTime", existing["EmploymentStatus"]);
        Assert.Equal(1, repository.GetByIdCallCount);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenAffiliationIsClearedTogether_ReturnsNoError()
    {
        var existing = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["EID"] = "Person_1",
            ["CompanyId"] = CompanyId,
            ["DepartmentId"] = DepartmentId,
            ["EmploymentStatus"] = "FullTime",
            ["IsEnabled"] = true
        };
        var validator = CreateValidator([], [], existing);

        var error = await validator.ValidateUpdateAsync(
            PersonCrudDescriptorFactory.Create(),
            "Person_1",
            new Dictionary<string, object?>
            {
                ["CompanyId"] = null,
                ["DepartmentId"] = null
            },
            CancellationToken.None);

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenAffiliationChanges_PreservesIndependentPosition()
    {
        var existing = Values(CompanyId, DepartmentId);
        existing["EID"] = "Person_1";
        existing["PositionId"] = "Position_1";
        var validator = CreateValidator(
            [Company(CompanyId, enabled: true), Company(OtherCompanyId, enabled: true)],
            [
                Department(DepartmentId, CompanyId, enabled: true),
                Department(OtherDepartmentId, OtherCompanyId, enabled: true)
            ],
            existing,
            positions: [Position("Position_1", enabled: false)]);

        var error = await validator.ValidateUpdateAsync(
            PersonCrudDescriptorFactory.Create(),
            "Person_1",
            new Dictionary<string, object?>
            {
                ["CompanyId"] = OtherCompanyId,
                ["DepartmentId"] = OtherDepartmentId
            },
            CancellationToken.None);

        Assert.Null(error);
        Assert.Equal("Position_1", existing["PositionId"]);
    }

    [Theory]
    [InlineData("position-missing", "Organization.Person.PositionNotFound")]
    [InlineData("position-disabled", "Organization.Person.PositionDisabled")]
    public async Task ValidateCreateAsync_WithInvalidPosition_ReturnsStableFieldError(
        string scenario,
        string expectedCode)
    {
        var positions = scenario switch
        {
            "position-missing" => Array.Empty<Position>(),
            _ => [Position("Position_1", enabled: false)]
        };
        var validator = CreateValidator(
            [Company(CompanyId, enabled: true)],
            [Department(DepartmentId, CompanyId, enabled: true)],
            positions: positions);
        var values = Values(CompanyId, DepartmentId);
        values["PositionId"] = "Position_1";

        var error = await validator.ValidateCreateAsync(
            PersonCrudDescriptorFactory.Create(),
            values,
            CancellationToken.None);

        AssertError(error, expectedCode, "PositionId");
    }

    [Fact]
    public async Task ValidateCreateAsync_WithPositionButNoAffiliation_ReturnsNoError()
    {
        var validator = CreateValidator([], [], positions: [Position("Position_1", enabled: true)]);
        var values = Values(null, null);
        values["PositionId"] = "Position_1";

        var error = await validator.ValidateCreateAsync(
            PersonCrudDescriptorFactory.Create(),
            values,
            CancellationToken.None);

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateCreateAsync_WithPositionAndCompleteAffiliation_ReturnsNoError()
    {
        var validator = CreateValidator(
            [Company(CompanyId, enabled: true)],
            [Department(DepartmentId, CompanyId, enabled: true)],
            positions: [Position("Position_1", enabled: true)]);
        var values = Values(CompanyId, DepartmentId);
        values["PositionId"] = "Position_1";

        var error = await validator.ValidateCreateAsync(
            PersonCrudDescriptorFactory.Create(),
            values,
            CancellationToken.None);

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WithPositionButNoAffiliation_ReturnsNoError()
    {
        var existing = Values(null, null);
        existing["EID"] = "Person_1";
        var validator = CreateValidator(
            [],
            [],
            existing,
            positions: [Position("Position_1", enabled: true)]);

        var error = await validator.ValidateUpdateAsync(
            PersonCrudDescriptorFactory.Create(),
            "Person_1",
            new Dictionary<string, object?> { ["PositionId"] = "Position_1" },
            CancellationToken.None);

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WithPositionAndCompleteAffiliation_ReturnsNoError()
    {
        var existing = Values(CompanyId, DepartmentId);
        existing["EID"] = "Person_1";
        var validator = CreateValidator(
            [Company(CompanyId, enabled: true)],
            [Department(DepartmentId, CompanyId, enabled: true)],
            existing,
            positions: [Position("Position_1", enabled: true)]);

        var error = await validator.ValidateUpdateAsync(
            PersonCrudDescriptorFactory.Create(),
            "Person_1",
            new Dictionary<string, object?> { ["PositionId"] = "Position_1" },
            CancellationToken.None);

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateUpdateAsync_CanClearCurrentDisabledPosition()
    {
        var existing = Values(null, null);
        existing["EID"] = "Person_1";
        existing["PositionId"] = "Position_Disabled";
        var validator = CreateValidator(
            [],
            [],
            existing,
            positions: [Position("Position_Disabled", enabled: false)]);

        var error = await validator.ValidateUpdateAsync(
            PersonCrudDescriptorFactory.Create(),
            "Person_1",
            new Dictionary<string, object?> { ["PositionId"] = null },
            CancellationToken.None);

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateUpdateAsync_CanReplaceCurrentDisabledPositionWithEnabledPosition()
    {
        var existing = Values(null, null);
        existing["EID"] = "Person_1";
        existing["PositionId"] = "Position_Disabled";
        var validator = CreateValidator(
            [],
            [],
            existing,
            positions:
            [
                Position("Position_Disabled", enabled: false),
                Position("Position_Enabled", enabled: true)
            ]);

        var error = await validator.ValidateUpdateAsync(
            PersonCrudDescriptorFactory.Create(),
            "Person_1",
            new Dictionary<string, object?> { ["PositionId"] = "Position_Enabled" },
            CancellationToken.None);

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateUpdateAsync_CannotReplaceCurrentDisabledPositionWithAnotherDisabledPosition()
    {
        var existing = Values(null, null);
        existing["EID"] = "Person_1";
        existing["PositionId"] = "Position_Disabled_1";
        var validator = CreateValidator(
            [],
            [],
            existing,
            positions:
            [
                Position("Position_Disabled_1", enabled: false),
                Position("Position_Disabled_2", enabled: false)
            ]);

        var error = await validator.ValidateUpdateAsync(
            PersonCrudDescriptorFactory.Create(),
            "Person_1",
            new Dictionary<string, object?> { ["PositionId"] = "Position_Disabled_2" },
            CancellationToken.None);

        AssertError(error, "Organization.Person.PositionDisabled", "PositionId");
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenBirthDateIsInFuture_ReturnsFieldError()
    {
        var validator = CreateValidator([], []);
        var values = Values(null, null);
        values["BirthDate"] = new DateOnly(9999, 12, 31);

        var error = await validator.ValidateCreateAsync(
            PersonCrudDescriptorFactory.Create(),
            values,
            CancellationToken.None);

        AssertError(error, "Organization.Person.BirthDateInFuture", "BirthDate");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ValidateCreateAsync_AllowsOneSidedEmploymentInterval(bool hasStart, bool hasEnd)
    {
        var validator = CreateValidator([], []);
        var values = Values(null, null);
        values["EmploymentStartedAt"] = hasStart ? new DateTime(2030, 1, 2, 0, 0, 0, DateTimeKind.Utc) : null;
        values["EmploymentEndedAt"] = hasEnd ? new DateTime(2030, 1, 3, 0, 0, 0, DateTimeKind.Utc) : null;

        var error = await validator.ValidateCreateAsync(
            PersonCrudDescriptorFactory.Create(),
            values,
            CancellationToken.None);

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateCreateAsync_WhenEmploymentEndPrecedesStart_ReturnsFieldError()
    {
        var validator = CreateValidator([], []);
        var values = Values(null, null);
        values["EmploymentStartedAt"] = new DateTime(2030, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        values["EmploymentEndedAt"] = new DateTime(2030, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var error = await validator.ValidateCreateAsync(
            PersonCrudDescriptorFactory.Create(),
            values,
            CancellationToken.None);

        AssertError(error, "Organization.Person.EmploymentIntervalInvalid", "EmploymentEndedAt");
    }

    private static PersonCrudValidator CreateValidator(
        IEnumerable<Company> companies,
        IEnumerable<Department> departments,
        IReadOnlyDictionary<string, object?>? existing = null,
        FakeStandardCrudRepository? repository = null,
        IEnumerable<Position>? positions = null) =>
        new(
            new FakeCompanyRepository(companies),
            new FakeDepartmentRepository(departments),
            new FakePositionRepository(positions ?? []),
            repository ?? new FakeStandardCrudRepository(existing));

    private static Dictionary<string, object?> Values(string? companyId, string? departmentId) => new(StringComparer.Ordinal)
    {
        ["PersonNo"] = "WF-P-001",
        ["ENAME"] = "Wayfinder 验收人员",
        ["CompanyId"] = companyId,
        ["DepartmentId"] = departmentId,
        ["PositionId"] = null,
        ["EmploymentStatus"] = "FullTime",
        ["IsEnabled"] = true
    };

    private static Company Company(string id, bool enabled) => new()
    {
        Id = id,
        Code = id,
        Name = id,
        IsEnabled = enabled
    };

    private static Department Department(string id, string companyId, bool enabled) => new()
    {
        Id = id,
        CompanyId = companyId,
        Code = id,
        Name = id,
        IsEnabled = enabled
    };

    private static Position Position(string id, bool enabled) => new()
    {
        Id = id,
        Name = id,
        IsEnabled = enabled
    };

    private static void AssertError(StandardCrudError? error, string expectedCode, string expectedField)
    {
        Assert.NotNull(error);
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(expectedCode, error.Code);
        var fieldError = Assert.Single(error.FieldErrors);
        Assert.Equal(expectedField, fieldError.Field);
        Assert.Equal(expectedCode, fieldError.Code);
    }

    private sealed class FakeCompanyRepository(IEnumerable<Company> companies) : ICompanyRepository
    {
        private readonly IReadOnlyDictionary<string, Company> _companies =
            companies.ToDictionary(company => company.Id, StringComparer.Ordinal);

        public Task<Company?> GetByIdAsync(string companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_companies.GetValueOrDefault(companyId));

        public Task<List<Company>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, Company>> GetByIdsAsync(IReadOnlyCollection<string> companyIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Company?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateAsync(Company company, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(Company company, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeDepartmentRepository(IEnumerable<Department> departments) : IDepartmentRepository
    {
        private readonly IReadOnlyDictionary<string, Department> _departments =
            departments.ToDictionary(department => department.Id, StringComparer.Ordinal);

        public Task<Department?> GetByIdAsync(string departmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_departments.GetValueOrDefault(departmentId));

        public Task<List<Department>> GetByCompanyIdAsync(string companyId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, Department>> GetByIdsAsync(IReadOnlyCollection<string> departmentIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsSiblingCodeAsync(string companyId, string? parentDepartmentId, string code, string? excludedDepartmentId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateAsync(Department department, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(Department department, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeStandardCrudRepository(IReadOnlyDictionary<string, object?>? existing = null) : IStandardCrudRepository
    {
        public int GetByIdCallCount { get; private set; }

        public Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
        {
            GetByIdCallCount++;
            return Task.FromResult(existing);
        }

        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(StandardCrudDescriptor descriptor, StandardCrudUniqueConstraint constraint, IReadOnlyDictionary<string, object?> equalityValues, string? excludedId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, object?>> CreateAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakePositionRepository(IEnumerable<Position> positions) : IPositionDirectory
    {
        private readonly IReadOnlyDictionary<string, Position> _positions =
            positions.ToDictionary(position => position.Id, StringComparer.Ordinal);

        public Task<Position?> GetByIdAsync(string positionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_positions.GetValueOrDefault(positionId));

        public Task<IReadOnlyDictionary<string, Position>> GetByIdsAsync(IReadOnlyCollection<string> positionIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

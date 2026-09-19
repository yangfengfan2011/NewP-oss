using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Persistence.Auditing;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class DepartmentCrudUniqueConstraintTests
{
    [Fact]
    public async Task CreateAsync_WhenCompanyCodeExists_BindsConflictToCodeAndKeepsCompanyScope()
    {
        var repository = new RecordingRepository(static constraint => constraint.Key == "code");

        var result = await CreateService(repository).CreateAsync(
            DepartmentCrudDescriptorFactory.Create(),
            DepartmentValues("Company_1", parentDepartmentId: null, code: "D001", name: "研发部"));

        Assert.Equal("Organization.Department.CodeDuplicated", result.Error?.Code);
        Assert.Equal("ECODE", Assert.Single(result.Error!.FieldErrors).Field);
        var check = Assert.Single(repository.Checks);
        Assert.Equal("COMPANY_1", check.Values["CompanyId"]);
        Assert.Equal("D001", check.Values["ECODE"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Department_Parent")]
    public async Task CreateAsync_WhenSiblingNameExists_BindsConflictToNameAndKeepsNullableParentScope(
        string? parentDepartmentId)
    {
        var repository = new RecordingRepository(static constraint => constraint.Key == "siblingName");

        var result = await CreateService(repository).CreateAsync(
            DepartmentCrudDescriptorFactory.Create(),
            DepartmentValues("Company_1", parentDepartmentId, "D001", "研发部"));

        Assert.Equal("Organization.Department.NameDuplicated", result.Error?.Code);
        Assert.Equal("ENAME", Assert.Single(result.Error!.FieldErrors).Field);
        var check = repository.Checks.Single(static check => check.Constraint.Key == "siblingName");
        Assert.Equal("COMPANY_1", check.Values["CompanyId"]);
        Assert.Equal(parentDepartmentId?.ToUpperInvariant(), check.Values["ParentDepartmentId"]);
        Assert.Equal("研发部", check.Values["ENAME"]);
    }

    [Fact]
    public async Task UpdateAsync_WhenMovingToDuplicatedSibling_RechecksNewParentScopeAndExcludesCurrentRow()
    {
        var repository = new RecordingRepository(
            static constraint => constraint.Key == "siblingName",
            existing: ExistingDepartmentValues("Company_1", "Department_OldParent", "D001", "旧名称"));

        var result = await CreateService(repository).UpdateAsync(
            DepartmentCrudDescriptorFactory.Create(),
            "Department_Current",
            new Dictionary<string, object?>
            {
                ["ParentDepartmentId"] = "Department_NewParent",
                ["ENAME"] = "研发部",
                ["SortOrder"] = "0100"
            });

        Assert.Equal("Organization.Department.NameDuplicated", result.Error?.Code);
        Assert.Equal("ENAME", Assert.Single(result.Error!.FieldErrors).Field);
        var check = repository.Checks.Single(static check => check.Constraint.Key == "siblingName");
        Assert.Equal("DEPARTMENT_NEWPARENT", check.Values["ParentDepartmentId"]);
        Assert.Equal("Department_Current", check.ExcludedId);
    }

    private static StandardCrudService CreateService(IStandardCrudRepository repository) => new StandardCrudService(
            repository,
            null!,
            new StandardCrudDefaultWriter(repository, [], [], auditValueProvider: new EntityAuditValueProvider(new FixedAuditStampProvider())),
            [],
            [],
            []);

    private static Dictionary<string, object?> DepartmentValues(
        string companyId,
        string? parentDepartmentId,
        string code,
        string name) => new(StringComparer.Ordinal)
    {
        ["CompanyId"] = companyId,
        ["ParentDepartmentId"] = parentDepartmentId,
        ["ECODE"] = code,
        ["ENAME"] = name,
        ["SortOrder"] = "0100"
    };

    private static Dictionary<string, object?> ExistingDepartmentValues(
        string companyId,
        string? parentDepartmentId,
        string code,
        string name) => new(StringComparer.Ordinal)
    {
        ["EID"] = "Department_Current",
        ["CompanyId"] = companyId,
        ["ParentDepartmentId"] = parentDepartmentId,
        ["ECODE"] = code,
        ["ENAME"] = name,
        ["IsEnabled"] = true,
        ["SortOrder"] = "0100",
        ["CreatedBy"] = "User_Admin",
        ["CreatedAt"] = DateTime.UtcNow,
        ["UpdatedBy"] = "User_Admin",
        ["UpdatedAt"] = DateTime.UtcNow
    };

    private sealed class FixedAuditStampProvider : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent() => new("User_Admin", DateTime.UtcNow);
    }

    private sealed class RecordingRepository(
        Func<StandardCrudUniqueConstraint, bool> conflicts,
        IReadOnlyDictionary<string, object?>? existing = null) : IStandardCrudRepository
    {
        public List<UniqueCheck> Checks { get; } = [];

        public Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) => Task.FromResult(existing);

        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudListQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> ExistsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudUniqueConstraint constraint,
            IReadOnlyDictionary<string, object?> equalityValues,
            string? excludedId = null,
            CancellationToken cancellationToken = default)
        {
            Checks.Add(new UniqueCheck(constraint, equalityValues, excludedId));
            return Task.FromResult(conflicts(constraint));
        }

        public Task<IReadOnlyDictionary<string, object?>> CreateAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
            StandardCrudDescriptor descriptor,
            string id,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record UniqueCheck(
        StandardCrudUniqueConstraint Constraint,
        IReadOnlyDictionary<string, object?> Values,
        string? ExcludedId);
}

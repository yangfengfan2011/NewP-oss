using ICP.Platform.Modules.Authentication.Persistence.Descriptors;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class DepartmentCrudValidatorTests
{
    [Fact]
    public async Task ValidateCreateAsync_RejectsDisabledAncestorBeyondImmediateParent()
    {
        await using var context = CreateContext();
        AddCompany(context, "company", true);
        AddDepartment(context, "root", "company", enabled: false);
        AddDepartment(context, "middle", "company", "root");
        AddDepartment(context, "leaf", "company", "middle");
        await context.SaveChangesAsync();
        var error = await CreateValidator(context).ValidateCreateAsync(DepartmentCrudDescriptorFactory.Create(),
            new Dictionary<string, object?> { ["CompanyId"] = "company", ["ParentDepartmentId"] = "leaf" });
        Assert.Equal("Organization.Department.ParentChainDisabled", error?.Code);
        Assert.Equal("ParentDepartmentId", Assert.Single(error!.FieldErrors).Field);
    }

    [Fact]
    public async Task ValidateUpdateAsync_ReparentToRootStillRequiresEnabledCompany()
    {
        await using var context = CreateContext();
        AddCompany(context, "company", false);
        AddDepartment(context, "root", "company");
        AddDepartment(context, "child", "company", "root");
        await context.SaveChangesAsync();
        var error = await CreateValidator(context).ValidateUpdateAsync(DepartmentCrudDescriptorFactory.Create(), "child",
            new Dictionary<string, object?> { ["ParentDepartmentId"] = null });
        Assert.Equal("Organization.Department.CompanyDisabled", error?.Code);
        Assert.Equal("CompanyId", Assert.Single(error!.FieldErrors).Field);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0100")]
    [InlineData("01001")]
    [InlineData("12345678901234567890123456789012")]
    public async Task ValidateCreateAsync_EnabledCompanyAndAsciiSortOrderAreAccepted(string sortOrder)
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1", true);
        await context.SaveChangesAsync();

        var error = await CreateValidator(context).ValidateCreateAsync(
            DepartmentCrudDescriptorFactory.Create(),
            new Dictionary<string, object?> { ["CompanyId"] = "Company_1", ["SortOrder"] = sortOrder });

        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 0100")]
    [InlineData("0100 ")]
    [InlineData("１２")]
    [InlineData("12A")]
    [InlineData("123456789012345678901234567890123")]
    public async Task ValidateCreateAsync_InvalidSortOrderReturnsFieldError(string sortOrder)
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1", true);
        await context.SaveChangesAsync();

        var error = await CreateValidator(context).ValidateCreateAsync(
            DepartmentCrudDescriptorFactory.Create(),
            new Dictionary<string, object?> { ["CompanyId"] = "Company_1", ["SortOrder"] = sortOrder });

        Assert.Equal(400, error?.StatusCode);
        Assert.Equal("Organization.Department.SortOrderInvalid", error?.Code);
        Assert.Equal("SortOrder", Assert.Single(error!.FieldErrors).Field);
    }

    [Theory]
    [InlineData("Company_Missing", 404, "Organization.Department.CompanyNotFound")]
    [InlineData("Company_Disabled", 409, "Organization.Department.CompanyDisabled")]
    public async Task ValidateCreateAsync_MissingOrDisabledCompanyReturnsStableError(
        string companyId,
        int statusCode,
        string code)
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_Disabled", false);
        await context.SaveChangesAsync();

        var error = await CreateValidator(context).ValidateCreateAsync(
            DepartmentCrudDescriptorFactory.Create(),
            new Dictionary<string, object?> { ["CompanyId"] = companyId, ["SortOrder"] = "0100" });

        Assert.Equal(statusCode, error?.StatusCode);
        Assert.Equal(code, error?.Code);
        Assert.Equal("CompanyId", Assert.Single(error!.FieldErrors).Field);
    }

    [Fact]
    public async Task ValidateUpdateAsync_DisablingReferencedRootDepartmentPreservesPersonnelAssignment()
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1", true);
        AddDepartment(context, "Department_1", "Company_1");
        AddPerson(context, "Person_1", "Company_1", "Department_1");
        await context.SaveChangesAsync();

        var error = await CreateValidator(context).ValidateUpdateAsync(
            DepartmentCrudDescriptorFactory.Create(),
            "Department_1",
            new Dictionary<string, object?> { ["IsEnabled"] = false, ["SortOrder"] = "0100" });

        Assert.Null(error);
        Assert.Equal(
            "Department_1",
            await context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Person"))
                .Select(person => EF.Property<string?>(person, "DepartmentId"))
                .SingleAsync());
    }

    [Theory]
    [InlineData("Department_Missing", 404, "Organization.Department.ParentNotFound")]
    [InlineData("Department_Disabled", 400, "Organization.Department.ParentDisabled")]
    [InlineData("Department_OtherCompany", 400, "Organization.Department.ParentCompanyMismatch")]
    public async Task ValidateCreateAsync_InvalidParentReturnsLocatableError(
        string parentId,
        int statusCode,
        string code)
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1", true);
        AddCompany(context, "Company_2", true);
        AddDepartment(context, "Department_Disabled", "Company_1", enabled: false);
        AddDepartment(context, "Department_OtherCompany", "Company_2");
        await context.SaveChangesAsync();

        var error = await CreateValidator(context).ValidateCreateAsync(
            DepartmentCrudDescriptorFactory.Create(),
            new Dictionary<string, object?>
            {
                ["CompanyId"] = "Company_1",
                ["ParentDepartmentId"] = parentId,
                ["SortOrder"] = "0100"
            });

        Assert.Equal(statusCode, error?.StatusCode);
        Assert.Equal(code, error?.Code);
        Assert.Equal("ParentDepartmentId", Assert.Single(error!.FieldErrors).Field);
    }

    [Fact]
    public async Task ValidateUpdateAsync_RejectsSelfAndAnyDepthDescendantAsParent()
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1", true);
        AddDepartment(context, "Department_Root", "Company_1");
        AddDepartment(context, "Department_Child", "Company_1", "Department_Root");
        AddDepartment(context, "Department_Grandchild", "Company_1", "Department_Child");
        await context.SaveChangesAsync();
        var validator = CreateValidator(
            context,
            new StubHierarchyQuery((current, parent) =>
                string.Equals(current, parent, StringComparison.Ordinal)
                    ? DepartmentParentRelationship.Self
                    : DepartmentParentRelationship.Descendant));

        var self = await validator.ValidateUpdateAsync(
            DepartmentCrudDescriptorFactory.Create(),
            "Department_Root",
            new Dictionary<string, object?> { ["ParentDepartmentId"] = "Department_Root" });
        var descendant = await validator.ValidateUpdateAsync(
            DepartmentCrudDescriptorFactory.Create(),
            "Department_Root",
            new Dictionary<string, object?> { ["ParentDepartmentId"] = "Department_Grandchild" });

        Assert.Equal("Organization.Department.ParentSelfReference", self?.Code);
        Assert.Equal("Organization.Department.ParentCycle", descendant?.Code);
        Assert.All([self!, descendant!], error =>
        {
            Assert.Equal(400, error.StatusCode);
            Assert.Equal("ParentDepartmentId", Assert.Single(error.FieldErrors).Field);
        });
    }

    [Fact]
    public async Task ValidateUpdateAsync_MapsDescendantClassificationToCycleError()
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1", true);
        string? parentId = null;
        for (var index = 0; index < 64; index++)
        {
            var id = $"Department_{index:D3}";
            AddDepartment(context, id, "Company_1", parentId);
            parentId = id;
        }

        await context.SaveChangesAsync();

        var error = await CreateValidator(
            context,
            new StubHierarchyQuery(static (_, _) => DepartmentParentRelationship.Descendant)).ValidateUpdateAsync(
            DepartmentCrudDescriptorFactory.Create(),
            "Department_000",
            new Dictionary<string, object?> { ["ParentDepartmentId"] = "Department_063" });

        Assert.Equal(400, error?.StatusCode);
        Assert.Equal("Organization.Department.ParentCycle", error?.Code);
    }

    [Fact]
    public async Task ValidateUpdateAsync_WhenExistingHierarchyContainsCycle_FailsClosed()
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1", true);
        AddDepartment(context, "Department_Current", "Company_1");
        AddDepartment(context, "Department_A", "Company_1", "Department_B");
        AddDepartment(context, "Department_B", "Company_1", "Department_A");
        await context.SaveChangesAsync();

        var error = await CreateValidator(
            context,
            new StubHierarchyQuery(static (_, _) => DepartmentParentRelationship.Corrupted)).ValidateUpdateAsync(
            DepartmentCrudDescriptorFactory.Create(),
            "Department_Current",
            new Dictionary<string, object?> { ["ParentDepartmentId"] = "Department_A" });

        Assert.Equal(409, error?.StatusCode);
        Assert.Equal("Organization.Department.HierarchyCorrupted", error?.Code);
    }

    [Fact]
    public async Task ValidateUpdateAsync_AllowsSameCompanyReparentAndClearingParent()
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1", true);
        AddDepartment(context, "Department_1", "Company_1");
        AddDepartment(context, "Department_2", "Company_1");
        await context.SaveChangesAsync();
        var validator = CreateValidator(
            context,
            new StubHierarchyQuery(static (_, _) => DepartmentParentRelationship.Allowed));

        var reparent = await validator.ValidateUpdateAsync(
            DepartmentCrudDescriptorFactory.Create(),
            "Department_1",
            new Dictionary<string, object?> { ["ParentDepartmentId"] = "Department_2" });
        var root = await validator.ValidateUpdateAsync(
            DepartmentCrudDescriptorFactory.Create(),
            "Department_1",
            new Dictionary<string, object?> { ["ParentDepartmentId"] = null });

        Assert.Null(reparent);
        Assert.Null(root);
    }

    [Fact]
    public async Task ValidateUpdateAsync_DisablingParentWithEnabledChildIsAllowed()
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1", true);
        AddDepartment(context, "Department_Parent", "Company_1");
        AddDepartment(context, "Department_Child", "Company_1", "Department_Parent");
        await context.SaveChangesAsync();

        var error = await CreateValidator(context).ValidateUpdateAsync(
            DepartmentCrudDescriptorFactory.Create(),
            "Department_Parent",
            new Dictionary<string, object?> { ["IsEnabled"] = false });

        Assert.Null(error);
    }

    private static DepartmentCrudValidator CreateValidator(EfPlatformDbContext context, IDepartmentHierarchyQuery? hierarchy = null)
    {
        var companies = context.ChangeTracker.Entries<Dictionary<string, object?>>()
            .Where(entry => entry.Metadata.Name == context.ResolveModelKey("Company")).Select(entry => entry.Entity)
            .Select(row => new ICP.Platform.Modules.Organization.Models.Company { Id = (string)row["EID"]!, IsEnabled = row["IsEnabled"] is true });
        var departments = context.ChangeTracker.Entries<Dictionary<string, object?>>()
            .Where(entry => entry.Metadata.Name == context.ResolveModelKey("Department")).Select(entry => entry.Entity)
            .Select(row => new ICP.Platform.Modules.Organization.Models.Department { Id = (string)row["EID"]!, CompanyId = (string)row["CompanyId"]!,
                ParentDepartmentId = row["ParentDepartmentId"] as string, IsEnabled = row["IsEnabled"] is true });
        var query = new RecordingOrganizationQuery(companies, departments);
        return new DepartmentCrudValidator(context, hierarchy ?? new SqlServerDepartmentHierarchyQuery(context), query);
    }
    private static EfPlatformDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;
        var definitions = PlatformPersistenceModelCatalog.AdaptPlatformCore(NonSqlPlatformBaselineModelSource.GetDefinitions())
            .Concat(IdentityModelDefinitions.All)
            .Concat(AuthenticationModelDefinitions.All)
            .ToArray();
        return new EfPlatformDbContext(options, definitions);
    }

    private static void AddCompany(EfPlatformDbContext context, string id, bool isEnabled) =>
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Company")).Add(Row(id, new()
        {
            ["ECODE"] = id,
            ["ENAME"] = id,
            ["IsEnabled"] = isEnabled,
            ["SortOrder"] = "0100"
        }));

    private static void AddDepartment(
        EfPlatformDbContext context,
        string id,
        string companyId,
        string? parentDepartmentId = null,
        bool enabled = true) =>
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Department")).Add(Row(id, new()
        {
            ["CompanyId"] = companyId,
            ["ParentDepartmentId"] = parentDepartmentId,
            ["ECODE"] = id,
            ["ENAME"] = id,
            ["IsEnabled"] = enabled,
            ["SortOrder"] = "0100"
        }));

    private static void AddPerson(EfPlatformDbContext context, string id, string companyId, string departmentId) =>
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Person")).Add(Row(id, new()
        {
            ["PersonNo"] = id,
            ["ENAME"] = id,
            ["CompanyId"] = companyId,
            ["DepartmentId"] = departmentId,
            ["PositionId"] = null,
            ["EmploymentStatus"] = "FullTime",
            ["IsEnabled"] = true,
            ["AccountEID"] = null,
            ["AccountBoundAt"] = null,
            ["AccountBoundBy"] = null,
            ["AccountBindingReason"] = null
        }));

    private static Dictionary<string, object?> Row(string id, Dictionary<string, object?> values)
    {
        values["EID"] = id;
        values["CreatedBy"] = "User_Admin";
        values["CreatedAt"] = DateTime.UtcNow;
        values["UpdatedBy"] = "User_Admin";
        values["UpdatedAt"] = DateTime.UtcNow;
        return values;
    }

    private sealed class StubHierarchyQuery(
        Func<string, string, DepartmentParentRelationship> classify) : IDepartmentHierarchyQuery
    {
        public Task<DepartmentParentRelationship> ClassifyParentAsync(
            string companyId,
            string currentDepartmentId,
            string candidateParentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(classify(currentDepartmentId, candidateParentId));

        public Task<DepartmentHierarchyCandidatePage> LookupParentCandidatesAsync(
            string companyId,
            string currentDepartmentId,
            string? keyword,
            int page,
            int pageSize,
            bool includeDisabled,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

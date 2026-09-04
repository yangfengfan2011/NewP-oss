using ICP.Platform.Modules.Authentication.Persistence.Descriptors;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class DepartmentCrudValidatorTests
{
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

        var error = await new DepartmentCrudValidator(context).ValidateCreateAsync(
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

        var error = await new DepartmentCrudValidator(context).ValidateCreateAsync(
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

        var error = await new DepartmentCrudValidator(context).ValidateCreateAsync(
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

        var error = await new DepartmentCrudValidator(context).ValidateUpdateAsync(
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

    private static void AddDepartment(EfPlatformDbContext context, string id, string companyId) =>
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Department")).Add(Row(id, new()
        {
            ["CompanyId"] = companyId,
            ["ParentDepartmentId"] = null,
            ["ECODE"] = id,
            ["ENAME"] = id,
            ["IsEnabled"] = true,
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
}

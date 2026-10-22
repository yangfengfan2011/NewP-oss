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

public sealed class CompanyCrudValidatorTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("0100")]
    [InlineData("01001")]
    [InlineData("12345678901234567890123456789012")]
    public async Task ValidateCreateAsync_AcceptsAsciiDigitSortOrderWithoutNormalizing(string sortOrder)
    {
        await using var context = CreateContext();
        var validator = new CompanyCrudValidator();

        var error = await validator.ValidateCreateAsync(
            CompanyCrudDescriptorFactory.Create(),
            new Dictionary<string, object?> { ["SortOrder"] = sortOrder });

        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 0100")]
    [InlineData("0100 ")]
    [InlineData("１２")]
    [InlineData("12A")]
    [InlineData("123456789012345678901234567890123")]
    public async Task ValidateCreateAsync_RejectsInvalidSortOrderWithFieldError(string sortOrder)
    {
        await using var context = CreateContext();
        var validator = new CompanyCrudValidator();

        var error = await validator.ValidateCreateAsync(
            CompanyCrudDescriptorFactory.Create(),
            new Dictionary<string, object?> { ["SortOrder"] = sortOrder });

        Assert.Equal(400, error?.StatusCode);
        Assert.Equal("Organization.Company.SortOrderInvalid", error?.Code);
        Assert.Equal("SortOrder", Assert.Single(error!.FieldErrors).Field);
    }

    [Fact]
    public async Task ValidateUpdateAsync_DisablingCompanyWithEnabledDepartmentIsAllowed()
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1", true);
        AddDepartment(context, "Department_1", "Company_1", true);
        await context.SaveChangesAsync();
        var validator = new CompanyCrudValidator();

        var error = await validator.ValidateUpdateAsync(
            CompanyCrudDescriptorFactory.Create(),
            "Company_1",
            new Dictionary<string, object?> { ["IsEnabled"] = false });

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateUpdateAsync_PersonReferenceAndDisabledDepartmentDoNotBlockDisable()
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1", true);
        AddDepartment(context, "Department_1", "Company_1", false);
        AddPerson(context, "Person_1", "Company_1", "Department_1");
        await context.SaveChangesAsync();
        var validator = new CompanyCrudValidator();

        var error = await validator.ValidateUpdateAsync(
            CompanyCrudDescriptorFactory.Create(),
            "Company_1",
            new Dictionary<string, object?> { ["IsEnabled"] = false });

        Assert.Null(error);
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
        Rows(context, "Company").Add(Row(id, new Dictionary<string, object?>
        {
            ["ECODE"] = id,
            ["ENAME"] = id,
            ["IsEnabled"] = isEnabled,
            ["SortOrder"] = "0100"
        }));

    private static void AddDepartment(EfPlatformDbContext context, string id, string companyId, bool isEnabled) =>
        Rows(context, "Department").Add(Row(id, new Dictionary<string, object?>
        {
            ["CompanyId"] = companyId,
            ["ParentDepartmentId"] = null,
            ["ECODE"] = id,
            ["ENAME"] = id,
            ["IsEnabled"] = isEnabled,
            ["SortOrder"] = "0100"
        }));

    private static void AddPerson(EfPlatformDbContext context, string id, string companyId, string departmentId) =>
        Rows(context, "Person").Add(Row(id, new Dictionary<string, object?>
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

    private static DbSet<Dictionary<string, object?>> Rows(EfPlatformDbContext context, string modelName) =>
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey(modelName));

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

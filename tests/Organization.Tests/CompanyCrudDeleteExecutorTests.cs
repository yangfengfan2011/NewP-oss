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

public sealed class CompanyCrudDeleteExecutorTests
{
    [Fact]
    public async Task DeleteAsync_UnreferencedCompanyDeletes()
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1");
        await context.SaveChangesAsync();
        var executor = CreateExecutor(context);

        var result = await executor.DeleteAsync(CompanyCrudDescriptorFactory.Create(), "Company_1");

        Assert.True(result.Succeeded);
        Assert.False(await Rows(context, "Company").AnyAsync());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DeleteAsync_DepartmentOrPersonReferenceReturnsStableConflict(bool addDepartment, bool addPerson)
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1");
        if (addDepartment)
        {
            AddDepartment(context, "Department_1", "Company_1");
        }
        if (addPerson)
        {
            AddPerson(context, "Person_1", "Company_1");
        }
        await context.SaveChangesAsync();
        var executor = CreateExecutor(context);

        var result = await executor.DeleteAsync(CompanyCrudDescriptorFactory.Create(), "Company_1");

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error?.StatusCode);
        Assert.Equal("Organization.Company.DeleteBlockedByReference", result.Error?.Code);
        Assert.True(await Rows(context, "Company").AnyAsync());
    }

    [Fact]
    public async Task DeleteAsync_MissingCompanyReturnsStandardNotFound()
    {
        await using var context = CreateContext();
        var executor = CreateExecutor(context);

        var result = await executor.DeleteAsync(CompanyCrudDescriptorFactory.Create(), "Company_Missing");

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Error?.StatusCode);
        Assert.Equal("StandardCrud.NotFound", result.Error?.Code);
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

    private static CompanyCrudDeleteExecutor CreateExecutor(EfPlatformDbContext context) =>
        new(context, new OrganizationConditionalDeleteExecutor(context));

    private static DbSet<Dictionary<string, object?>> Rows(EfPlatformDbContext context, string modelName) =>
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey(modelName));

    private static void AddCompany(EfPlatformDbContext context, string id) =>
        Rows(context, "Company").Add(Row(id, new Dictionary<string, object?>
        {
            ["ECODE"] = id,
            ["ENAME"] = id,
            ["IsEnabled"] = true,
            ["SortOrder"] = "0100"
        }));

    private static void AddDepartment(EfPlatformDbContext context, string id, string companyId) =>
        Rows(context, "Department").Add(Row(id, new Dictionary<string, object?>
        {
            ["CompanyId"] = companyId,
            ["ParentDepartmentId"] = null,
            ["ECODE"] = id,
            ["ENAME"] = id,
            ["IsEnabled"] = true,
            ["SortOrder"] = "0100"
        }));

    private static void AddPerson(EfPlatformDbContext context, string id, string companyId) =>
        Rows(context, "Person").Add(Row(id, new Dictionary<string, object?>
        {
            ["PersonNo"] = id,
            ["ENAME"] = id,
            ["CompanyId"] = companyId,
            ["DepartmentId"] = null,
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

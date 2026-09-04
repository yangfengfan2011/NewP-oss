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

public sealed class DepartmentCrudDeleteExecutorTests
{
    [Fact]
    public async Task DeleteAsync_UnreferencedRootDepartmentDeletes()
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1");
        AddDepartment(context, "Department_1", "Company_1");
        await context.SaveChangesAsync();

        var result = await new DepartmentCrudDeleteExecutor(context).DeleteAsync(
            DepartmentCrudDescriptorFactory.Create(),
            "Department_1");

        Assert.True(result.Succeeded);
        Assert.False(await Rows(context, "Department").AnyAsync());
    }

    [Fact]
    public async Task DeleteAsync_PersonReferenceReturnsStableConflictAndPreservesDepartment()
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1");
        AddDepartment(context, "Department_1", "Company_1");
        AddPerson(context, "Person_1", "Company_1", "Department_1");
        await context.SaveChangesAsync();

        var result = await new DepartmentCrudDeleteExecutor(context).DeleteAsync(
            DepartmentCrudDescriptorFactory.Create(),
            "Department_1");

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error?.StatusCode);
        Assert.Equal("Organization.Department.DeleteBlockedByPerson", result.Error?.Code);
        Assert.True(await Rows(context, "Department").AnyAsync());
    }

    [Fact]
    public async Task DeleteAsync_ChildReferenceReturnsStableConflictAndPreservesParent()
    {
        await using var context = CreateContext();
        AddCompany(context, "Company_1");
        AddDepartment(context, "Department_Parent", "Company_1");
        AddDepartment(context, "Department_Child", "Company_1", "Department_Parent");
        await context.SaveChangesAsync();

        var result = await new DepartmentCrudDeleteExecutor(context).DeleteAsync(
            DepartmentCrudDescriptorFactory.Create(),
            "Department_Parent");

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error?.StatusCode);
        Assert.Equal("Organization.Department.DeleteBlockedByChild", result.Error?.Code);
        Assert.Equal(2, await Rows(context, "Department").CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_MissingDepartmentReturnsStandardNotFound()
    {
        await using var context = CreateContext();

        var result = await new DepartmentCrudDeleteExecutor(context).DeleteAsync(
            DepartmentCrudDescriptorFactory.Create(),
            "Department_Missing");

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

    private static DbSet<Dictionary<string, object?>> Rows(EfPlatformDbContext context, string modelName) =>
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey(modelName));

    private static void AddCompany(EfPlatformDbContext context, string id) =>
        Rows(context, "Company").Add(Row(id, new()
        {
            ["ECODE"] = id,
            ["ENAME"] = id,
            ["IsEnabled"] = true,
            ["SortOrder"] = "0100"
        }));

    private static void AddDepartment(
        EfPlatformDbContext context,
        string id,
        string companyId,
        string? parentDepartmentId = null) =>
        Rows(context, "Department").Add(Row(id, new()
        {
            ["CompanyId"] = companyId,
            ["ParentDepartmentId"] = parentDepartmentId,
            ["ECODE"] = id,
            ["ENAME"] = id,
            ["IsEnabled"] = true,
            ["SortOrder"] = "0100"
        }));

    private static void AddPerson(EfPlatformDbContext context, string id, string companyId, string departmentId) =>
        Rows(context, "Person").Add(Row(id, new()
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

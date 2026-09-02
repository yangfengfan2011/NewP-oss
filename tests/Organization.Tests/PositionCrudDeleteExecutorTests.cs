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

public sealed class PositionCrudDeleteExecutorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeleteAsync_UnreferencedPositionInAnyLifecycleState_Deletes(bool isEnabled)
    {
        await using var context = CreateContext();
        AddPosition(context, "Position_1", isEnabled);
        await context.SaveChangesAsync();
        var executor = new PositionCrudDeleteExecutor(context);

        var result = await executor.DeleteAsync(PositionCrudDescriptorFactory.Create(), "Position_1");

        Assert.True(result.Succeeded);
        Assert.False(await Positions(context).AnyAsync(row => EF.Property<string>(row, "EID") == "Position_1"));
    }

    [Fact]
    public async Task DeleteAsync_CurrentlyReferencedPosition_ReturnsStableConflictAndPreservesBothRows()
    {
        await using var context = CreateContext();
        AddPosition(context, "Position_1", true);
        AddPerson(context, "Person_1", "Position_1");
        await context.SaveChangesAsync();
        var executor = new PositionCrudDeleteExecutor(context);

        var result = await executor.DeleteAsync(PositionCrudDescriptorFactory.Create(), "Position_1");

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error?.StatusCode);
        Assert.Equal("Organization.Position.DeleteBlockedByPersonReference", result.Error?.Code);
        Assert.Equal("organization.position.deleteBlockedByPersonReference", result.Error?.ReasonKey);
        Assert.True(await Positions(context).AnyAsync(row => EF.Property<string>(row, "EID") == "Position_1"));
        Assert.True(await Persons(context).AnyAsync(row => EF.Property<string?>(row, "PositionId") == "Position_1"));
    }

    [Fact]
    public async Task DeleteAsync_PastReferenceThatWasCleared_DoesNotBlockDeletion()
    {
        await using var context = CreateContext();
        AddPosition(context, "Position_1", true);
        AddPerson(context, "Person_1", "Position_1");
        await context.SaveChangesAsync();
        var person = await Persons(context).SingleAsync();
        person["PositionId"] = null;
        await context.SaveChangesAsync();
        var executor = new PositionCrudDeleteExecutor(context);

        var result = await executor.DeleteAsync(PositionCrudDescriptorFactory.Create(), "Position_1");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task DeleteAsync_MissingPosition_ReturnsStableNotFound()
    {
        await using var context = CreateContext();
        var executor = new PositionCrudDeleteExecutor(context);

        var result = await executor.DeleteAsync(PositionCrudDescriptorFactory.Create(), "Position_Missing");

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

    private static DbSet<Dictionary<string, object?>> Positions(EfPlatformDbContext context) =>
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Position"));

    private static DbSet<Dictionary<string, object?>> Persons(EfPlatformDbContext context) =>
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Person"));

    private static void AddPosition(EfPlatformDbContext context, string id, bool isEnabled) =>
        Positions(context).Add(new Dictionary<string, object?>
        {
            ["EID"] = id,
            ["ENAME"] = id,
            ["Description"] = null,
            ["IsEnabled"] = isEnabled,
            ["CreatedBy"] = "User_Admin",
            ["CreatedAt"] = DateTime.UtcNow,
            ["UpdatedBy"] = "User_Admin",
            ["UpdatedAt"] = DateTime.UtcNow
        });

    private static void AddPerson(EfPlatformDbContext context, string id, string? positionId) =>
        Persons(context).Add(new Dictionary<string, object?>
        {
            ["EID"] = id,
            ["PersonNo"] = id,
            ["ENAME"] = id,
            ["CompanyId"] = null,
            ["DepartmentId"] = null,
            ["PositionId"] = positionId,
            ["EmploymentStatus"] = "FullTime",
            ["IsEnabled"] = true,
            ["Revision"] = 0L,
            ["AccountEID"] = null,
            ["AccountBoundAt"] = null,
            ["AccountBoundBy"] = null,
            ["AccountBindingReason"] = null,
            ["CreatedBy"] = "User_Admin",
            ["CreatedAt"] = DateTime.UtcNow,
            ["UpdatedBy"] = "User_Admin",
            ["UpdatedAt"] = DateTime.UtcNow
        });
}

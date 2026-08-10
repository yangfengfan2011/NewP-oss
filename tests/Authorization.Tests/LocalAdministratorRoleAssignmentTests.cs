using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Authorization.Tests;

public sealed class LocalAdministratorRoleAssignmentTests
{
    [Fact]
    public async Task AssignAsync_UsesExistingLocalAdminRoleWithoutChangingTheCatalog()
    {
        await using var context = CreateContext();
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Role")).Add(Role("Role_1", "LocalAdmin", enabled: true));
        await context.SaveChangesAsync();
        var assignment = new EfLocalAdministratorRoleAssignment(context, new FixedObjectIdGenerator());

        await assignment.AssignAsync("User_1", "platform:local-maintenance");
        await context.SaveChangesAsync();

        Assert.Equal(LocalAdministratorAssignmentState.Assigned, await assignment.InspectAsync("User_1"));
        Assert.Single(await context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Role")).ToListAsync());
        var link = Assert.Single(await context.Set<Dictionary<string, object?>>(context.ResolveModelKey("UserRole")).ToListAsync());
        Assert.Equal("Role_1", link["RoleId"]);
        Assert.Equal("User_1", link["UserId"]);
    }

    [Fact]
    public async Task AssignAsync_WhenLocalAdminRoleIsMissing_FailsWithoutCreatingAssignment()
    {
        await using var context = CreateContext();
        var assignment = new EfLocalAdministratorRoleAssignment(context, new FixedObjectIdGenerator());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            assignment.AssignAsync("User_1", "platform:local-maintenance").AsTask());

        Assert.Equal("AUTHORIZATION_LOCAL_ADMIN_ROLE_UNAVAILABLE", exception.Message);
        Assert.Empty(await context.Set<Dictionary<string, object?>>(context.ResolveModelKey("UserRole")).ToListAsync());
    }

    private static EfPlatformDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new EfPlatformDbContext(options, AuthorizationModelDefinitions.All);
    }

    private static Dictionary<string, object?> Role(string eid, string code, bool enabled) => new(StringComparer.Ordinal)
    {
        ["EID"] = eid,
        ["ECODE"] = code,
        ["ENAME"] = code,
        ["Description"] = null,
        ["IsEnabled"] = enabled,
        ["IsBuiltIn"] = true,
        ["CreatedAt"] = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc),
        ["CreatedBy"] = "system",
        ["UpdatedAt"] = null,
        ["UpdatedBy"] = null
    };

    private sealed class FixedObjectIdGenerator : ICP.Platform.Crud.ObjectIds.IPlatformObjectIdGenerator
    {
        public string NewId(string objectName) => $"{objectName}_1";
    }
}

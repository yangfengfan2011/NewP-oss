using ICP.Platform.Authorization;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.Authorization.Persistence.Ef;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace ICP.Platform.Modules.Authorization.Tests;
public sealed class FeatureDecisionSourceTests
{
    [Fact]
    public async Task UsesEnabledRoleUnionAndCurrentLinksWithoutSubstitutingAnotherBehavior()
    {
        using var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new AuthorizationModelMetadataProvider().GetDefinitions());
        var roles = new EfRoleRepository(db);
        var role = new Role { Code = "Editor", Name = "Editor", IsEnabled = true }; await roles.CreateAsync(role);
        await new EfUserRoleRepository(db).CreateBatchAsync([new UserRole { UserId = "actor", RoleId = role.Id }]);
        var catalog = new ActiveFeatureCatalog();
        var points = FeatureCatalogCompiler.Compile([new("Authorization", "workbench", "navigation", "工作台", null, "read")]);
        catalog.Publish(points, new Dictionary<string, string> { [points[0].Position] = "point-1" });
        var grants = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant"));
        var row = new Dictionary<string, object?> { ["EID"] = "grant-1", ["RoleId"] = role.Id, ["FunctionPointId"] = "point-1", ["GrantedAt"] = DateTime.UtcNow };
        grants.Add(row); await db.SaveChangesAsync();
        var source = new FeatureDecisionSource(db, catalog);
        Assert.Equal(PermissionDecision.Granted, await source.EvaluateAsync("actor", points[0].Position, "read", default));
        Assert.Equal(PermissionDecision.Denied, await source.EvaluateAsync("actor", points[0].Position, "save", default));
        role.IsEnabled = false; await roles.UpdateAsync(role);
        Assert.Equal(PermissionDecision.Denied, await source.EvaluateAsync("actor", points[0].Position, "read", default));
        role.IsEnabled = true; await roles.UpdateAsync(role);
        grants.Remove(row); await db.SaveChangesAsync();
        Assert.Equal(PermissionDecision.Denied, await source.EvaluateAsync("actor", points[0].Position, "read", default));
    }
}

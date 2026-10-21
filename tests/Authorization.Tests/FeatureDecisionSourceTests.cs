using ICP.Platform.Authorization;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.Authorization.Persistence.Ef;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;
namespace ICP.Platform.Modules.Authorization.Tests;
public sealed class FeatureDecisionSourceTests
{
    [Fact]
    public void PublicationRejectsPartialOrMergedIdentityAndKeepsPreviousSnapshot()
    {
        var catalog = new ActiveFeatureCatalog();
        var points = FeatureCatalogCompiler.Compile([new("Authorization", "first", "navigation", "一", null, "read"),
            new("Authorization", "second", "navigation", "二", null, "read")]);
        catalog.Publish(points, new Dictionary<string, string> { [points[0].Position] = "one", [points[1].Position] = "two" });
        var previous = catalog.Snapshot;
        Assert.Throws<InvalidOperationException>(() => catalog.Publish(points, new Dictionary<string, string> { [points[0].Position] = "one" }));
        Assert.Throws<ArgumentException>(() => catalog.Publish(points, new Dictionary<string, string> { [points[0].Position] = "same", [points[1].Position] = "same" }));
        Assert.Same(previous, catalog.Snapshot);
    }
    [Fact]
    public async Task UsesEnabledRoleUnionAndCurrentLinksWithoutSubstitutingAnotherBehavior()
    {
        using var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options,
            new AuthorizationModelMetadataProvider().GetDefinitions());
        var roles = new EfRoleRepository(db);
        var role = new Role { Code = "Editor", Name = "Editor", IsEnabled = true }; await roles.CreateAsync(role);
        await new EfUserRoleRepository(db).CreateBatchAsync([new UserRole { UserId = "actor", RoleId = role.Id }]);
        var catalog = new ActiveFeatureCatalog();
        var points = FeatureCatalogCompiler.Compile([new("Authorization", "workbench", "navigation", "工作台", null, "read"), new("Other", "workbench", "navigation", "其他", null, "read")]);
        catalog.Publish(points, new Dictionary<string, string> { [points[0].Position] = "point-1", [points[1].Position] = "point-2" });
        var grants = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant"));
        var row = new Dictionary<string, object?> { ["EID"] = "grant-1", ["RoleId"] = role.Id, ["FunctionPointId"] = "point-1", ["GrantedAt"] = DateTime.UtcNow };
        grants.Add(row); await db.SaveChangesAsync();
        var source = new FeatureDecisionSource(db, catalog);
        Assert.Equal(PermissionDecision.Granted, await source.EvaluateAsync("actor", points[0].Position, "Authorization", "read", default));
        Assert.Equal(PermissionDecision.Denied, await source.EvaluateAsync("actor", points[0].Position, "Authorization", "save", default));
        Assert.Equal(PermissionDecision.Denied, await source.EvaluateAsync("actor", points[0].Position, "Other", "read", default));
        role.IsEnabled = false; await roles.UpdateAsync(role);
        Assert.Equal(PermissionDecision.Denied, await source.EvaluateAsync("actor", points[0].Position, "Authorization", "read", default));
        role.IsEnabled = true; await roles.UpdateAsync(role);
        grants.Remove(row); await db.SaveChangesAsync();
        Assert.Equal(PermissionDecision.Denied, await source.EvaluateAsync("actor", points[0].Position, "Authorization", "read", default));
    }
}

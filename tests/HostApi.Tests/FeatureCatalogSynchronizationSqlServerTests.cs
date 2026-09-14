using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class FeatureCatalogSynchronizationSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "目录删除与新增在保存、提交失败后完整回滚并允许同作用域重试")]
    public async Task FailedSynchronization_RollsBackDatabaseAndPublication_AndRetriesInSameScope()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var role = await FeatureHttpTestRequests.CreateAsync(admin, http.Role("create"), "/api/authorization/roles", new { Code = "sync-retry", Name = "目录重试" });
        var roleId = role.GetProperty("id").GetString()!;
        var removed = http.Organization("persons", "edit", "list.rowActions");
        foreach (var mode in new[] { "save", "commit" })
        {
            await http.GrantAsync(admin, roleId, removed);
            using var scope = fixture.Services.CreateScope();
            var seed = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            seed.Set<Dictionary<string, object?>>(seed.ResolveModelKey("Permission")).Add(new()
            {
                ["EID"] = "Permission_rollback", ["ECODE"] = "Legacy.Rollback", ["ENAME"] = "旧权限", ["ModuleId"] = "Example"
            });
            seed.Set<Dictionary<string, object?>>(seed.ResolveModelKey("RolePermission")).Add(new()
            {
                ["EID"] = "RolePermission_rollback", ["RoleId"] = roleId, ["PermissionId"] = "Permission_rollback", ["GrantedAt"] = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
            var synchronizer = scope.ServiceProvider.GetRequiredService<IFeatureCatalogSynchronizer>();
            var catalog = scope.ServiceProvider.GetRequiredService<ActiveFeatureCatalog>();
            var before = catalog.Snapshot;
            var stored = await SnapshotAsync(fixture);
            var replacement = removed with { Position = removed.Position + "-new", Key = removed.Key + "-new" };
            var candidate = before.Points.Where(point => point.Position != removed.Position).Append(replacement).ToArray();
            fixture.Fault.Mode = mode;
            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => synchronizer.SynchronizeAsync(candidate));
            }
            finally { fixture.Fault.Mode = ""; }
            Assert.Same(before, catalog.Snapshot);
            Assert.Equal(stored, await SnapshotAsync(fixture));

            // 使用故障时同一服务与DbContext，确保失败的新增项不会污染明确发起的重试。
            await synchronizer.SynchronizeAsync(candidate);
            Assert.DoesNotContain(removed.Position, catalog.Snapshot.ByPosition.Keys);
            Assert.Contains(replacement.Position, catalog.Snapshot.ByPosition.Keys);
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.False(await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant"))
                .AnyAsync(row => EF.Property<string>(row, "FunctionPointId") == before.ByPosition[removed.Position].Id));
            Assert.False(await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Permission")).AnyAsync());
            Assert.False(await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RolePermission")).AnyAsync());
            await fixture.RestoreFeatureCatalogAsync();
        }
        await fixture.RepeatFeatureSynchronizationAsync();
    }

    private static async Task<string[]> SnapshotAsync(BusinessAuditHostFixture fixture)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var points = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("FunctionPoint"))
            .AsNoTracking().Select(row => new { Id = EF.Property<string>(row, "EID"), Key = EF.Property<string>(row, "PositionKey") }).ToArrayAsync();
        var grants = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant"))
            .AsNoTracking().Select(row => EF.Property<string>(row, "EID") + ":" + EF.Property<string>(row, "RoleId") + ":" + EF.Property<string>(row, "FunctionPointId")).ToArrayAsync();
        var legacy = new List<string>();
        foreach (var model in new[] { "Permission", "RolePermission" })
            legacy.AddRange((await db.Set<Dictionary<string, object?>>(db.ResolveModelKey(model)).AsNoTracking().ToArrayAsync())
                .Select(row => model + ":" + System.Text.Json.JsonSerializer.Serialize(row.OrderBy(pair => pair.Key))));
        return points.Select(point => point.Id + ":" + point.Key).Concat(grants).Concat(legacy).Order(StringComparer.Ordinal).ToArray();
    }
}

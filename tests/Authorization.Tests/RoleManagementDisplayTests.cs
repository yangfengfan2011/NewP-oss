using System.Text.Json;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Presentation;
using ICP.Platform.Query.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public sealed class RoleManagementDisplayTests
{
    [Theory]
    [InlineData("zh-CN", "Asia/Taipei", "2026/9/4 15:24:26")]
    [InlineData("en-US", "America/Los_Angeles", "9/4/2026 12:24:26 AM")]
    public async Task FormalRoleRead_ProjectsBothAuditColumnsInOneBatchAndPreservesRawValues(string locale, string zone, string expected)
    {
        var models = AuthorizationModelDefinitions.All.Select(model => model with { OwnerModuleId = "Authorization" }).ToArray();
        var plan = new CompiledPersistencePlan("candidate", "snapshot", "hash", default, "digest", [], models, []);
        var registry = new StandardCrudResourceRegistry([RoleReadResource.Create()], plan);
        var stamp = new Stamp();
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options;
        await using var db = new EfPlatformDbContext(options, plan, stamp);
        var rows = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role"));
        foreach (var id in new[] { "role-1", "role-2" })
        {
            stamp.Actor = id == "role-1" ? "actor-1" : "actor-2";
            rows.Add(new Dictionary<string, object?> { ["EID"] = id, ["ECODE"] = id, ["ENAME"] = id,
                ["Description"] = null, ["IsEnabled"] = true, ["IsBuiltIn"] = false });
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        var names = new Names();
        var service = new StandardCrudService(new EfStandardCrudRepository(db, new PlatformObjectIdGenerator()),
            new EfPlatformQueryExecutor(db), [new RoleReadResource()], [], [], [], [], [],
            temporalDisplayProjectionExecutor: new TemporalDisplayProjectionExecutor(new Presentation(locale, zone), new TemporalDisplayFormatter()),
            userDisplayNameDirectory: names);
        var query = new RoleManagementQuery(service, registry);
        var page = JsonSerializer.SerializeToElement(await query.ListAsync("role", 1, 20, default));
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Equal(1, names.Calls);
        Assert.Equal(new[] { "actor-1", "actor-2" }, names.Ids);
        foreach (var row in page.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(row.GetProperty("EID").GetString() == "role-1" ? "actor-1" : "actor-2", row.GetProperty("CreatedBy").GetString());
            Assert.Equal(stamp.Instant, row.GetProperty("CreatedAt").GetDateTime());
            Assert.Equal("Current account", row.GetProperty("CreatedBy_DisplayValue").GetString());
            Assert.Equal("Current account", row.GetProperty("UpdatedBy_DisplayValue").GetString());
            Assert.Equal(expected, row.GetProperty("CreatedAt_DisplayValue").GetString());
            Assert.Equal(expected, row.GetProperty("UpdatedAt_DisplayValue").GetString());
        }
        var detail = await query.GetAsync("role-1", default);
        Assert.Equal(2, names.Calls);
        Assert.Equal("actor-1", detail!.CreatedBy);
        Assert.Equal(stamp.Instant, detail.CreatedAt);
        Assert.Equal("Current account", detail.CreatedBy_DisplayValue);
        Assert.Equal("Current account", detail.UpdatedBy_DisplayValue);
        Assert.Equal(expected, detail.CreatedAt_DisplayValue);
        Assert.Equal(expected, detail.UpdatedAt_DisplayValue);
        Assert.Null(await query.GetAsync("missing", default));
        Assert.Equal(2, names.Calls);
        Assert.False(registry.GetAll().Single().MapStandardEndpoints);
    }

    private sealed class Stamp : IEntityAuditStampProvider
    {
        public DateTime Instant { get; } = new(2026, 9, 4, 7, 24, 26, DateTimeKind.Utc);
        public string Actor { get; set; } = "actor-1";
        public EntityAuditStamp GetCurrent() => new(Actor, Instant);
    }
    private sealed class Names : IUserDisplayNameDirectory
    {
        public int Calls { get; private set; }
        public string[] Ids { get; private set; } = [];
        public Task<IReadOnlyDictionary<string, string>> FindManyAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
        {
            Calls++;
            Ids = ids.Order().ToArray();
            return Task.FromResult<IReadOnlyDictionary<string, string>>(ids.ToDictionary(id => id, _ => "Current account"));
        }
    }
    private sealed class Presentation(string locale, string zone) : IRequestPresentationContextAccessor
    {
        public RequestPresentationContext? Current => new(locale, zone);
    }
}

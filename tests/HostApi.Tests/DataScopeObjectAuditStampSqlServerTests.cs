using System.Net;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeObjectAuditStampSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "单对象配置行/动作行与业务记录共享可信stamp，无变化不产生记录")]
    public async Task DirectReplacement_SharesOneTrustedStamp_AndNextChangedOperationObservesANewStamp()
    {
        var clock = new SteppingClock();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "Stamp295Part");
        var manage = fixture.FeatureCatalog.Single(point => point.ModuleId == "Part" && point.Key == "authorize" && point.ActionScope == "list.rowActions");
        var path = "/api/part/parts/" + part.Id + "/permissions";
        var input = new DataScopeObjectDto("part.part", part.Id,
            [new("Stamp295Subject", "all", null, 0, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "deny" })]);
        clock.Enabled = true;
        await SaveAsync(input);
        var first = await AssertStampAsync();
        var changed = input with { Subjects = [input.Subjects[0] with { Priority = 1,
            Actions = new Dictionary<string, string> { ["read"] = "deny", ["edit"] = "allow" } }] };
        await SaveAsync(changed);
        var second = await AssertStampAsync();
        Assert.True(second > first);
        await SaveAsync(changed);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.Equal(2, await Records(db).CountAsync());

        async Task SaveAsync(DataScopeObjectDto configuration)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, configuration);
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }
        async Task<DateTime> AssertStampAsync()
        {
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var record = await Records(db).OrderByDescending(row => EF.Property<DateTime>(row, "OccurredAt")).FirstAsync();
            var subject = await db.Set<Dictionary<string, object?>>("DataScopeObjectSubject").AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "EID") == "Stamp295Subject");
            var actions = await db.Set<Dictionary<string, object?>>("DataScopeObjectAction").AsNoTracking()
                .Where(row => EF.Property<string>(row, "EntryId") == "Stamp295Subject").ToArrayAsync();
            Assert.Equal(2, actions.Length);
            var stamp = (DateTime)record["OccurredAt"]!;
            Assert.Equal(stamp, subject["UpdatedAt"]);
            Assert.Equal(record["ActorId"], subject["UpdatedBy"]);
            Assert.All(actions, action => { Assert.Equal(stamp, action["UpdatedAt"]); Assert.Equal(record["ActorId"], action["UpdatedBy"]); });
            return stamp;
        }
        IQueryable<Dictionary<string, object?>> Records(EfPlatformDbContext db) => db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
            .Where(row => EF.Property<string>(row, "TargetId") == "part.part/" + part.Id && EF.Property<string>(row, "EventType") == "dataScope.object.changed");
    }

    private sealed class SteppingClock : TimeProvider
    {
        private readonly DateTimeOffset start = DateTimeOffset.UtcNow;
        private long ticks;
        public bool Enabled { get; set; }
        public override DateTimeOffset GetUtcNow() => start.AddTicks(Enabled ? Interlocked.Add(ref ticks, TimeSpan.TicksPerMillisecond) : 0);
    }
}

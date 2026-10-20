using System.Net;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeDynamicSubjectSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "动态创建人/所属人/部门按目标可信事实，scalar与SQL和解释一致，规则不扩主体")]
    public async Task PartDynamicSubjects_UseEachTargetsTrustedFacts_AndRemainDirectOnly()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var entry = Point(fixture, "workspace.spaces.access");
        var read = fixture.FeatureCatalog.First(point => point.ReadBehaviors.Contains("workspace.folder.read"));
        var create = Point(fixture, "workspace.folder.createPart");
        var manage = fixture.FeatureCatalog.Single(point => point.Key == "authorizeContent" && point.ActionScope == "list.rowActions");
        var actors = new List<(string AccountId, HttpClient Client)>();
        try
        {
            foreach (var suffix in new[] { "Creator", "Owner", "Department" })
            {
                var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Dynamic295" + suffix,
                    "动态主体" + suffix, "dynamic295-" + suffix.ToLowerInvariant(), suffix, entry, read, create);
                actors.Add((actor.AccountId, actor.Client));
            }
            var original = await CreateAsync(fixture, actors[0].Client, position, "Part", "parts", "createPart", "Dynamic295Original");
            var next = await CreateAsync(fixture, actors[1].Client, position, "Part", "parts", "createPart", "Dynamic295NewTarget");
            var department = (await fixture.Services.GetRequiredService<IOrganizationQuery>().ReadAsync()).FindByAccount(actors[2].AccountId)!
                .Department!["EID"] as string;
            Assert.False(string.IsNullOrEmpty(department));
            using (var setup = fixture.Services.CreateScope())
            {
                var db = setup.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                // 隔离准备已持久化的不同归属事实；不是开放客户端编辑字段或提前实现转交。
                await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Part")).Where(row => EF.Property<string>(row, "EID") == original.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string?>(row, "OwnerId"), actors[1].AccountId)
                        .SetProperty(row => EF.Property<string?>(row, "OwnerDepartmentId"), department));
            }
            var configuration = new DataScopeObjectDto("part.part", original.Id,
                [new("Dynamic295All", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny", ["edit"] = "deny", ["delete"] = "deny", ["authorize"] = "deny" }),
                 new("Dynamic295Creator", "creator", null, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow" }),
                 new("Dynamic295Owner", "owner", null, 1, new Dictionary<string, string> { ["read"] = "allow", ["delete"] = "allow" }),
                 new("Dynamic295Department", "ownerDepartment", null, 1, new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = "allow" })]);
            foreach (var target in new[] { original, next })
            {
                var input = configuration with { ObjectId = target.Id,
                    Subjects = configuration.Subjects.Select(subject => subject with { Id = subject.Id + (target == next ? "New" : "") }).ToArray() };
                using var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, PermissionPath(target), input);
                Assert.True(saved.StatusCode == HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());
            }
            for (var index = 0; index < actors.Count; index++)
            {
                await AssertDecisionAsync(actors[index].AccountId, original.Id, true, index == 0, index == 1, index == 2);
                await AssertDecisionAsync(actors[index].AccountId, next.Id, index == 1, index == 1, index == 1, index == 1);
            }
            // 规则与直接项共用旧身份契约，但不能因此间接接收需要对象字段的动态类型。
            var ruleSave = Point(fixture, "authorization.dataScope.save");
            foreach (var type in new[] { "creator", "owner", "ownerDepartment" })
            {
                var rule = new DataScopeRuleDto("Dynamic295Rule", "Dynamic295Rule", null, "不得扩规则主体", true, "part.part",
                    new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(original.Id)),
                    [new("Dynamic295RuleSubject", type, null, 1, new Dictionary<string, string> { ["read"] = "allow" })], []);
                using var rejected = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, ruleSave, HttpMethod.Put,
                    "/api/authorization/data-scope/rules/Dynamic295Rule", rule);
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            }
            using (var setup = fixture.Services.CreateScope())
            {
                var db = setup.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Part")).Where(row => EF.Property<string>(row, "EID") == original.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string>(row, "CreatedBy"), "missing-creator")
                        .SetProperty(row => EF.Property<string?>(row, "OwnerId"), "missing-owner")
                        .SetProperty(row => EF.Property<string?>(row, "OwnerDepartmentId"), "missing-department"));
            }
            foreach (var actor in actors) await AssertDecisionAsync(actor.AccountId, original.Id, false, false, false, false);

            string PermissionPath(Target target) => "/api/workspace/content/" + target.LinkId + "/permissions?queryScope="
                + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            async Task AssertDecisionAsync(string accountId, string objectId, bool readable, bool edit, bool delete, bool authorize)
            {
                using var scope = fixture.Services.CreateScope();
                var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
                var previous = accessor.HttpContext;
                accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, accountId)], "test-trusted-account")) };
                try
                {
                    var decisions = scope.ServiceProvider.GetRequiredService<IDataScopeObjectDecisionProvider>();
                    var plans = scope.ServiceProvider.GetRequiredService<IDataScopeReadPlanProvider>();
                    Assert.Equal(readable, await decisions.IsAllowedAsync("part.part", objectId, "read"));
                    Assert.Equal(edit, await decisions.IsAllowedAsync("part.part", objectId, "edit"));
                    Assert.Equal(delete, await decisions.IsAllowedAsync("part.part", objectId, "delete"));
                    Assert.Equal(authorize, await decisions.IsAllowedAsync("part.part", objectId, "authorize"));
                    var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                    var plan = await plans.GetReadPlanAsync("part.part");
                    var query = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Part")).AsNoTracking()
                        .Where(row => EF.Property<string>(row, "EID") == objectId);
                    if (plan.Predicate is not null) query = query.Where(plan.Predicate);
                    Assert.Equal(readable ? 1 : 0, await query.CountAsync());
                    Assert.Equal(readable ? 1 : 0, (await query.OrderBy(row => EF.Property<string>(row, "EID")).Take(1).ToArrayAsync()).Length);
                    var explanation = await ((DataScopeReadPlans)plans).ExplainAsync("part.part", objectId, accountId, default);
                    foreach (var (key, allowed) in new[] { ("read", readable), ("edit", edit), ("delete", delete), ("authorize", authorize) })
                        Assert.Equal(allowed, explanation.Single(action => action.Key == key).Allowed);
                }
                finally { accessor.HttpContext = previous; }
            }
        }
        finally { foreach (var actor in actors) actor.Client.Dispose(); }
    }
}

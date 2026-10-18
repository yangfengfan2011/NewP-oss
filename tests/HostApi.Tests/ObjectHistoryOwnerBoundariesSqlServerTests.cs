using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class ObjectHistoryOwnerBoundariesSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "两独立owner真实4出口沿自身history资格，Workspace私人出现位置不借EID权限放行且根不开放")]
    public async Task IndependentOwners_UseTheirRealHistoryFeature_AndWorkspaceStillRechecksPrivateSources()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var historyRequests = new ObjectHistoryHttpTestRequests(fixture);
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "IndependentPartHistory296");
        var document = await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "IndependentDocumentHistory296");
        var workspaceHistory = Point(fixture, "workspace.folder.history");
        var partHistory = Point(fixture, "part.part.history");
        var documentHistory = Point(fixture, "document.document.history");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "IndependentHistory296", "独立履历", "independent-history296", "独立履历账号",
            Point(fixture, "workspace.spaces.access"), workspaceHistory, partHistory, documentHistory);
        using var ordinary = actor.Client;
        var filter = new ObjectHistoryFilter(EventType: "entity.ownership.initialized");
        foreach (var (target, owner, feature, otherFeature) in new[] {
            (part, "part", partHistory, documentHistory), (document, "document", documentHistory, partHistory) })
        {
            var path = "/api/" + owner + "/" + owner + "s/" + target.Id + "/history";
            var page = await historyRequests.ReadAsync(ordinary, feature, path + "?eventType=" + filter.EventType);
            Assert.Equal(1, page.GetProperty("total").GetInt32());
            var item = Assert.Single(page.GetProperty("items").EnumerateArray());
            Assert.Equal(target.Name, item.GetProperty("target").GetProperty("name").GetString());
            Assert.Equal("current", item.GetProperty("target").GetProperty("nameSource").GetString());
            var recordId = item.GetProperty("id").GetString();
            using (var detail = await historyRequests.SendAsync(ordinary, feature, HttpMethod.Get, path + "/" + recordId + "?eventType=" + filter.EventType))
                Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            using (var print = await historyRequests.SendAsync(ordinary, feature, HttpMethod.Post, path + "/print", new ObjectHistoryOutputRequest("selected", filter, [recordId!])))
            {
                var printed = await ReadOkAsync(print);
                Assert.Equal(recordId, Assert.Single(printed.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
            }
            using (var exported = await historyRequests.SendAsync(ordinary, feature, HttpMethod.Post, path + "/export", new ObjectHistoryOutputRequest("all", filter)))
            {
                Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
                Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", exported.Content.Headers.ContentType?.MediaType);
            }
            using (var wrongOwner = await historyRequests.SendAsync(ordinary, otherFeature, HttpMethod.Get, path))
                Assert.Equal(HttpStatusCode.Forbidden, wrongOwner.StatusCode);
            using (var noIndependentFeature = await historyRequests.SendAsync(ordinary, workspaceHistory, HttpMethod.Get, path))
                Assert.Equal(HttpStatusCode.Forbidden, noIndependentFeature.StatusCode);
            using (var forgedScope = await historyRequests.SendAsync(ordinary, feature, HttpMethod.Get, path + "?queryScope=forged"))
                Assert.Equal(HttpStatusCode.BadRequest, forgedScope.StatusCode);
            var configuration = new DataScopeObjectDto(owner + "." + owner, target.Id,
                [new("owner-history-" + owner, "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = owner == "part" ? "allow" : "deny", ["history"] = owner == "part" ? "deny" : "allow" })]);
            using (var denied = await historyRequests.SendAsync(admin, Point(fixture, owner + "." + owner + ".authorize"), HttpMethod.Put,
                "/api/" + owner + "/" + owner + "s/" + target.Id + "/permissions", configuration))
                Assert.True(denied.StatusCode == HttpStatusCode.NoContent, await denied.Content.ReadAsStringAsync());
            using var deniedQuery = await historyRequests.SendAsync(ordinary, feature, HttpMethod.Get, path);
            var declaredRead = fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog.Any(item => item.ModuleId == feature.ModuleId)
                && configuration.Subjects[0].Actions["read"] == "allow";
            Assert.Equal(declaredRead ? HttpStatusCode.OK : HttpStatusCode.NotFound, deniedQuery.StatusCode);
        }

        using var ensured = await historyRequests.SendAsync(admin, Point(fixture, "workspace.spaces.access"), HttpMethod.Post,
            "/api/workspace/spaces/personal/ensure");
        var personalRoot = await ReadOkAsync(ensured);
        var privatePosition = new Position(personalRoot.GetProperty("spaceId").GetString()!, personalRoot.GetProperty("rootFolderId").GetString()!, []);
        var privateFolder = await CreateAsync(fixture, admin, privatePosition, "WorkspaceFolder", "folders", "create", "私人履历目录296");
        var privateChildPosition = privatePosition with { LinkIds = [privateFolder.LinkId] };
        var privatePart = await CreateAsync(fixture, admin, privateChildPosition, "Part", "parts", "createPart", "PrivateOccurrenceHistory296");
        // 独立Part使用同一个EID及其ACL；有对象资格仍不能借该EID进入他人私人LID来源。
        var ownPath = "/api/part/parts/" + privatePart.Id + "/history?eventType=" + filter.EventType;
        var privatePartPage = await historyRequests.ReadAsync(ordinary, partHistory, ownPath);
        var privateRecord = Assert.Single(privatePartPage.GetProperty("items").EnumerateArray()).GetProperty("id").GetString();
        var sourcePath = HistoryPath(privateChildPosition, privatePart.LinkId);
        foreach (var suffix in new[] { "", "/" + privateRecord, "/export", "/print" })
        {
            var path = sourcePath.Replace("/history?", "/history" + suffix + "?", StringComparison.Ordinal);
            using var denied = await historyRequests.SendAsync(ordinary, workspaceHistory,
                suffix is "/export" or "/print" ? HttpMethod.Post : HttpMethod.Get, path,
                suffix is "/export" or "/print" ? new ObjectHistoryOutputRequest("all", filter) : null);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
        foreach (var root in new[] { position with { LinkIds = [] }, privatePosition })
        {
            using var rootQuery = await historyRequests.SendAsync(admin, workspaceHistory, HttpMethod.Get, HistoryPath(root, root.RootFolderId));
            Assert.Equal(HttpStatusCode.NotFound, rootQuery.StatusCode);
        }
        using var missingSource = await historyRequests.SendAsync(ordinary, workspaceHistory, HttpMethod.Get,
            "/api/workspace/content/" + privatePart.Id + "/history?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        Assert.Equal(HttpStatusCode.NotFound, missingSource.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "最近三个月默认/显式历史范围、同时间EID倒序、实际操作者与当前英语描述筛选均在Count分页前生效")]
    public async Task Filters_UseThreeMonthDefaultStableEventIdsAndCurrentLanguageDescriptions()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(useRequestPresentation: true, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var historyRequests = new ObjectHistoryHttpTestRequests(fixture);
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "HistoryFilters296");
        var history = Point(fixture, "part.part.history");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "HistoryFilters296", "履历筛选", "history-filters296", "履历筛选账号", history);
        using var ordinary = actor.Client;
        ordinary.DefaultRequestHeaders.Add("X-ICP-Locale", "en-US");
        var now = fixture.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var ids = Enumerable.Range(0, 5).Select(_ => "BusinessChange_" + Guid.NewGuid().ToString("N")).OrderDescending(StringComparer.Ordinal).ToArray();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var changes = db.Set<Dictionary<string, object?>>("BusinessChange");
            var template = await changes.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "Resource") == "part.part"
                && EF.Property<string>(row, "TargetId") == part.Id && EF.Property<string>(row, "EventType") == "part.created");
            for (var index = 0; index < ids.Length; index++)
                changes.Add(new(template) { ["EID"] = ids[index], ["EventType"] = "part.updated",
                    ["OccurredAt"] = index == 4 ? now.AddMonths(-3).AddTicks(-1) : index == 3 ? now.AddMonths(-3) : index == 2 ? now.AddDays(-2) : now.AddDays(-1),
                    ["ActorId"] = index == 0 ? actor.AccountId : index == 2 ? "IdentityAccount_" + Guid.NewGuid().ToString("N") : fixture.ActorId });
            await db.SaveChangesAsync();
        }
        var path = "/api/part/parts/" + part.Id + "/history?eventType=part.updated";
        fixture.Fault.Commands.Clear();
        var first = await historyRequests.ReadAsync(ordinary, history, path + "&pageSize=1");
        Assert.Equal(4, first.GetProperty("total").GetInt32());
        var firstItem = Assert.Single(first.GetProperty("items").EnumerateArray());
        Assert.Equal(ids[0], firstItem.GetProperty("id").GetString());
        Assert.Equal("available", firstItem.GetProperty("actor").GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(firstItem.GetProperty("actor").GetProperty("name").GetString()));
        var count = Assert.Single(fixture.Fault.Commands, sql => sql.Contains("COUNT(*)", StringComparison.Ordinal));
        Assert.Contains("[OccurredAt]", count, StringComparison.Ordinal);
        var second = await historyRequests.ReadAsync(ordinary, history, path + "&pageSize=1&pageIndex=2");
        Assert.Equal(ids[1], Assert.Single(second.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        var missingActor = await historyRequests.ReadAsync(ordinary, history, path + "&pageSize=1&pageIndex=3");
        var unavailable = Assert.Single(missingActor.GetProperty("items").EnumerateArray()).GetProperty("actor");
        Assert.Equal("unavailable", unavailable.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, unavailable.GetProperty("name").ValueKind);
        var allHistory = await historyRequests.ReadAsync(ordinary, history, path + "&from=" + Uri.EscapeDataString(now.AddMonths(-6).ToString("O")));
        Assert.Equal(5, allHistory.GetProperty("total").GetInt32());
        var self = await historyRequests.ReadAsync(ordinary, history, path + "&actorId=" + Uri.EscapeDataString(actor.AccountId));
        Assert.Equal(ids[0], Assert.Single(self.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        var description = await historyRequests.ReadAsync(ordinary, history, path + "&description=" + Uri.EscapeDataString("Object updated"));
        Assert.Equal(4, description.GetProperty("total").GetInt32());
        foreach (var notVisible in new[] { "Part.HistoryUpdated", "修改对象", "Configuration" })
            Assert.Equal(0, (await historyRequests.ReadAsync(ordinary, history, path + "&description=" + Uri.EscapeDataString(notVisible))).GetProperty("total").GetInt32());
    }

    private static string HistoryPath(Position position, string linkId) => "/api/workspace/content/" + linkId + "/history?queryScope="
        + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}

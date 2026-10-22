using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Xunit;
using Xunit.Abstractions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class ObjectHistorySqlServerHttpApiTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "普通Workspace来源Part履历要求独立功能及read/history，精确SQL目标分页且不公开管理员载荷")]
    public async Task WorkspacePartHistory_RequiresHistoryAndRealSource_AndProjectsOnlySafeTargetEvents()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var historyRequests = new ObjectHistoryHttpTestRequests(fixture);
        var history = fixture.FeatureCatalog.FirstOrDefault(point => point.BehaviorKey == "workspace.folder.history");
        Assert.NotNull(history);
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "HistoryPart296");
        _ = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "UnrelatedHistory296");
        var read = fixture.FeatureCatalog.First(point => point.ReadBehaviors.Contains("workspace.folder.read"));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "History296", "普通履历", "history296", "普通履历账号",
            Point(fixture, "workspace.spaces.access"), read, history!);
        using var ordinary = actor.Client;
        var manage = Point(fixture, "part.part.authorize");
        var management = new FeatureHttpTestRequests(fixture).Declaration("part.part.authorize", manage);
        var configuration = new DataScopeObjectDto("part.part", part.Id,
            [new("History296All", "all", null, 1, new Dictionary<string, string> { ["read"] = "allow" })]);
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put,
            "/api/part/parts/" + part.Id + "/permissions", configuration, management))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var scope = Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var path = "/api/workspace/content/" + part.LinkId + "/history?queryScope=" + scope;
        using (var missingFeature = await historyRequests.SendAsync(ordinary, read, HttpMethod.Get, path))
            Assert.Equal(HttpStatusCode.Forbidden, missingFeature.StatusCode);
        using (var adminSurface = await historyRequests.SendAsync(ordinary, history!, HttpMethod.Get,
            "/api/audit/business-changes?resource=part.part&targetId=" + part.Id))
            Assert.Equal(HttpStatusCode.Forbidden, adminSurface.StatusCode);
        fixture.Fault.Commands.Clear();
        var page = await historyRequests.ReadAsync(ordinary, history!, path + "&pageSize=1");
        Assert.Equal(4, page.GetProperty("total").GetInt32());
        var countSql = Assert.Single(fixture.Fault.Commands, sql => sql.Contains("COUNT(*)", StringComparison.Ordinal));
        foreach (var field in new[] { "[EventType]", "[Resource]", "[TargetId]", "[OccurredAt]" }) Assert.Contains(field, countSql, StringComparison.Ordinal);
        Assert.All(fixture.Fault.Commands.Where(sql => sql.StartsWith("SELECT", StringComparison.Ordinal)), sql =>
        {
            // FieldsJson只在固定OPENJSON schema内解码，普通投影与HTTP载荷不搬运原列。
            Assert.DoesNotContain("SELECT [b].[FieldsJson]", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("AS [FieldsJson]", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("[TraceId]", sql, StringComparison.Ordinal);
        });
        var first = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("Part", first.GetProperty("objectType").GetString());
        Assert.Contains(first.GetProperty("target").GetProperty("nameSource").GetString(), new[] { "current", "historical" });
        var json = page.GetRawText();
        foreach (var forbidden in new[] { "FieldsJson", "fieldsJson", "TraceId", "traceId", "Configuration", "configuration", part.Id, actor.AccountId })
            Assert.DoesNotContain(forbidden, json, StringComparison.Ordinal);
        var next = await historyRequests.ReadAsync(ordinary, history!, path + "&pageSize=1&pageIndex=2");
        Assert.Equal(4, next.GetProperty("total").GetInt32());
        var second = Assert.Single(next.GetProperty("items").EnumerateArray());
        Assert.NotEqual(first.GetProperty("id").GetString(), second.GetProperty("id").GetString());
        var complete = await historyRequests.ReadAsync(ordinary, history!, path);
        Assert.Equal(new[] { "dataScope.object.changed", "entity.ownership.initialized", "part.created", "workspace.reference.added" },
            complete.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("eventType").GetString()).Order(StringComparer.Ordinal));
        var filtered = await historyRequests.ReadAsync(ordinary, history!, path + "&eventType=entity.ownership.initialized");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        var allFilters = await historyRequests.ReadAsync(ordinary, history!, path + "&pageIndex=1&pageSize=20&objectType=Part&actorId="
            + Uri.EscapeDataString(fixture.ActorId) + "&eventType=entity.ownership.initialized&from="
            + Uri.EscapeDataString(DateTime.UtcNow.AddMonths(-1).ToString("O")) + "&to=" + Uri.EscapeDataString(DateTime.UtcNow.AddDays(1).ToString("O")));
        Assert.Equal(1, allFilters.GetProperty("total").GetInt32());
        foreach (var invalid in new[] { "&unknown=forged", "&pageSize=1&pageSize=2", "&queryScope=" + scope })
        {
            using var invalidQuery = await historyRequests.SendAsync(ordinary, history!, HttpMethod.Get, path + invalid);
            Assert.Equal(HttpStatusCode.BadRequest, invalidQuery.StatusCode);
        }
        using (var wrongPosition = await historyRequests.SendAsync(ordinary, history!, HttpMethod.Get,
            "/api/workspace/content/" + part.LinkId + "/history?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(position with { LinkIds = [] }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))
            Assert.Equal(HttpStatusCode.NotFound, wrongPosition.StatusCode);
        using (var bareObject = await historyRequests.SendAsync(ordinary, history!, HttpMethod.Get,
            "/api/workspace/content/" + part.Id + "/history?queryScope=" + scope))
            Assert.Equal(HttpStatusCode.NotFound, bareObject.StatusCode);
        using (var denied = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put,
            "/api/part/parts/" + part.Id + "/permissions", configuration with { Subjects = [configuration.Subjects[0] with {
                Actions = new Dictionary<string, string> { ["read"] = "allow" } }] }, management))
            Assert.Equal(HttpStatusCode.NoContent, denied.StatusCode);
        using (var missingHistory = await historyRequests.SendAsync(ordinary, history!, HttpMethod.Get, path))
            Assert.Equal(HttpStatusCode.OK, missingHistory.StatusCode);
        using (var deniedRead = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put,
            "/api/part/parts/" + part.Id + "/permissions", configuration with { Subjects = [configuration.Subjects[0] with {
                Actions = new Dictionary<string, string> { ["read"] = "deny" } }] }, management))
            Assert.Equal(HttpStatusCode.NoContent, deniedRead.StatusCode);
        using (var missingRead = await historyRequests.SendAsync(ordinary, history!, HttpMethod.Get, path))
            Assert.Equal(HttpStatusCode.NotFound, missingRead.StatusCode);
        using var retained = await historyRequests.SendAsync(admin, Point(fixture, "audit.change.read"), HttpMethod.Get,
            "/api/audit/business-changes/" + first.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "固定正式裁剪Host与native包实际消费普通Workspace到Part履历，不借独立Part或管理员审计资格")]
    public async Task WorkspacePartHistory_FormalNativeHostConsumesDeclaredPoliciesAndIndependentFeature()
    {
        var inputPath = Environment.GetEnvironmentVariable("NEWP_ACCEPTANCE_WORKSPACE_DELIVERY_INPUT")
            ?? throw new InvalidOperationException("正式履历首片需要已登记发行物。");
        using var input = JsonDocument.Parse(await File.ReadAllTextAsync(inputPath));
        var package = input.RootElement.GetProperty("packagePath").GetString()!;
        Assert.Equal(input.RootElement.GetProperty("packageSha256").GetString(),
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(package))));
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([], async owned =>
        {
            await EntityOwnershipMigrationSqlServerTests.DeployAsync(owned, "install-native", package);
            await EntityOwnershipMigrationSqlServerTests.DeployAsync(owned, "enable-bundle", package);
        }, sourceHostRoot: input.RootElement.GetProperty("hostRoot").GetString()!);
        var http = new FeatureHttpTestRequests(fixture);
        var admin = fixture.Administrator;
        var historyRequests = await ObjectHistoryHttpTestRequests.LoadAsync(admin);
        var entry = fixture.Catalog.Single(point => point.BehaviorKey == "workspace.spaces.access");
        var read = fixture.Catalog.First(point => point.ReadBehaviors.Contains("workspace.folder.read"));
        var history = fixture.Catalog.First(point => point.BehaviorKey == "workspace.folder.history");
        await http.InitializeOrganizationAsync(admin);
        var root = await Post("workspace.spaces.access", "/api/workspace/spaces/public/initialize");
        var rootPosition = new Position(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
        var container = await Post("workspace.folder.create", "/api/workspace/content/folders",
            new { position = rootPosition, values = new { ENAME = "正式履历目录" } });
        var containerId = container.GetProperty("id").GetString()!;
        var rootRows = await Rows(rootPosition, rootPosition.RootFolderId);
        var containerLink = Assert.Single(rootRows.GetProperty("items").EnumerateArray(), row =>
            row.GetProperty("associatedObject").GetProperty("id").GetString() == containerId).GetProperty("linkId").GetString()!;
        var position = rootPosition with { LinkIds = [containerLink] };
        await Post("workspace.folder.createPart", "/api/workspace/content/parts",
            new { position, values = new { ECODE = "HistoryFormal296", ENAME = "正式履历零组件" } });
        var row = Assert.Single((await Rows(position, containerId)).GetProperty("items").EnumerateArray());
        var partId = row.GetProperty("associatedObject").GetProperty("id").GetString()!;
        var linkId = row.GetProperty("linkId").GetString()!;
        using (var saved = await historyRequests.SendAsync(admin, fixture.Catalog.First(point => point.BehaviorKey == "part.part.authorize"),
            HttpMethod.Put, "/api/part/parts/" + partId + "/permissions", new DataScopeObjectDto("part.part", partId,
                [new("FormalHistory296", "all", null, 1, new Dictionary<string, string> { ["read"] = "allow" })])))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var roleCode = "History296" + suffix;
        var role = await http.CreateButtonAsync(admin, http.Role("create"), "/api/authorization/roles", new { Code = roleCode, Name = "普通履历角色" });
        var accountCode = "history296-" + suffix;
        var account = await http.CreateButtonAsync(admin, http.Identity("create"), "/api/identity/accounts", new { ECODE = accountCode, ENAME = "普通履历账号" });
        var accountId = account.GetProperty("account").GetProperty("eid").GetString()!;
        await HttpTestActors.PrepareOrganizationAsync(admin, http, account.GetProperty("account"));
        using (var assigned = await historyRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put,
            "/api/authorization/users/" + accountId + "/roles", new { RoleCodes = new[] { roleCode } }))
            Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        await http.GrantAsync(admin, role.GetProperty("id").GetString()!, entry, read, history);
        var credential = account.GetProperty("temporaryCredential").GetString()!;
        using var ordinary = await fixture.LoginAsync(accountCode, credential);
        using var before = await ordinary.GetFromJsonAsync<JsonDocument>("/api/auth/current-user");
        using (var password = await ordinary.PostAsJsonAsync("/api/identity/me/password-change", new {
            CurrentPassword = credential, NewPassword = "FormalHistory296-secure-!", ExpectedRevision = before!.RootElement.GetProperty("revision").GetInt64() }))
            Assert.True(password.IsSuccessStatusCode, await password.Content.ReadAsStringAsync());
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(ordinary);
        using var current = await ordinary.GetFromJsonAsync<JsonDocument>("/api/auth/current-user");
        var granted = current!.RootElement.GetProperty("featurePositions").EnumerateArray().Select(value => value.GetString()).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(fixture.Catalog.Where(point => point.ModuleId is "Part" or "Audit"), point => granted.Contains(point.Position));
        var path = "/api/workspace/content/" + linkId + "/history?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using (var wrongFeature = await historyRequests.SendAsync(ordinary, read, HttpMethod.Get, path))
            Assert.Equal(HttpStatusCode.Forbidden, wrongFeature.StatusCode);
        var page = await historyRequests.ReadAsync(ordinary, history, path);
        Assert.Equal(4, page.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "dataScope.object.changed", "entity.ownership.initialized", "part.created", "workspace.reference.added" },
            page.GetProperty("items").EnumerateArray().Select(value => value.GetProperty("eventType").GetString()).Order(StringComparer.Ordinal));
        foreach (var forbidden in new[] { "traceId", "fieldsJson", "configuration", partId, accountId })
            Assert.DoesNotContain(forbidden, page.GetRawText(), StringComparison.Ordinal);
        using var adminOnly = await historyRequests.SendAsync(ordinary, history, HttpMethod.Get, "/api/audit/business-changes");
        Assert.Equal(HttpStatusCode.Forbidden, adminOnly.StatusCode);

        async Task<JsonElement> Post(string behavior, string route, object? body = null)
        {
            using var response = await historyRequests.SendAsync(admin, fixture.Catalog.First(point => point.BehaviorKey == behavior), HttpMethod.Post, route, body);
            return await ReadOkAsync(response);
        }
        Task<JsonElement> Rows(Position source, string anchor) => historyRequests.ReadAsync(admin, entry,
            "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId=" + Uri.EscapeDataString(anchor)
            + "&queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(new { source.SpaceId, source.RootFolderId, linkIds = JsonSerializer.Serialize(source.LinkIds) }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    }


    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "旧removed事件在原Link已删除后仍精确关联Part及原父，同一事件不复制且不泄漏原关联ID")]
    public async Task RemovedReference_OldFieldFactsAssociateTheSameEventWithPartAndParentAfterLinkDeletion()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var historyRequests = new ObjectHistoryHttpTestRequests(fixture);
        await InitializeAsync(fixture, admin);
        var first = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, first, "Part", "parts", "createPart", "RemovedHistoryPart296");
        var second = await CreateAsync(fixture, admin, first with { LinkIds = [] }, "WorkspaceFolder", "folders", "create", "剩余履历目录296");
        var secondPosition = first with { LinkIds = [second.LinkId] };
        using (var added = await historyRequests.SendAsync(admin, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
            "/api/workspace/content/references", new { position = secondPosition, target = new { modelKey = "Part", id = part.Id } }))
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        string parentId, remainingLink;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var links = db.Set<Dictionary<string, object?>>("WorkspaceFolderContent");
            parentId = (string)(await links.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "LID") == first.LinkIds[0]))["CEID"]!;
            remainingLink = (string)(await links.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "PEID") == second.Id
                && EF.Property<string>(row, "CEID") == part.Id))["LID"]!;
        }
        using (var removed = await historyRequests.SendAsync(admin, Point(fixture, "workspace.folder.remove"), HttpMethod.Delete,
            "/api/workspace/content/references/" + part.LinkId, new { position = first }))
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using (var scope = fixture.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent")
                .AnyAsync(row => EF.Property<string>(row, "LID") == part.LinkId));
        // 先核实未改旧writer的真实存储语义；只有该前置成立，后面的缺失才是旧事件关联反例。
        var legacy = Assert.Single((await historyRequests.ReadAsync(admin, Point(fixture, "audit.change.read"),
            "/api/audit/business-changes?eventType=workspace.reference.removed&targetId=" + Uri.EscapeDataString(part.LinkId))).GetProperty("items").EnumerateArray());
        Assert.Equal("workspace.content", legacy.GetProperty("Resource").GetString());
        Assert.Equal(part.LinkId, legacy.GetProperty("TargetId").GetString());
        var fields = legacy.GetProperty("Changes").EnumerateArray().ToDictionary(field => field.GetProperty("field").GetString()!);
        Assert.Equal(3, fields.Count);
        Assert.Equal("workspace.folder", fields["PEID"].GetProperty("before").GetProperty("kind").GetString());
        Assert.Equal(parentId, fields["PEID"].GetProperty("before").GetProperty("value").GetString());
        Assert.Equal("Part", fields["CEID"].GetProperty("before").GetProperty("kind").GetString());
        Assert.Equal(part.Id, fields["CEID"].GetProperty("before").GetProperty("value").GetString());
        Assert.Equal("Part", fields["ChildModelKey"].GetProperty("before").GetProperty("value").GetString());
        foreach (var field in fields.Values) Assert.Equal(JsonValueKind.Null, field.GetProperty("after").GetProperty("value").ValueKind);
        var eventId = legacy.GetProperty("EID").GetString();
        output.WriteLine(JsonSerializer.Serialize(new { eventId, legacyResource = "workspace.content", legacyTargetId = part.LinkId, fieldFacts = legacy.GetProperty("Changes") }));
        var history = Point(fixture, "workspace.folder.history");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "RemovedHistory296", "旧关系履历", "removed-history296", "旧关系履历账号",
            Point(fixture, "workspace.spaces.access"), history);
        using var ordinary = actor.Client;
        fixture.Fault.Commands.Clear();
        var page = await historyRequests.ReadAsync(ordinary, history, HistoryPath(secondPosition, remainingLink));
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Equal(eventId, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        var countSql = Assert.Single(fixture.Fault.Commands, sql => sql.Contains("COUNT(*)", StringComparison.Ordinal));
        Assert.Contains("OPENJSON", countSql, StringComparison.Ordinal);

        var detailPath = "/api/workspace/content/" + remainingLink + "/history/" + eventId + "?queryScope="
            + Uri.EscapeDataString(JsonSerializer.Serialize(secondPosition, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var detail = await historyRequests.ReadAsync(ordinary, history, detailPath);
        Assert.Equal(eventId, detail.GetProperty("id").GetString());
        Assert.Equal("Part", detail.GetProperty("objectType").GetString());
        using (var wrongType = await historyRequests.SendAsync(ordinary, history, HttpMethod.Get, detailPath + "&objectType=WorkspaceFolder"))
            Assert.Equal(HttpStatusCode.NotFound, wrongType.StatusCode);
        var unrelated = await CreateAsync(fixture, admin, secondPosition, "Part", "parts", "createPart", "OtherHistoryPart296");
        var unrelatedRecord = Assert.Single((await historyRequests.ReadAsync(admin, Point(fixture, "audit.change.read"),
            "/api/audit/business-changes?resource=part.part&eventType=entity.ownership.initialized&targetId=" + Uri.EscapeDataString(unrelated.Id))).GetProperty("items").EnumerateArray());
        using (var forged = await historyRequests.SendAsync(ordinary, history, HttpMethod.Get,
            "/api/workspace/content/" + remainingLink + "/history/" + unrelatedRecord.GetProperty("EID").GetString() + "?queryScope="
                + Uri.EscapeDataString(JsonSerializer.Serialize(secondPosition, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))
            Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
        var parent = await historyRequests.ReadAsync(ordinary, history, HistoryPath(first with { LinkIds = [] }, first.LinkIds[0]));
        Assert.Equal(1, parent.GetProperty("total").GetInt32());
        var parentItem = Assert.Single(parent.GetProperty("items").EnumerateArray());
        Assert.Equal(eventId, parentItem.GetProperty("id").GetString());
        Assert.Equal("Part", parentItem.GetProperty("objectType").GetString());
        Assert.Equal(1, (await historyRequests.ReadAsync(ordinary, history, HistoryPath(first with { LinkIds = [] }, first.LinkIds[0]) + "&objectType=Part")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await historyRequests.ReadAsync(ordinary, history, HistoryPath(first with { LinkIds = [] }, first.LinkIds[0]) + "&objectType=WorkspaceFolder")).GetProperty("total").GetInt32());
        foreach (var json in new[] { page.GetRawText(), parent.GetRawText() })
            foreach (var hidden in new[] { part.LinkId, remainingLink, part.Id, parentId, "traceId", "fieldsJson", "configuration" })
                Assert.DoesNotContain(hidden, json, StringComparison.Ordinal);

        static string HistoryPath(Position source, string linkId) => "/api/workspace/content/" + linkId + "/history?queryScope="
            + Uri.EscapeDataString(JsonSerializer.Serialize(source, new JsonSerializerOptions(JsonSerializerDefaults.Web))) + "&eventType=workspace.reference.removed";
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "可信连接IP进入同一安全履历，伪造转发头不生效，旧缺失保留null并在SQL筛选")]
    public async Task HistoryIp_UsesConnectionAddressAndPreservesUnrecordedFacts()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddSingleton<IStartupFilter, ConnectionAddressStartupFilter>(), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var historyRequests = new ObjectHistoryHttpTestRequests(fixture);
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        admin.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.99");
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "HistoryIP296");
        var auditPoint = Point(fixture, "audit.change.read");
        var created = Assert.Single((await historyRequests.ReadAsync(admin, auditPoint,
            "/api/audit/business-changes?eventType=entity.ownership.initialized&targetId=" + part.Id)).GetProperty("items").EnumerateArray());
        Assert.True(created.TryGetProperty("IpAddress", out var storedIp));
        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(storedIp.GetString()!)));
        Assert.NotEqual("203.0.113.99", storedIp.GetString());
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            // 代表没有采集IP的既有事实；不补造地址，其余历史事实保持原值。
            await db.Set<Dictionary<string, object?>>("BusinessChange")
                .Where(row => EF.Property<string>(row, "EID") == created.GetProperty("EID").GetString())
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string?>(row, "IpAddress"), (string?)null));
        }
        var history = Point(fixture, "workspace.folder.history");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "HistoryIP296", "IP履历", "history-ip296", "IP履历账号",
            Point(fixture, "workspace.spaces.access"), history);
        using var ordinary = actor.Client;
        var configuration = new DataScopeObjectDto("part.part", part.Id,
            [new("history-ip", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = "allow" })]);
        using (var saved = await historyRequests.SendAsync(admin, Point(fixture, "part.part.authorize"), HttpMethod.Put,
            "/api/part/parts/" + part.Id + "/permissions", configuration)) Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var path = "/api/workspace/content/" + part.LinkId + "/history?queryScope="
            + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var page = await historyRequests.ReadAsync(ordinary, history, path);
        Assert.Equal(4, page.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, Assert.Single(page.GetProperty("items").EnumerateArray(), row =>
            row.GetProperty("eventType").GetString() == "entity.ownership.initialized").GetProperty("ipAddress").ValueKind);
        fixture.Fault.Commands.Clear();
        var filtered = await historyRequests.ReadAsync(ordinary, history, path + "&ipAddress=" + Uri.EscapeDataString(storedIp.GetString()!));
        Assert.Equal(3, filtered.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "dataScope.object.changed", "part.created", "workspace.reference.added" },
            filtered.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("eventType").GetString()).Order(StringComparer.Ordinal));
        Assert.All(filtered.GetProperty("items").EnumerateArray(), item => Assert.Equal(storedIp.GetString(), item.GetProperty("ipAddress").GetString()));
        Assert.Contains("[IpAddress]", Assert.Single(fixture.Fault.Commands, sql => sql.Contains("COUNT(*)", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Equal(0, (await historyRequests.ReadAsync(ordinary, history, path + "&ipAddress=203.0.113.99")).GetProperty("total").GetInt32());
        foreach (var invalid in new[] { "&ipAddress=not-an-ip", "&ipAddress=127.0.0.1&ipAddress=127.0.0.2" })
        {
            using var rejected = await historyRequests.SendAsync(ordinary, history, HttpMethod.Get, path + invalid);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
    }

    // TestServer没有TCP连接；只在此场景的真实请求连接feature准备可信来源，不伪装转发头。
    private sealed class ConnectionAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                return continuation();
            });
            next(app);
        };
    }

}

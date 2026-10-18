using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;
using static ICP.Platform.HostApi.Tests.EntityOwnershipTransferSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipTransferReceivingSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "私人/失效/裸EID来源拒绝，批量任一edit或当前所属人不符全拒绝，普通接收Folder引用扩张范围但不越过显式deny")]
    public async Task PrivateFolderSourceAndMixedQualificationFailClosed_WhileNormalReceivingReferencesRespectReadDeny()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var read = Point(fixture, "workspace.spaces.access"); var transfer = Point(fixture, "workspace.folder.transfer");
        var notices = Point(fixture, "workspace.transfers.read");
        var manage = fixture.FeatureCatalog.Single(point => point.ModuleId == "Workspace" && point.Key == "authorizeContent" && point.ActionScope == "list.rowActions");
        var owner = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PrivateTransferOwner", "私人转交人",
            "private-transfer-owner", "私人转交人", read, transfer, manage, Point(fixture, "workspace.folder.create"));
        var recipient = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PrivateTransferRecipient", "私人接收人",
            "private-transfer-recipient", "私人接收人", read, transfer, notices);
        using var ownerClient = owner.Client; using var recipientClient = recipient.Client;
        var position = await PersonalAsync(fixture, ownerClient);
        var first = await CreateAsync(fixture, ownerClient, position, "WorkspaceFolder", "folders", "create", "PRIVATE-RECEIVED-ORIGINAL");
        var second = await CreateAsync(fixture, ownerClient, position, "WorkspaceFolder", "folders", "create", "PRIVATE-UNCHANGED");
        await ConfigureAsync(first, "allow"); await ConfigureAsync(second, "allow");
        using (var hidden = await FeatureHttpTestRequests.SendAsync(recipientClient, read, HttpMethod.Get, "/api/workspace/folders/" + first.Id))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        var models = new[] { "WorkspaceSpace", "WorkspaceFolder", "WorkspaceFolderContent", "WorkspaceTransferNotification", "BusinessChange" };
        var beforeInvalid = await SnapshotAsync(fixture, models);
        foreach (var selected in new[] { new[] { first.Id }, new[] { "missing-reference" } })
        {
            using var invalid = await TransferAsync(fixture, ownerClient, position, selected, recipient.AccountId);
            Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        }
        using (var privateSource = await TransferAsync(fixture, recipientClient, position, [first.LinkId], recipient.AccountId))
            Assert.Equal(HttpStatusCode.NotFound, privateSource.StatusCode);
        Assert.Equal(beforeInvalid, await SnapshotAsync(fixture, models));
        await ConfigureAsync(second, "deny");
        var beforeDenied = await SnapshotAsync(fixture, models);
        using (var denied = await TransferAsync(fixture, ownerClient, position, [first.LinkId, second.LinkId], recipient.AccountId))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(beforeDenied, await SnapshotAsync(fixture, models));
        await ConfigureAsync(second, "allow");
        using (var received = await TransferAsync(fixture, ownerClient, position, [first.LinkId], recipient.AccountId))
            Assert.Equal(1, (await ReadOkAsync(received)).GetProperty("changed").GetInt32());
        var beforeOwnerMismatch = await SnapshotAsync(fixture, models);
        using (var mixedOwner = await TransferAsync(fixture, ownerClient, position, [first.LinkId, second.LinkId], recipient.AccountId))
            Assert.Equal(HttpStatusCode.Forbidden, mixedOwner.StatusCode);
        Assert.Equal(beforeOwnerMismatch, await SnapshotAsync(fixture, models));
        // 同一EID在新的普通边可达；原私人位置仍然不是接收人的可用来源。
        using (var reached = await FeatureHttpTestRequests.SendAsync(recipientClient, read, HttpMethod.Get, "/api/workspace/folders/" + first.Id))
            Assert.Equal(first.Id, (await ReadOkAsync(reached)).GetProperty("id").GetString());
        using (var originalPrivate = await FeatureHttpTestRequests.SendAsync(recipientClient, read, HttpMethod.Get, Properties(position, first.LinkId)))
            Assert.Equal(HttpStatusCode.NotFound, originalPrivate.StatusCode);
        var page = await FeatureHttpTestRequests.ReadAsync(recipientClient, notices, "/api/workspace/transfers/notifications");
        var location = Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("location");
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var linkId = location.GetProperty("sourceLinkId").GetString()!;
        var link = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().SingleAsync(row => EF.Property<string>(row, "LID") == linkId);
        Assert.Equal(false, link["IsScopeNeutral"]); Assert.Equal(first.Id, link["CEID"]);
        await ConfigureAsync(first, "allow", recipient.AccountId);
        using (var denied = await FeatureHttpTestRequests.SendAsync(recipientClient, read, HttpMethod.Get, "/api/workspace/folders/" + first.Id))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var hiddenPage = await FeatureHttpTestRequests.ReadAsync(recipientClient, notices, "/api/workspace/transfers/notifications");
        var hiddenItem = Assert.Single(hiddenPage.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, hiddenItem.GetProperty("targetName").ValueKind);
        Assert.Equal(JsonValueKind.Null, hiddenItem.GetProperty("location").ValueKind);
        Assert.DoesNotContain(first.Id, hiddenPage.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(first.Name, hiddenPage.GetRawText(), StringComparison.Ordinal);

        async Task ConfigureAsync(Target target, string edit, string? deniedRecipient = null)
        {
            var subjects = new List<DataScopeDirectSubjectDto> { new("private-all-" + target.Id, "all", null, 0,
                new Dictionary<string, string> { ["read"] = "allow", ["edit"] = edit, ["authorize"] = "allow" }) };
            if (deniedRecipient is not null) subjects.Add(new("private-deny-" + target.Id, "user", deniedRecipient, 0, new Dictionary<string, string> { ["read"] = "deny" }));
            using var configured = await FeatureHttpTestRequests.SendAsync(ownerClient, manage, HttpMethod.Put, Permissions(position, target.LinkId),
                new DataScopeObjectDto("workspace.folder", target.Id, subjects));
            Assert.True(configured.StatusCode == HttpStatusCode.NoContent, await configured.Content.ReadAsStringAsync());
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "同分钟同名接收目录复用、原目录已有对象不阻止接收、往返真实转交保既有接收引用不重复")]
    public async Task SameMinuteReceivingReusesDirectoryAndReference_WithoutSkippingAnObjectReferencedElsewhere()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FrozenClock())), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var read = Point(fixture, "workspace.spaces.access"); var transfer = Point(fixture, "workspace.folder.transfer");
        var notices = Point(fixture, "workspace.transfers.read");
        var owner = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ReuseTransferOwner", "原所属人",
            "reuse-transfer-owner", "原所属人", read, transfer, Point(fixture, "workspace.folder.createPart"));
        var recipient = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ReuseTransferRecipient", "接收人",
            "reuse-transfer-recipient", "接收人", read, transfer, notices, Point(fixture, "workspace.folder.create"), Point(fixture, "workspace.folder.add"));
        using var ownerClient = owner.Client; using var recipientClient = recipient.Client;
        var first = await CreateAsync(fixture, ownerClient, position, "Part", "parts", "createPart", "REUSE-TRANSFER-FIRST");
        var second = await CreateAsync(fixture, ownerClient, position, "Part", "parts", "createPart", "REUSE-TRANSFER-SECOND");
        var personal = await PersonalAsync(fixture, recipientClient);
        var elsewhere = await CreateAsync(fixture, recipientClient, personal, "WorkspaceFolder", "folders", "create", "另一个已有引用的目录");
        using (var added = await FeatureHttpTestRequests.SendAsync(recipientClient, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
                   "/api/workspace/content/references", new { position = personal with { LinkIds = [elsewhere.LinkId] }, target = new { modelKey = "Part", id = first.Id } }))
            await ReadOkAsync(added);
        using (var received = await TransferAsync(fixture, ownerClient, position, [first.LinkId], recipient.AccountId))
            Assert.Equal(1, (await ReadOkAsync(received)).GetProperty("changed").GetInt32());
        var firstNotice = Assert.Single((await FeatureHttpTestRequests.ReadAsync(recipientClient, notices, "/api/workspace/transfers/notifications")).GetProperty("items").EnumerateArray());
        var location = firstNotice.GetProperty("location");
        var receivingPosition = JsonSerializer.Deserialize<Position>(location.GetProperty("position"), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var receivingLink = location.GetProperty("sourceLinkId").GetString()!;
        using (var received = await TransferAsync(fixture, ownerClient, position, [second.LinkId], recipient.AccountId))
            Assert.Equal(1, (await ReadOkAsync(received)).GetProperty("changed").GetInt32());
        using (var returned = await TransferAsync(fixture, recipientClient, receivingPosition, [receivingLink], owner.AccountId))
            Assert.Equal(1, (await ReadOkAsync(returned)).GetProperty("changed").GetInt32());
        using (var receivedAgain = await TransferAsync(fixture, ownerClient, position, [first.LinkId], recipient.AccountId))
            Assert.Equal(1, (await ReadOkAsync(receivedAgain)).GetProperty("changed").GetInt32());
        var page = await FeatureHttpTestRequests.ReadAsync(recipientClient, notices, "/api/workspace/transfers/notifications");
        var items = page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, items.Length);
        Assert.All(items, item => Assert.Equal(location.GetProperty("position").GetRawText(), item.GetProperty("location").GetProperty("position").GetRawText()));
        Assert.Equal(2, items.Count(item => item.GetProperty("location").GetProperty("sourceLinkId").GetString() == receivingLink));
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var links = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().ToArrayAsync();
        var directoryLink = links.Single(link => Equals(link["LID"], receivingPosition.LinkIds.Single()));
        var directory = (string)directoryLink["CEID"]!;
        Assert.Single(links, link => Equals(link["PEID"], directory) && Equals(link["CEID"], first.Id));
        Assert.Single(links, link => Equals(link["PEID"], directory) && Equals(link["CEID"], second.Id));
        Assert.Single(links, link => Equals(link["PEID"], elsewhere.Id) && Equals(link["CEID"], first.Id));
        var folder = await db.Set<Dictionary<string, object?>>("WorkspaceFolder").AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == directory);
        var sameName = await db.Set<Dictionary<string, object?>>("WorkspaceFolder").AsNoTracking().Where(row => EF.Property<string>(row, "ENAME") == (string)folder["ENAME"]!).ToArrayAsync();
        Assert.Single(sameName);
        var events = await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
            .Where(row => EF.Property<string>(row, "EventType") == "workspace.reference.added" && EF.Property<string>(row, "TargetId") == receivingLink).ToArrayAsync();
        Assert.Single(events);
    }

    private static async Task<Position> PersonalAsync(BusinessAuditHostFixture fixture, HttpClient client)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.spaces.access"), HttpMethod.Post, "/api/workspace/spaces/personal/ensure");
        var root = await ReadOkAsync(response);
        return new(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
    }
    private static Task<HttpResponseMessage> TransferAsync(BusinessAuditHostFixture fixture, HttpClient client, Position position, string[] linkIds, string recipientId) =>
        FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder.transfer"), HttpMethod.Post, "/api/workspace/ownership/transfer", new { position, linkIds, recipientId });
    private static string Properties(Position position, string linkId) => "/api/workspace/content/" + linkId + "/properties?queryScope="
        + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    private static string Permissions(Position position, string linkId) => Properties(position, linkId).Replace("/properties?", "/permissions?", StringComparison.Ordinal);
    private sealed class FrozenClock : TimeProvider
    {
        private readonly DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
    }
}

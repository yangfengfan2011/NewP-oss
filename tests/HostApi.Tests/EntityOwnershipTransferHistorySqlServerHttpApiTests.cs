using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipTransferHistorySqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "三owner转交与接收引用进入普通安全履历，原/接收来源精确同记录去重，隐藏关联不泄露名称或ID")]
    public async Task ThreeOwners_TransferAndReceivingReferencesUseSafeHistory_WithoutHiddenTargetLeaks()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var read = Point(fixture, "workspace.spaces.access");
        var history = Point(fixture, "workspace.folder.history");
        var transfer = Point(fixture, "workspace.folder.transfer");
        var notices = Point(fixture, "workspace.transfers.read");
        var owner = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "TransferHistoryOwner", "履历转交人",
            "transfer-history-owner", "原所属人", read, history, transfer,
            Point(fixture, "workspace.folder.create"), Point(fixture, "workspace.folder.createPart"), Point(fixture, "workspace.folder.createDocument"));
        var recipient = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "TransferHistoryRecipient", "履历接收人",
            "transfer-history-recipient", "接收人", read, history, notices);
        using var ownerClient = owner.Client; using var recipientClient = recipient.Client;
        var targets = new List<Target>();
        foreach (var (model, kind, action) in Kinds)
        {
            var target = await CreateAsync(fixture, ownerClient, position, model, kind, action, "TRANSFER-HISTORY-SECRET-" + model);
            targets.Add(target);
            var resource = model == "WorkspaceFolder" ? "workspace.folder" : model.ToLowerInvariant() + "." + model.ToLowerInvariant();
            var manage = fixture.FeatureCatalog.Single(point => point.ModuleId == (model == "WorkspaceFolder" ? "Workspace" : model)
                && point.Key == (model == "WorkspaceFolder" ? "authorizeContent" : "authorize") && point.ActionScope == "list.rowActions");
            var permissions = model == "WorkspaceFolder" ? Source(position, target.LinkId, "permissions")
                : "/api/" + model.ToLowerInvariant() + "/" + kind + "/" + target.Id + "/permissions";
            using var configured = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, permissions,
                new DataScopeObjectDto(resource, target.Id, [new("history-all-" + model, "all", null, 0,
                    new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow" })]));
            Assert.True(configured.StatusCode == HttpStatusCode.NoContent, await configured.Content.ReadAsStringAsync());
        }
        using (var transferred = await FeatureHttpTestRequests.SendAsync(ownerClient, transfer, HttpMethod.Post, "/api/workspace/ownership/transfer",
                   new { position, linkIds = targets.Select(target => target.LinkId).ToArray(), recipientId = recipient.AccountId }, OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer)))
            Assert.Equal(3, (await ReadOkAsync(transferred)).GetProperty("changed").GetInt32());
        var notifications = await FeatureHttpTestRequests.ReadAsync(recipientClient, notices, "/api/workspace/transfers/notifications");
        var locations = notifications.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("location").Clone()).ToArray();
        Assert.Equal(3, locations.Length);
        foreach (var target in targets)
        {
            var path = Source(position, target.LinkId, "history") + "&eventType=entity.ownership.transferred";
            var original = await new ObjectHistoryHttpTestRequests(fixture).ReadAsync(ownerClient, history, path);
            Assert.Equal(1, original.GetProperty("total").GetInt32());
            var item = Assert.Single(original.GetProperty("items").EnumerateArray());
            Assert.Equal("entity.ownership.transferred", item.GetProperty("eventType").GetString());
            Assert.Equal(target.Model, item.GetProperty("objectType").GetString());
            var prefix = target.Model == "WorkspaceFolder" ? "Workspace" : target.Model;
            Assert.Equal(prefix + ".HistoryOwnershipTransferred", item.GetProperty("description").GetProperty("key").GetString());
            foreach (var forbidden in new[] { target.Id, owner.AccountId, recipient.AccountId, "fieldsJson", "changes", "traceId", "OwnerId" })
                Assert.DoesNotContain(forbidden, original.GetRawText(), StringComparison.Ordinal);
            var location = Assert.Single(locations, value => value.GetProperty("objectId").GetString() == target.Id);
            var receivedPath = "/api/workspace/content/" + location.GetProperty("sourceLinkId").GetString() + "/history?queryScope="
                + Uri.EscapeDataString(location.GetProperty("position").GetRawText()) + "&eventType=entity.ownership.transferred";
            var received = await new ObjectHistoryHttpTestRequests(fixture).ReadAsync(recipientClient, history, receivedPath);
            Assert.Equal(1, received.GetProperty("total").GetInt32());
            Assert.Equal(item.GetProperty("id").GetString(), Assert.Single(received.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        }
        // 接收目录按真实父关系查询：关联事件保留自己的记录键，不泄露已不可读的对象身份。
        var receivedPosition = locations[0].GetProperty("position");
        var directoryLink = Assert.Single(receivedPosition.GetProperty("linkIds").EnumerateArray()).GetString()!;
        var rootPosition = new Position(receivedPosition.GetProperty("spaceId").GetString()!, receivedPosition.GetProperty("rootFolderId").GetString()!, []);
        var directoryPath = Source(rootPosition, directoryLink, "history") + "&eventType=workspace.reference.added&objectType=Part";
        var directoryHistory = await new ObjectHistoryHttpTestRequests(fixture).ReadAsync(recipientClient, history, directoryPath);
        var relation = Assert.Single(directoryHistory.GetProperty("items").EnumerateArray());
        var part = targets.Single(target => target.Model == "Part");
        Assert.Equal(part.Name, relation.GetProperty("target").GetProperty("name").GetString());
        var partManage = fixture.FeatureCatalog.Single(point => point.ModuleId == "Part" && point.Key == "authorize" && point.ActionScope == "list.rowActions");
        using (var deny = await FeatureHttpTestRequests.SendAsync(admin, partManage, HttpMethod.Put, "/api/part/parts/" + part.Id + "/permissions",
                   new DataScopeObjectDto("part.part", part.Id, [new("deny-receiver", "user", recipient.AccountId, 0,
                       new Dictionary<string, string> { ["read"] = "deny" })])))
            Assert.True(deny.StatusCode == HttpStatusCode.NoContent, await deny.Content.ReadAsStringAsync());
        var hidden = await new ObjectHistoryHttpTestRequests(fixture).ReadAsync(recipientClient, history, directoryPath);
        var hiddenRelation = Assert.Single(hidden.GetProperty("items").EnumerateArray());
        Assert.Equal(relation.GetProperty("id").GetString(), hiddenRelation.GetProperty("id").GetString());
        Assert.Equal("unavailable", hiddenRelation.GetProperty("target").GetProperty("nameSource").GetString());
        Assert.Equal(JsonValueKind.Null, hiddenRelation.GetProperty("target").GetProperty("name").ValueKind);
        Assert.DoesNotContain(part.Id, hidden.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(part.Name, hidden.GetRawText(), StringComparison.Ordinal);
    }

    private static string Source(Position position, string linkId, string surface) => "/api/workspace/content/" + linkId + "/" + surface
        + "?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}

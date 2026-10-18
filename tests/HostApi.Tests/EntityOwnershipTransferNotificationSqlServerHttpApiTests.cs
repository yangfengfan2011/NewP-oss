using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipTransferNotificationSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "接收通知仅当前接收人可见，安全位置可打开原对象，失权后既有位置及刷新列表均不泄漏")]
    public async Task SafeNotificationLocation_OpensTheOriginalObject_AndIsRemovedWhenReadIsRevoked()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var browse = Point(fixture, "workspace.spaces.access");
        var transfer = Point(fixture, "workspace.folder.transfer");
        var notifications = Point(fixture, "workspace.transfers.read");
        var owner = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "NotifyTransferOwner", "通知转交所属人", "notify-owner", "原所属人",
            browse, notifications, transfer, Point(fixture, "workspace.folder.create"));
        var recipient = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "NotifyTransferRecipient", "通知接收人", "notify-recipient", "接收人", browse, notifications);
        using var ownerClient = owner.Client; using var recipientClient = recipient.Client;
        var target = await CreateAsync(fixture, ownerClient, position, "WorkspaceFolder", "folders", "create", "NOTIFY-PRIVATE-TARGET");
        using (var transferred = await FeatureHttpTestRequests.SendAsync(ownerClient, transfer, HttpMethod.Post, "/api/workspace/ownership/transfer",
                   new { position, linkIds = new[] { target.LinkId }, recipientId = recipient.AccountId }))
            Assert.Equal(1, (await ReadOkAsync(transferred)).GetProperty("changed").GetInt32());
        var received = await FeatureHttpTestRequests.ReadAsync(recipientClient, notifications, "/api/workspace/transfers/notifications");
        var notification = Assert.Single(received.GetProperty("items").EnumerateArray());
        Assert.Equal("available", notification.GetProperty("status").GetString());
        Assert.Equal(target.Name, notification.GetProperty("targetName").GetString());
        var location = notification.GetProperty("location");
        Assert.Equal(target.Id, location.GetProperty("objectId").GetString());
        Assert.Equal("WorkspaceFolder", location.GetProperty("modelKey").GetString());
        var sourceUrl = $"/api/workspace/content/{location.GetProperty("sourceLinkId").GetString()}/properties?queryScope="
            + Uri.EscapeDataString(location.GetProperty("position").GetRawText());
        using (var opened = await FeatureHttpTestRequests.SendAsync(recipientClient, browse, HttpMethod.Get, sourceUrl))
            Assert.Equal(target.Id, (await ReadOkAsync(opened)).GetProperty("objectId").GetString());
        var senderNotifications = await FeatureHttpTestRequests.ReadAsync(ownerClient, notifications, "/api/workspace/transfers/notifications");
        Assert.Empty(senderNotifications.GetProperty("items").EnumerateArray());
        var manage = fixture.FeatureCatalog.Single(point => point.Key == "authorizeContent" && point.ActionScope == "list.rowActions");
        var originalUrl = $"/api/workspace/content/{target.LinkId}/permissions?queryScope="
            + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using (var denied = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, originalUrl,
                   new DataScopeObjectDto("workspace.folder", target.Id, [new("deny-notification-recipient", "user", recipient.AccountId, 0,
                       new Dictionary<string, string> { ["read"] = "deny" })])))
            Assert.True(denied.StatusCode == HttpStatusCode.NoContent, await denied.Content.ReadAsStringAsync());
        using (var stale = await FeatureHttpTestRequests.SendAsync(recipientClient, browse, HttpMethod.Get, sourceUrl))
            Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        var hidden = await FeatureHttpTestRequests.ReadAsync(recipientClient, notifications, "/api/workspace/transfers/notifications");
        var unavailable = Assert.Single(hidden.GetProperty("items").EnumerateArray());
        Assert.Equal(notification.GetProperty("id").GetString(), unavailable.GetProperty("id").GetString());
        Assert.Equal("unavailable", unavailable.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, unavailable.GetProperty("targetName").ValueKind);
        Assert.Equal(JsonValueKind.Null, unavailable.GetProperty("location").ValueKind);
        Assert.DoesNotContain(target.Id, hidden.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(target.Name, hidden.GetRawText(), StringComparison.Ordinal);
    }
}

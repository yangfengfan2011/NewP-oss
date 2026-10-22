using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipTransferSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "普通所属人转交整批不以接收者读取资格为前置，审计失败归属/接收/通知全部回滚")]
    public async Task OrdinaryOwner_TransfersToUnreadableRecipient_AndAuditFailureRollsBackTheWholeBatch()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var browse = Point(fixture, "workspace.spaces.access");
        var transfer = Point(fixture, "workspace.folder.transfer");
        var edit = Point(fixture, "workspace.folder.editObject");
        var owner = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "TransferOwner", "转交所属人",
            "transfer-owner", "原所属人", browse, transfer, edit, Point(fixture, "workspace.folder.createPart"));
        using var ownerClient = owner.Client;
        var recipient = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "TransferRecipient", "接收人",
            "transfer-recipient", "接收人", browse, Point(fixture, "workspace.transfers.read"));
        using var recipientClient = recipient.Client;
        Target[] targets =
        [
            await CreateAsync(fixture, ownerClient, position, "Part", "parts", "createPart", "TRANSFER-SECRET-A"),
            await CreateAsync(fixture, ownerClient, position, "Part", "parts", "createPart", "TRANSFER-SECRET-B")
        ];
        var manage = fixture.FeatureCatalog.Single(point => point.ModuleId == "Part" && point.Key == "authorize"
            && point.ActionScope == "list.rowActions");
        foreach (var target in targets)
        {
            var configuration = new DataScopeObjectDto("part.part", target.Id,
            [
                new("transfer-owner-" + target.Id, "user", owner.AccountId, 0,
                    new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow" }),
                new("transfer-recipient-" + target.Id, "user", recipient.AccountId, 0,
                    new Dictionary<string, string> { ["read"] = "deny", ["edit"] = "deny" })
            ]);
            using var configured = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, manage, HttpMethod.Put,
                $"/api/part/parts/{target.Id}/permissions", configuration);
            Assert.True(configured.StatusCode == HttpStatusCode.NoContent, await configured.Content.ReadAsStringAsync());
            using var unreadable = await FeatureHttpTestRequests.SendAsync(recipientClient, browse, HttpMethod.Get,
                PropertiesUrl(position, target.LinkId), dataAccess: OwnershipReadDeclaration(fixture, "workspace.properties.read", browse, "workspace.content.properties"));
            Assert.Equal(HttpStatusCode.NotFound, unreadable.StatusCode);
        }
        var before = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var target in targets) before.Add(target.Id, await RowAsync(fixture, target));
        var aclBefore = await SnapshotAsync(fixture, "DataScopeObjectSubject", "DataScopeObjectAction");
        var relationshipsBefore = await SnapshotAsync(fixture, "WorkspaceSpace", "WorkspaceFolder", "WorkspaceFolderContent");
        var auditBefore = await SnapshotAsync(fixture, "BusinessChange");
        var input = new { position, linkIds = targets.Select(target => target.LinkId).ToArray(), recipientId = recipient.AccountId };

        // 真实 recorder 的写失败发生在业务登记后，不能残留目录、引用、通知或部分归属。
        fixture.Fault.Mode = "write";
        try
        {
            var failure = await Record.ExceptionAsync(async () =>
            {
                using var failed = await FeatureHttpTestRequests.SendAsync(ownerClient, transfer, HttpMethod.Post,
                    "/api/workspace/ownership/transfer", input, OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer));
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            });
            if (failure is not null)
                Assert.StartsWith("injected ", Assert.IsType<InvalidOperationException>(failure).Message, StringComparison.Ordinal);
        }
        finally { fixture.Fault.Mode = ""; }
        foreach (var target in targets)
            Assert.Equal(JsonSerializer.Serialize(before[target.Id]), JsonSerializer.Serialize(await RowAsync(fixture, target)));
        Assert.Equal(relationshipsBefore, await SnapshotAsync(fixture, "WorkspaceSpace", "WorkspaceFolder", "WorkspaceFolderContent"));
        Assert.Equal(auditBefore, await SnapshotAsync(fixture, "BusinessChange"));
        Assert.Equal("[]", await SnapshotAsync(fixture, "WorkspaceTransferNotification"));

        using (var succeeded = await FeatureHttpTestRequests.SendAsync(ownerClient, transfer, HttpMethod.Post,
                   "/api/workspace/ownership/transfer", input, OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer)))
        {
            var receipt = await ReadOkAsync(succeeded);
            Assert.True(receipt.GetProperty("succeeded").GetBoolean());
            Assert.DoesNotContain("TRANSFER-SECRET", receipt.GetRawText(), StringComparison.Ordinal);
        }
        using var recipientScope = fixture.Services.CreateScope();
        var trustedRecipient = (await recipientScope.ServiceProvider.GetRequiredService<IAccountOwnershipDirectory>()
            .FindManyAsync([recipient.AccountId]))[recipient.AccountId];
        foreach (var target in targets)
        {
            var after = await RowAsync(fixture, target);
            Assert.Equal(recipient.AccountId, after["OwnerId"]);
            Assert.Equal(trustedRecipient.DepartmentId, after["OwnerDepartmentId"]);
            Assert.Equal(before[target.Id]["CreatedBy"], after["CreatedBy"]);
            Assert.Equal(before[target.Id]["CreatedAt"], after["CreatedAt"]);
            using var unreadable = await FeatureHttpTestRequests.SendAsync(recipientClient, browse, HttpMethod.Get,
                PropertiesUrl(position, target.LinkId), dataAccess: OwnershipReadDeclaration(fixture, "workspace.properties.read", browse, "workspace.content.properties"));
            Assert.Equal(HttpStatusCode.NotFound, unreadable.StatusCode);
            using var saved = await FeatureHttpTestRequests.SendAsync(ownerClient, edit, HttpMethod.Put,
                $"/api/workspace/content/{target.LinkId}", new { position, values = new { Description = "转交后的普通保存" } }, OwnershipDeclaration(fixture, "workspace.folder.editObject", edit));
            await ReadOkAsync(saved);
            var persisted = await RowAsync(fixture, target);
            Assert.Equal(recipient.AccountId, persisted["OwnerId"]);
            Assert.Equal(trustedRecipient.DepartmentId, persisted["OwnerDepartmentId"]);
        }
        Assert.Equal(aclBefore, await SnapshotAsync(fixture, "DataScopeObjectSubject", "DataScopeObjectAction"));
        var db = recipientScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var personal = await db.Set<Dictionary<string, object?>>("WorkspaceSpace").AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "SpaceKind") == "Personal" && EF.Property<string>(row, "SubjectId") == recipient.AccountId);
        var personalRoot = (string)personal["RootFolderId"]!;
        // 转交创建的接收空间与本人初始化共用存储名称、平台 ID 和根校验；接收人随后初始化必须复用而不是另建。
        var rootFolder = await db.Set<Dictionary<string, object?>>("WorkspaceFolder").AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "EID") == personalRoot);
        Assert.Equal("个人空间", personal["ENAME"]);
        Assert.Equal("个人空间", rootFolder["ENAME"]);
        Assert.StartsWith("WorkspaceSpace_", (string)personal["EID"]!, StringComparison.Ordinal);
        Assert.Equal(recipient.AccountId, rootFolder["OwnerId"]);
        using (var ensured = await FeatureHttpTestRequests.SendAsync(recipientClient, browse, HttpMethod.Post, "/api/workspace/spaces/personal/ensure"))
        {
            var ensuredRoot = await ReadOkAsync(ensured);
            Assert.Equal(personal["EID"], ensuredRoot.GetProperty("spaceId").GetString());
            Assert.Equal(personalRoot, ensuredRoot.GetProperty("rootFolderId").GetString());
        }
        var receiveDirectory = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "PEID") == personalRoot);
        var directoryId = (string)receiveDirectory["CEID"]!;
        var directory = await db.Set<Dictionary<string, object?>>("WorkspaceFolder").AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "EID") == directoryId);
        Assert.Equal(recipient.AccountId, directory["OwnerId"]);
        Assert.Equal(trustedRecipient.DepartmentId, directory["OwnerDepartmentId"]);
        var received = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
            .Where(row => EF.Property<string>(row, "PEID") == directoryId)
            .Select(row => EF.Property<string>(row, "CEID")).ToArrayAsync();
        Assert.Equal(targets.Select(target => target.Id).Order(StringComparer.Ordinal), received.Order(StringComparer.Ordinal));
        Assert.Equal(2, await db.Set<Dictionary<string, object?>>("WorkspaceTransferNotification").CountAsync());
        using var notificationResponse = await FeatureHttpTestRequests.SendAsync(recipientClient, Point(fixture, "workspace.transfers.read"), HttpMethod.Get,
            "/api/workspace/transfers/notifications");
        var notifications = await ReadOkAsync(notificationResponse);
        Assert.Equal(2, notifications.GetProperty("items").GetArrayLength());
        foreach (var item in notifications.GetProperty("items").EnumerateArray())
        {
            Assert.Equal("unavailable", item.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("location").ValueKind);
        }
        var transferred = await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
            .Where(row => EF.Property<string>(row, "EventType") == "entity.ownership.transferred").ToArrayAsync();
        Assert.Equal(2, transferred.Length);
        Assert.All(transferred, row => Assert.Equal(owner.AccountId, row["ActorId"]));
        foreach (var target in targets)
        {
            Assert.DoesNotContain(target.Name, notifications.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(target.Id, notifications.GetRawText(), StringComparison.Ordinal);
        }
    }

    // 完整父位置始终随实际 LID 传输；接收者的显式对象 deny 不由转交改写。
    private static string PropertiesUrl(Position position, string linkId) =>
        $"/api/workspace/content/{linkId}/properties?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(position,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    // 事务不变量直接比较持久化事实；按主键稳定排序，不依赖字典物化或 SQL 默认顺序。
    internal static async Task<string> SnapshotAsync(BusinessAuditHostFixture fixture, params string[] models)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var snapshots = new List<string>();
        foreach (var model in models)
        {
            var entity = db.Model.FindEntityType(model)!;
            var key = entity.FindPrimaryKey()!.Properties.Single().Name;
            var rows = await db.Set<Dictionary<string, object?>>(model).AsNoTracking()
                .OrderBy(row => EF.Property<string>(row, key)).ToArrayAsync();
            snapshots.Add(JsonSerializer.Serialize(rows.Select(row => row.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value))));
        }
        return snapshots.Count == 1 ? snapshots[0] : JsonSerializer.Serialize(snapshots);
    }
}

using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;
using static ICP.Platform.HostApi.Tests.EntityOwnershipTransferSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipTransferPreparationSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "准备及账号候选仅当前所属人edit可见，管理员不可代转，撤权后提交重验，disabled空部门仍为合法接收人")]
    public async Task PreparationRequiresCurrentOwnerAndEdit_AndSubmissionRechecksWithoutRestrictingTheRecipient()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var transfer = Point(fixture, "workspace.folder.transfer");
        var independent = Point(fixture, "part.part.transfer");
        var owner = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PrepareTransferOwner", "转交准备所属人",
            "prepare-transfer-owner", "转交准备所属人", Point(fixture, "workspace.spaces.access"), transfer, independent,
            Point(fixture, "workspace.folder.createPart"));
        using var ownerClient = owner.Client;
        var target = await CreateAsync(fixture, ownerClient, position, "Part", "parts", "createPart", "PREPARE-TRANSFER-PRIVATE");
        var http = new FeatureHttpTestRequests(fixture);
        var recipient = await http.CreateButtonAsync(admin, http.Identity("create"), "/api/identity/accounts",
            new { ECODE = "prepare-disabled-recipient", ENAME = "停用且无人员部门的接收人" });
        var account = recipient.GetProperty("account");
        var recipientId = account.GetProperty("eid").GetString()!;
        using (var disabled = await http.SendButtonAsync(admin, http.Identity("disable"), HttpMethod.Put,
                   $"/api/identity/accounts/{recipientId}/status/disable", new { TargetStatus = "Disabled",
                       ExpectedRevision = account.GetProperty("revision").GetInt64(), Reason = "验证转交不附加账号启用条件" }))
            await ReadOkAsync(disabled);
        var scope = Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var ids = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { target.LinkId }));
        var candidateUrl = $"/api/workspace/ownership/transfer/accounts?queryScope={scope}&selectedIds={ids}&query=prepare-disabled-recipient";
        using (var prepared = await FeatureHttpTestRequests.SendAsync(ownerClient, transfer, HttpMethod.Get,
                   $"/api/workspace/ownership/transfer/eligibility?queryScope={scope}&selectedIds={ids}", dataAccess: OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer)))
            Assert.True((await ReadOkAsync(prepared)).GetProperty("allowed").GetBoolean());
        using (var candidates = await FeatureHttpTestRequests.SendAsync(ownerClient, transfer, HttpMethod.Get, candidateUrl, dataAccess: OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer)))
            AssertCandidate(await ReadOkAsync(candidates), recipientId);
        using (var candidates = await FeatureHttpTestRequests.SendAsync(ownerClient, independent, HttpMethod.Get,
                   $"/api/part/parts/{target.Id}/transfer/accounts?query=prepare-disabled-recipient", dataAccess: OwnershipDeclaration(fixture, "part.part.transfer", independent)))
            AssertCandidate(await ReadOkAsync(candidates), recipientId);
        using (var denied = await FeatureHttpTestRequests.SendAsync(admin, transfer, HttpMethod.Get, candidateUrl, dataAccess: OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.DoesNotContain(recipientId, await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        var input = new { position, linkIds = new[] { target.LinkId }, recipientId };
        using (var substitute = await FeatureHttpTestRequests.SendAsync(admin, transfer, HttpMethod.Post, "/api/workspace/ownership/transfer", input, OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer)))
            Assert.Equal(HttpStatusCode.Forbidden, substitute.StatusCode);
        var manage = fixture.FeatureCatalog.Single(point => point.ModuleId == "Part" && point.Key == "authorize" && point.ActionScope == "list.rowActions");
        await SetOwnerEditAsync("deny");
        var models = new[] { "Part", "WorkspaceSpace", "WorkspaceFolder", "WorkspaceFolderContent", "BusinessChange", "WorkspaceTransferNotification" };
        var before = await SnapshotAsync(fixture, models);
        using (var stale = await FeatureHttpTestRequests.SendAsync(ownerClient, transfer, HttpMethod.Post, "/api/workspace/ownership/transfer", input, OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer)))
            Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        using (var denied = await FeatureHttpTestRequests.SendAsync(ownerClient, transfer, HttpMethod.Get, candidateUrl, dataAccess: OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer)))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(before, await SnapshotAsync(fixture, models));
        await SetOwnerEditAsync("allow");
        using (var success = await FeatureHttpTestRequests.SendAsync(ownerClient, transfer, HttpMethod.Post, "/api/workspace/ownership/transfer", input, OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer)))
            Assert.Equal(1, (await ReadOkAsync(success)).GetProperty("changed").GetInt32());
        var row = await RowAsync(fixture, target);
        Assert.Equal(recipientId, row["OwnerId"]);
        Assert.Null(row["OwnerDepartmentId"]);

        async Task SetOwnerEditAsync(string edit)
        {
            using var configured = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, manage, HttpMethod.Put,
                $"/api/part/parts/{target.Id}/permissions", new DataScopeObjectDto("part.part", target.Id,
                    [new("prepare-owner", "user", owner.AccountId, 0, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = edit })]));
            Assert.True(configured.StatusCode == HttpStatusCode.NoContent, await configured.Content.ReadAsStringAsync());
        }
    }

    // 候选展示不把部门缺失或停用账号变成新增资格；真实提交仍读取事务内当前事实。
    private static void AssertCandidate(JsonElement candidates, string recipientId)
    {
        var candidate = Assert.Single(candidates.EnumerateArray());
        Assert.Equal(recipientId, candidate.GetProperty("accountId").GetString());
        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("departmentId").ValueKind);
    }
}

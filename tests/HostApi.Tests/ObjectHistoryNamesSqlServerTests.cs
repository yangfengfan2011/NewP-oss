using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class ObjectHistoryNamesSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "历史文本不随改名重写，旧缺失明确当前；父关系按真实Part主体且隐藏/删除不借历史或原ID兜底")]
    public async Task Names_UseRealHistoricalOrCurrentTexts_AndMaskUnreadableRelatedTargets()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var historyRequests = new ObjectHistoryHttpTestRequests(fixture);
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "HistoryName296");
        var history = Point(fixture, "workspace.folder.history");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "HistoryName296", "履历名称", "history-name296", "履历名称账号",
            Point(fixture, "workspace.spaces.access"), history);
        using var ordinary = actor.Client;
        var partPath = Path(position, part.LinkId);
        var initial = Assert.Single((await historyRequests.ReadAsync(ordinary, history, partPath + "&eventType=entity.ownership.initialized")).GetProperty("items").EnumerateArray());
        AssertTarget(initial, part.Name, null, "current"); // 归属事件没有历史名，不把当前名伪装为历史。
        var created = Assert.Single((await historyRequests.ReadAsync(ordinary, history, partPath + "&eventType=part.created")).GetProperty("items").EnumerateArray());
        AssertTarget(created, part.Name, part.Name, "historical");
        using (var edited = await historyRequests.SendAsync(admin, Point(fixture, "workspace.folder.editObject"), HttpMethod.Put,
            "/api/workspace/content/" + part.LinkId, new { position, values = new { ENAME = "改名后的对象", ECODE = "HistoryNameChanged296" } })) await ReadOkAsync(edited);
        var historical = Assert.Single((await historyRequests.ReadAsync(ordinary, history, partPath + "&eventType=part.created")).GetProperty("items").EnumerateArray());
        AssertTarget(historical, part.Name, part.Name, "historical");
        var updated = Assert.Single((await historyRequests.ReadAsync(ordinary, history, partPath + "&eventType=part.updated")).GetProperty("items").EnumerateArray());
        AssertTarget(updated, "改名后的对象", "HistoryNameChanged296", "historical");
        var current = Assert.Single((await historyRequests.ReadAsync(ordinary, history, partPath + "&eventType=entity.ownership.initialized")).GetProperty("items").EnumerateArray());
        AssertTarget(current, "改名后的对象", null, "current");
        var second = await CreateAsync(fixture, admin, position with { LinkIds = [] }, "WorkspaceFolder", "folders", "create", "名称剩余入口296");
        var secondPosition = position with { LinkIds = [second.LinkId] };
        using (var added = await historyRequests.SendAsync(admin, Point(fixture, "workspace.folder.add"), HttpMethod.Post,
            "/api/workspace/content/references", new { position = secondPosition, target = new { modelKey = "Part", id = part.Id } })) await ReadOkAsync(added);
        string remaining;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ICP.Platform.Persistence.EfCore.EfPlatformDbContext>();
            remaining = (string)(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").Where(row => Microsoft.EntityFrameworkCore.EF.Property<string>(row, "PEID") == second.Id
                    && Microsoft.EntityFrameworkCore.EF.Property<string>(row, "CEID") == part.Id)))["LID"]!;
        }
        using (var removed = await historyRequests.SendAsync(admin, Point(fixture, "workspace.folder.remove"), HttpMethod.Delete,
            "/api/workspace/content/references/" + part.LinkId, new { position })) await ReadOkAsync(removed);
        var parentPath = Path(position with { LinkIds = [] }, position.LinkIds[0]) + "&eventType=workspace.reference.removed";
        var related = Assert.Single((await historyRequests.ReadAsync(ordinary, history, parentPath)).GetProperty("items").EnumerateArray());
        Assert.Equal("Part", related.GetProperty("objectType").GetString());
        AssertTarget(related, "改名后的对象", null, "current"); // 不能拿查询Folder的名称填Part事件。
        var configuration = new DataScopeObjectDto("part.part", part.Id,
            [new("hidden-name", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = "deny" })]);
        using (var denied = await historyRequests.SendAsync(admin, Point(fixture, "part.part.authorize"), HttpMethod.Put,
            "/api/part/parts/" + part.Id + "/permissions", configuration)) Assert.Equal(HttpStatusCode.NoContent, denied.StatusCode);
        var hidden = Assert.Single((await historyRequests.ReadAsync(ordinary, history, parentPath)).GetProperty("items").EnumerateArray());
        AssertTarget(hidden, null, null, "unavailable");
        Assert.DoesNotContain(part.Id, hidden.GetRawText(), StringComparison.Ordinal);
        using (var deleted = await historyRequests.SendAsync(admin, Point(fixture, "workspace.folder.deleteObject"), HttpMethod.Delete,
            "/api/workspace/content/" + remaining, new { position = secondPosition })) await ReadOkAsync(deleted);
        var missing = Assert.Single((await historyRequests.ReadAsync(ordinary, history, parentPath)).GetProperty("items").EnumerateArray());
        AssertTarget(missing, null, null, "unavailable");
        using (var deletedTarget = await historyRequests.SendAsync(ordinary, history, HttpMethod.Get, Path(secondPosition, remaining)))
            Assert.Equal(HttpStatusCode.NotFound, deletedTarget.StatusCode);
        using var retained = await historyRequests.SendAsync(admin, Point(fixture, "audit.change.read"), HttpMethod.Get,
            "/api/audit/business-changes/" + created.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.OK, retained.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "新Folder名称目录供既有管理员审计读取当前名，普通账号不借history资格进入原始审计，删除后事实仍保留")]
    public async Task FolderNameDirectory_PreservesExistingAdministratorProjectionAndAdmission()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var historyRequests = new ObjectHistoryHttpTestRequests(fixture);
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var folder = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "AdminFolderName296");
        using (var renamed = await historyRequests.SendAsync(admin, Point(fixture, "workspace.folder.editObject"), HttpMethod.Put,
            "/api/workspace/content/" + folder.LinkId, new { position, values = new { ENAME = "AdminFolderRenamed296" } })) await ReadOkAsync(renamed);
        var audit = Point(fixture, "audit.change.read");
        var path = "/api/audit/business-changes?Resource=workspace.folder&TargetId=" + folder.Id + "&EventType=workspace.folder.created";
        var page = await historyRequests.ReadAsync(admin, audit, path);
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        var record = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("AdminFolderRenamed296", record.GetProperty("TargetId_DisplayValue").GetString());
        var detailPath = "/api/audit/business-changes/" + record.GetProperty("EID").GetString();
        var detail = await historyRequests.ReadAsync(admin, audit, detailPath);
        Assert.Equal(folder.Id, detail.GetProperty("fields").GetProperty("TargetId").GetString());
        Assert.Equal("AdminFolderRenamed296", detail.GetProperty("fields").GetProperty("TargetId_DisplayValue").GetString());
        Assert.False(string.IsNullOrWhiteSpace(detail.GetProperty("fields").GetProperty("TraceId").GetString()));
        Assert.Equal(2, detail.GetProperty("summaries").GetProperty("changes").GetProperty("data").GetArrayLength());
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "AuditFolder296", "审计目录边界", "audit-folder296", "普通履历账号",
            Point(fixture, "workspace.folder.history"), audit);
        using var ordinary = actor.Client;
        foreach (var ordinaryPath in new[] { path, detailPath })
        {
            using var denied = await historyRequests.SendAsync(ordinary, audit, HttpMethod.Get, ordinaryPath);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using (var deleted = await historyRequests.SendAsync(admin, Point(fixture, "workspace.folder.deleteObject"), HttpMethod.Delete,
            "/api/workspace/content/" + folder.LinkId, new { position })) await ReadOkAsync(deleted);
        var retained = await historyRequests.ReadAsync(admin, audit, detailPath);
        Assert.Equal(folder.Id, retained.GetProperty("fields").GetProperty("TargetId").GetString());
        Assert.Equal(JsonValueKind.Null, retained.GetProperty("fields").GetProperty("TargetId_DisplayValue").ValueKind);
    }

    private static string Path(Position position, string linkId) => "/api/workspace/content/" + linkId + "/history?queryScope="
        + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static void AssertTarget(JsonElement item, string? name, string? code, string source)
    {
        Assert.Equal(name, item.GetProperty("target").GetProperty("name").GetString());
        Assert.Equal(code, item.GetProperty("target").GetProperty("code").GetString());
        Assert.Equal(source, item.GetProperty("target").GetProperty("nameSource").GetString());
    }
}

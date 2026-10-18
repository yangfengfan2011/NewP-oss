using System.Net;
using System.Text.Json;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class ObjectHistoryLegacyEventsSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真副本及两类Folder副本通过真实旧字段关联源新对象和已记录父，缺失旧父不补造，同父关联同事件去重")]
    public async Task ObjectAndFolderCopies_UseRecordedSourceDestinationAndParentFacts()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var historyRequests = new ObjectHistoryHttpTestRequests(fixture);
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "LegacyCopies296", "旧副本履历", "legacy-copies296", "普通副本履历账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.history"));
        using var ordinary = actor.Client;
        var parent = ParentOccurrence(position);
        var original = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "LegacyOriginal296");
        var preparation = await CommandAsync(fixture, admin, "pasteObjectCopy", "objects/copy/prepare", new {
            mode = "copy", sourcePosition = position, linkIds = new[] { original.LinkId }, targetPosition = position });
        await CommandAsync(fixture, admin, "pasteObjectCopy", "objects/copy", new {
            operationId = preparation.GetProperty("operationId").GetString(), values = new { ECODE = "LegacyNewPart296", ENAME = "LegacyNewPart296", Description = "独立副本" } });
        var copied = await FindTargetAsync(fixture, "Part", "LegacyNewPart296");
        var objectEvent = await EventAsync(fixture, "workspace.object.copied", copied.LinkId);
        var sourceParent = await LinkAsync(fixture, position.LinkIds[^1]);
        AssertFact(objectEvent, "SourceObject", "Part", original.Id);
        AssertFact(objectEvent, "SourcePEID", "workspace.folder", (string)sourceParent["CEID"]!);
        AssertFact(objectEvent, "PEID", "workspace.folder", (string)sourceParent["CEID"]!);
        AssertFact(objectEvent, "CEID", "Part", copied.Id);
        var objectEventId = (string)objectEvent["EID"]!;
        foreach (var occurrence in new[] { new Occurrence(position, original.LinkId), new Occurrence(position, copied.LinkId), parent })
        {
            var item = await SingleEventAsync(fixture, ordinary, occurrence, "workspace.object.copied", objectEventId, "Part", "LegacyNewPart296",
                includeDetail: occurrence.LinkId == original.LinkId);
            AssertSafe(item, original.Id, copied.Id, original.LinkId, copied.LinkId, (string)sourceParent["CEID"]!, actor.AccountId);
        }
        // SourcePEID与PEID相同，父对象仍只能得到同一事件一次；类型和Count在SQL完成。
        fixture.Fault.Commands.Clear();
        var parentPage = await HistoryAsync(fixture, ordinary, parent, "workspace.object.copied", "&objectType=Part&pageSize=1");
        Assert.Equal(1, parentPage.GetProperty("total").GetInt32());
        var countSql = Assert.Single(fixture.Fault.Commands, sql => sql.Contains("COUNT(*)", StringComparison.Ordinal));
        Assert.Contains("OPENJSON", countSql, StringComparison.Ordinal);
        Assert.Equal(0, (await HistoryAsync(fixture, ordinary, parent, "workspace.object.copied", "&objectType=WorkspaceFolder")).GetProperty("total").GetInt32());

        var sourceFolder = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "LegacySourceFolder296");
        var destination = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "LegacyCopyDestination296");
        var destinationPosition = In(position, destination);
        await CommandAsync(fixture, admin, "pasteFolderCopy", "folders/copy", new {
            mode = "copy", sourcePosition = position, linkIds = new[] { sourceFolder.LinkId }, targetPosition = destinationPosition, name = "LegacyShallow296" });
        var shallow = await FindTargetAsync(fixture, "WorkspaceFolder", "LegacyShallow296");
        var shallowEvent = await EventAsync(fixture, "workspace.folder.shallowCopied", shallow.Id);
        AssertFact(shallowEvent, "SourcePEID", "workspace.folder", sourceFolder.Id); // 旧字段确实保存源对象，不能把名称当作源父语义。
        AssertFact(shallowEvent, "PEID", "workspace.folder", destination.Id);
        AssertFact(shallowEvent, "CEID", "workspace.folder", shallow.Id);
        foreach (var occurrence in new[] { new Occurrence(position, sourceFolder.LinkId), new Occurrence(destinationPosition, shallow.LinkId), new Occurrence(position, destination.LinkId) })
        {
            var item = await SingleEventAsync(fixture, ordinary, occurrence, "workspace.folder.shallowCopied", (string)shallowEvent["EID"]!, "WorkspaceFolder", shallow.Name);
            AssertSafe(item, sourceFolder.Id, destination.Id, shallow.Id, sourceFolder.LinkId, shallow.LinkId);
        }
        Assert.Equal(0, (await HistoryAsync(fixture, ordinary, parent, "workspace.folder.shallowCopied")).GetProperty("total").GetInt32());

        var sourceChild = await CreateAsync(fixture, admin, In(position, sourceFolder), "WorkspaceFolder", "folders", "create", "LegacyStructureChild296");
        await CommandAsync(fixture, admin, "pasteFolderStructure", "folders/copy-structure", new {
            mode = "copy", sourcePosition = position, linkIds = new[] { sourceFolder.LinkId }, targetPosition = destinationPosition, name = "LegacyStructure296" });
        var structure = await FindTargetAsync(fixture, "WorkspaceFolder", "LegacyStructure296");
        var newChildLink = Assert.Single(await LinksToParentAsync(fixture, structure.Id));
        var newChild = new Target("WorkspaceFolder", (string)newChildLink["CEID"]!, (string)newChildLink["LID"]!, sourceChild.Name);
        var structureEvent = await EventAsync(fixture, "workspace.folder.structureCopied", structure.Id);
        AssertFact(structureEvent, "SourcePEID", "workspace.folder", sourceFolder.Id);
        AssertFact(structureEvent, "CEID", "workspace.folder", structure.Id);
        foreach (var occurrence in new[] { new Occurrence(position, sourceFolder.LinkId), new Occurrence(destinationPosition, structure.LinkId) })
            await SingleEventAsync(fixture, ordinary, occurrence, "workspace.folder.structureCopied", (string)structureEvent["EID"]!, "WorkspaceFolder", structure.Name);
        var childEvent = await EventAsync(fixture, "workspace.folder.structureCopied", newChild.Id);
        foreach (var occurrence in new[] { new Occurrence(In(position, sourceFolder), sourceChild.LinkId), new Occurrence(In(destinationPosition, structure), newChild.LinkId) })
            await SingleEventAsync(fixture, ordinary, occurrence, "workspace.folder.structureCopied", (string)childEvent["EID"]!, "WorkspaceFolder", newChild.Name);
        // structureCopied没有parent字段，不聚合后代事件；真实父关系由其配套reference.copied记录关联。
        Assert.Equal(0, (await HistoryAsync(fixture, ordinary, new(position, destination.LinkId), "workspace.folder.structureCopied")).GetProperty("total").GetInt32());
        Assert.Equal(0, (await HistoryAsync(fixture, ordinary, parent, "workspace.folder.structureCopied")).GetProperty("total").GetInt32());
        var rootRelation = await EventAsync(fixture, "workspace.reference.copied", structure.LinkId);
        AssertFact(rootRelation, "SourcePEID", "workspace.folder", (string)sourceParent["CEID"]!);
        AssertFact(rootRelation, "PEID", "workspace.folder", destination.Id);
        AssertFact(rootRelation, "CEID", "WorkspaceFolder", structure.Id);
        foreach (var occurrence in new[] { parent, new Occurrence(position, destination.LinkId) })
            await SingleEventAsync(fixture, ordinary, occurrence, "workspace.reference.copied", (string)rootRelation["EID"]!, "WorkspaceFolder", structure.Name);
        var internalRelation = await EventAsync(fixture, "workspace.reference.copied", newChild.LinkId);
        AssertFact(internalRelation, "SourcePEID", "workspace.folder", sourceFolder.Id);
        AssertFact(internalRelation, "PEID", "workspace.folder", structure.Id);
        foreach (var occurrence in new[] { new Occurrence(position, sourceFolder.LinkId), new Occurrence(In(destinationPosition, structure), newChild.LinkId) })
            await SingleEventAsync(fixture, ordinary, occurrence, "workspace.reference.copied", (string)internalRelation["EID"]!, "WorkspaceFolder", newChild.Name);
        var structureParentPage = await HistoryAsync(fixture, ordinary, new(destinationPosition, structure.LinkId), "workspace.reference.copied");
        Assert.Equal(2, structureParentPage.GetProperty("total").GetInt32());
        Assert.Equal(new[] { (string)rootRelation["EID"]!, (string)internalRelation["EID"]! }.Order(StringComparer.Ordinal),
            structureParentPage.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).Order(StringComparer.Ordinal));
        AssertSafe(structureParentPage, sourceFolder.Id, structure.Id, newChild.Id, sourceFolder.LinkId, structure.LinkId, newChild.LinkId);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "引用复制移动排序从旧新父及真实child关联同事件，旧LID删除仍可查，类型筛选和稳定分页不重复")]
    public async Task ReferenceCopyMoveAndReorder_UseTypedFactsForOrdinaryHistory()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var historyRequests = new ObjectHistoryHttpTestRequests(fixture);
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "LegacyRelations296", "旧关系履历", "legacy-relations296", "普通关系履历账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.history"));
        using var ordinary = actor.Client;
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "LegacyReferencePart296");
        var first = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "LegacyReferenceFirst296");
        var second = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "LegacyReferenceSecond296");
        await CommandAsync(fixture, admin, "paste", "paste", new { mode = "copy", sourcePosition = position, linkIds = new[] { part.LinkId }, targetPosition = In(position, first) });
        var copiedLink = Assert.Single(await LinksToParentAsync(fixture, first.Id));
        var copiedEvent = await EventAsync(fixture, "workspace.reference.copied", (string)copiedLink["LID"]!);
        AssertFact(copiedEvent, "SourceLID", "workspace.content", part.LinkId);
        AssertFact(copiedEvent, "SourcePEID", "workspace.folder", (string)(await LinkAsync(fixture, position.LinkIds[^1]))["CEID"]!);
        AssertFact(copiedEvent, "PEID", "workspace.folder", first.Id);
        AssertFact(copiedEvent, "CEID", "Part", part.Id);
        foreach (var occurrence in new[] { new Occurrence(position, part.LinkId), new Occurrence(In(position, first), (string)copiedLink["LID"]!), ParentOccurrence(position), new Occurrence(position, first.LinkId) })
        {
            var item = await SingleEventAsync(fixture, ordinary, occurrence, "workspace.reference.copied", (string)copiedEvent["EID"]!, "Part", part.Name,
                includeDetail: occurrence.LinkId == part.LinkId);
            AssertSafe(item, part.Id, part.LinkId, first.Id, first.LinkId, (string)copiedLink["LID"]!);
        }
        await CommandAsync(fixture, admin, "paste", "paste", new { mode = "cut", sourcePosition = In(position, first), linkIds = new[] { (string)copiedLink["LID"]! }, targetPosition = In(position, second) });
        var movedLink = Assert.Single(await LinksToParentAsync(fixture, second.Id));
        var movedEvent = await EventAsync(fixture, "workspace.reference.moved", (string)movedLink["LID"]!);
        AssertFact(movedEvent, "SourceLID", "workspace.content", (string)copiedLink["LID"]!);
        AssertFact(movedEvent, "SourcePEID", "workspace.folder", first.Id);
        AssertFact(movedEvent, "PEID", "workspace.folder", second.Id);
        AssertFact(movedEvent, "CEID", "Part", part.Id);
        using (var scope = fixture.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent")
                .AnyAsync(row => EF.Property<string>(row, "LID") == (string)copiedLink["LID"]!));
        foreach (var occurrence in new[] { new Occurrence(position, part.LinkId), new Occurrence(In(position, second), (string)movedLink["LID"]!), new Occurrence(position, first.LinkId), new Occurrence(position, second.LinkId) })
        {
            var item = await SingleEventAsync(fixture, ordinary, occurrence, "workspace.reference.moved", (string)movedEvent["EID"]!, "Part", part.Name);
            AssertSafe(item, part.Id, part.LinkId, first.Id, second.Id, (string)copiedLink["LID"]!, (string)movedLink["LID"]!);
        }
        var left = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "LegacyReorderLeft296");
        var right = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "LegacyReorderRight296");
        await CommandAsync(fixture, admin, "reorder", "tree-order", new {
            parentPosition = new { spaceId = position.SpaceId, rootFolderId = position.RootFolderId, linkIds = position.LinkIds },
            linkId = right.LinkId, direction = "up" });
        var reordered = await EventsAsync(fixture, "workspace.reference.reordered");
        Assert.Equal(2, reordered.Length);
        foreach (var target in new[] { left, right })
        {
            var record = Assert.Single(reordered, row => Equals(row["TargetId"], target.LinkId));
            var item = await SingleEventAsync(fixture, ordinary, new(position, target.LinkId), "workspace.reference.reordered", (string)record["EID"]!, "WorkspaceFolder", target.Name);
            AssertSafe(item, target.Id, target.LinkId, first.Id, second.Id);
        }
        fixture.Fault.Commands.Clear();
        var parent = ParentOccurrence(position);
        var firstPage = await HistoryAsync(fixture, ordinary, parent, "workspace.reference.reordered", "&objectType=WorkspaceFolder&pageSize=1&pageIndex=1");
        Assert.Equal(2, firstPage.GetProperty("total").GetInt32());
        var countSql = Assert.Single(fixture.Fault.Commands, sql => sql.Contains("COUNT(*)", StringComparison.Ordinal));
        Assert.Contains("OPENJSON", countSql, StringComparison.Ordinal);
        var nextPage = await HistoryAsync(fixture, ordinary, parent, "workspace.reference.reordered", "&objectType=WorkspaceFolder&pageSize=1&pageIndex=2");
        Assert.Equal(2, nextPage.GetProperty("total").GetInt32());
        var pageIds = new[] { Assert.Single(firstPage.GetProperty("items").EnumerateArray()).GetProperty("id").GetString()!, Assert.Single(nextPage.GetProperty("items").EnumerateArray()).GetProperty("id").GetString()! };
        Assert.Equal(2, pageIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(reordered.Select(row => (string)row["EID"]!).Order(StringComparer.Ordinal), pageIds.Order(StringComparer.Ordinal));
        Assert.Equal(0, (await HistoryAsync(fixture, ordinary, parent, "workspace.reference.reordered", "&objectType=Part")).GetProperty("total").GetInt32());
        AssertSafe(firstPage, left.Id, right.Id, left.LinkId, right.LinkId, actor.AccountId);
    }

    private sealed record Occurrence(Position Position, string LinkId);
    private static Position In(Position parent, Target folder) => parent with { LinkIds = [.. parent.LinkIds, folder.LinkId] };
    private static Occurrence ParentOccurrence(Position position) => new(position with { LinkIds = position.LinkIds[..^1] }, position.LinkIds[^1]);
    private static async Task<JsonElement> CommandAsync(BusinessAuditHostFixture fixture, HttpClient client, string action, string suffix, object body)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder." + action), HttpMethod.Post, "/api/workspace/content/" + suffix, body);
        return await ReadOkAsync(response);
    }
    private static string HistoryPath(Occurrence occurrence) => "/api/workspace/content/" + occurrence.LinkId + "/history?queryScope="
        + Uri.EscapeDataString(JsonSerializer.Serialize(occurrence.Position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    private static Task<JsonElement> HistoryAsync(BusinessAuditHostFixture fixture, HttpClient client, Occurrence occurrence, string eventType, string suffix = "") =>
        new ObjectHistoryHttpTestRequests(fixture).ReadAsync(client, Point(fixture, "workspace.folder.history"), HistoryPath(occurrence) + "&eventType=" + Uri.EscapeDataString(eventType) + suffix);
    private static async Task<JsonElement> SingleEventAsync(BusinessAuditHostFixture fixture, HttpClient client, Occurrence occurrence, string eventType, string eventId, string model, string name,
        bool includeDetail = false)
    {
        var page = await HistoryAsync(fixture, client, occurrence, eventType);
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(eventId, item.GetProperty("id").GetString()); Assert.Equal(model, item.GetProperty("objectType").GetString());
        Assert.Equal(name, item.GetProperty("target").GetProperty("name").GetString());
        Assert.Equal("current", item.GetProperty("target").GetProperty("nameSource").GetString());
        Assert.Null(item.GetProperty("target").GetProperty("code").GetString());
        AssertSafe(item, occurrence.LinkId);
        // 每项Fact只选一个源对象验证fresh详情；其余出现位置用列表保留完整关联断言。
        if (includeDetail)
        {
            using var detail = await FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder.history"), HttpMethod.Get,
                HistoryPath(occurrence).Replace("/history?", "/history/" + eventId + "?", StringComparison.Ordinal) + "&eventType=" + Uri.EscapeDataString(eventType));
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var detailItem = JsonSerializer.Deserialize<JsonElement>(await detail.Content.ReadAsStringAsync());
            Assert.Equal(eventId, detailItem.GetProperty("id").GetString());
            AssertSafe(detailItem, occurrence.LinkId);
        }
        return item;
    }
    private static async Task<Target> FindTargetAsync(BusinessAuditHostFixture fixture, string model, string name)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var row = await db.Set<Dictionary<string, object?>>(model).AsNoTracking().SingleAsync(row => EF.Property<string>(row, "ENAME") == name);
        var id = (string)row["EID"]!;
        var link = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().SingleAsync(row => EF.Property<string>(row, "CEID") == id);
        return new(model, id, (string)link["LID"]!, name);
    }
    private static async Task<Dictionary<string, object?>> LinkAsync(BusinessAuditHostFixture fixture, string id)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().SingleAsync(row => EF.Property<string>(row, "LID") == id);
    }
    private static async Task<Dictionary<string, object?>[]> LinksToParentAsync(BusinessAuditHostFixture fixture, string id)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().Where(row => EF.Property<string>(row, "PEID") == id).ToArrayAsync();
    }
    private static async Task<Dictionary<string, object?>[]> EventsAsync(BusinessAuditHostFixture fixture, string eventType)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking().Where(row => EF.Property<string>(row, "EventType") == eventType).ToArrayAsync();
    }
    private static async Task<Dictionary<string, object?>> EventAsync(BusinessAuditHostFixture fixture, string eventType, string targetId) =>
        Assert.Single(await EventsAsync(fixture, eventType), row => Equals(row["TargetId"], targetId));
    private static void AssertFact(Dictionary<string, object?> record, string field, string kind, string value)
    {
        var fields = JsonSerializer.Deserialize<JsonElement>((string)record["FieldsJson"]!);
        var fact = Assert.Single(fields.EnumerateArray(), entry => entry.GetProperty("Field").GetString() == field).GetProperty("After");
        Assert.Equal(kind, fact.GetProperty("Kind").GetString()); Assert.Equal(value, fact.GetProperty("Value").GetString());
    }
    private static void AssertSafe(JsonElement item, params string[] ids)
    {
        var json = item.GetRawText();
        foreach (var forbidden in ids.Concat(new[] { "FieldsJson", "fieldsJson", "TraceId", "traceId", "Configuration", "configuration", "SourceLID", "SourcePEID", "SourceObject" }))
            Assert.DoesNotContain(forbidden, json, StringComparison.Ordinal);
    }
}

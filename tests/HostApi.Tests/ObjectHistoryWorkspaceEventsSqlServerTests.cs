using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.HostApi.Persistence;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class ObjectHistoryWorkspaceEventsSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "普通Folder CRUD和三模型初始引用、普通Add有真实事件，复制移动无重复added，删除保留每条入向旧关系")]
    public async Task FolderAndReferences_RecordActualEventsWithoutCopyOrNoOpDuplicates()
    {
        var ticks = new AuditTicks();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.Replace(
            ServiceDescriptor.Scoped<IEntityAuditStampProvider>(provider => new AdvancingStamp(new HttpContextEntityAuditStampProvider(
                provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), provider.GetRequiredService<TimeProvider>()), ticks))), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var historyRequests = new ObjectHistoryHttpTestRequests(fixture);
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var folder = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "HistoryFolder296");
        var created = Assert.Single(await EventsAsync(fixture, "workspace.folder", folder.Id), row => Equals(row["EventType"], "workspace.folder.created"));
        AssertSnapshot(created, folder.Name, null);
        var row = await RowAsync(fixture, folder);
        Assert.Equal(row["CreatedBy"], created["ActorId"]); Assert.Equal(row["CreatedAt"], created["OccurredAt"]);
        var folderLink = await LinkAsync(fixture, folder.LinkId);
        await AssertAddedAsync(fixture, folderLink);
        using (var unchanged = await CommandAsync(fixture, admin, "editObject", HttpMethod.Put, folder.LinkId, new { position, values = new { ENAME = folder.Name } }))
            await ReadOkAsync(unchanged);
        Assert.DoesNotContain(await EventsAsync(fixture, "workspace.folder", folder.Id), item => Equals(item["EventType"], "workspace.folder.updated"));
        using (var invalid = await CommandAsync(fixture, admin, "editObject", HttpMethod.Put, folder.LinkId, new { position, values = new { ENAME = "" } }))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var edited = await CommandAsync(fixture, admin, "editObject", HttpMethod.Put, folder.LinkId, new { position, values = new { ENAME = "HistoryFolderRenamed296", Description = "说明" } }))
            await ReadOkAsync(edited);
        var updated = Assert.Single(await EventsAsync(fixture, "workspace.folder", folder.Id), item => Equals(item["EventType"], "workspace.folder.updated"));
        row = await RowAsync(fixture, folder);
        AssertSnapshot(updated, "HistoryFolderRenamed296", null);
        Assert.Equal(row["UpdatedBy"], updated["ActorId"]); Assert.Equal(row["UpdatedAt"], updated["OccurredAt"]);
        Assert.NotEqual(created["OccurredAt"], updated["OccurredAt"]);
        using (var description = await CommandAsync(fixture, admin, "editObject", HttpMethod.Put, folder.LinkId, new { position, values = new { Description = (string?)null } }))
            await ReadOkAsync(description);
        using (var unchangedNull = await CommandAsync(fixture, admin, "editObject", HttpMethod.Put, folder.LinkId, new { position, values = new { Description = (string?)null } }))
            await ReadOkAsync(unchangedNull);
        Assert.Equal(2, (await EventsAsync(fixture, "workspace.folder", folder.Id)).Count(item => Equals(item["EventType"], "workspace.folder.updated")));

        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "HistoryWorkspacePart296");
        var document = await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "HistoryWorkspaceDocument296");
        await AssertAddedAsync(fixture, await LinkAsync(fixture, part.LinkId));
        await AssertAddedAsync(fixture, await LinkAsync(fixture, document.LinkId));
        var destinations = new List<Target>();
        for (var index = 0; index < 3; index++)
            destinations.Add(await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "HistoryDestination296" + index));
        Position In(Target target) => position with { LinkIds = [.. position.LinkIds, target.LinkId] };
        var firstPosition = In(destinations[0]);
        using (var added = await CommandAsync(fixture, admin, "add", HttpMethod.Post, "references", new { position = firstPosition, target = new { modelKey = "Part", id = part.Id } }))
            await ReadOkAsync(added);
        var addedLink = Assert.Single(await IncomingAsync(fixture, "Part", part.Id), item => Equals(item["PEID"], destinations[0].Id));
        await AssertAddedAsync(fixture, addedLink);
        var beforeDuplicate = await RecordCountAsync(fixture);
        using (var duplicate = await CommandAsync(fixture, admin, "add", HttpMethod.Post, "references", new { position = firstPosition, target = new { modelKey = "Part", id = part.Id } }))
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(beforeDuplicate, await RecordCountAsync(fixture));
        using (var copied = await CommandAsync(fixture, admin, "paste", HttpMethod.Post, "paste", new { mode = "copy", sourcePosition = position, linkIds = new[] { part.LinkId }, targetPosition = In(destinations[1]) }))
            await ReadOkAsync(copied);
        var copiedLink = Assert.Single(await IncomingAsync(fixture, "Part", part.Id), item => Equals(item["PEID"], destinations[1].Id));
        var copiedEvents = await EventsAsync(fixture, "workspace.content", (string)copiedLink["LID"]!);
        Assert.Single(copiedEvents, item => Equals(item["EventType"], "workspace.reference.copied"));
        Assert.DoesNotContain(copiedEvents, item => Equals(item["EventType"], "workspace.reference.added"));
        using (var moved = await CommandAsync(fixture, admin, "paste", HttpMethod.Post, "paste", new { mode = "cut", sourcePosition = In(destinations[1]), linkIds = new[] { (string)copiedLink["LID"]! }, targetPosition = In(destinations[2]) }))
            await ReadOkAsync(moved);
        var movedLink = Assert.Single(await IncomingAsync(fixture, "Part", part.Id), item => Equals(item["PEID"], destinations[2].Id));
        var movedEvents = await EventsAsync(fixture, "workspace.content", (string)movedLink["LID"]!);
        Assert.Single(movedEvents, item => Equals(item["EventType"], "workspace.reference.moved"));
        Assert.DoesNotContain(movedEvents, item => Equals(item["EventType"], "workspace.reference.added"));
        var beforeNoOp = await RecordCountAsync(fixture);
        using (var noOp = await CommandAsync(fixture, admin, "paste", HttpMethod.Post, "paste", new { mode = "cut", sourcePosition = In(destinations[2]), linkIds = new[] { (string)movedLink["LID"]! }, targetPosition = In(destinations[2]) }))
            Assert.True((await ReadOkAsync(noOp)).GetProperty("noChange").GetBoolean());
        Assert.Equal(beforeNoOp, await RecordCountAsync(fixture));
        var incoming = await IncomingAsync(fixture, "Part", part.Id);
        Assert.Equal(3, incoming.Length);
        using (var deleted = await CommandAsync(fixture, admin, "deleteObject", HttpMethod.Delete, part.LinkId, new { position }))
            await ReadOkAsync(deleted);
        foreach (var link in incoming) await AssertRemovedAsync(fixture, link);
        Assert.Empty(await IncomingAsync(fixture, "Part", part.Id));
        using (var deleted = await CommandAsync(fixture, admin, "deleteObject", HttpMethod.Delete, folder.LinkId, new { position }))
            await ReadOkAsync(deleted);
        var deletion = Assert.Single(await EventsAsync(fixture, "workspace.folder", folder.Id), item => Equals(item["EventType"], "workspace.folder.deleted"));
        AssertSnapshot(deletion, "HistoryFolderRenamed296", null);
        AssertSnapshot(Assert.Single(await EventsAsync(fixture, "workspace.folder", folder.Id), item => Equals(item["EventType"], "workspace.folder.created")), folder.Name, null);
        await AssertRemovedAsync(fixture, folderLink);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.False(await db.Set<Dictionary<string, object?>>("WorkspaceFolder").AnyAsync(item => EF.Property<string>(item, "EID") == folder.Id));
        Assert.False(await db.Set<Dictionary<string, object?>>("BusinessChange").AnyAsync(item => EF.Property<string>(item, "TargetId") == position.RootFolderId && EF.Property<string>(item, "EventType") == "workspace.folder.created"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Folder与普通引用审计失败、真实Flush后请求取消均保持对象、关系和事件原状")]
    public async Task FolderAndReferences_AuditFailureAndRequestCancellationRollBackAllFacts()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var historyRequests = new ObjectHistoryHttpTestRequests(fixture);
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var folder = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "RollbackFolder296");
        var destination = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "RollbackDestination296");
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "RollbackPart296");
        var destinationPosition = position with { LinkIds = [.. position.LinkIds, destination.LinkId] };
        foreach (var eventType in new[] { "workspace.folder.created", "workspace.folder.updated", "workspace.folder.deleted", "workspace.reference.added", "workspace.reference.removed" })
        foreach (var cancel in new[] { false, true })
        {
            // removed逐旧边登记时遵守recorder只登记契约；删除后Flush取消由folder.deleted覆盖同一组旧关系事件。
            if (cancel && eventType == "workspace.reference.removed") continue;
            var before = await SnapshotAsync(fixture);
            var reached = false; var flushed = false;
            using var cancellation = new CancellationTokenSource();
            fixture.Fault.AfterRegister = async (change, token) =>
            {
                if (change.EventType != eventType) return;
                reached = true;
                if (!cancel) throw new InvalidOperationException("injected Workspace business audit failure");
                var context = fixture.Services.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext!;
                await context.RequestServices.GetRequiredService<IPlatformTransactionExecutor>().FlushAsync(token);
                flushed = true;
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            };
            try
            {
                var action = eventType switch
                {
                    "workspace.folder.created" => "create",
                    "workspace.folder.updated" => "editObject",
                    "workspace.reference.added" => "add",
                    _ => "deleteObject"
                };
                var suffix = eventType switch
                {
                    "workspace.folder.created" => "folders",
                    "workspace.reference.added" => "references",
                    "workspace.reference.removed" => part.LinkId,
                    _ => folder.LinkId
                };
                var method = action == "editObject" ? HttpMethod.Put : action == "deleteObject" ? HttpMethod.Delete : HttpMethod.Post;
                object body = action switch
                {
                    "create" => new { position, values = new { ENAME = "CancelledFolder296" } },
                    "editObject" => new { position, values = new { ENAME = "must rollback", Description = "must rollback" } },
                    "add" => new { position = destinationPosition, target = new { modelKey = "Part", id = part.Id } },
                    _ => new { position }
                };
                using var request = new HttpRequestMessage(method, "/api/workspace/content/" + suffix)
                {
                    Content = JsonContent.Create(body, options: new JsonSerializerOptions())
                };
                request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder." + action).Position);
                if (cancel)
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    {
                        using var response = await admin.SendAsync(request, cancellation.Token);
                        Assert.Fail($"请求未进入取消点，HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                    });
                else
                {
                    using var response = await admin.SendAsync(request);
                    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                }
            }
            finally { fixture.Fault.AfterRegister = null; }
            Assert.True(reached, eventType);
            if (cancel) Assert.True(flushed, eventType);
            Assert.Equal(before, await SnapshotAsync(fixture));
        }
    }

    private static async Task<string> SnapshotAsync(BusinessAuditHostFixture fixture)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var snapshot = new Dictionary<string, string[]>();
        foreach (var model in new[] { "WorkspaceFolder", "Part", "WorkspaceFolderContent", "BusinessChange" })
        {
            var idField = model == "WorkspaceFolderContent" ? "LID" : "EID";
            var rows = await db.Set<Dictionary<string, object?>>(model).AsNoTracking().OrderBy(row => EF.Property<string>(row, idField)).ToArrayAsync();
            snapshot[model] = rows.Select(row => JsonSerializer.Serialize(row.OrderBy(pair => pair.Key, StringComparer.Ordinal))).ToArray();
        }
        return JsonSerializer.Serialize(snapshot);
    }

    private static Task<HttpResponseMessage> CommandAsync(BusinessAuditHostFixture fixture, HttpClient client, string action, HttpMethod method, string suffix, object body) =>
        FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.folder." + action), method, "/api/workspace/content/" + suffix, body);

    private static async Task<Dictionary<string, object?>> LinkAsync(BusinessAuditHostFixture fixture, string id)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "LID") == id);
    }
    private static async Task<Dictionary<string, object?>[]> IncomingAsync(BusinessAuditHostFixture fixture, string model, string id)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
            .Where(row => EF.Property<string>(row, "ChildModelKey") == model && EF.Property<string>(row, "CEID") == id).ToArrayAsync();
    }
    private static async Task<Dictionary<string, object?>[]> EventsAsync(BusinessAuditHostFixture fixture, string resource, string id)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
            .Where(row => EF.Property<string>(row, "Resource") == resource && EF.Property<string>(row, "TargetId") == id).ToArrayAsync();
    }
    private static async Task<int> RecordCountAsync(BusinessAuditHostFixture fixture)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("BusinessChange").CountAsync();
    }
    private static async Task AssertAddedAsync(BusinessAuditHostFixture fixture, Dictionary<string, object?> link)
    {
        var record = Assert.Single(await EventsAsync(fixture, "workspace.content", (string)link["LID"]!), row => Equals(row["EventType"], "workspace.reference.added"));
        AssertRelationship(record, link, "After");
        Assert.Equal(link["LCreatedBy"], record["ActorId"]); Assert.Equal(link["LCreatedAt"], record["OccurredAt"]);
    }
    private static async Task AssertRemovedAsync(BusinessAuditHostFixture fixture, Dictionary<string, object?> link)
    {
        var record = Assert.Single(await EventsAsync(fixture, "workspace.content", (string)link["LID"]!), row => Equals(row["EventType"], "workspace.reference.removed"));
        AssertRelationship(record, link, "Before");
        foreach (var field in JsonSerializer.Deserialize<JsonElement>((string)record["FieldsJson"]!).EnumerateArray())
            Assert.Equal(JsonValueKind.Null, field.GetProperty("After").GetProperty("Value").ValueKind);
    }
    private static void AssertRelationship(Dictionary<string, object?> record, Dictionary<string, object?> link, string side)
    {
        var fields = JsonSerializer.Deserialize<JsonElement>((string)record["FieldsJson"]!);
        foreach (var (field, kind, value) in new[] { ("PEID", "workspace.folder", (string)link["PEID"]!), ("ChildModelKey", "text", (string)link["ChildModelKey"]!), ("CEID", (string)link["ChildModelKey"]!, (string)link["CEID"]!) })
        {
            var fact = Assert.Single(fields.EnumerateArray(), item => item.GetProperty("Field").GetString() == field).GetProperty(side);
            Assert.Equal(kind, fact.GetProperty("Kind").GetString()); Assert.Equal(value, fact.GetProperty("Value").GetString());
        }
    }
    private static void AssertSnapshot(Dictionary<string, object?> record, string name, string? code)
    {
        var fields = JsonSerializer.Deserialize<JsonElement>((string)record["FieldsJson"]!);
        foreach (var (key, value) in new[] { ("ObjectName", name), ("ObjectCode", code) })
        {
            var fact = Assert.Single(fields.EnumerateArray(), item => item.GetProperty("Field").GetString() == key);
            Assert.Equal(JsonValueKind.Null, fact.GetProperty("Before").ValueKind);
            Assert.Equal("text", fact.GetProperty("After").GetProperty("Kind").GetString()); Assert.Equal(value, fact.GetProperty("After").GetProperty("Value").GetString());
        }
    }
    private sealed class AuditTicks { internal long Count; }
    private sealed class AdvancingStamp(HttpContextEntityAuditStampProvider inner, AuditTicks ticks) : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent()
        {
            var stamp = inner.GetCurrent();
            return new(stamp.ActorId, stamp.OccurredAtUtc.AddMilliseconds(Interlocked.Increment(ref ticks.Count)));
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipBackfillSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "三模型真实创建、SQL待补齐OR分页、可信批次及未知账号受控人工补齐")]
    public async Task ThreeModels_AutomaticBatchLeavesUnknownAccountsForManualInitialization_AndPreservesOldAudit()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var department = await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var targets = new List<Target>();
        foreach (var (model, kind, action) in Kinds)
        {
            foreach (var state in new[] { "complete", "known", "department", "unknown" })
            {
                var target = await CreateAsync(fixture, admin, position, model, kind, action, model + "-" + state);
                targets.Add(target);
                var row = await RowAsync(fixture, target);
                Assert.Equal(fixture.ActorId, row["OwnerId"]);
                Assert.Equal(department, row["OwnerDepartmentId"]);
                if (state == "complete")
                {
                    using var forged = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.editObject"), HttpMethod.Put,
                        $"/api/workspace/content/{target.LinkId}", new { position, values = new { OwnerId = "forged-owner", OwnerDepartmentId = "forged-department" } });
                    Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
                    using var saved = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.editObject"), HttpMethod.Put,
                        $"/api/workspace/content/{target.LinkId}", new { position, values = new { Description = "ordinary update" } });
                    await ReadOkAsync(saved);
                    var persisted = await RowAsync(fixture, target);
                    foreach (var name in new[] { "OwnerId", "OwnerDepartmentId", "CreatedBy", "CreatedAt" }) Assert.Equal(row[name], persisted[name]);
                    continue;
                }
                using var scope = fixture.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                var query = db.Set<Dictionary<string, object?>>(model).Where(row => EF.Property<string>(row, "EID") == target.Id);
                // 冻结旧行用于迁移/补齐消费者取证；不是普通保存或前端伪造归属。
                if (state == "department")
                    await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string?>(row, "OwnerDepartmentId"), (string?)null));
                else
                    await query.ExecuteUpdateAsync(setters => setters
                        .SetProperty(row => EF.Property<string?>(row, "OwnerId"), (string?)null)
                        .SetProperty(row => EF.Property<string?>(row, "OwnerDepartmentId"), (string?)null)
                        .SetProperty(row => EF.Property<string>(row, "CreatedBy"), state == "unknown" ? "missing-account" : fixture.ActorId));
            }
        }
        var before = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var target in targets) before[target.Id] = await RowAsync(fixture, target);
        var ownership = Point(fixture, "workspace.folder.initializeOwnership");
        using var pending = await FeatureHttpTestRequests.SendAsync(admin, ownership, HttpMethod.Get, PendingUrl(position, pageSize: 2));
        var page = await ReadOkAsync(pending);
        Assert.Equal(9, page.GetProperty("total").GetInt32());
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain("complete", page.GetRawText(), StringComparison.Ordinal);
        foreach (var model in new[] { "Part", "Document" })
        {
            var slug = model.ToLowerInvariant();
            using var ownerPending = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, $"{slug}.{slug}.initializeOwnership"), HttpMethod.Get,
                $"/api/{slug}/{(model == "Part" ? "parts" : "documents")}/ownership/pending?page=1&pageSize=2");
            var ownPage = await ReadOkAsync(ownerPending);
            Assert.Equal(3, ownPage.GetProperty("total").GetInt32());
            Assert.Equal(2, ownPage.GetProperty("items").GetArrayLength());
        }
        var selected = targets.Where(target => !target.Name.EndsWith("complete", StringComparison.Ordinal)).ToArray();
        var batchBody = new { position, linkIds = selected.Select(target => target.LinkId).ToArray() };
        using var batch = await FeatureHttpTestRequests.SendAsync(admin, ownership, HttpMethod.Post, "/api/workspace/ownership/initialize", batchBody);
        var receipt = await ReadOkAsync(batch);
        Assert.Equal(6, receipt.GetProperty("changed").GetInt32());
        Assert.Equal(3, receipt.GetProperty("unresolved").GetInt32());
        using var retry = await FeatureHttpTestRequests.SendAsync(admin, ownership, HttpMethod.Post, "/api/workspace/ownership/initialize", batchBody);
        Assert.Equal(0, (await ReadOkAsync(retry)).GetProperty("changed").GetInt32());
        foreach (var target in targets)
        {
            var current = await RowAsync(fixture, target);
            foreach (var audit in new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" }) Assert.Equal(before[target.Id][audit], current[audit]);
            if (target.Name.EndsWith("unknown", StringComparison.Ordinal))
            {
                Assert.Null(current["OwnerId"]);
                using var automatic = await FeatureHttpTestRequests.SendAsync(admin, ownership, HttpMethod.Post,
                    $"/api/workspace/content/{target.LinkId}/ownership", new { position });
                Assert.Equal(HttpStatusCode.Conflict, automatic.StatusCode);
                using var manual = await FeatureHttpTestRequests.SendAsync(admin, ownership, HttpMethod.Post,
                    $"/api/workspace/content/{target.LinkId}/ownership", new { position, accountId = fixture.ActorId });
                Assert.True((await ReadOkAsync(manual)).GetProperty("changed").GetBoolean());
                Assert.Equal("missing-account", (await RowAsync(fixture, target))["CreatedBy"]);
            }
            else
            {
                Assert.Equal(fixture.ActorId, current["OwnerId"]);
                Assert.Equal(department, current["OwnerDepartmentId"]);
            }
        }
        using var finalPending = await FeatureHttpTestRequests.SendAsync(admin, ownership, HttpMethod.Get, PendingUrl(position));
        Assert.Equal(0, (await ReadOkAsync(finalPending)).GetProperty("total").GetInt32());
        using var finalScope = fixture.Services.CreateScope();
        var changes = await finalScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("BusinessChange")
            .Where(row => EF.Property<string>(row, "EventType") == "entity.ownership.backfilled").ToArrayAsync();
        Assert.Equal(9, changes.Length);
        foreach (var target in targets.Where(target => target.Name.EndsWith("department", StringComparison.Ordinal)))
        {
            var record = Assert.Single(changes, row => Equals(row["TargetId"], target.Id));
            Assert.Single(JsonSerializer.Deserialize<JsonElement>((string)record["FieldsJson"]!).EnumerateArray());
        }
    }

    internal static string PendingUrl(Position position, int page = 1, int pageSize = 20) =>
        "/api/workspace/ownership/pending?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
        + $"&page={page}&pageSize={pageSize}";

    internal static readonly (string Model, string Kind, string Action)[] Kinds =
        [("WorkspaceFolder", "folders", "create"), ("Part", "parts", "createPart"), ("Document", "documents", "createDocument")];
    internal sealed record Position(string SpaceId, string RootFolderId, string[] LinkIds);
    internal sealed record Target(string Model, string Id, string LinkId, string Name);
    internal static FeaturePoint Point(BusinessAuditHostFixture fixture, string behavior) => fixture.FeatureCatalog.First(point => point.BehaviorKey == behavior);
    internal static async Task<JsonElement> ReadOkAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        return JsonSerializer.Deserialize<JsonElement>(text);
    }
    internal static async Task<string> InitializeAsync(BusinessAuditHostFixture fixture, HttpClient client)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, Point(fixture, "organization.company.initialize"), HttpMethod.Post, "/api/organization/initialization");
        return (await ReadOkAsync(response)).GetProperty("departmentId").GetString()!;
    }
    internal static async Task<Position> ContainerAsync(BusinessAuditHostFixture fixture, HttpClient client)
    {
        using var initialized = await FeatureHttpTestRequests.SendAsync(client, Point(fixture, "workspace.spaces.access"), HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        var root = await ReadOkAsync(initialized);
        var position = new Position(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
        var target = await CreateAsync(fixture, client, position, "WorkspaceFolder", "folders", "create", "归属维护目录");
        return position with { LinkIds = [target.LinkId] };
    }
    internal static async Task<Target> CreateAsync(BusinessAuditHostFixture fixture, HttpClient client, Position position,
        string model, string kind, string action, string name)
    {
        var values = new Dictionary<string, object?> { ["ENAME"] = name };
        if (model != "WorkspaceFolder") values["ECODE"] = name;
        var feature = Point(fixture, "workspace.folder." + action);
        var execution = model switch { "WorkspaceFolder" => "workspace.folder.create", "Part" => "workspace.content.createPart",
            "Document" => "workspace.content.createDocument", _ => throw new ArgumentOutOfRangeException(nameof(model)) };
        var declaration = Assert.Single(fixture.Services.GetRequiredService<ICP.Platform.Kernel.ModulePlanning.CanonicalModulePlan>()
            .ActionDataAccessCatalog, binding => binding.Execution == execution && binding.FeaturePositions.Contains(feature.Position));
        using var response = await FeatureHttpTestRequests.SendAsync(client, feature, HttpMethod.Post,
            "/api/workspace/content/" + kind, new { position, values }, declaration);
        await ReadOkAsync(response);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var row = await db.Set<Dictionary<string, object?>>(model).AsNoTracking().SingleAsync(row => EF.Property<string>(row, "ENAME") == name);
        var id = (string)row["EID"]!;
        var link = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().SingleAsync(row => EF.Property<string>(row, "CEID") == id);
        return new(model, id, (string)link["LID"]!, name);
    }
    internal static async Task<Dictionary<string, object?>> RowAsync(BusinessAuditHostFixture fixture, Target target)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>(target.Model).AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "EID") == target.Id);
    }
}

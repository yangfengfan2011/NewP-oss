using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

// 只采集现有组合HTTP请求；计数、原SQL回放与锁时点复用308/314仪器。
public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占比较Workspace单对象和Part/Document双对象的真实创建上传编辑删除成本")]
    public async Task WorkspaceObjects_RecordsSingleAndMixedExecutionCosts()
    {
        var cost = new ClipboardCost(); var clock = new FolderCopyLockClock();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>(
            (_, options) => options.AddInterceptors(cost, clock, new FolderCopyTransactionClock(clock))), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var parent = await Folder(fixture, admin, root, "组合成本父容器315");
        var other = await Folder(fixture, admin, root, "组合成本另一引用315");
        var entry = Point(fixture, "workspace.spaces.access");
        var createPart = Point(fixture, "workspace.folder.createPart");
        var createDocument = Point(fixture, "workspace.folder.createDocument");
        var edit = RowPoint(fixture, "edit"); var delete = RowPoint(fixture, "delete");
        var selectedDelete = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace"
            && point.Key == "deleteObjects" && point.ActionScope == "list.pageActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "WorkspaceObjectsCost315", "组合执行成本",
            "workspace-objects-cost315", "组合成本普通主体", entry, createPart, createDocument, edit, delete, selectedDelete);
        using var ordinary = actor.Client;
        var parentId = FolderId(parent, await Rows(fixture, "WorkspaceFolderContent"));
        DataScopeDirectSubjectDto[] subjects = [
            new("Cost315All", "all", null, 0, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow" }),
            new("Cost315Actor", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow", ["authorize"] = "deny" }),
            new("Cost315Creator", "creator", null, 5, new Dictionary<string, string> { ["delete"] = "allow" }) ];
        var authorize = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "workspace.folder.authorize" && point.Key == "authorize");
        using (var configured = await FeatureHttpTestRequests.SendAsync(admin, authorize, HttpMethod.Put,
            "/api/workspace/content/" + parent.LinkIds[^1] + "/permissions?queryScope="
                + Uri.EscapeDataString(JsonSerializer.Serialize(new { spaceId = root.SpaceId, rootFolderId = root.RootFolderId, linkIds = root.LinkIds })),
            new DataScopeObjectConfiguration("workspace.folder", parentId, subjects)))
            Assert.Equal(HttpStatusCode.NoContent, configured.StatusCode);
        string connectionString;
        using (var scope = fixture.Services.CreateScope()) connectionString = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
        var catalog = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        using var timing = new ClipboardSqlTiming(catalog); using var permissions = new ClipboardPermissionQueries(catalog);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var stage = Environment.GetEnvironmentVariable("NEWP_EXECUTION_COST_STAGE") ?? "candidate";
        Assert.Contains(stage, new[] { "baseline", "candidate" });
        var run = Path.Combine(ClipboardCostEvidenceRoot(), "315", "object-cost-" + stage + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        await File.WriteAllTextAsync(Path.Combine(run, "candidate.json"), JsonSerializer.Serialize(new
        { stage, productionBaseline = "464bc966edca1b1eccc5184e23490bd2387cdfb1", plan.FeatureCatalog, plan.ActionDataAccessCatalog }));
        var partValues = new { ECODE = "COST315-PART", ENAME = "组合Part成本" };
        var docValues = new { ECODE = "COST315-DOC", ENAME = "组合Document成本" };
        await Measure("part-create", 1, 1, () => Send(createPart, "workspace.content.createPart", HttpMethod.Post,
            "/api/workspace/content/parts", new { position = parent, values = partValues }));
        var part = await Target("Part", partValues.ECODE);
        JsonElement staged = default;
        await Measure("document-stage", 1, 0, async () => staged = await Stage(docValues));
        await Measure("document-create", 1, 1, () => Send(createDocument, "workspace.content.createDocument", HttpMethod.Post,
            "/api/workspace/content/documents", new { position = parent, values = docValues,
                uploads = new { mainUploadId = staged.GetProperty("uploadId").GetString()! } }));
        var document = await Target("Document", docValues.ECODE);
        await Measure("part-edit-values", 1, 1, () => Send(edit, "workspace.content.edit", HttpMethod.Post,
            "/api/workspace/content/" + part.Lid + "/edit-values", new { position = parent }));
        await Measure("part-update", 1, 1, () => Send(edit, "workspace.content.edit", HttpMethod.Put,
            "/api/workspace/content/" + part.Lid, new { position = parent, values = new { ECODE = partValues.ECODE, ENAME = "组合Part已编辑" } }));
        await AddOtherReference("Document", document.Id);
        await Measure("document-delete", 1, 2, () => Send(delete, "workspace.content.delete", HttpMethod.Delete,
            "/api/workspace/content/" + document.Lid, new { position = parent }));
        // 两对象成本使用同一普通创建主体；准备期另建真实Document主文件，所有目标均有两个入向引用。
        var mixedValues = new { ECODE = "COST315-MIXED-DOC", ENAME = "双对象Document" };
        var mixedStage = await Stage(mixedValues);
        await Send(createDocument, "workspace.content.createDocument", HttpMethod.Post, "/api/workspace/content/documents",
            new { position = parent, values = mixedValues, uploads = new { mainUploadId = mixedStage.GetProperty("uploadId").GetString()! } });
        var mixedDocument = await Target("Document", mixedValues.ECODE);
        await AddOtherReference("Part", part.Id); await AddOtherReference("Document", mixedDocument.Id);
        await Measure("mixed-selected-delete", 2, 4, () => Send(selectedDelete, "workspace.content.delete", HttpMethod.Post,
            "/api/workspace/content/delete", new { position = parent, linkIds = new[] { part.Lid, mixedDocument.Lid } }));
        output.WriteLine("Workspace object cost artifacts: " + run);

        ActionDataAccessRequirement? Declaration(string execution, FeaturePoint feature)
        {
            var result = plan.ActionDataAccessCatalog.SingleOrDefault(item => item.Execution == execution
                && item.ViewKey == "workspace.folder.list" && item.FeaturePositions.Contains(feature.Position));
            if (result is null) Assert.Equal("baseline", stage);
            return result;
        }
        async Task Send(FeaturePoint feature, string execution, HttpMethod method, string path, object body)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, feature, method, path, body, Declaration(execution, feature));
            Assert.True(response.StatusCode == HttpStatusCode.OK, execution + ": HTTP " + (int)response.StatusCode);
            await response.Content.ReadAsByteArrayAsync();
        }
        async Task<JsonElement> Stage(object values)
        {
            using var request = DocumentFilesSqlServerHttpApiTests.UploadRequest(fixture, parent, values,
                new ByteArrayContent([0x25, 0x50, 0x44, 0x46, 0x31]), "cost315.pdf");
            if (Declaration("workspace.content.createDocument", createDocument) is { } declaration)
                request.Headers.Add("X-NewP-Data-Declaration", declaration.Id);
            using var response = await ordinary.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.OK, "document-stage: HTTP " + (int)response.StatusCode);
            return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsByteArrayAsync());
        }
        async Task<(string Id, string Lid)> Target(string model, string code)
        {
            var row = Assert.Single(await Rows(fixture, model), row => Equals(row["ECODE"], code));
            var id = (string)row["EID"]!;
            var link = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], model)
                && Equals(row["CEID"], id) && Equals(row["PEID"], parentId));
            return (id, (string)link["LID"]!);
        }
        Task AddOtherReference(string model, string id) => Command(fixture, admin, "workspace.folder.add",
            "/api/workspace/content/references", new { position = other, target = new { modelKey = model, id } });
        async Task Measure(string operation, int objects, int references, Func<Task> consume)
        {
            cost.Begin(); timing.Begin(); clock.Begin(); permissions.Begin();
            var watch = Stopwatch.StartNew(); object database; object sqlTiming;
            try { await consume(); }
            finally { watch.Stop(); database = cost.End(); sqlTiming = timing.End(); permissions.End(); }
            var request = new { stage, operation, objects, references, ordinaryRoles = 1, parentDirectSubjects = 3, pooling = false,
                fileBytes = 5, database, sqlTiming, elapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
                graphLockWaitMs = clock.WaitMs, graphLockHoldMs = clock.HoldMs, transactionMs = clock.TransactionMs };
            await File.WriteAllTextAsync(Path.Combine(run, operation + "-request.json"), JsonSerializer.Serialize(request));
            var permissionPlans = await permissions.ExplainAsync(connectionString, Path.Combine(run, operation + "-replay"));
            await File.WriteAllTextAsync(Path.Combine(run, operation + ".json"), JsonSerializer.Serialize(new { request, permissionPlans },
                new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

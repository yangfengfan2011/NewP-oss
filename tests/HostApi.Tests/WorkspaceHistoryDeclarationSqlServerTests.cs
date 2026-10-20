using System.Net;
using System.Text.Json;
using System.Net.Http.Json;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceHistoryDeclarationSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实Workspace零组件履历由按钮read声明授权，不能继续额外要求history")]
    public async Task WorkspacePart_ReadDeclarationSucceedsWithoutHistory_AndReadRevocationStillDenies()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "DeclaredHistory301");
        var history = Point(fixture, "workspace.folder.history");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "DeclaredHistory301", "声明履历", "declared-history301", "声明履历账号",
            Point(fixture, "workspace.spaces.access"), history);
        using var ordinary = actor.Client;
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var list = plan.ActionDataAccessCatalog.Single(item => item.ModuleId == "Workspace"
            && item.ViewKey == "workspace.history.list" && item.Surface == "read");
        var entered = HistoryButton(plan, "history");
        var exported = HistoryButton(plan, "exportAll");
        Assert.Equal(new[] { "read" }, list.Actions);
        var configuration = new DataScopeObjectDto("part.part", part.Id,
            [new("Declared301", "all", null, 1, new Dictionary<string, string> { ["read"] = "allow" })]);
        await Configure(configuration);
        var path = "/api/workspace/content/" + part.LinkId + "/history?queryScope="
            + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, history, HttpMethod.Get, path, dataAccess: list))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, history, HttpMethod.Get, path)) Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, history, HttpMethod.Get, path, dataAccess: entered)) Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
        var checkPath = path.Replace("/history?", "/history/check/enter?", StringComparison.Ordinal);
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, history, HttpMethod.Get, checkPath)) Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var check = await FeatureHttpTestRequests.SendAsync(ordinary, history, HttpMethod.Get, checkPath, dataAccess: entered)) Assert.Equal(HttpStatusCode.NoContent, check.StatusCode);
        using (var preview = await FeatureHttpTestRequests.SendAsync(ordinary, history, HttpMethod.Get,
            path.Replace("/history?", "/history/check/preview?", StringComparison.Ordinal), dataAccess: HistoryButton(plan, "output")))
            Assert.Equal(HttpStatusCode.NoContent, preview.StatusCode);
        using (var detail = await FeatureHttpTestRequests.SendAsync(ordinary, history, HttpMethod.Get,
            path.Replace("/history?", "/history/check/detail?", StringComparison.Ordinal), dataAccess: HistoryButton(plan, "view")))
            Assert.Equal(HttpStatusCode.NoContent, detail.StatusCode);
        var exportPath = path.Replace("/history?", "/history/export?", StringComparison.Ordinal);
        using (var output = await FeatureHttpTestRequests.SendAsync(ordinary, history, HttpMethod.Post, exportPath,
            new ObjectHistoryOutputRequest("all", new()), exported)) Assert.Equal(HttpStatusCode.OK, output.StatusCode);
        using (var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, history, HttpMethod.Post, exportPath,
            new ObjectHistoryOutputRequest("all", new()), HistoryButton(plan, "output"))) Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
        await Configure(configuration with { Subjects = [configuration.Subjects[0] with { Actions = new Dictionary<string, string> { ["read"] = "deny" } }] });
        using (var denied = await FeatureHttpTestRequests.SendAsync(ordinary, history, HttpMethod.Get, path, dataAccess: list)) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using (var denied = await FeatureHttpTestRequests.SendAsync(ordinary, history, HttpMethod.Post, exportPath,
            new ObjectHistoryOutputRequest("all", new()), exported)) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        async Task Configure(DataScopeObjectDto config)
        {
            var manage = Point(fixture, "part.part.authorize");
            using var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put,
                "/api/part/parts/" + part.Id + "/permissions", config,
                OwnershipDeclaration(fixture, "part.part.authorization.manage", manage));
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实三类Workspace目标的声明入口、列表、详情、输出及来源重判，缺失/换绑声明不能免检")]
    public async Task EveryWorkspaceHistoryExit_RechecksItsOwnDeclarationAndFullHeterogeneousSource()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var history = Point(fixture, "workspace.folder.history");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "HistoryExits301", "声明出口", "history-exits301", "声明出口账号",
            Point(fixture, "workspace.spaces.access"), history);
        using var ordinary = actor.Client;
        foreach (var (model, kind, action) in new[] { ("WorkspaceFolder", "folders", "create"), ("Part", "parts", "createPart"), ("Document", "documents", "createDocument") })
        {
            var target = await CreateAsync(fixture, admin, position, model, kind, action, "HistoryExits301" + model);
            var resource = model == "WorkspaceFolder" ? "workspace.folder" : model.ToLowerInvariant() + "." + model.ToLowerInvariant();
            var encoded = Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var basePath = "/api/workspace/content/" + target.LinkId + "/history";
            var query = "?queryScope=" + encoded;
            var config = new DataScopeObjectDto(resource, target.Id, [new("exits301-" + model, "all", null, 1,
                new Dictionary<string, string> { ["read"] = "allow" })]);
            using (var saved = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.authorize"), HttpMethod.Put,
                "/api/workspace/content/" + target.LinkId + "/permissions" + query, config,
                OwnershipDeclaration(fixture, "workspace.content.authorization.manage", Point(fixture, "workspace.folder.authorize")))) Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
            using (var check = await Send("workspace.history.enter", HttpMethod.Get, basePath + "/check/enter" + query)) Assert.Equal(HttpStatusCode.NoContent, check.StatusCode);
            using var listed = await Send("workspace.history.list", HttpMethod.Get, basePath + query);
            var page = await ReadOkAsync(listed);
            var record = page.GetProperty("items")[0].GetProperty("id").GetString()!;
            using (var detail = await Send("workspace.history.detail", HttpMethod.Get, basePath + "/" + record + query)) Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            foreach (var output in new[] { "export", "print" })
            {
                using var response = await Send("workspace.history." + output, HttpMethod.Post, basePath + "/" + output + query,
                    new ObjectHistoryOutputRequest("selected", new(), [record]));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var forged = await Send("workspace.history." + output, HttpMethod.Post, basePath + "/" + output + query,
                    new ObjectHistoryOutputRequest("selected", new(), [record, "foreign-record"]));
                Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
            }
            using (var badSource = await Send("workspace.history.list", HttpMethod.Get, basePath + "?queryScope="
                + Uri.EscapeDataString(JsonSerializer.Serialize(position with { LinkIds = [] }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))
                Assert.Equal(HttpStatusCode.NotFound, badSource.StatusCode);
            using (var bareId = await Send("workspace.history.list", HttpMethod.Get, basePath.Replace(target.LinkId, target.Id, StringComparison.Ordinal) + query))
                Assert.Equal(HttpStatusCode.NotFound, bareId.StatusCode);
            // 允许预检后移除真实来源，不能将允许结果当后续列表或输出凭据。
            using (var removed = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.removeSelected"), HttpMethod.Post,
                "/api/workspace/content/references/remove", new { position, linkIds = new[] { target.LinkId } }, WorkspaceClipboardSqlServerHttpApiTests.ClipboardDeclaration(fixture, Point(fixture, "workspace.folder.removeSelected"))))
                Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
            using var stale = await Send("workspace.history.list", HttpMethod.Get, basePath + query);
            Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        }

        async Task<HttpResponseMessage> Send(string execution, HttpMethod method, string path, object? body = null)
        {
            var operation = execution[(execution.LastIndexOf('.') + 1)..];
            var key = operation == "enter" ? "history" : operation == "preview" ? "output" : operation == "detail" ? "view"
                : operation + (body is ObjectHistoryOutputRequest { Mode: "selected" } ? "Selected" : "All");
            var requirement = operation == "list" ? plan.ActionDataAccessCatalog.Single(item => item.Execution == execution) : HistoryButton(plan, key);
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-NewP-Feature", history.Position);
            request.Headers.Add(requirement.BusinessBinding is null ? "X-NewP-Data-Declaration" : "X-NewP-Button", requirement.Id);
            if (body is not null) request.Content = JsonContent.Create(body);
            return await ordinary.SendAsync(request);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "read停用与管理员数据豁免不放宽Workspace私人来源和真实LID路径")]
    public async Task DisabledReadAndAdministratorDataExemption_StillRespectPrivateOccurrencesAndPaths()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true, actionStates: new Dictionary<string, bool> { ["read"] = false });
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var historyRequests = new ObjectHistoryHttpTestRequests(fixture);
        var history = Point(fixture, "workspace.folder.history");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "HistoryPrivate301", "私人来源", "history-private301", "私人来源账号",
            Point(fixture, "workspace.spaces.access"), history);
        using var ordinary = actor.Client;
        using var ensured = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.spaces.access"), HttpMethod.Post, "/api/workspace/spaces/personal/ensure");
        var root = await ReadOkAsync(ensured);
        var position = new Position(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
        var folder = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "HistoryPrivate301");
        var childPosition = position with { LinkIds = [folder.LinkId] };
        var part = await CreateAsync(fixture, admin, childPosition, "Part", "parts", "createPart", "PrivatePart301");
        var scope = "?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(childPosition, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var path = "/api/workspace/content/" + part.LinkId + "/history";
        using (var allowed = await historyRequests.SendAsync(admin, history, HttpMethod.Get, path + scope)) Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        foreach (var suffix in new[] { "", "/check/enter", "/fake-record", "/print", "/export" })
        {
            using var denied = await historyRequests.SendAsync(ordinary, history,
                suffix is "/print" or "/export" ? HttpMethod.Post : HttpMethod.Get, path + suffix + scope,
                suffix is "/print" or "/export" ? new ObjectHistoryOutputRequest("all", new()) : null);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
        using var wrongPath = await historyRequests.SendAsync(admin, history, HttpMethod.Get, path + "?queryScope="
            + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        Assert.Equal(HttpStatusCode.NotFound, wrongPath.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "同一正式业务的双按钮分别声明实际对象read/edit，并由真实HTTP按自身要求裁决")]
    public async Task SameBusinessButtons_KeepIndependentProjectedAndServerDataRequirements()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true, configureServices: services =>
        {
            var original = (CanonicalModulePlan)services.Last(item => item.ServiceType == typeof(CanonicalModulePlan)).ImplementationInstance!;
            var ids = original.Modules.Select(module => module.ModuleId).ToHashSet(StringComparer.Ordinal);
            var reader = new ModuleCapsuleCandidateReader();
            var candidates = original.Modules.Select(module => Path.GetDirectoryName(module.PackageRoot)!).Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(reader.ReadBuiltIn).Where(candidate => ids.Contains(candidate.ModuleId)).ToArray();
            var owner = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Workspace");
            var features = candidates[owner].Features.ToArray();
            var index = Array.FindIndex(features, feature => feature.DataAccess.Any(item => item.BusinessBinding == "workspace.folder.history" && item.ActionKey == "history"));
            var declared = features[index].DataAccess.Single(item => item.BusinessBinding == "workspace.folder.history" && item.ActionKey == "history");
            features[index] = features[index] with { DataAccess = [.. features[index].DataAccess,
                declared with { ViewKey = "workspace.history.withEdit", ObjectRequirements = declared.ObjectRequirements!
                    .Select(item => item.Object == "Part" ? item with { Actions = ["read", "edit"] } : item).ToArray() }] };
            candidates[owner] = candidates[owner] with { Features = features };
            var compiled = new CanonicalModulePlanCompiler().Compile("independent-button-requirements", candidates);
            Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
            services.Replace(ServiceDescriptor.Singleton(compiled.Plan!));
        });
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var target = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "DifferentButtons301");
        var history = Point(fixture, "workspace.folder.history");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "DifferentButtons301", "独立按钮要求", "different-buttons301", "独立按钮账号",
            Point(fixture, "workspace.spaces.access"), history);
        using var ordinary = actor.Client;
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "part.part.authorize"), HttpMethod.Put,
            "/api/part/parts/" + target.Id + "/permissions", new DataScopeObjectDto("part.part", target.Id,
                [new("different-buttons301", "all", null, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "deny" })]),
            OwnershipDeclaration(fixture, "part.part.authorization.manage", Point(fixture, "part.part.authorize"))))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var declarations = plan.ActionDataAccessCatalog.Where(item => item.ModuleId == "Workspace" && item.BusinessBinding == "workspace.folder.history" && item.ActionKey == "history").ToArray();
        Assert.Equal(2, declarations.Length);
        var manifestText = new ICP.Platform.Kernel.PluginPackages.MetadataRuntimeManifestBuilder().Build(Path.GetDirectoryName(typeof(WorkspaceHistoryDeclarationSqlServerTests).Assembly.Location)!,
            plan.Modules.Select(module => module.ModuleId).ToArray(), plan.Modules.Select(module => module.ModuleId).ToArray(), plan.FeatureCatalog, plan.ActionDataAccessCatalog);
        using var manifest = JsonDocument.Parse(manifestText);
        var frontend = manifest.RootElement.GetProperty("modules").EnumerateArray().Single(item => item.GetProperty("moduleId").GetString() == "Workspace").GetProperty("dataAccess");
        foreach (var declaration in declarations)
        {
            Assert.Equal(declaration.GetObjectRequirements().Single(item => item.Object == "Part").Actions,
                frontend.EnumerateArray().Single(item => item.GetProperty("id").GetString() == declaration.Id)
                    .GetProperty("objectRequirements").EnumerateArray().Single(item => item.GetProperty("object").GetString() == "Part")
                    .GetProperty("actions").EnumerateArray().Select(item => item.GetString()!));
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/workspace/content/" + target.LinkId + "/history/check/enter?queryScope="
                + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            request.Headers.Add("X-NewP-Feature", history.Position);
            request.Headers.Add("X-NewP-Button", declaration.Id);
            using var response = await ordinary.SendAsync(request);
            Assert.Equal(declaration.ViewKey == "workspace.history.withEdit" ? HttpStatusCode.Forbidden : HttpStatusCode.NoContent, response.StatusCode);
        }
    }
    private static ActionDataAccessRequirement HistoryButton(CanonicalModulePlan plan, string key) =>
        Assert.Single(plan.ActionDataAccessCatalog, item => item.ModuleId == "Workspace"
            && item.BusinessBinding == "workspace.folder.history" && item.ActionKey == key);

}

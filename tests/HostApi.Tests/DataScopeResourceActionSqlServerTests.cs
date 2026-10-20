using System.Net;
using System.Text.Json;
using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeResourceActionSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "资源实际动作来自owner静态声明，目录解释和保存不混入其他模型动作")]
    public async Task FolderAndDocument_ExposeTheirActualActions_AndRejectUnsupportedReplacement()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var catalog = await FeatureHttpTestRequests.ReadAsync(admin, Point(fixture, "authorization.dataScope.read"), "/api/authorization/data-scope/catalog");
        Assert.DoesNotContain("history", Keys(catalog));
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var folder = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "Action295Folder");
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "Action295Part");
        var document = await CreateAsync(fixture, admin, position, "Document", "documents", "createDocument", "Action295Document");
        var read = fixture.FeatureCatalog.First(point => point.ReadBehaviors.Contains("workspace.folder.read"));
        var manage = fixture.FeatureCatalog.Single(point => point.Key == "authorizeContent" && point.ActionScope == "list.rowActions");
        var partRead = fixture.FeatureCatalog.Single(point => point.ModuleId == "Part" && point.Kind == "navigation" && point.Key == "part.parts");
        var partManage = fixture.FeatureCatalog.Single(point => point.ModuleId == "Part" && point.Key == "authorize" && point.ActionScope == "list.rowActions");
        var documentRead = fixture.FeatureCatalog.Single(point => point.ModuleId == "Document" && point.Kind == "navigation" && point.Key == "document.documents");
        var documentManage = fixture.FeatureCatalog.Single(point => point.ModuleId == "Document" && point.Key == "authorize" && point.ActionScope == "list.rowActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Action295", "实际动作", "action295", "动作账号",
            Point(fixture, "workspace.spaces.access"), read, manage, partRead, partManage, documentRead, documentManage);
        using var ordinary = actor.Client;
        var folderCatalog = await FeatureHttpTestRequests.ReadAsync(ordinary, read, Path(folder) + "/catalog" + Query(), dataAccess: new FeatureHttpTestRequests(fixture).ReadDeclaration("workspace.content.authorization.view", read));
        var documentCatalog = await FeatureHttpTestRequests.ReadAsync(ordinary, read, Path(document) + "/catalog" + Query(), dataAccess: new FeatureHttpTestRequests(fixture).ReadDeclaration("workspace.content.authorization.view", read));
        Assert.Equal(new[] { "authorize", "create", "delete", "edit", "read", "workspaceCopy" }, Keys(folderCatalog));
        Assert.Equal(new[] { "authorize", "create", "delete", "documentCopy", "documentObjectCopy", "download", "edit", "read" }, Keys(documentCatalog));
        // 新独立owner实际接入；Workspace功能头不能冒充独立Document浏览资格。
        using (var wrongFeature = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, "/api/document/documents/" + document.Id + "/permissions/catalog", dataAccess: new FeatureHttpTestRequests(fixture).ReadDeclaration("document.document.authorization.view", documentRead)))
            Assert.Equal(HttpStatusCode.Forbidden, wrongFeature.StatusCode);
        var independentDocument = "/api/document/documents/" + document.Id + "/permissions";
        var ownCatalog = await FeatureHttpTestRequests.ReadAsync(ordinary, documentRead, independentDocument + "/catalog", dataAccess: new FeatureHttpTestRequests(fixture).ReadDeclaration("document.document.authorization.view", documentRead));
        Assert.Equal(Keys(documentCatalog), Keys(ownCatalog));
        var independentPart = "/api/part/parts/" + part.Id + "/permissions";
        var partConfig = await FeatureHttpTestRequests.ReadAsync(ordinary, partManage, independentPart, dataAccess: new FeatureHttpTestRequests(fixture).Declaration(partManage.BehaviorKey, partManage));
        Assert.Equal("part.part", partConfig.GetProperty("resourceKey").GetString());
        Assert.Equal(part.Id, partConfig.GetProperty("objectId").GetString());
        var partInput = new DataScopeObjectDto("part.part", part.Id,
            [new("Action295PartSubject", "all", null, 0, new Dictionary<string, string> { ["read"] = "allow" })]);
        using (var partSaved = await new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary, partManage, HttpMethod.Put, independentPart, partInput))
            Assert.Equal(HttpStatusCode.NoContent, partSaved.StatusCode);
        var partCurrent = await FeatureHttpTestRequests.ReadAsync(ordinary, manage, Path(part) + Query(), dataAccess: new FeatureHttpTestRequests(fixture).Declaration(manage.BehaviorKey, manage));
        Assert.Single(partCurrent.GetProperty("subjects").EnumerateArray());
        var invalid = new DataScopeObjectDto("workspace.folder", folder.Id,
            [new("Action295Invalid", "all", null, 0, new Dictionary<string, string> { ["download"] = "deny" })]);
        using (var rejected = await new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary, manage, HttpMethod.Put, Path(folder) + Query(), invalid))
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using (var retired = await new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary, manage, HttpMethod.Put, Path(folder) + Query(),
            invalid with { Subjects = [invalid.Subjects[0] with { Actions = new Dictionary<string, string> { ["history"] = "allow" } }] }))
            Assert.Equal(HttpStatusCode.BadRequest, retired.StatusCode);
        var unchanged = await FeatureHttpTestRequests.ReadAsync(ordinary, manage, Path(folder) + Query(), dataAccess: new FeatureHttpTestRequests(fixture).Declaration(manage.BehaviorKey, manage));
        Assert.Empty(unchanged.GetProperty("subjects").EnumerateArray());
        var valid = invalid with { ResourceKey = "document.document", ObjectId = document.Id,
            Subjects = [invalid.Subjects[0] with { Id = "Action295Valid" }] };
        using (var saved = await new FeatureHttpTestRequests(fixture).SendButtonAsync(ordinary, documentManage, HttpMethod.Put, independentDocument, valid))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var explanation = await FeatureHttpTestRequests.ReadAsync(ordinary, documentRead, independentDocument + "/explanation", dataAccess: new FeatureHttpTestRequests(fixture).ReadDeclaration("document.document.authorization.view", documentRead));
        Assert.Equal(Keys(documentCatalog).Where(key => key != "create"), Keys(explanation));
        Assert.False(explanation.GetProperty("actions").EnumerateArray().Single(action => action.GetProperty("key").GetString() == "download")
            .GetProperty("allowed").GetBoolean());
        using var scope = fixture.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, actor.AccountId)], "test-trusted-account")) };
        try { Assert.False(await scope.ServiceProvider.GetRequiredService<IDataScopeObjectDecisionProvider>().IsAllowedAsync("document.document", document.Id, "download")); }
        finally { accessor.HttpContext = previous; }

        string Path(Target target) => "/api/workspace/content/" + target.LinkId + "/permissions";
        string Query() => "?queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        static string[] Keys(JsonElement result) => result.GetProperty("actions").EnumerateArray().Select(action => action.GetProperty("key").GetString()!)
            .Order(StringComparer.Ordinal).ToArray();
    }
}

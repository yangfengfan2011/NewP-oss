using System.Net;
using System.Text;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class WorkspaceAcceptanceFixtureTests
{
    [Fact]
    public void PublishedUpgrade_AllowsOnlyTreeOrdinalBackfillAndTheConfirmedWorkspaceTails()
    {
        var (before, after) = TreeOrderUpgrade();
        PublishedComponentAcceptanceFixture.VerifyPreservedPredecessor(before, after, "Workspace");
        var explicitNull = after with { Links = after.Links.Replace("\"TreeOrdinal\":1", "\"TreeOrdinal\":1,\"IsScopeNeutral\":null", StringComparison.Ordinal) };
        PublishedComponentAcceptanceFixture.VerifyPreservedPredecessor(before, explicitNull, "Workspace");
        Assert.Throws<InvalidOperationException>(() => PublishedComponentAcceptanceFixture.VerifyPreservedPredecessor(before, after, "Files"));
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("audit")]
    [InlineData("missing-ordinal")]
    [InlineData("business-ordinal")]
    [InlineData("history-prefix")]
    [InlineData("unexpected-tail")]
    [InlineData("other-data")]
    [InlineData("scope-true")]
    [InlineData("scope-false")]
    public void PublishedUpgrade_StillRejectsChangesOutsideTheConfirmedBackfill(string corruption)
    {
        var (before, after) = TreeOrderUpgrade();
        after = corruption switch
        {
            "identity" => after with { Links = after.Links.Replace("folder-link", "replacement-link", StringComparison.Ordinal) },
            "audit" => after with { Links = after.Links.Replace("original-actor", "different-actor", StringComparison.Ordinal) },
            "missing-ordinal" => after with { Links = after.Links.Replace(",\"TreeOrdinal\":1", "", StringComparison.Ordinal) },
            "business-ordinal" => after with { Links = after.Links.Replace("\"ChildModelKey\":\"Part\"", "\"ChildModelKey\":\"Part\",\"TreeOrdinal\":2", StringComparison.Ordinal) },
            "history-prefix" => after with { History = after.History.Replace("old-prefix", "changed-prefix", StringComparison.Ordinal) },
            "unexpected-tail" => after with { History = after.History.Replace("20261014142313_Workspace_000005_TreeOrderBackfill", "unexpected-migration", StringComparison.Ordinal) },
            "other-data" => after with { PartRow = "changed-object" },
            "scope-true" => after with { Links = after.Links.Replace("\"TreeOrdinal\":1", "\"TreeOrdinal\":1,\"IsScopeNeutral\":true", StringComparison.Ordinal) },
            "scope-false" => after with { Links = after.Links.Replace("\"TreeOrdinal\":1", "\"TreeOrdinal\":1,\"IsScopeNeutral\":false", StringComparison.Ordinal) },
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        Assert.Throws<InvalidOperationException>(() => PublishedComponentAcceptanceFixture.VerifyPreservedPredecessor(before, after, "Workspace"));
    }

    private static (PublishedComponentAcceptanceFixture.Continuity Before, PublishedComponentAcceptanceFixture.Continuity After) TreeOrderUpgrade()
    {
        const string links = """
            [{"LID":"folder-link","PEID":"parent","ChildModelKey":"WorkspaceFolder","LUpdatedBy":"original-actor"},
             {"LID":"part-link","PEID":"parent","ChildModelKey":"Part","LUpdatedBy":"original-actor"}]
            """;
        var before = new PublishedComponentAcceptanceFixture.Continuity("mapping", "object", """
            [{"OwnerModuleId":"Workspace","MigrationId":"old-prefix"}]
            """, Links: links);
        var after = before with
        {
            Links = links.Replace("\"ChildModelKey\":\"WorkspaceFolder\"", "\"ChildModelKey\":\"WorkspaceFolder\",\"TreeOrdinal\":1", StringComparison.Ordinal),
            History = """
                [{"OwnerModuleId":"Workspace","MigrationId":"old-prefix"},
                 {"OwnerModuleId":"Workspace","MigrationId":"20261014142253_Workspace_000004_TreeOrder"},
                 {"OwnerModuleId":"Workspace","MigrationId":"20261014142313_Workspace_000005_TreeOrderBackfill"},
                 {"OwnerModuleId":"Workspace","MigrationId":"20261016054922_Workspace_000006_ScopeNeutral"},
                 {"OwnerModuleId":"Workspace","MigrationId":"20261016132111_Workspace_000007_ObjectCopy"}]
                """
        };
        return (before, after);
    }

    [Fact]
    public async Task EmptyCommand_PreservesNoBodyAndCarriesFeatureAndAntiforgeryHeaders()
    {
        var commands = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/auth/antiforgery-token")
                return Json(HttpStatusCode.OK, "{\"requestToken\":\"fixture-token\"}");
            commands++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/workspace/spaces/personal/ensure", request.RequestUri.AbsolutePath);
            Assert.Null(request.Content);
            Assert.Equal("Workspace/navigation:workspace", Assert.Single(request.Headers.GetValues("X-NewP-Feature")));
            Assert.Equal("fixture-token", Assert.Single(request.Headers.GetValues("X-CSRF-TOKEN")));
            return Json(HttpStatusCode.OK, "{\"initialized\":true}");
        })) { BaseAddress = new Uri("http://127.0.0.1:5000") };

        await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Post, "/api/workspace/spaces/personal/ensure",
            null, "Workspace/navigation:workspace", CancellationToken.None);

        Assert.Equal(1, commands);
    }

    [Fact]
    public async Task FailedAntiforgery_DoesNotSendInitializationCommand()
    {
        var requests = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            requests++;
            Assert.Equal("/api/auth/antiforgery-token", request.RequestUri!.AbsolutePath);
            return Json(HttpStatusCode.ServiceUnavailable, "antiforgery unavailable");
        })) { BaseAddress = new Uri("http://127.0.0.1:5000") };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => WorkspaceAcceptanceFixture.SendAsync(
            client, HttpMethod.Post, "/api/workspace/spaces/public/initialize", null, "Workspace/navigation:workspace", CancellationToken.None));

        Assert.Equal(1, requests);
        Assert.Contains("503", failure.Message);
        Assert.Contains("antiforgery unavailable", failure.Message);
    }

    [Fact]
    public async Task OrphanPreparation_UsesCurrentPersonalRootAndDeletesTheMatchingRelationInsteadOfTheEntity()
    {
        var entry = new FeaturePoint("entry", "Workspace", "workspace", "navigation", "workspace", null, "workspace.spaces.access", [], 0);
        var create = new FeaturePoint("create", "Workspace", "create", "action", "create", "entry", "workspace.folder.create", [], 0)
            { ActionScope = "list.pageActions", StaticScope = "folder" };
        var remove = new FeaturePoint("remove", "Workspace", "remove", "action", "remove", "entry", "workspace.folder.remove", [], 0)
            { ActionScope = "list.rowActions", StaticScope = "folder" };
        var catalog = new AcceptanceFeatureCatalog([entry, create, remove]);
        var deleted = false;
        using var client = new HttpClient(new AsyncHandler(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/auth/antiforgery-token") return Json(HttpStatusCode.OK, "{\"requestToken\":\"fixture-token\"}");
            if (path == "/api/workspace/spaces/personal/ensure")
            {
                Assert.Null(request.Content);
                return Json(HttpStatusCode.OK, "{\"initialized\":true}");
            }
            if (path == "/api/workspace/spaces") return Json(HttpStatusCode.OK,
                "[{\"spaceKind\":\"Public\",\"spaceId\":\"public-space\",\"rootFolderId\":\"public-root\"},{\"spaceKind\":\"Personal\",\"spaceId\":\"personal-space\",\"rootFolderId\":\"personal-root\"}]");
            if (path == "/api/workspace/content/folders")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("personal-space", body.RootElement.GetProperty("position").GetProperty("spaceId").GetString());
                Assert.Equal("personal-root", body.RootElement.GetProperty("position").GetProperty("rootFolderId").GetString());
                Assert.Empty(body.RootElement.GetProperty("position").GetProperty("linkIds").EnumerateArray());
                Assert.Single(body.RootElement.GetProperty("values").EnumerateObject());
                Assert.Equal("source-folder", body.RootElement.GetProperty("values").GetProperty("ENAME").GetString());
                Assert.Equal("create", Assert.Single(request.Headers.GetValues("X-NewP-Feature")));
                return Json(HttpStatusCode.OK, "{\"id\":\"object-EID\",\"fields\":{}}");
            }
            if (path == "/api/links/workspace.content")
            {
                Assert.Contains("anchorId=personal-root", request.RequestUri.Query);
                return Json(HttpStatusCode.OK, "{\"items\":[{\"linkId\":\"other-LID\",\"associatedObject\":{\"id\":\"other-EID\"}},{\"linkId\":\"relation-LID\",\"associatedObject\":{\"id\":\"object-EID\"}}]}");
            }
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("/api/workspace/content/references/relation-LID", path);
            Assert.Equal("remove", Assert.Single(request.Headers.GetValues("X-NewP-Feature")));
            using var removal = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("personal-space", removal.RootElement.GetProperty("position").GetProperty("spaceId").GetString());
            deleted = true;
            return Json(HttpStatusCode.OK, "{\"succeeded\":true}");
        })) { BaseAddress = new Uri("http://127.0.0.1:5000") };

        var folderId = await WorkspaceAcceptanceFixture.PrepareOrphanFolderAsync(client, catalog, "source-folder", CancellationToken.None);

        Assert.Equal("object-EID", folderId);
        Assert.True(deleted);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string text) =>
        new(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("WorkspaceFolder")]
    [InlineData("Part")]
    [InlineData("Document")]
    public async Task ReversePreparation_UsesTypedAnchorAndSourceEntryWithoutForwardPositionScope(string model)
    {
        var entry = new FeaturePoint("source-entry", "Workspace", "workspace", "navigation", "workspace", null, "workspace.spaces.access", [], 0);
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/links/workspace.content", request.RequestUri!.AbsolutePath);
            Assert.Equal("source-entry", Assert.Single(request.Headers.GetValues("X-NewP-Feature")));
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            Assert.Equal(["direction", "anchorModelKey", "anchorId"], query.AllKeys);
            Assert.Equal("reverse", query["direction"]);
            Assert.Equal(model, query["anchorModelKey"]);
            Assert.Equal("same/id:with?reserved", query["anchorId"]);
            Assert.Null(request.Content);
            return Json(HttpStatusCode.OK, "{\"items\":[],\"total\":0}");
        })) { BaseAddress = new Uri("http://127.0.0.1:5000") };

        await WorkspaceAcceptanceFixture.ReadDirectReferencesAsync(client, new([entry]), model, "same/id:with?reserved", CancellationToken.None);
    }

    [Fact]
    public async Task PositionCreation_PreservesFullChainAndUsesReturnedMatchingLinkForTheNewOccurrence()
    {
        var entry = new FeaturePoint("entry", "Workspace", "workspace", "navigation", "workspace", null, "workspace.spaces.access", [], 0);
        var create = new FeaturePoint("create", "Workspace", "create", "action", "create", "entry", "workspace.folder.create", [], 0)
            { ActionScope = "list.pageActions", StaticScope = "folder" };
        var parent = new WorkspaceFolderPosition("space", "root-EID", "parent-EID", ["first-LID"]);
        using var client = new HttpClient(new AsyncHandler(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/auth/antiforgery-token") return Json(HttpStatusCode.OK, "{\"requestToken\":\"fixture-token\"}");
            if (path == "/api/workspace/content/folders")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var position = body.RootElement.GetProperty("position");
                Assert.Equal(["spaceId", "rootFolderId", "linkIds"], position.EnumerateObject().Select(property => property.Name).ToArray());
                Assert.Equal("root-EID", position.GetProperty("rootFolderId").GetString());
                Assert.Equal("first-LID", Assert.Single(position.GetProperty("linkIds").EnumerateArray()).GetString());
                Assert.Equal("child", body.RootElement.GetProperty("values").GetProperty("ENAME").GetString());
                Assert.Equal("create", Assert.Single(request.Headers.GetValues("X-NewP-Feature")));
                return Json(HttpStatusCode.OK, "{\"id\":\"child-EID\",\"fields\":{}}");
            }
            Assert.Equal("/api/links/workspace.content", path);
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            Assert.Equal("parent-EID", query["anchorId"]);
            using var scope = JsonDocument.Parse(query["queryScope"]!);
            Assert.Equal("space", scope.RootElement.GetProperty("spaceId").GetString());
            Assert.Equal("root-EID", scope.RootElement.GetProperty("rootFolderId").GetString());
            Assert.Equal("[\"first-LID\"]", scope.RootElement.GetProperty("linkIds").GetString());
            return Json(HttpStatusCode.OK, "{\"items\":[{\"linkId\":\"other-LID\",\"associatedObject\":{\"id\":\"other-EID\"}},{\"linkId\":\"child-LID\",\"associatedObject\":{\"id\":\"child-EID\"}}]}");
        })) { BaseAddress = new Uri("http://127.0.0.1:5000") };

        var child = await WorkspaceAcceptanceFixture.CreateFolderAtPositionAsync(client, new([entry, create]), parent, "child", CancellationToken.None);

        Assert.Equal("child-EID", child.FolderId);
        Assert.Equal(["first-LID", "child-LID"], child.LinkIds);
        Assert.Equal(["first-LID"], parent.LinkIds);
    }

    [Theory]
    [InlineData("Part", "part.part.read", "/api/part/parts")]
    [InlineData("Document", "document.document.read", "/api/document/documents")]
    public async Task MetadataPreparation_UsesGrantedCreatePointAndThe201FieldsEid(string module, string behavior, string path)
    {
        var entry = new FeaturePoint("entry", module, "entry", "navigation", "entry", null, behavior, [], 0);
        var create = new FeaturePoint("create", module, "create", "action", "create", "entry", "create", [], 0) { ActionScope = "list.pageActions" };
        var created = false;
        using var client = new HttpClient(new AsyncHandler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/auth/antiforgery-token") return Json(HttpStatusCode.OK, "{\"requestToken\":\"fixture-token\"}");
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(path, request.RequestUri.AbsolutePath);
            Assert.Equal("create", Assert.Single(request.Headers.GetValues("X-NewP-Feature")));
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(["ECODE", "ENAME"], body.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
            Assert.Equal("CODE", body.RootElement.GetProperty("ECODE").GetString());
            Assert.Equal("名称", body.RootElement.GetProperty("ENAME").GetString());
            created = true;
            return Json(HttpStatusCode.Created, "{\"id\":\"wrong-top-level-id\",\"fields\":{\"EID\":\"real-EID\"},\"summaries\":{}}");
        })) { BaseAddress = new Uri("http://127.0.0.1:5000") };

        var id = await WorkspaceAcceptanceFixture.CreateMetadataObjectAsync(client, new([entry, create]), module, behavior, path, "CODE", "名称", CancellationToken.None);

        Assert.True(created);
        Assert.Equal("real-EID", id);
    }

    [Fact]
    public async Task RestartPreparation_RebuildsDeletedBrowserContentUsingWorkspacePermissionsAndQueriesReceiptIdentities()
    {
        var entry = new FeaturePoint("entry", "Workspace", "workspace", "navigation", "workspace", null, "workspace.spaces.access", [], 0);
        var features = new AcceptanceFeatureCatalog([entry,
            .. new[] { "create", "createPart", "createDocument" }.Select(action =>
                new FeaturePoint(action, "Workspace", action, "action", action, "entry", action, [], 0)
                    { ActionScope = "list.pageActions", StaticScope = "folder" })]);
        using var roots = JsonDocument.Parse("""
            [{"spaceKind":"Public","spaceId":"public-space","rootFolderId":"public-root"},
             {"spaceKind":"Personal","spaceId":"personal-space","rootFolderId":"personal-root"}]
            """);
        // 浏览器已经删除全部直接内容；模拟正式创建回执不携带 Part/Document 身份。
        var items = new List<object>();
        var folderItems = new List<object>();
        var writes = 0;
        using var client = new HttpClient(new AsyncHandler(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/auth/antiforgery-token") return Json(HttpStatusCode.OK, "{\"requestToken\":\"fixture-token\"}");
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("entry", Assert.Single(request.Headers.GetValues("X-NewP-Feature")));
                if (path is "/api/workspace/spaces/public-space/root" or "/api/workspace/spaces/personal-space/root")
                    return Json(HttpStatusCode.OK, "{}");
                Assert.Equal("/api/links/workspace.content", path);
                var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
                Assert.Equal("forward", query["direction"]);
                Assert.Equal("WorkspaceFolder", query["anchorModelKey"]);
                using var scope = JsonDocument.Parse(query["queryScope"]!);
                var isFolder = query["anchorId"] == "folder-EID";
                var isPublic = isFolder || query["anchorId"] == "public-root";
                Assert.Equal(isPublic ? "public-space" : "personal-space", scope.RootElement.GetProperty("spaceId").GetString());
                Assert.Equal(isPublic ? "public-root" : "personal-root", scope.RootElement.GetProperty("rootFolderId").GetString());
                Assert.Equal(isFolder ? "[\"create-LID\"]" : "[]", scope.RootElement.GetProperty("linkIds").GetString());
                var contents = isFolder ? folderItems : isPublic ? items : [];
                return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { items = contents, total = contents.Count }));
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("fixture-token", Assert.Single(request.Headers.GetValues("X-CSRF-TOKEN")));
            var (action, model, id) = path switch
            {
                "/api/workspace/content/folders" => ("create", "WorkspaceFolder", "folder-EID"),
                "/api/workspace/content/parts" => ("createPart", "Part", "part-EID"),
                "/api/workspace/content/documents" => ("createDocument", "Document", "document-EID"),
                _ => throw new InvalidOperationException("重启准备不应扩大权限或使用其他写入入口：" + path)
            };
            Assert.Equal(action, Assert.Single(request.Headers.GetValues("X-NewP-Feature")));
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var position = body.RootElement.GetProperty("position");
            Assert.Equal("public-space", position.GetProperty("spaceId").GetString());
            Assert.Equal("public-root", position.GetProperty("rootFolderId").GetString());
            Assert.Equal(action == "create" ? [] : new[] { "create-LID" }, position.GetProperty("linkIds").EnumerateArray().Select(value => value.GetString()).ToArray());
            var values = body.RootElement.GetProperty("values");
            Assert.False(string.IsNullOrWhiteSpace(values.GetProperty("ENAME").GetString()));
            if (action != "create") Assert.False(string.IsNullOrWhiteSpace(values.GetProperty("ECODE").GetString()));
            (action == "create" ? items : folderItems).Add(new { linkId = action + "-LID", associatedObject = new { modelKey = model, id } });
            writes++;
            return Json(HttpStatusCode.OK, action == "create" ? "{\"id\":\"folder-EID\",\"fields\":{}}" : "{\"succeeded\":true}");
        })) { BaseAddress = new Uri("http://127.0.0.1:5000") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => WorkspaceAcceptanceFixture.ReadRestartContentAsync(
            client, roots.RootElement, "entry", new("public-space", "public-root", "folder-EID", ["create-LID"]), CancellationToken.None));
        var sampleFolder = await WorkspaceAcceptanceFixture.PrepareRestartContentAsync(client, features, roots.RootElement, "12345678-run", CancellationToken.None);
        var before = await WorkspaceAcceptanceFixture.ReadRestartContentAsync(client, roots.RootElement, "entry", sampleFolder, CancellationToken.None);
        var after = await WorkspaceAcceptanceFixture.ReadRestartContentAsync(client, roots.RootElement, "entry", sampleFolder, CancellationToken.None);

        Assert.Equal(3, writes);
        var expected = new[]
        {
            "public-space:" + JsonSerializer.Serialize(new[] { "create-LID", "WorkspaceFolder", "folder-EID" }),
            "public-space:folder-EID:" + JsonSerializer.Serialize(new[] { "createPart-LID", "Part", "part-EID" }),
            "public-space:folder-EID:" + JsonSerializer.Serialize(new[] { "createDocument-LID", "Document", "document-EID" })
        };
        Assert.Equal(expected.Order(StringComparer.Ordinal), before);
        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData(false, "缺少已建立的三类型混合引用")]
    [InlineData(true, "未在一页完整返回")]
    public async Task RestartSnapshot_RejectsMissingTypesAndPartialPages(bool partialPage, string failure)
    {
        using var roots = JsonDocument.Parse("""
            [{"spaceKind":"Public","spaceId":"space","rootFolderId":"root"}]
            """);
        using var client = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath == "/api/workspace/spaces/space/root"
            ? Json(HttpStatusCode.OK, "{}")
            : Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                total = partialPage ? 2 : 1,
                items = new[] { new { linkId = "folder-LID", associatedObject = new { modelKey = "WorkspaceFolder", id = "folder-EID" } } }
            })))) { BaseAddress = new Uri("http://127.0.0.1:5000") };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => WorkspaceAcceptanceFixture.ReadRestartContentAsync(
            client, roots.RootElement, "entry", new("space", "root", "folder-EID", ["folder-LID"]), CancellationToken.None));

        Assert.Contains(failure, exception.Message);
    }

    [Fact]
    public void RestartContentIdentities_PreserveModelAndRelationIdentityButIgnoreQueryOrderAndPresentation()
    {
        using var before = JsonDocument.Parse("""
            {"items":[
              {"linkId":"link2","associatedObject":{"modelKey":"Part","id":"same-id"},"fields":{"Name":"before"}},
              {"linkId":"link1","associatedObject":{"modelKey":"Document","id":"same-id"}},
              {"linkId":"link3","associatedObject":{"modelKey":"Part","id":"same-id"}}]}
            """);
        using var after = JsonDocument.Parse("""
            {"items":[
              {"linkId":"link3","associatedObject":{"modelKey":"Part","id":"same-id"}},
              {"linkId":"link1","associatedObject":{"modelKey":"Document","id":"same-id"}},
              {"linkId":"link2","associatedObject":{"modelKey":"Part","id":"same-id"},"fields":{"Name":"after"}}]}
            """);
        var identities = WorkspaceAcceptanceFixture.ContentIdentities(before.RootElement);
        Assert.Equal(3, identities.Distinct().Count());
        Assert.Equal(identities, WorkspaceAcceptanceFixture.ContentIdentities(after.RootElement));
        using var wrongModel = JsonDocument.Parse(after.RootElement.GetRawText().Replace("Document", "Part", StringComparison.Ordinal));
        Assert.NotEqual(identities, WorkspaceAcceptanceFixture.ContentIdentities(wrongModel.RootElement));
        using var wrongLink = JsonDocument.Parse(after.RootElement.GetRawText().Replace("link3", "replacement-link", StringComparison.Ordinal));
        Assert.NotEqual(identities, WorkspaceAcceptanceFixture.ContentIdentities(wrongLink.RootElement));
        using var wrongEntity = JsonDocument.Parse(after.RootElement.GetRawText().Replace("same-id", "replacement-id", StringComparison.Ordinal));
        Assert.NotEqual(identities, WorkspaceAcceptanceFixture.ContentIdentities(wrongEntity.RootElement));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}

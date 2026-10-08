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
    public void RestartContentIdentities_PreserveModelAndRelationIdentityButIgnoreQueryOrderAndPresentation()
    {
        using var before = JsonDocument.Parse("""
            {"items":[
              {"linkId":"link2","associatedObject":{"modelKey":"Part/Part","id":"same-id"},"fields":{"Name":"before"}},
              {"linkId":"link1","associatedObject":{"modelKey":"Document/Document","id":"same-id"}},
              {"linkId":"link3","associatedObject":{"modelKey":"Part/Part","id":"same-id"}}]}
            """);
        using var after = JsonDocument.Parse("""
            {"items":[
              {"linkId":"link3","associatedObject":{"modelKey":"Part/Part","id":"same-id"}},
              {"linkId":"link1","associatedObject":{"modelKey":"Document/Document","id":"same-id"}},
              {"linkId":"link2","associatedObject":{"modelKey":"Part/Part","id":"same-id"},"fields":{"Name":"after"}}]}
            """);
        var identities = WorkspaceAcceptanceRunner.ContentIdentities(before.RootElement);
        Assert.Equal(3, identities.Distinct().Count());
        Assert.Equal(identities, WorkspaceAcceptanceRunner.ContentIdentities(after.RootElement));
        using var wrongModel = JsonDocument.Parse(after.RootElement.GetRawText().Replace("Document/Document", "Part/Part", StringComparison.Ordinal));
        Assert.NotEqual(identities, WorkspaceAcceptanceRunner.ContentIdentities(wrongModel.RootElement));
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

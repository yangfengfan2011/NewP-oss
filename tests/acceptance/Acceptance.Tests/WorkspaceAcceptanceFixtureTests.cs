using System.Net;
using System.Text;
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

    private static HttpResponseMessage Json(HttpStatusCode status, string text) =>
        new(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}

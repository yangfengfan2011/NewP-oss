using System.Net;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class FeatureHttpTestRequestsTests
{
    [Fact]
    public async Task PositiveCreateUsesTheExactCachedFeatureWhileRawMissingIdentityStaysMissing()
    {
        var point = new FeaturePoint("position", "Identity", "create", "action", "创建", "entry", "identity.account.create", [], 0);
        var declaration = new ActionDataAccessRequirement("actual-button", "Identity", "account.list", "pageActions", "create", "",
            ["identity.account"], [], [point.Position]) { BusinessBinding = point.BehaviorKey };
        var recorder = new Requests();
        using var client = new HttpClient(recorder) { BaseAddress = new Uri("http://test.local") };
        var http = new FeatureHttpTestRequests([point], [declaration]);
        await http.CreateButtonAsync(client, point, "/formal-command", new { name = "value" });
        Assert.Equal("actual-button", recorder.Buttons.Single());
        using var missing = await FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Post, "/formal-command", new { name = "value" });
        Assert.Null(recorder.Buttons.Last());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FeatureHttpTestRequests([point], []).CreateButtonAsync(client, point, "/formal-command", new { }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FeatureHttpTestRequests([point], [declaration, declaration]).CreateButtonAsync(client, point, "/formal-command", new { }));
        Assert.Equal(2, recorder.Buttons.Count);
    }

    [Fact]
    public async Task ExplicitMutationUsesItsOwnFeatureAndNeverSubstitutesAReadOrOtherPosition()
    {
        var point = new FeaturePoint("actual-position", "Authorization", "save", "action", "保存", "entry", "authorization.workbench.save", [], 0);
        var own = new ActionDataAccessRequirement("own-button", "Authorization", "feature:workbench", "featureActions", "save", "",
            [], [], [point.Position]) { BusinessBinding = point.BehaviorKey };
        var otherPosition = own with { Id = "other-button", FeaturePositions = ["other-position"] };
        var legacyRead = own with { Id = "read-only", BusinessBinding = null, Execution = point.BehaviorKey };
        var recorder = new Requests();
        using var client = new HttpClient(recorder) { BaseAddress = new Uri("http://test.local") };
        var http = new FeatureHttpTestRequests([point], [otherPosition, legacyRead, own]);
        using var sent = await http.SendButtonAsync(client, point, HttpMethod.Put, "/formal-command", new { });
        Assert.Equal("own-button", Assert.Single(recorder.Buttons));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FeatureHttpTestRequests([point], [otherPosition, legacyRead])
            .SendButtonAsync(client, point, HttpMethod.Put, "/formal-command", new { }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FeatureHttpTestRequests([point], [own, own])
            .SendButtonAsync(client, point, HttpMethod.Put, "/formal-command", new { }));
        using var missing = await FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Put, "/formal-command", new { });
        Assert.Null(recorder.Buttons.Last());
        Assert.Equal(2, recorder.Buttons.Count);
    }

    private sealed class Requests : HttpMessageHandler
    {
        internal List<string?> Buttons { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Buttons.Add(request.Headers.TryGetValues("X-NewP-Button", out var values) ? values.Single() : null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") });
        }
    }
}

using System.Net;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class FeatureHttpTestRequestsTests
{
    [Theory]
    [Trait("Category", "ModuleMetadataConsumer")]
    [InlineData("copySelected", "list.pageActions")]
    [InlineData("removeSelected", "list.pageActions")]
    [InlineData("remove", "list.rowActions")]
    public async Task WorkspaceSharedCallers_SelectTheirActualButtonAfterTheLegacyEntryRetires(string key, string surface)
    {
        var result = new CanonicalModulePlanCompiler().Compile("workspace-callers", new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var plan = result.Plan!;
        var point = Assert.Single(plan.FeatureCatalog, item => item.ModuleId == "Workspace" && item.Key == key
            && item.ActionScope == surface && item.StaticScope == "folder");
        var declaration = new FeatureHttpTestRequests(plan.FeatureCatalog, plan.ActionDataAccessCatalog).Declaration(point.BehaviorKey, point);
        Assert.Empty(declaration.Execution);
        Assert.Equal("WorkspaceFolder", declaration.Object);
        Assert.Equal(point.BehaviorKey, declaration.BusinessBinding);
        var recorder = new Requests();
        using var client = new HttpClient(recorder) { BaseAddress = new Uri("http://test.local") };
        using var response = key == "remove"
            ? await new ObjectHistoryHttpTestRequests(plan.ActionDataAccessCatalog).SendAsync(client, point, HttpMethod.Delete,
                "/api/workspace/content/references/current-link", new { position = new { } })
            : await FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Post, "/explicit-command", new { }, declaration);
        Assert.Equal(declaration.Id, Assert.Single(recorder.Buttons));
        Assert.Null(Assert.Single(recorder.LegacyDeclarations));
    }

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
        internal List<string?> LegacyDeclarations { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Buttons.Add(request.Headers.TryGetValues("X-NewP-Button", out var values) ? values.Single() : null);
            LegacyDeclarations.Add(request.Headers.TryGetValues("X-NewP-Data-Declaration", out var legacy) ? legacy.Single() : null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") });
        }
    }
}

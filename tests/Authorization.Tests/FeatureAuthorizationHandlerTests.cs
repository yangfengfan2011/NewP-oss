using System.Security.Claims;
using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Xunit;
namespace ICP.Platform.Modules.Authorization.Tests;
public sealed class FeatureAuthorizationHandlerTests
{
    [Theory]
    [InlineData(PermissionDecision.Granted, true)]
    [InlineData(PermissionDecision.Denied, false)]
    public async Task UsesTrustedSubjectAndRegisteredBehavior(PermissionDecision decision, bool allowed)
    {
        var source = new Source(decision);
        var handler = new FeatureAuthorizationHandler(source);
        var requirement = new FeatureAuthorizationRequirement("save");
        var http = new DefaultHttpContext();
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new EndpointModuleAttribute("A")), "fixture"));
        http.Request.Headers["X-NewP-Feature"] = "A/navigation:workbench/action:save";
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actor")], "fixture"));
        var context = new AuthorizationHandlerContext([requirement], user, http);
        await handler.HandleAsync(context);
        Assert.Equal(allowed, context.HasSucceeded);
        Assert.Equal(("actor", "A/navigation:workbench/action:save", "A", "save"), source.Last);
    }
    [Fact]
    public async Task DependencyFailureIsIndeterminateAndNeverSucceeds()
    {
        var handler = new FeatureAuthorizationHandler(new Source(null));
        var http = new DefaultHttpContext();
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new EndpointModuleAttribute("A")), "fixture"));
        http.Request.Headers["X-NewP-Feature"] = "known";
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actor")], "fixture"));
        var context = new AuthorizationHandlerContext([new FeatureAuthorizationRequirement("save")], user, http);
        await handler.HandleAsync(context);
        Assert.False(context.HasSucceeded);
        Assert.Contains(context.FailureReasons, reason => reason.Message == PermissionGateFailureCodes.DecisionUnavailable);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task MissingOrAmbiguousServerOwnerFailsBeforeDecision(int count)
    {
        var source = new Source(PermissionDecision.Granted);
        var http = new DefaultHttpContext();
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(Enumerable.Range(0, count).Select(_ => new EndpointModuleAttribute("A"))), "fixture"));
        http.Request.Headers["X-NewP-Feature"] = "A/navigation:workbench/action:save";
        http.Request.Headers["X-NewP-Module"] = "A";
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actor")], "fixture"));
        var context = new AuthorizationHandlerContext([new FeatureAuthorizationRequirement("save")], user, http);
        await new FeatureAuthorizationHandler(source).HandleAsync(context);
        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
        Assert.Null(source.Last);
    }
    private sealed class Source(PermissionDecision? decision) : IFeatureDecisionSource
    {
        public (string, string, string, string)? Last { get; private set; }
        public ValueTask<PermissionDecision> EvaluateAsync(string subjectId, string position, string moduleId, string behaviorKey, CancellationToken cancellationToken)
        {
            Last = (subjectId, position, moduleId, behaviorKey);
            if (decision is null) throw new InvalidOperationException("storage unavailable");
            return ValueTask.FromResult(decision.Value);
        }
    }
}

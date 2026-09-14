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
        http.Request.Headers["X-NewP-Feature"] = "A/navigation:workbench/action:save";
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actor")], "fixture"));
        var context = new AuthorizationHandlerContext([requirement], user, http);
        await handler.HandleAsync(context);
        Assert.Equal(allowed, context.HasSucceeded);
        Assert.Equal(("actor", "A/navigation:workbench/action:save", "save"), source.Last);
    }
    [Fact]
    public async Task DependencyFailureIsIndeterminateAndNeverSucceeds()
    {
        var handler = new FeatureAuthorizationHandler(new Source(null));
        var http = new DefaultHttpContext();
        http.Request.Headers["X-NewP-Feature"] = "known";
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actor")], "fixture"));
        var context = new AuthorizationHandlerContext([new FeatureAuthorizationRequirement("save")], user, http);
        await handler.HandleAsync(context);
        Assert.False(context.HasSucceeded);
        Assert.Contains(context.FailureReasons, reason => reason.Message == PermissionGateFailureCodes.DecisionUnavailable);
    }
    private sealed class Source(PermissionDecision? decision) : IFeatureDecisionSource
    {
        public (string, string, string)? Last { get; private set; }
        public ValueTask<PermissionDecision> EvaluateAsync(string subjectId, string position, string behaviorKey, CancellationToken cancellationToken)
        {
            Last = (subjectId, position, behaviorKey);
            if (decision is null) throw new InvalidOperationException("storage unavailable");
            return ValueTask.FromResult(decision.Value);
        }
    }
}

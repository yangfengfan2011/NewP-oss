using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class PartAuthorizationSourceTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("foreign-resource")]
    [InlineData("wrong-endpoint-owner")]
    [InlineData("wrong-resource-owner")]
    [InlineData("wrong-feature-behavior")]
    public async Task OwnEndpoint_UsesItsActualButtonWithoutRelaxingRegisteredOwner(string variant)
    {
        var compiled = new CanonicalModulePlanCompiler().Compile("part-authorization-source",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.BusinessBinding == "part.part.authorize"
            && item.Surface == "rowActions");
        Assert.Equal(new[] { "authorize" }, declaration.Actions);
        Assert.Null(declaration.Provider); Assert.Null(declaration.Requirements);
        var context = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "source-test")], "test")) };
        context.SetEndpoint(variant == "wrong-resource-owner"
            ? new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("api/authorization/data-scope/objects/{resourceKey}/{objectId}"), 0,
                new EndpointMetadataCollection(new EndpointModuleAttribute("Authorization"), new AuthenticatedOnlyAttribute(),
                    new ResourceFeatureBindingAttribute("authorize"), new ButtonBusinessHandlerAttribute(typeof(Probe))), "actual-generic-owner-proof")
            : new Endpoint(null, new EndpointMetadataCollection(
                new EndpointModuleAttribute(variant == "wrong-endpoint-owner" ? "Authorization" : "Part"),
                new RequireFeatureAttribute(variant == "wrong-feature-behavior" ? "part.part.history" : "part.part.authorize"),
                new ButtonBusinessHandlerAttribute(typeof(Probe))), "actual-owner-endpoint"));
        context.Request.RouteValues["resourceKey"] = "part.part";
        context.Request.RouteValues["objectId"] = "Part_actual";
        context.Request.Headers["X-NewP-Feature"] = Assert.Single(declaration.FeaturePositions);
        if (variant != "missing") context.Request.Headers["X-NewP-Button"] = variant == "foreign-resource"
            ? Assert.Single(plan.ActionDataAccessCatalog, item => item.BusinessBinding == "authorization.role.authorize").Id : declaration.Id;
        var checks = new Checks();
        var access = new ButtonBusinessExecutor(new ButtonExecutionAccess(plan, new HttpContextAccessor { HttpContext = context },
            new ResourceCatalog(variant == "wrong-resource-owner"), checks));
        var handler = new Probe(new ButtonExecutionTarget("part.part", "Part", "Part_actual", new Dictionary<string, object?> { ["EID"] = "Part_actual" }));
        if (variant == "valid")
        {
            Assert.Equal(1, await access.ExecuteAsync(handler, 1));
            Assert.Equal(new[] { "Part:Part_actual:authorize" }, checks.Calls);
            Assert.True(handler.Executed);
        }
        else
        {
            await Assert.ThrowsAsync<ActionDataAccessException>(() => access.ExecuteAsync(handler, 1));
            Assert.Empty(checks.Calls); Assert.False(handler.Executed);
        }
    }

    private sealed class ResourceCatalog(bool wrongOwner) : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources { get; } = [new("part.part", wrongOwner ? "Document" : "Part", "Part", "EID")];
    }

    private sealed class Checks : IObjectActionAccess
    {
        internal List<string> Calls { get; } = [];
        public Task<DataExecutionDecision> CheckAsync(string model, string id, IReadOnlyList<string> actions, CancellationToken token = default)
        { Calls.Add(model + ":" + id + ":" + string.Join(",", actions)); return Task.FromResult(DataExecutionDecision.Allowed); }
    }

    private sealed class Probe(ButtonBusinessTarget target) : IButtonBusinessHandler<int, int, int>
    {
        internal bool Executed;
        public Task<int> WithinBoundaryAsync(int input, string actionKey, Func<int, CancellationToken, Task<int>> execute, CancellationToken token) => execute(input, token);
        public ButtonBusinessTarget Target(int state) => target;
        public Task<int> ExecuteAsync(int state, CancellationToken token) { Executed = true; return Task.FromResult(state); }
    }
}

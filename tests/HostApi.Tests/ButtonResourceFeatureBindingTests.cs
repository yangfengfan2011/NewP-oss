using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Workspace;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class ButtonBusinessEndpointTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("unknown-resource")]
    [InlineData("wrong-owner")]
    [InlineData("wrong-action")]
    [InlineData("missing-authentication")]
    [InlineData("missing-module")]
    [InlineData("duplicate-resource-binding")]
    [InlineData("mixed-feature-binding")]
    [InlineData("incomplete-route")]
    [InlineData("missing-object")]
    [InlineData("mixed-identity")]
    [InlineData("wrong-feature")]
    [InlineData("wrong-handler")]
    [InlineData("wrong-target-id")]
    [InlineData("wrong-target-resource")]
    [InlineData("target-set")]
    public async Task ResourceBoundHandler_UsesItsRegisteredRouteOwnerAndExactObject(string problem)
    {
        var plan = Plan();
        var declaration = plan.ActionDataAccessCatalog.Single(item => item.BusinessBinding == "workspace.folder.reorder" && item.ActionKey == "moveUp");
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "actor")], "resource-button-test")) };
        http.SetEndpoint(ResourceEndpoint(problem, problem == "wrong-handler" ? typeof(object) : typeof(ResourceProbe)));
        http.Request.RouteValues["resourceKey"] = problem == "unknown-resource" ? "unknown.resource" : "workspace.folder";
        if (problem != "missing-object") http.Request.RouteValues["objectId"] = "WorkspaceFolder_parent";
        http.Request.Headers["X-NewP-Button"] = declaration.Id;
        http.Request.Headers["X-NewP-Feature"] = problem == "wrong-feature" ? "other/feature" : declaration.FeaturePositions[0];
        if (problem == "mixed-identity") http.Request.Headers["X-NewP-Data-Declaration"] = declaration.Id;
        var before = new Dictionary<string, object?> { ["EID"] = problem == "wrong-target-id" ? "WorkspaceFolder_other" : "WorkspaceFolder_parent" };
        ButtonBusinessTarget target = new ButtonExecutionTarget(problem == "wrong-target-resource" ? "part.part" : "workspace.folder",
            "WorkspaceFolder", (string)before["EID"]!, before);
        if (problem == "target-set") target = new ButtonTargetSet([(ButtonObjectTarget)target]);
        var handler = new ResourceProbe(target);
        var checks = new Checks();
        var executor = new ButtonBusinessExecutor(new ButtonExecutionAccess(plan, new HttpContextAccessor { HttpContext = http },
            new BoundResourceCatalog(problem == "wrong-owner" ? "other-owner" : "Workspace"), checks));
        if (problem == "valid")
        {
            Assert.Equal(1, await executor.ExecuteAsync(handler, 1));
            Assert.Equal(1, checks.Calls);
            Assert.True(handler.Executed);
        }
        else
        {
            await Assert.ThrowsAsync<ActionDataAccessException>(() => executor.ExecuteAsync(handler, 1));
            Assert.Equal(0, checks.Calls);
            Assert.False(handler.Executed);
        }
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("missing-catalog")]
    [InlineData("wrong-owner")]
    [InlineData("wrong-action")]
    [InlineData("missing-authentication")]
    [InlineData("missing-module")]
    [InlineData("duplicate-resource-binding")]
    [InlineData("mixed-feature-binding")]
    [InlineData("incomplete-route")]
    [InlineData("unregistered-handler")]
    public void ResourceBoundHandlerCoverage_RequiresTheExistingResourceFeatureProof(string problem)
    {
        var services = new ServiceCollection();
        if (problem != "unregistered-handler") services.AddScoped<WorkspaceReorderHandler>();
        if (problem != "missing-catalog") services.AddSingleton<IDataScopeResourceCatalog>(
            new BoundResourceCatalog(problem == "wrong-owner" ? "other-owner" : "Workspace"));
        using var provider = services.BuildServiceProvider();
        EndpointDataSource[] endpoints = [new DefaultEndpointDataSource(ResourceEndpoint(problem, typeof(WorkspaceReorderHandler)))];
        if (problem == "valid") StandardCrudDataExecutions.ValidateCoverage(endpoints, Plan(reorderOnly: true), provider);
        else Assert.StartsWith("BUTTON_", Assert.Throws<InvalidOperationException>(() =>
            StandardCrudDataExecutions.ValidateCoverage(endpoints, Plan(reorderOnly: true), provider)).Message);
    }

    private static RouteEndpoint ResourceEndpoint(string problem, Type handler)
    {
        var metadata = new List<object> { new ButtonBusinessHandlerAttribute(handler),
            new ResourceFeatureBindingAttribute(problem == "wrong-action" ? "edit" : "reorder") };
        if (problem != "missing-module") metadata.Add(new EndpointModuleAttribute("Authorization"));
        if (problem != "missing-authentication") metadata.Add(new AuthenticatedOnlyAttribute());
        if (problem == "duplicate-resource-binding") metadata.Add(new ResourceFeatureBindingAttribute("reorder"));
        if (problem == "mixed-feature-binding") metadata.Add(new RequireFeatureAttribute("workspace.folder.reorder"));
        return new(_ => Task.CompletedTask, RoutePatternFactory.Parse(problem == "incomplete-route"
            ? "api/resource/{resourceKey}" : "api/resource/{resourceKey}/{objectId}"), 0, new EndpointMetadataCollection(metadata), "resource-bound-handler");
    }

    private sealed class BoundResourceCatalog(string owner) : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources { get; } = [new("workspace.folder", owner, "WorkspaceFolder", "EID")];
    }

    private sealed class ResourceProbe(ButtonBusinessTarget target) : IButtonBusinessHandler<int, int, int>
    {
        internal bool Executed;
        public Task<int> WithinBoundaryAsync(int input, string actionKey, Func<int, CancellationToken, Task<int>> execute, CancellationToken token) => execute(input, token);
        public ButtonBusinessTarget Target(int state) => target;
        public Task<int> ExecuteAsync(int state, CancellationToken token) { Executed = true; return Task.FromResult(state); }
    }
}

using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Workspace;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class ButtonBusinessOperationTests
{
    [Theory]
    [InlineData("inspection", "inspection", false)]
    [InlineData("save", "save", false)]
    [InlineData("inspection", "save", true)]
    [InlineData("save", "inspection", true)]
    [InlineData("wrong-feature", "inspection", true)]
    [InlineData("missing-position", "inspection", true)]
    public async Task ActualButtonPipeline_UsesBothTheImplementationAndItsOriginalFeature(string button, string endpoint, bool rejected)
    {
        var plan = OperationPlan(button == "wrong-feature");
        var declaration = plan.ActionDataAccessCatalog.Single(item => item.ActionKey == (button == "save" ? "authorize" : "viewAuthorization"));
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actor")], "operation")) };
        http.SetEndpoint(OperationEndpoint(endpoint == "inspection" ? "viewAuthorization" : null));
        http.Request.RouteValues["resourceKey"] = "example.record";
        http.Request.RouteValues["objectId"] = "ExampleRecord_official";
        http.Request.Headers["X-NewP-Button"] = declaration.Id;
        http.Request.Headers["X-NewP-Feature"] = button == "missing-position" ? "unknown/position" : declaration.FeaturePositions[0];
        var checks = new OperationChecks();
        var handler = new OperationHandler();
        var executor = new ButtonBusinessExecutor(new ButtonExecutionAccess(plan, new HttpContextAccessor { HttpContext = http }, new OperationResources(), checks));
        if (!rejected)
        {
            Assert.Equal(1, await executor.ExecuteAsync(handler, 1));
            Assert.Equal(1, handler.Calls);
            Assert.Equal(new[] { "read", "authorize" }, checks.Actions);
        }
        else
        {
            var error = await Assert.ThrowsAsync<ActionDataAccessException>(() => executor.ExecuteAsync(handler, 1));
            Assert.Equal(button == "missing-position" ? 403 : 400, error.Status);
            Assert.Equal(0, handler.Calls);
            Assert.Null(checks.Actions);
        }
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("unknown-implementation")]
    [InlineData("wrong-feature")]
    [InlineData("unregistered-handler")]
    public void ActualOperationCoverage_RequiresRegisteredImplementationAndFormalFeature(string problem)
    {
        var services = new ServiceCollection().AddSingleton<IDataScopeResourceCatalog, OperationResources>();
        if (problem != "unregistered-handler") services.AddScoped<OperationHandler>();
        using var provider = services.BuildServiceProvider();
        EndpointDataSource[] endpoints = [new DefaultEndpointDataSource(OperationEndpoint(null),
            OperationEndpoint(problem == "unknown-implementation" ? "unknown" : "viewAuthorization"))];
        if (problem == "valid") StandardCrudDataExecutions.ValidateCoverage(endpoints, OperationPlan(), provider);
        else Assert.StartsWith("BUTTON_", Assert.Throws<InvalidOperationException>(() =>
            StandardCrudDataExecutions.ValidateCoverage(endpoints, OperationPlan(problem == "wrong-feature"), provider)).Message);
    }

    [Theory]
    [InlineData("viewAuthorization", "example.record.viewAuthorization", "example.record.authorize", "Example", true)]
    [InlineData("viewAuthorization", "example.record.authorize", "example.record.authorize", "Example", false)]
    [InlineData(null, "example.record.viewAuthorization", "example.record.authorize", "Example", false)]
    [InlineData(null, "example.record.authorize", "example.record.authorize", "Example", true)]
    [InlineData("viewAuthorization", "example.record.viewAuthorization", "example.record.read", "Example", false)]
    [InlineData("viewAuthorization", "example.record.viewAuthorization", "example.record.authorize", "Other", false)]
    [InlineData("viewAuthorization", "example.record.viewAuthorization", null, "Example", false)]
    [InlineData("", "example.record.authorize", "example.record.authorize", "Example", false)]
    public void ResourceBusinessOperation_SeparatesImplementationFromTheExistingFeature(string? operation, string business,
        string? featureBehavior, string owner, bool allowed)
    {
        var endpoint = new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("api/objects/{resourceKey}/{objectId}"), 0,
            new EndpointMetadataCollection(new EndpointModuleAttribute("Authorization"), new AuthenticatedOnlyAttribute(),
                new ResourceFeatureBindingAttribute("authorize"), new ButtonBusinessHandlerAttribute(typeof(WorkspaceReorderHandler), operation)), "actual-operation");
        Assert.Equal(allowed, ButtonBusinessEndpointBindings.Matches(endpoint, "Example", business,
            new DataScopeResource("example.record", owner, "ExampleRecord", "EID"), featureBehavior));
    }

    private static RouteEndpoint OperationEndpoint(string? operation) => new(_ => Task.CompletedTask,
        RoutePatternFactory.Parse("api/authorization/data-scope/objects/{resourceKey}/{objectId}" + (operation is null ? "" : "/inspection")), 0,
        new EndpointMetadataCollection(new EndpointModuleAttribute("Authorization"), new AuthenticatedOnlyAttribute(),
            new ResourceFeatureBindingAttribute("authorize"), new ButtonBusinessHandlerAttribute(typeof(OperationHandler), operation)), "operation-proof");

    private static CanonicalModulePlan OperationPlan(bool wrongFeature = false)
    {
        var result = new CanonicalModulePlanCompiler().Compile("operation-source", new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var plan = result.Plan!;
        var modules = plan.Modules.Select(module => module with
        {
            DataExecutions = [], DataExecutionProviders = [],
            Features = module.Features.Select(feature => feature with
            {
                BehaviorKey = wrongFeature && feature.ModuleId == "Example" && feature.Key == "viewAuthorization" ? "example.record.read" : feature.BehaviorKey,
                DataAccess = feature.DataAccess.Where(item => item.ModuleId == "Example" && item.Surface == "rowActions"
                    && item.ViewKey == "example.record.list" && item.ActionKey is "authorize" or "viewAuthorization")
                    .Select(item => new ActionDataAccessDeclaration(item.ModuleId, item.ViewKey, item.Surface, item.ActionKey, "", ["example.record"], ["read", "authorize"])
                        { Object = "ExampleRecord", BusinessBinding = "example.record." + item.ActionKey }).ToArray()
            }).ToArray()
        }).ToArray();
        return new(plan.CandidateId, plan.CompilerContractVersion, plan.PlanHash, plan.SnapshotId, plan.CanonicalBytes, modules);
    }

    private sealed class OperationResources : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources { get; } = [new("example.record", "Example", "ExampleRecord", "EID")];
    }

    private sealed class OperationChecks : IObjectActionAccess
    {
        internal IReadOnlyList<string>? Actions;
        public Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions, CancellationToken token = default)
        { Actions = actions; return Task.FromResult(DataExecutionDecision.Allowed); }
    }

    private sealed class OperationHandler : IButtonBusinessHandler<int, int, int>
    {
        internal int Calls;
        public Task<int> WithinBoundaryAsync(int input, string actionKey, Func<int, CancellationToken, Task<int>> execute, CancellationToken token) => execute(input, token);
        public ButtonBusinessTarget Target(int state) => new ButtonExecutionTarget("example.record", "ExampleRecord", "ExampleRecord_official",
            new Dictionary<string, object?> { ["EID"] = "ExampleRecord_official" });
        public Task<int> ExecuteAsync(int state, CancellationToken token) { ++Calls; return Task.FromResult(state); }
    }
}

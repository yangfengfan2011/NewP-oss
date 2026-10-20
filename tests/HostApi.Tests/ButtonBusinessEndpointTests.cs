using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Workspace;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class ButtonBusinessEndpointTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("unknown-business")]
    [InlineData("wrong-module")]
    [InlineData("wrong-type")]
    [InlineData("unregistered")]
    public void FormalHandlerBinding_RejectsMissingUnknownOrUnregisteredImplementations(string problem)
    {
        var plan = Plan(reorderOnly: true);
        var services = new ServiceCollection();
        if (problem != "unregistered") services.AddScoped<WorkspaceReorderHandler>();
        using var provider = services.BuildServiceProvider();
        var metadata = new List<object> { new EndpointModuleAttribute(problem == "wrong-module" ? "Part" : "Workspace"),
            new RequireFeatureAttribute(problem == "unknown-business" ? "unknown.business" : "workspace.folder.reorder") };
        if (problem != "missing") metadata.Add(new ButtonBusinessHandlerAttribute(problem == "wrong-type" ? typeof(object) : typeof(WorkspaceReorderHandler)));
        EndpointDataSource[] sources = [new DefaultEndpointDataSource(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "formal-sort"))];
        if (problem == "valid") StandardCrudDataExecutions.ValidateCoverage(sources, plan, provider);
        else Assert.StartsWith("BUTTON_", Assert.Throws<InvalidOperationException>(() => StandardCrudDataExecutions.ValidateCoverage(sources, plan, provider)).Message);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("no-requirement")]
    [InlineData("missing-target")]
    [InlineData("wrong-target")]
    [InlineData("missing-identity")]
    [InlineData("mixed")]
    [InlineData("wrong-handler")]
    [InlineData("wrong-feature")]
    [InlineData("decision-failure")]
    public async Task RegisteredPipeline_ControlsCheckAndBusinessExecutionInsideTheSameBoundary(string problem)
    {
        var plan = Plan();
        var declaration = problem == "no-requirement"
            ? plan.ActionDataAccessCatalog.First(item => item.ModuleId == "Workspace" && item.BusinessBinding is not null && item.Object is null)
            : plan.ActionDataAccessCatalog.Single(item => item.BusinessBinding == "workspace.folder.reorder" && item.ActionKey == "moveUp");
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actor")], "button-business-test")) };
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new EndpointModuleAttribute("Workspace"),
            new RequireFeatureAttribute(declaration.BusinessBinding!), new ButtonBusinessHandlerAttribute(problem == "wrong-handler" ? typeof(object) : typeof(Probe))), "handler"));
        if (problem != "missing-identity") http.Request.Headers["X-NewP-Button"] = declaration.Id;
        http.Request.Headers["X-NewP-Feature"] = problem == "wrong-feature" ? "unrelated/feature" : declaration.FeaturePositions[0];
        if (problem == "mixed") http.Request.Headers["X-NewP-Data-Declaration"] = declaration.Id;
        var check = new Checks { Fail = problem == "decision-failure" };
        var target = problem is "missing-target" or "no-requirement" ? null
            : new ButtonExecutionTarget("workspace.folder", "WorkspaceFolder", "WorkspaceFolder_parent",
                new Dictionary<string, object?> { ["EID"] = problem == "wrong-target" ? "WorkspaceFolder_other" : "WorkspaceFolder_parent" });
        var handler = new Probe(target);
        var executor = new ButtonBusinessExecutor(new ButtonExecutionAccess(plan, new HttpContextAccessor { HttpContext = http }, new ResourceCatalog(), check));
        if (problem is "valid" or "no-requirement")
        {
            Assert.Equal(1, await executor.ExecuteAsync(handler, 1));
            Assert.Equal(problem == "valid" ? 1 : 0, check.Calls);
            Assert.True(handler.Executed);
        }
        else
        {
            var failure = await Record.ExceptionAsync(() => executor.ExecuteAsync(handler, 1));
            if (problem == "decision-failure") Assert.IsType<InvalidOperationException>(failure);
            else Assert.IsType<ActionDataAccessException>(failure);
            Assert.False(handler.Executed);
        }
        Assert.False(handler.Protected);
    }

    private static CanonicalModulePlan Plan(bool reorderOnly = false)
    {
        var compiled = new CanonicalModulePlanCompiler().Compile("button-business", new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        if (!reorderOnly) return plan;
        // 覆盖检查仅选择本次真实排序消费者，仍从实际模块feature派生目录，不手工拼位置或动作要求。
        var modules = plan.Modules.Select(module => module with
        {
            Features = module.Features.Select(feature => feature with
            { DataAccess = feature.DataAccess.Where(item => item.BusinessBinding == "workspace.folder.reorder").ToArray() }).ToArray(),
            DataExecutions = [], DataExecutionProviders = []
        }).ToArray();
        return new(plan.CandidateId, plan.CompilerContractVersion, plan.PlanHash, plan.SnapshotId, plan.CanonicalBytes, modules);
    }

    private sealed class Probe(ButtonExecutionTarget? target) : IButtonBusinessHandler<int, int, int>
    {
        internal bool Protected;
        internal bool Executed;
        public async Task<int> WithinBoundaryAsync(int input, string actionKey, Func<int, CancellationToken, Task<int>> execute, CancellationToken token)
        {
            Protected = true;
            try { return await execute(input, token); }
            finally { Protected = false; }
        }
        public ButtonBusinessTarget? Target(int state) { Assert.True(Protected); return target; }
        public Task<int> ExecuteAsync(int state, CancellationToken token) { Assert.True(Protected); Executed = true; return Task.FromResult(state); }
    }
    private sealed class ResourceCatalog : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources { get; } = [new("workspace.folder", "Workspace", "WorkspaceFolder", "EID")];
    }
    private sealed class Checks : IObjectActionAccess
    {
        internal bool Fail;
        internal int Calls;
        public Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions, CancellationToken token = default)
        {
            ++Calls;
            return Fail ? throw new InvalidOperationException("injected decision failure") : Task.FromResult(DataExecutionDecision.Allowed);
        }
    }
}

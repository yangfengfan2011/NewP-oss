using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class ButtonMultipleTargetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArrayRequirements_DoNotBecomeNoDataWhenScalarObjectIsNull(bool missingTarget)
    {
        var plan = Plan();
        var button = plan.ActionDataAccessCatalog.Single(item => item.BusinessBinding == "workspace.folder.reorder" && item.ActionKey == "moveUp");
        Assert.Null(button.Object);
        Assert.Equal(2, button.GetObjectRequirements().Count);
        var checks = new Checks();
        var handler = new Handler(missingTarget ? null : Existing("workspace.folder", "WorkspaceFolder", "parent"));
        var executor = Executor(plan, button, checks);
        if (missingTarget)
        {
            await Assert.ThrowsAsync<ActionDataAccessException>(() => executor.ExecuteAsync(handler, 1));
            Assert.Equal(0, handler.Calls);
        }
        else
        {
            Assert.Equal(1, await executor.ExecuteAsync(handler, 1));
            Assert.Equal(1, handler.Calls);
            Assert.Equal(new[] { "WorkspaceFolder:read,edit" }, checks.Calls);
        }
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("denied")]
    [InlineData("failure")]
    [InlineData("unknown")]
    [InlineData("empty")]
    [InlineData("part-only")]
    public async Task AllActualTargets_MustPassBeforeTheSingleBusinessCallback(string problem)
    {
        var plan = Plan();
        var button = plan.ActionDataAccessCatalog.Single(item => item.BusinessBinding == "workspace.folder.reorder" && item.ActionKey == "moveUp");
        var checks = new Checks { Problem = problem };
        ButtonObjectTarget[] targets = problem == "empty" ? [] : problem == "part-only" ? [Existing("part.part", "Part", "selected")]
            : [Existing("workspace.folder", "WorkspaceFolder", "parent"), problem == "unknown"
                ? Existing("document.document", "Document", "selected") : Existing("part.part", "Part", "selected")];
        var handler = new Handler(new ButtonTargetSet(targets));
        var executor = Executor(plan, button, checks);
        if (problem is "valid" or "part-only")
        {
            Assert.Equal(1, await executor.ExecuteAsync(handler, 1));
            Assert.Equal(1, handler.Calls);
            Assert.Equal(problem == "valid" ? new[] { "WorkspaceFolder:read,edit", "Part:read" } : new[] { "Part:read" }, checks.Calls);
        }
        else
        {
            var failure = await Record.ExceptionAsync(() => executor.ExecuteAsync(handler, 1));
            if (problem == "failure") Assert.Same(checks.Failure, failure);
            else Assert.IsType<ActionDataAccessException>(failure);
            Assert.Equal(0, handler.Calls);
            Assert.Equal(problem == "empty" ? 0 : problem == "unknown" ? 1 : 2, checks.Calls.Count);
        }
    }

    [Fact, Trait("Category", "ModuleMetadataConsumer")]
    public void ArrayButton_StillRequiresItsRegisteredRealEndpoint()
    {
        var plan = Plan();
        var modules = plan.Modules.Select(module => module with
        {
            Features = module.Features.Select(feature => feature with
            { DataAccess = feature.DataAccess.Where(item => item.BusinessBinding == "workspace.folder.reorder").ToArray() }).ToArray(),
            DataExecutions = [], DataExecutionProviders = []
        }).ToArray();
        plan = new(plan.CandidateId, plan.CompilerContractVersion, plan.PlanHash, plan.SnapshotId, plan.CanonicalBytes, modules);
        Assert.Equal("BUTTON_EXECUTION_ENDPOINT_MISSING", Assert.Throws<InvalidOperationException>(() =>
            StandardCrudDataExecutions.ValidateCoverage([], plan)).Message);
    }

    private static ButtonBusinessExecutor Executor(CanonicalModulePlan plan, ActionDataAccessRequirement button, Checks checks)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actor")], "multiple-target-test")) };
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new EndpointModuleAttribute("Workspace"),
            new RequireFeatureAttribute(button.BusinessBinding!), new ButtonBusinessHandlerAttribute(typeof(Handler))), "formal-handler"));
        context.Request.Headers["X-NewP-Button"] = button.Id;
        context.Request.Headers["X-NewP-Feature"] = button.FeaturePositions[0];
        return new(new ButtonExecutionAccess(plan, new HttpContextAccessor { HttpContext = context }, new ResourceCatalog(), checks));
    }

    private static ButtonExecutionTarget Existing(string resource, string model, string suffix) => new(resource, model,
        model + "_" + suffix, new Dictionary<string, object?> { ["EID"] = model + "_" + suffix });

    private static CanonicalModulePlan Plan()
    {
        var compiled = new CanonicalModulePlanCompiler().Compile("multiple-target", new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        var modules = plan.Modules.Select(module => module with { Features = module.Features.Select(feature => feature with
        {
            DataResources = feature.DataResources.Concat(new[] { "part.part" }).Distinct(StringComparer.Ordinal).ToArray(),
            DataAccess = feature.DataAccess.Select(item => item.BusinessBinding != "workspace.folder.reorder" ? item : item with
            {
                Object = null, Actions = [], Resources = ["workspace.folder", "part.part"],
                ObjectRequirements = [new("WorkspaceFolder", ["read", "edit"], "workspace.folder"), new("Part", ["read"], "part.part")]
            }).ToArray()
        }).ToArray() }).ToArray();
        return new(plan.CandidateId, plan.CompilerContractVersion, plan.PlanHash, plan.SnapshotId, plan.CanonicalBytes, modules);
    }

    private sealed class Handler(ButtonBusinessTarget? target) : IButtonBusinessHandler<int, int, int>
    {
        public int Calls;
        public Task<int> WithinBoundaryAsync(int input, string actionKey, Func<int, CancellationToken, Task<int>> execute, CancellationToken token) => execute(input, token);
        public ButtonBusinessTarget? Target(int state) => target;
        public Task<int> ExecuteAsync(int state, CancellationToken token) { ++Calls; return Task.FromResult(state); }
    }

    private sealed class ResourceCatalog : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources { get; } = [new("workspace.folder", "Workspace", "WorkspaceFolder", "EID"), new("part.part", "Part", "Part", "EID"), new("document.document", "Document", "Document", "EID")];
    }

    private sealed class Checks : IObjectActionAccess
    {
        public List<string> Calls { get; } = [];
        public string? Problem;
        public readonly Exception Failure = new InvalidOperationException("Injected second-target failure.");
        public Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions, CancellationToken token = default)
        {
            Calls.Add(objectName + ":" + string.Join(",", actions));
            if (objectName == "Part" && Problem == "failure") return Task.FromException<DataExecutionDecision>(Failure);
            return Task.FromResult(objectName == "Part" && Problem == "denied" ? DataExecutionDecision.Denied : DataExecutionDecision.Allowed);
        }
    }
}

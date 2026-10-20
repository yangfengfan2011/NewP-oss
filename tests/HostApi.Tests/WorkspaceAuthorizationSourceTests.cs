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
public sealed class WorkspaceAuthorizationSourceTests
{
    [Theory]
    [InlineData("workspace.folder")]
    [InlineData("part.part")]
    [InlineData("document.document")]
    [InlineData("mixed")]
    [InlineData("empty")]
    [InlineData("missing")]
    [InlineData("foreign-declaration")]
    [InlineData("wrong-endpoint")]
    [InlineData("wrong-feature")]
    [InlineData("unknown-target-resource")]
    public async Task CollectionSource_UsesActualButtonOwnerAndEachFormalTarget(string variant)
    {
        var plan = Plan();
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.BusinessBinding == "workspace.folder.authorize" && item.Surface == "pageActions");
        Assert.Equal(new[] { "WorkspaceFolder", "Part", "Document" }, declaration.GetObjectRequirements().Select(item => item.Object));
        var context = Context();
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EndpointModuleAttribute(variant == "wrong-endpoint" ? "Authorization" : "Workspace"),
            new RequireFeatureAttribute(variant == "wrong-feature" ? "workspace.folder.edit" : "workspace.folder.authorize"),
            new ButtonBusinessHandlerAttribute(typeof(Probe))), "actual-workspace-permission-endpoint"));
        context.Request.Headers["X-NewP-Feature"] = Assert.Single(declaration.FeaturePositions);
        if (variant != "missing") context.Request.Headers["X-NewP-Button"] = variant == "foreign-declaration"
            ? Assert.Single(plan.ActionDataAccessCatalog, item => item.BusinessBinding == "part.part.authorize" && item.Surface == "rowActions").Id : declaration.Id;
        var resources = new ResourceCatalog();
        IReadOnlyList<DataScopeResource> selected = variant == "mixed" ? resources.Resources : variant == "empty" ? []
            : resources.Resources.Where(item => item.ResourceKey == (variant.Contains('.') ? variant : "part.part")).ToArray();
        var targets = selected.Select(resource => (ButtonObjectTarget)Target(resource.ResourceKey, resource.ModelName)).ToArray();
        if (variant == "unknown-target-resource") targets = [Target("unknown.resource", "Part")];
        var checks = new Checks(); var handler = new Probe(new ButtonTargetSet(targets));
        var executor = new ButtonBusinessExecutor(new ButtonExecutionAccess(plan, new HttpContextAccessor { HttpContext = context }, resources, checks));
        if (variant is "workspace.folder" or "part.part" or "document.document" or "mixed")
        {
            Assert.Equal(1, await executor.ExecuteAsync(handler, 1));
            Assert.Equal(targets.Select(target => target.ObjectName + ":authorize"), checks.Calls);
            Assert.True(handler.Executed);
        }
        else
        {
            await Assert.ThrowsAsync<ActionDataAccessException>(() => executor.ExecuteAsync(handler, 1));
            Assert.Empty(checks.Calls); Assert.False(handler.Executed);
        }
    }

    [Theory]
    [InlineData("workspace.folder")]
    [InlineData("part.part")]
    [InlineData("document.document")]
    [InlineData("view-provider")]
    [InlineData("wrong-handler")]
    [InlineData("wrong-feature")]
    [InlineData("wrong-resource-owner")]
    public async Task GenericManagement_ConsumesTheActualResourceButtonAndOwner(string variant)
    {
        var plan = Plan();
        var resource = variant.Contains('.') ? variant : "workspace.folder";
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => variant == "view-provider"
            ? item.Execution == "workspace.content.authorization.view" && item.Surface == "read"
            : item.BusinessBinding == resource + ".authorize" && item.Surface == "featureActions");
        var context = Context();
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("api/authorization/data-scope/objects/{resourceKey}/{objectId}"), 0,
            new EndpointMetadataCollection(new EndpointModuleAttribute("Authorization"), new AuthenticatedOnlyAttribute(),
                new ResourceFeatureBindingAttribute("authorize"), new ButtonBusinessHandlerAttribute(variant == "wrong-handler" ? typeof(object) : typeof(Probe))), "actual-generic-management"));
        var resources = new ResourceCatalog(variant == "wrong-resource-owner");
        var targetResource = resources.Resources.Single(item => item.ResourceKey == resource);
        var target = Target(resource, targetResource.ModelName);
        context.Request.RouteValues["resourceKey"] = resource; context.Request.RouteValues["objectId"] = target.ObjectId;
        context.Request.Headers["X-NewP-Feature"] = variant == "wrong-feature" ? "unknown-feature" : variant == "view-provider"
            ? Assert.Single(plan.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Kind == "navigation" && point.Key == "workspace").Position
            : Assert.Single(declaration.FeaturePositions);
        context.Request.Headers[variant == "view-provider" ? "X-NewP-Data-Declaration" : "X-NewP-Button"] = declaration.Id;
        var checks = new Checks(); var handler = new Probe(target);
        var executor = new ButtonBusinessExecutor(new ButtonExecutionAccess(plan, new HttpContextAccessor { HttpContext = context }, resources, checks));
        if (variant is "workspace.folder" or "part.part" or "document.document")
        {
            Assert.Equal(1, await executor.ExecuteAsync(handler, 1));
            Assert.Equal(new[] { target.ObjectName + ":authorize" }, checks.Calls); Assert.True(handler.Executed);
        }
        else
        {
            await Assert.ThrowsAsync<ActionDataAccessException>(() => executor.ExecuteAsync(handler, 1));
            Assert.Empty(checks.Calls); Assert.False(handler.Executed);
        }
    }

    private static CanonicalModulePlan Plan()
    {
        var compiled = new CanonicalModulePlanCompiler().Compile("workspace-authorization-source",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics)); return compiled.Plan!;
    }
    private static DefaultHttpContext Context() => new() { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "ordinary")], "test")) };
    private static ButtonExecutionTarget Target(string resource, string model) => new(resource, model, model + "_actual", new Dictionary<string, object?> { ["EID"] = model + "_actual" });
    private sealed class ResourceCatalog(bool wrongFolder = false) : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources { get; } = [new("workspace.folder", wrongFolder ? "Part" : "Workspace", "WorkspaceFolder", "EID"),
            new("part.part", "Part", "Part", "EID"), new("document.document", "Document", "Document", "EID")];
    }
    private sealed class Checks : IObjectActionAccess
    {
        internal List<string> Calls { get; } = [];
        public Task<DataExecutionDecision> CheckAsync(string model, string id, IReadOnlyList<string> actions, CancellationToken token = default)
        { Calls.Add(model + ":" + string.Join(",", actions)); return Task.FromResult(DataExecutionDecision.Allowed); }
    }
    private sealed class Probe(ButtonBusinessTarget target) : IButtonBusinessHandler<int, int, int>
    {
        internal bool Executed;
        public Task<int> WithinBoundaryAsync(int input, string actionKey, Func<int, CancellationToken, Task<int>> execute, CancellationToken token) => execute(input, token);
        public ButtonBusinessTarget Target(int state) => target;
        public Task<int> ExecuteAsync(int state, CancellationToken token) { Executed = true; return Task.FromResult(state); }
    }
}

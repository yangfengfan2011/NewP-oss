using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class WorkspaceAuthorizationSourceTests
{
    [Theory]
    [InlineData("workspace.folder")]
    [InlineData("part.part")]
    [InlineData("document.document")]
    [InlineData("empty")]
    [InlineData("missing")]
    [InlineData("foreign-declaration")]
    [InlineData("wrong-endpoint")]
    [InlineData("wrong-feature")]
    [InlineData("unavailable-resource-owner")]
    public async Task CollectionSource_UsesActualExecutionOwnerAndAllRegisteredResourceDependencies(string variant)
    {
        var compiled = new CanonicalModulePlanCompiler().Compile("workspace-authorization-source",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "workspace.content.authorization.batch");
        var context = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "ordinary")], "test")) };
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EndpointModuleAttribute(variant == "wrong-endpoint" ? "Authorization" : "Workspace"),
            new RequireFeatureAttribute(variant == "wrong-feature" ? "workspace.folder.edit" : "workspace.folder.authorize")), "actual-workspace-permission-endpoint"));
        context.Request.Headers["X-NewP-Feature"] = Assert.Single(declaration.FeaturePositions);
        if (variant != "missing") context.Request.Headers["X-NewP-Data-Declaration"] = variant == "foreign-declaration"
            ? Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "part.part.authorization.manage" && item.Surface == "rowActions").Id : declaration.Id;
        var reads = new Reads(); var decisions = new Decisions();
        var access = new DataExecutionAccess(plan, new HttpContextAccessor { HttpContext = context }, reads, decisions,
            new ResourceCatalog(variant == "unavailable-resource-owner"));
        var binding = new DataExecutionBindingReference("workspace.content.authorization.batch", "authorization.collection.manage", "prepare");
        var valid = variant is "workspace.folder" or "part.part" or "document.document" or "empty";
        var targets = variant == "empty" ? Array.Empty<DataExecutionTargetInput>() : [new(variant.Contains('.') ? variant : "part.part", "actual-target")];
        var results = new List<DataExecutionTargetDecision>();
        async Task Evaluate()
        {
            await foreach (var result in access.EvaluateObjectsAsync(binding, "target", targets)) results.Add(result);
        }
        if (valid)
        {
            await Evaluate();
            Assert.Equal(targets.Length, results.Count);
            Assert.All(results, result => Assert.Equal(DataExecutionDecision.Allowed, result.Decision));
            Assert.Equal(targets.Length, reads.Calls);
            Assert.Equal(Enumerable.Repeat("authorize", targets.Length), decisions.Calls);
        }
        else
        {
            await Assert.ThrowsAsync<ActionDataAccessException>(Evaluate);
            Assert.Equal(0, reads.Calls); Assert.Empty(decisions.Calls);
        }
    }

    [Theory]
    [InlineData("workspace.folder")]
    [InlineData("part.part")]
    [InlineData("document.document")]
    [InlineData("view-provider")]
    [InlineData("unregistered-provider")]
    [InlineData("wrong-feature")]
    [InlineData("wrong-resource-owner")]
    public async Task GenericManagement_ConsumesOnlyItsRegisteredCapabilityAndActualResourceOwner(string variant)
    {
        var compiled = new CanonicalModulePlanCompiler().Compile("workspace-generic-authorization-source",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == (variant == "view-provider"
            ? "workspace.content.authorization.view" : "workspace.content.authorization.batch") && item.Surface == (variant == "view-provider" ? "read" : "pageActions"));
        var context = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "ordinary")], "test")) };
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EndpointModuleAttribute("Authorization"),
            new ResourceFeatureBindingAttribute("authorize"),
            new DataExecutionEndpointAttribute(variant == "unregistered-provider" ? "authorization.object.manage" : "authorization.collection.manage", "prepare")), "actual-generic-management"));
        context.Request.Headers["X-NewP-Feature"] = variant == "wrong-feature" ? "unknown-feature" : Assert.Single(declaration.FeaturePositions);
        context.Request.Headers["X-NewP-Data-Declaration"] = declaration.Id;
        var reads = new Reads(); var decisions = new Decisions();
        var access = new DataExecutionAccess(plan, new HttpContextAccessor { HttpContext = context }, reads, decisions,
            new ResourceCatalog(false, variant == "wrong-resource-owner"));
        var resource = variant.Contains('.') ? variant : "workspace.folder";
        async Task Evaluate()
        {
            var binding = access.BindResourceEndpoint(resource)!;
            await foreach (var result in access.EvaluateObjectsAsync(binding, "target", [new(resource, "actual-target")]))
                Assert.Equal(DataExecutionDecision.Allowed, result.Decision);
        }
        if (variant == "workspace.folder")
        {
            await Evaluate();
            Assert.Equal(1, reads.Calls); Assert.Equal(new[] { "authorize" }, decisions.Calls);
        }
        else
        {
            await Assert.ThrowsAsync<ActionDataAccessException>(Evaluate);
            Assert.Equal(0, reads.Calls); Assert.Empty(decisions.Calls);
        }
    }

    private sealed class ResourceCatalog(bool wrongOwner, bool wrongFolder = false) : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources { get; } = [new("workspace.folder", wrongFolder ? "Part" : "Workspace", "WorkspaceFolder", "EID"),
            new("part.part", "Part", "Part", "EID"), new("document.document", wrongOwner ? "Files" : "Document", "Document", "EID")];
    }
    private sealed class Reads : IDataScopeObjectAccess
    {
        internal int Calls;
        public Task<bool> IsReadableAsync(string resourceKey, string objectId, CancellationToken token = default)
        { Calls++; return Task.FromResult(true); }
    }
    private sealed class Decisions : IDataScopeObjectDecisionProvider
    {
        internal List<string> Calls { get; } = [];
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken token = default)
        { Calls.Add(actionKey); return Task.FromResult(true); }
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey,
            CancellationToken token = default) => throw new NotSupportedException();
    }
}

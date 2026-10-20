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
public sealed class PartAuthorizationSourceTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("foreign-resource")]
    [InlineData("wrong-endpoint-owner")]
    [InlineData("wrong-resource-owner")]
    [InlineData("wrong-feature-behavior")]
    public async Task OwnEndpoint_UsesTheRegisteredProviderWithoutRelaxingActualOwner(string variant)
    {
        var compiled = new CanonicalModulePlanCompiler().Compile("part-authorization-source",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "part.part.authorization.manage"
            && item.Surface == "rowActions");
        var target = Assert.Single(declaration.Requirements!, item => item.Role == "target" && item.Stage == "prepare");
        Assert.Equal(new[] { "read", "authorize" }, Assert.Single(target.AnyOf));
        var context = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "source-test")], "test")) };
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(
            new EndpointModuleAttribute(variant == "wrong-endpoint-owner" ? "Authorization" : "Part"),
            new RequireFeatureAttribute(variant == "wrong-feature-behavior" ? "part.part.history" : "part.part.authorize")), "actual-owner-endpoint"));
        context.Request.Headers["X-NewP-Feature"] = Assert.Single(declaration.FeaturePositions);
        if (variant != "missing") context.Request.Headers["X-NewP-Data-Declaration"] = variant == "foreign-resource"
            ? Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "authorization.role.authorization.manage").Id : declaration.Id;
        var reads = new Reads(); var decisions = new Decisions();
        var access = new DataExecutionAccess(plan, new HttpContextAccessor { HttpContext = context }, reads, decisions,
            new ResourceCatalog(variant == "wrong-resource-owner"));
        var binding = new DataExecutionBindingReference("part.part.authorization.manage", "authorization.object.manage", "prepare");
        if (variant == "valid")
        {
            Assert.Equal(DataExecutionDecision.Allowed, await access.EvaluateObjectAsync(binding, "part.part", "target", "actual-part"));
            Assert.Equal(1, reads.Calls); Assert.Equal(new[] { "authorize" }, decisions.Calls);
        }
        else
        {
            await Assert.ThrowsAsync<ActionDataAccessException>(() => access.EvaluateObjectAsync(binding, "part.part", "target", "actual-part"));
            Assert.Equal(0, reads.Calls); Assert.Empty(decisions.Calls);
        }
    }

    private sealed class ResourceCatalog(bool wrongOwner) : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources { get; } = [new("part.part", wrongOwner ? "Document" : "Part", "Part", "EID")];
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

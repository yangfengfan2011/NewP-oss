using System.Security.Claims;
using System.Reflection;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using ICP.Platform.Modules.Authorization.Controllers;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class InputReadSourceEndpointTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("missing-reference")]
    [InlineData("wrong-position")]
    [InlineData("wrong-endpoint")]
    [InlineData("wrong-owner")]
    [InlineData("generic")]
    [InlineData("mixed-header")]
    [InlineData("duplicate-feature")]
    [InlineData("submit")]
    public void ActualInputReadProjection_OnlyBindsItsCurrentStaticFeatureEndpoint(string problem)
    {
        var (access, binding, reads) = Create(problem);
        if (problem == "valid")
            foreach (var resource in new[] { "workspace.folder", "part.part", "document.document" }) access.Validate([binding], resource);
        else Assert.Throws<ActionDataAccessException>(() => access.Validate([binding], "workspace.folder"));
        Assert.Equal(0, reads.Calls);
    }

    [Fact]
    public async Task ActualInputReadProjection_StillPerformsTheOriginalObjectRead()
    {
        var (access, binding, reads) = Create("valid");
        Assert.Equal(DataExecutionDecision.NotFound, await access.EvaluateObjectAsync(binding, "workspace.folder", "view", "WorkspaceFolder_one"));
        Assert.Equal(1, reads.Calls);
    }

    private static (DataExecutionAccess Access, DataExecutionBindingReference Binding, Reads Reads) Create(string problem)
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var index = Array.FindIndex(candidates, item => item.ModuleId == "Workspace");
        candidates[index] = candidates[index] with
        {
            Features = candidates[index].Features.Select(feature => feature with { DataAccess = feature.DataAccess.Select(item =>
                item.BusinessBinding == "workspace.folder.authorize" && item.Surface == "pageActions" && item.ActionKey == "batchAuthorize"
                    ? item with { InputReadSourceKey = problem == "missing-reference" ? null : "workspace.content.permissions" }
                    : item).ToArray() }).ToArray()
        };
        var result = new CanonicalModulePlanCompiler().Compile("input-read-endpoint", candidates);
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var plan = result.Plan!;
        var button = Assert.Single(plan.ActionDataAccessCatalog, item => item.Surface == "pageActions" && item.ActionKey == "batchAuthorize");
        var source = Assert.Single(plan.ActionDataAccessCatalog, item => item.ViewKey == "workspace.content.permissions" && item.Surface == "read");
        var position = button.FeaturePositions[0];
        if (problem == "wrong-position") position = source.FeaturePositions.First(item => !button.FeaturePositions.Contains(item));
        var http = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "input-read-test")], "test")) };
        var metadata = new List<object> { new EndpointModuleAttribute(problem == "wrong-owner" ? "Part" : problem == "generic" ? "Authorization" : "Workspace") };
        if (problem == "generic")
        {
            // 直接消费当前generic控制器元数据，不给它补造旧读取provider或混合协议。
            var controller = typeof(DataScopeObjectsController);
            var method = controller.GetMethod(nameof(DataScopeObjectsController.Get))!;
            http.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
                RoutePatternFactory.Parse(controller.GetCustomAttribute<RouteAttribute>()!.Template!), 0,
                new EndpointMetadataCollection(controller.GetCustomAttributes(true).Concat(method.GetCustomAttributes(true))), "actual-generic-controller"));
            http.Request.RouteValues["resourceKey"] = "workspace.folder";
            http.Request.RouteValues["objectId"] = "WorkspaceFolder_one";
        }
        else
        {
            metadata.Add(new RequireFeatureAttribute(problem == "wrong-endpoint" ? "workspace.folder.transfer" : "workspace.folder.authorize"));
            if (problem == "duplicate-feature") metadata.Add(new RequireFeatureAttribute("workspace.folder.read"));
            http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "input-read-owner"));
        }
        http.Request.Headers["X-NewP-Data-Declaration"] = source.Id;
        http.Request.Headers["X-NewP-Feature"] = position;
        if (problem == "mixed-header") http.Request.Headers["X-NewP-Button"] = button.Id;
        var reads = new Reads();
        return (new(plan, new HttpContextAccessor { HttpContext = http }, reads, new Decisions(), new ResourceCatalog()),
            new(source.Execution, source.Provider!, problem == "submit" ? "submit" : "prepare"), reads);
    }

    private sealed class ResourceCatalog : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources { get; } = [new("workspace.folder", "Workspace", "WorkspaceFolder", "EID"),
            new("part.part", "Part", "Part", "EID"), new("document.document", "Document", "Document", "EID")];
    }

    private sealed class Reads : IDataScopeObjectAccess
    {
        internal int Calls;
        public Task<bool> IsReadableAsync(string resourceKey, string objectId, CancellationToken token = default) { ++Calls; return Task.FromResult(false); }
    }

    private sealed class Decisions : IDataScopeObjectDecisionProvider
    {
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken token = default) => throw new InvalidOperationException("Read source cannot perform a management action.");
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken token = default) => throw new NotSupportedException();
    }
}

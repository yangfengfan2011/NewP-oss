using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Workspace;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataExecutionQueryRoleTests
{
    [Fact]
    public async Task FixedApiObject_ConsumesOnlyTheCurrentRoleAtItsOriginalPoint()
    {
        var (access, source, _, counts) = ApiExecution();
        Assert.Equal(DataExecutionDecision.Allowed, (await access.EvaluateApiObjectAsync(source, "parent", new("workspace.folder", "parent-eid"))).Decision);
        Assert.Equal(1, counts.Resolved); Assert.Equal(new[] { "edit" }, counts.Actions);
        Assert.Equal(DataExecutionDecision.Allowed, (await access.EvaluateApiObjectAsync(source, "target", new("workspace.folder", "target-eid"))).Decision);
        Assert.Equal(2, counts.Resolved); Assert.Equal(new[] { "edit" }, counts.Actions);
        Assert.Equal(0, counts.UnresolvedReads);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("boundary")]
    [InlineData("identity")]
    [InlineData("provider")]
    [InlineData("route")]
    [InlineData("method")]
    [InlineData("owner")]
    [InlineData("header")]
    [InlineData("resource")]
    [InlineData("role")]
    [InlineData("shape")]
    [InlineData("stage")]
    [InlineData("branch")]
    public async Task FixedApiObject_RejectsCrossedSourcesBeforeAnyTargetRead(string mismatch)
    {
        var (access, source, http, counts) = ApiExecution(mismatch);
        var resource = mismatch == "resource" ? "part.part" : "workspace.folder";
        var role = mismatch == "role" ? "unknown-role" : "parent";
        source = mismatch switch
        {
            "unknown" => source with { Binding = source.Binding with { Execution = "unknown-api" } },
            "boundary" => source with { Binding = source.Binding with { Execution = "workspace.clipboard.copy" } },
            "identity" => source with { Identity = "foreign-api" },
            "provider" => source with { Binding = source.Binding with { Provider = "other.backend" } },
            "stage" => source with { Binding = source.Binding with { Stage = "read" } },
            "branch" => source with { Binding = source.Binding with { Branch = "copy" } },
            _ => source
        };
        if (mismatch == "header") http.Request.Headers["X-NewP-Data-Declaration"] = "foreign-entry";
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.EvaluateApiObjectAsync(source, role, new(resource, "actual-eid")));
        Assert.Equal(0, counts.Resolved); Assert.Empty(counts.Actions); Assert.Equal(0, counts.UnresolvedReads);
    }

    // 正式API合同/资源目录参与装配；计数resolver只证明来源与阶段分派，不代替真实SQL权限。
    private static (IDataExecutionAccess Access, DataExecutionApiObjectExecutionContext Source, DefaultHttpContext Http, ApiExecutionCounts Counts)
        ApiExecution(string? mismatch = null)
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        if (mismatch == "shape")
        {
            var owner = Array.FindIndex(candidates, item => item.ModuleId == "Workspace");
            candidates[owner] = candidates[owner] with { DataExecutions = candidates[owner].DataExecutions.Select(contract => contract.Key != "workspace.reference.add" ? contract
                : contract with { Requirements = contract.Requirements.Select(role => role.Role != "parent" ? role : role with { Shape = "objects", MinimumCount = 0, MaximumCount = null }).ToArray() }).ToArray() };
        }
        var compilation = new CanonicalModulePlanCompiler().Compile("api-object-execution", candidates);
        Assert.True(compilation.IsSuccess, JsonSerializer.Serialize(compilation.Diagnostics));
        var http = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actual-actor")], "test")) };
        http.Request.Method = mismatch == "method" ? "GET" : "POST";
        http.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(mismatch == "route" ? "api/other" : "api/workspace/content/references"), 0,
            new EndpointMetadataCollection(new EndpointModuleAttribute(mismatch == "owner" ? "Part" : "Workspace"), new HttpMethodMetadata(["POST"])), "actual-reference-api"));
        var services = new ServiceCollection(); new WorkspaceModule().ConfigureServices(services);
        var resources = new ApiQueryResources(services.Where(service => service.ServiceType == typeof(DataScopeResource))
            .Select(service => service.ImplementationInstance).OfType<DataScopeResource>().ToArray());
        var counts = new ApiExecutionCounts();
        var resolvers = new[] { new ApiExecutionResolver("workspace.position", counts), new ApiExecutionResolver("workspace.clipboard.endpoint", counts) };
        return (new DataExecutionAccess(compilation.Plan!, new HttpContextAccessor { HttpContext = http }, counts, counts, resources, resolvers),
            new(new("workspace.reference.add", "workspace.backend", "submit"), "workspace.reference.add"), http, counts);
    }

    private sealed class ApiExecutionCounts : IDataScopeObjectAccess, IDataScopeObjectDecisionProvider
    {
        internal int Resolved; internal int UnresolvedReads; internal List<string> Actions = [];
        public Task<bool> IsReadableAsync(string resourceKey, string objectId, CancellationToken token = default)
        { UnresolvedReads++; return Task.FromResult(true); }
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken token = default)
        { Actions.Add(actionKey); return Task.FromResult(true); }
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken token = default) => throw new NotSupportedException();
    }
    private sealed class ApiExecutionResolver(string key, ApiExecutionCounts counts) : IDataExecutionTargetResolver
    {
        public string Key => key; public string ModuleId => "Workspace";
        public Task<DataExecutionResolvedTarget?> ResolveAsync(DataExecutionTargetInput target, CancellationToken token)
        { counts.Resolved++; return Task.FromResult<DataExecutionResolvedTarget?>(new(target.ResourceKey, target.ObjectId!)); }
    }
}
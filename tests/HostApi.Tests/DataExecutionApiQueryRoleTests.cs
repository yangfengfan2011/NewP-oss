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
    [Theory]
    [InlineData("api/workspace/folders/candidates")]
    [InlineData("/api/workspace/folders/candidates")]
    public async Task ApiQueryRole_UsesTheActualEndpointTemplateAndOnePlanWithoutAViewDeclaration(string template)
    {
        var (access, context, http, queries) = ApiQuery(template: template);
        // 只证明匹配到的RouteEndpoint来源；真实HTTP路由/功能授权另由API消费者验证。
        http.Request.Path = "/not-used-as-the-template";
        Assert.Empty(http.Request.Headers);
        Assert.Same(queries.Plan, await access.GetApiQueryReadPlanAsync(context, "workspace.folder"));
        Assert.Equal(1, queries.Calls);
        Assert.Equal(0, queries.TargetCalls);
    }

    [Fact]
    public async Task ApiQueryRole_DoesNotNormalizeRepeatedLeadingSlashesInTheRegisteredRoute()
    {
        var (access, context, _, queries) = ApiQuery("double-leading-slash");
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetApiQueryReadPlanAsync(context, "workspace.folder"));
        Assert.Equal(0, queries.Calls);
        Assert.Equal(0, queries.TargetCalls);
    }

    [Theory]
    [InlineData("route")]
    [InlineData("method")]
    [InlineData("method-metadata")]
    [InlineData("owner")]
    [InlineData("provider")]
    [InlineData("identity")]
    [InlineData("resource")]
    [InlineData("entry")]
    [InlineData("internal")]
    [InlineData("view-header")]
    [InlineData("button-header")]
    [InlineData("blank-button-header")]
    [InlineData("blank-view-header")]
    [InlineData("mixed-headers")]
    [InlineData("role")]
    [InlineData("binding-stage")]
    [InlineData("shape")]
    [InlineData("stage")]
    [InlineData("minimum")]
    [InlineData("maximum")]
    [InlineData("action")]
    [InlineData("extra-role")]
    public async Task ApiQueryRole_RejectsForeignSourcesBeforeObtainingAnyPlan(string mismatch)
    {
        var (access, context, http, queries) = ApiQuery(mismatch);
        var resource = "workspace.folder";
        switch (mismatch)
        {
            case "method": http.Request.Method = "POST"; break;
            case "provider": context = context with { Binding = context.Binding with { Provider = "example.backend" } }; break;
            case "identity": context = context with { Identity = "different-api" }; break;
            case "resource": resource = "part.part"; break;
            case "entry": context = context with { Binding = new("workspace.history.enter", "workspace.backend", "read") }; break;
            case "internal": context = context with { Binding = new("workspace.properties.detail", "workspace.backend", "read") }; break;
            case "view-header": http.Request.Headers["X-NewP-Data-Declaration"] = "foreign-view-declaration"; break;
            case "button-header": http.Request.Headers["X-NewP-Button"] = "foreign-button"; break;
            case "blank-button-header": http.Request.Headers["X-NewP-Button"] = ""; break;
            case "blank-view-header": http.Request.Headers["X-NewP-Data-Declaration"] = ""; break;
            case "mixed-headers":
                http.Request.Headers["X-NewP-Button"] = "foreign-button";
                http.Request.Headers["X-NewP-Data-Declaration"] = "foreign-entry"; break;
            case "role": context = context with { Role = "target" }; break;
            case "binding-stage": context = context with { Binding = context.Binding with { Stage = "submit" } }; break;
        }
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetApiQueryReadPlanAsync(context, resource));
        Assert.Equal(0, queries.Calls);
        Assert.Equal(0, queries.TargetCalls);
    }

    // 真实候选合同和模块资源贡献用于入口边界控制；假的read计划只计调用，不作为权限证据。
    private static (IDataExecutionAccess Access, DataExecutionApiQueryContext Context, DefaultHttpContext Http, Queries Queries) ApiQuery(
        string? mismatch = null, string template = "api/workspace/folders/candidates", Func<DataExecutionContract, DataExecutionContract>? configure = null)
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var index = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Workspace");
        var contract = candidates[index].DataExecutions.Single(contract => contract.Key == WorkspaceFolderExecutions.Candidates);
        var requirement = contract.Requirements[0];
        requirement = mismatch switch
        {
            "shape" => requirement with { Shape = "objects" },
            "stage" => requirement with { Stage = "prepare" },
            "minimum" => requirement with { MinimumCount = 1 },
            "maximum" => requirement with { MaximumCount = 10 },
            "action" => requirement with { AnyOf = [["read", "edit"]] },
            _ => requirement
        };
        contract = contract with { Requirements = mismatch == "extra-role" ? [requirement, requirement with { Role = "other" }] : [requirement] };
        if (mismatch == "double-leading-slash") contract = contract with
        { FixedBindings = contract.FixedBindings.Select(source => source with { Route = "//api/workspace/folders/candidates" }).ToArray() };
        contract = configure?.Invoke(contract) ?? contract;
        candidates[index] = candidates[index] with { DataExecutions = candidates[index].DataExecutions.Select(item => item.Key == contract.Key ? contract : item).ToArray() };
        var compiled = new CanonicalModulePlanCompiler().Compile("api-query-314", candidates);
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var http = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "api-test")], "test")) };
        http.Request.Method = "GET";
        http.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(mismatch == "route" ? "api/other" : template), 0,
            new EndpointMetadataCollection(new EndpointModuleAttribute(mismatch == "owner" ? "Part" : "Workspace"),
                new HttpMethodMetadata([mismatch == "method-metadata" ? "POST" : "GET"])), "actual-api-template"));
        var services = new ServiceCollection();
        new WorkspaceModule().ConfigureServices(services);
        var resources = new ApiQueryResources(services.Where(service => service.ServiceType == typeof(DataScopeResource))
            .Select(service => service.ImplementationInstance).OfType<DataScopeResource>().ToArray());
        var queries = new Queries();
        return (new DataExecutionAccess(compiled.Plan!, new HttpContextAccessor { HttpContext = http }, queries, queries,
            resources: resources, queryReads: queries, targetReads: queries), WorkspaceFolderExecutions.CandidatesQuery(), http, queries);
    }

    private sealed class ApiQueryResources(IReadOnlyList<DataScopeResource> resources) : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources => resources;
    }
}

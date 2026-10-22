using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

/// <summary>启停的正式端点和当前按钮必须双向一致，不能借用同为edit要求的更新按钮。</summary>
public sealed class StandardCrudStateButtonEndpointTests
{
    [Theory]
    [InlineData("enable", "enable")]
    [InlineData("disable", "disable")]
    [InlineData("update", "update")]
    [InlineData("enable", "disable")]
    [InlineData("disable", "enable")]
    [InlineData("enable", "update")]
    [InlineData("update", "enable")]
    [InlineData("disable", "update")]
    [InlineData("update", "disable")]
    public async Task FormalStateEndpoint_RequiresItsOwnButtonEvenWhenAllRequireEdit(string endpointOperation, string buttonOperation)
    {
        var plan = Plan();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(plan);
        await using var app = builder.Build();
        var descriptor = new StandardCrudDescriptor
        {
            ModuleId = "Part", ResourceKey = "part.part", ModelName = "Part", Route = "api/parts", IdField = "EID", DisplayField = "ENAME",
            FieldSet = new(["EID", "ENAME"], ["EID", "ENAME"], ["ENAME"], ["ENAME"]),
            Authorization = StandardCrudAuthorization.ByFeature("part.part", StandardCrudOperation.Update, StandardCrudOperation.Enable, StandardCrudOperation.Disable)
        };
        app.MapPost("api/parts/{id}/" + endpointOperation, () => Results.NoContent())
            .WithMetadata(new EndpointModuleAttribute("Part"), new RequireFeatureAttribute("part.part." + endpointOperation))
            .WithDataExecution(app.Services, descriptor, endpointOperation);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).Single();
        Assert.Equal(new ButtonExecutionEndpoint("part.part." + endpointOperation, "part.part", "Part"), endpoint.Metadata.GetMetadata<ButtonExecutionEndpoint>());
        var button = plan.ActionDataAccessCatalog.Single(item => item.BusinessBinding == "part.part." + buttonOperation);
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actor")], "state-button")) };
        http.SetEndpoint(endpoint);
        http.Request.RouteValues["id"] = "Part_official";
        http.Request.Headers["X-NewP-Button"] = button.Id;
        http.Request.Headers["X-NewP-Feature"] = button.FeaturePositions[0];
        var checks = new Checks();
        var access = new ButtonExecutionAccess(plan, new HttpContextAccessor { HttpContext = http }, new ResourceCatalog(), checks);
        var writes = 0;
        if (endpointOperation != buttonOperation)
            Assert.Equal(400, Assert.Throws<ActionDataAccessException>(() => access.Bind("part.part", "Part", endpointOperation, "Part_official")).Status);
        else
        {
            var source = access.Bind("part.part", "Part", endpointOperation, "Part_official");
            await access.ExecuteAsync(source, new ButtonExecutionTarget("part.part", "Part", "Part_official", new Dictionary<string, object?> { ["EID"] = "Part_official" }),
                _ => { writes++; return Task.FromResult(1); });
            Assert.Equal(new[] { "edit" }, checks.Actions);
        }
        Assert.Equal(endpointOperation == buttonOperation ? 1 : 0, writes);
        Assert.Equal(writes, checks.Calls);
    }

    // 正式metadata compiler从实际标准动作生成按钮和功能位置，不手写用于绑定的身份。
    private static CanonicalModulePlan Plan()
    {
        using var nav = JsonDocument.Parse("""{"navigation":{"items":[{"key":"parts","featureAuthorization":true,"targetKey":"part.part.list"}]}}""");
        using var model = JsonDocument.Parse("""{"Part":{"kind":"Entity"}}""");
        using var view = JsonDocument.Parse("""
            {"view":{"key":"part.part.list","mode":"list","model":"Part","resourceKey":"part.part","rowActions":[
              {"key":"edit","standardAction":"edit","dataAccess":{"object":"Part","actions":["edit"]}},
              {"key":"enable","standardAction":"enable","dataAccess":{"object":"Part","actions":["edit"]}},
              {"key":"disable","standardAction":"disable","dataAccess":{"object":"Part","actions":["edit"]}}]}}
            """);
        var compiled = new CanonicalModulePlanCompiler().Compile("state-endpoint", new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        var features = StandardFeatureMetadataCompiler.Read("Part", [nav.RootElement, model.RootElement, view.RootElement]);
        return new(plan.CandidateId, plan.CompilerContractVersion, plan.PlanHash, plan.SnapshotId, plan.CanonicalBytes,
            plan.Modules.Select(module => module with { Features = module.ModuleId == "Part" ? features : [], DataExecutions = [], DataExecutionProviders = [] }).ToArray());
    }
    private sealed class ResourceCatalog : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources { get; } = [new("part.part", "Part", "Part", "EID")];
    }
    private sealed class Checks : IObjectActionAccess
    {
        internal int Calls;
        internal IReadOnlyList<string>? Actions;
        public Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions, CancellationToken token = default)
        { Calls++; Actions = actions; return Task.FromResult(DataExecutionDecision.Allowed); }
    }
}

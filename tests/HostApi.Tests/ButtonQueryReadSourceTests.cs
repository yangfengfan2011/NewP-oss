using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class ButtonQueryReadSourceTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("outside")]
    [InlineData("binding")]
    [InlineData("role")]
    [InlineData("resource")]
    [InlineData("missing")]
    [InlineData("mixed")]
    [InlineData("failure")]
    public async Task ActualExportQuery_RequiresItsCurrentButtonScopeAndPropagatesTheSqlReadPlan(string problem)
    {
        var plan = Plan();
        var button = Assert.Single(plan.ActionDataAccessCatalog, item => item.BusinessBinding == "organization.person.export");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actor")], "query-test")) };
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new EndpointModuleAttribute("Organization"),
            new RequireFeatureAttribute("organization.person.export"), new ButtonBusinessHandlerAttribute(typeof(Handler))), "export"));
        context.Request.Headers["X-NewP-Feature"] = button.FeaturePositions[0];
        if (problem != "missing") context.Request.Headers["X-NewP-Button"] = button.Id;
        if (problem == "mixed") context.Request.Headers["X-NewP-Data-Declaration"] = button.Id;
        var http = new HttpContextAccessor { HttpContext = context };
        var scope = new ButtonQueryReadScope();
        var resources = new ResourceCatalog();
        var noObjects = new NoObjects();
        var buttons = new ButtonExecutionAccess(plan, http, resources, noObjects, scope);
        var reader = new QueryReads { Fail = problem == "failure" };
        var access = new DataExecutionAccess(plan, http, noObjects, noObjects, resources, queryReads: reader, buttonQueries: scope, buttons: buttons);
        var source = Source();
        var requested = problem == "binding" ? source with { Binding = source.Binding with { Execution = "organization.person.list" } }
            : problem == "role" ? source with { Role = "other" } : source;
        var handler = new Handler(new("organization.person", "Person", source), async token =>
        {
            Assert.NotNull(scope.Current);
            var result = await access.GetQueryReadPlanAsync(requested, problem == "resource" ? "organization.company" : "organization.person", token);
            Assert.Same(reader.Plan, result);
            return 1;
        });
        if (problem == "outside")
            await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetQueryReadPlanAsync(source, "organization.person"));
        else if (problem == "valid") Assert.Equal(1, await new ButtonBusinessExecutor(buttons).ExecuteAsync(handler, 0));
        else
        {
            var failure = await Record.ExceptionAsync(() => new ButtonBusinessExecutor(buttons).ExecuteAsync(handler, 0));
            if (problem == "failure") Assert.Same(reader.Failure, failure);
            else Assert.IsType<ActionDataAccessException>(failure);
        }
        Assert.Null(scope.Current);
        Assert.Equal(problem is "valid" or "failure" ? 1 : 0, reader.Calls);
    }

    [Fact]
    public async Task NestedQuerySource_RestoresTheOuterSourceAndExpiresOnDispose()
    {
        var scope = new ButtonQueryReadScope();
        var outer = new ButtonBusinessInvocation("outer", "feature", "business", "export", typeof(Handler));
        var target = new ButtonQueryTarget("organization.person", "Person", Source());
        using (scope.Enter(outer, target))
        {
            using (scope.Enter(outer with { ButtonId = "inner" }, target))
            {
                await Task.Yield();
                Assert.Equal("inner", scope.Current!.Invocation.ButtonId);
            }
            Assert.Equal("outer", scope.Current!.Invocation.ButtonId);
        }
        Assert.Null(scope.Current);
    }

    private static DataExecutionQueryContext Source() => new(new("organization.person.export", "standardCrud.export", "read"), "query");
    private static CanonicalModulePlan Plan()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var owner = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Organization");
        candidates[owner] = candidates[owner] with { Features = candidates[owner].Features.Select(feature => feature with
        {
            DataAccess = feature.DataAccess.Select(declaration => declaration.Execution != "organization.person.export" ? declaration
                : declaration with { Execution = "", Object = "Person", Actions = ["read"], BusinessBinding = "organization.person.export" }).ToArray()
        }).ToArray() };
        var result = new CanonicalModulePlanCompiler().Compile("query-read-source", candidates);
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        return result.Plan!;
    }
    private sealed class Handler(ButtonQueryTarget target, Func<CancellationToken, Task<int>> read) : IButtonBusinessHandler<int, int, int>
    {
        public Task<int> WithinBoundaryAsync(int input, string actionKey, Func<int, CancellationToken, Task<int>> execute, CancellationToken cancellationToken) => execute(input, cancellationToken);
        public ButtonBusinessTarget Target(int state) => target;
        public Task<int> ExecuteAsync(int state, CancellationToken cancellationToken) => read(cancellationToken);
    }
    private sealed class ResourceCatalog : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources { get; } = [new("organization.person", "Organization", "Person", "EID")];
    }
    private sealed class NoObjects : IObjectActionAccess, IDataScopeObjectAccess, IDataScopeObjectDecisionProvider
    {
        public Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Query must not precheck individual objects.");
        public Task<bool> IsReadableAsync(string resourceKey, string objectId, CancellationToken token = default) => throw new InvalidOperationException("Query must remain in SQL.");
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Query must remain in SQL.");
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Query must remain in SQL.");
    }
    private sealed class QueryReads : IDataScopeReadPlanProvider
    {
        public readonly DataScopeReadPlan Plan = new(row => false);
        public readonly Exception Failure = new InvalidOperationException("Injected SQL read-plan failure.");
        public bool Fail;
        public int Calls;
        public Task<DataScopeReadPlan> GetReadPlanAsync(string resourceKey, CancellationToken cancellationToken = default)
        {
            ++Calls;
            return Fail ? Task.FromException<DataScopeReadPlan>(Failure) : Task.FromResult(Plan);
        }
    }
}

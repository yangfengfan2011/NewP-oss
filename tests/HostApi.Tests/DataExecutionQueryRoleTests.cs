using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Models;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Http;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Modules.Workspace;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataExecutionQueryRoleTests
{
    [Fact]
    public async Task DisplayComposition_WithRealWorkspaceResolversConstructsBeforeResolvingItsBatchExecutor()
    {
        var (access, _, _, queries) = Create();
        var services = new ServiceCollection();
        services.AddLogging(); services.AddHttpContextAccessor();
        new ICP.Platform.Modules.Organization.OrganizationModule().ConfigureServices(services);
        new ICP.Platform.Modules.Identity.IdentityModule().ConfigureServices(services);
        services.AddSingleton<IOrganizationQuery, ConstructionOnlyOrganization>();
        services.AddSingleton<IIdentityAccountDirectory, ConstructionOnlyAccounts>();
        services.AddScoped(_ => new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>().Options,
            Array.Empty<ICP.Platform.Modeling.Definitions.ModelDefinition>()));
        services.AddSingleton<IDataScopeReadPlanProvider>(queries);
        services.AddSingleton(new LinkTreeCatalog([])); services.AddScoped<LinkTreeReader>(); services.AddScoped<WorkspacePositions>();
        services.AddScoped<IDataExecutionTargetResolver, WorkspacePositionDataExecutionResolver>();
        services.AddScoped<IDataExecutionTargetResolver, WorkspaceClipboardEndpointDataExecutionResolver>();
        services.AddScoped<IDataExecutionAccess>(provider =>
        {
            var resolvers = provider.GetServices<IDataExecutionTargetResolver>().ToArray();
            Assert.Equal(2, resolvers.Length);
            return access;
        });
        var provider = services.BuildServiceProvider(); var scope = provider.CreateScope();
        // 纯构造回归没有数据库连接；若重新引入DI闭环，定时失败后不在同一锁上阻塞Dispose。
        Assert.Same(access, await Task.Run(() => scope.ServiceProvider.GetRequiredService<IDataExecutionAccess>()).WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(2, scope.ServiceProvider.GetServices<IStandardCrudDisplayValueResolver>().Count());
        Assert.NotEmpty(scope.ServiceProvider.GetServices<IStandardCrudSummaryProvider>());
        Assert.IsType<ICP.Platform.Modules.Identity.Application.IdentityUserDisplayNameDirectory>(scope.ServiceProvider.GetRequiredService<ICP.Platform.Presentation.IUserDisplayNameDirectory>());
        Assert.Equal(0, queries.Calls); Assert.Equal(0, queries.TargetCalls);
        scope.Dispose(); provider.Dispose();
    }

    [Fact]
    public async Task QueryRole_ReturnsTheOriginalPredicateWithoutMaterializingOrPrecheckingObjects()
    {
        var (access, context, _, queries) = Create();
        var result = await access.GetQueryReadPlanAsync(context, "example.record");
        Assert.Same(queries.Plan, result);
        Assert.Equal(1, queries.Calls);
        await access.GetQueryReadPlanAsync(context, "example.record");
        Assert.Equal(2, queries.Calls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("list-as-export")]
    [InlineData("resource")]
    [InlineData("role")]
    [InlineData("stage")]
    [InlineData("position")]
    public async Task QueryRole_RejectsMismatchedSourcesBeforeObtainingAnyPlan(string problem)
    {
        var (access, context, http, queries) = Create();
        var resource = "example.record";
        switch (problem)
        {
            case "missing": http.Request.Headers.Remove("X-NewP-Data-Declaration"); break;
            case "list-as-export": context = context with { Binding = new("example.record.list", "standardCrud.list", "read") }; break;
            case "resource": resource = "organization.person"; break;
            case "role": context = context with { Role = "target" }; break;
            case "stage": context = context with { Binding = context.Binding with { Stage = "submit" } }; break;
            case "position": http.Request.Headers["X-NewP-Feature"] = "unregistered-position"; break;
        }
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetQueryReadPlanAsync(context, resource));
        Assert.Equal(0, queries.Calls);
    }

    [Fact]
    public async Task OptionsRole_KeepsAnExplicitCrossChunkCurrentSetSeparateFromTheQuery()
    {
        var (access, context, _, queries) = Create("organization.position.options", "organization.person.list");
        var ids = Enumerable.Range(0, 101).Select(index => "current-" + index).ToArray();
        context = context with { TargetSets = [new("current", ids)] };
        Assert.Same(queries.Plan, await access.GetQueryReadPlanAsync(context, "organization.position"));
        Assert.Equal(1, queries.Calls);
        foreach (var invalid in new IReadOnlyList<DataExecutionReadTargetSet>[] { [], [new("wrong", ids)], [new("current", [""])], [new("current", ids), new("current", ids)] })
            await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetQueryReadPlanAsync(context with { TargetSets = invalid }, "organization.position"));
        Assert.Equal(1, queries.Calls);
    }

    [Theory]
    [InlineData("example.record.read", "read")]
    [InlineData("example.record.update", "prepare")]
    public async Task ObjectReadRole_UsesOnlyTheTwoRealDetailSourcesAndTheOriginalPlan(string execution, string stage)
    {
        var (access, context, _, queries) = Create(execution);
        var bindings = new DataExecutionBindingReference[] { new("example.record.read", "standardCrud.read", "read"), new("example.record.update", "standardCrud.update", "prepare") };
        Assert.Equal(stage, bindings.Single(binding => binding.Execution == execution).Stage);
        Assert.Same(queries.Plan, await access.GetObjectReadPlanAsync(bindings, "example.record", "target", "real-id"));
        Assert.Equal(1, queries.Calls);
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetObjectReadPlanAsync([context.Binding], "example.record", "query", "real-id"));
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetObjectReadPlanAsync(bindings, "example.record", "target", ""));
        Assert.Equal(1, queries.Calls);
    }

    [Fact]
    public async Task ObjectReadRole_DoesNotAcceptAListOrExportDeclaration()
    {
        var (access, context, _, queries) = Create();
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetObjectReadPlanAsync([context.Binding], "example.record", "target", "real-id"));
        Assert.Equal(0, queries.Calls);
    }

    [Fact]
    public async Task InternalRead_UsesItsRegisteredCallerAndOneOriginalBatchWithoutHttpCredentials()
    {
        var (access, _, http, queries) = Create();
        http.Request.Headers.Clear(); http.SetEndpoint(null);
        var binding = new DataExecutionInternalReadBinding("organization.person.display", "organization.backend",
            typeof(ICP.Platform.Modules.Organization.Crud.PersonCrudDisplayValueResolver));
        var ids = Enumerable.Range(0, 101).Select(index => "related-" + index).ToArray();
        Assert.Equal(ids.Length, (await access.GetInternalReadableIdsAsync(binding, "organization.company", ids)).Count);
        Assert.Equal(1, queries.TargetCalls); Assert.Equal(0, queries.Calls);
        foreach (var invalid in new[] { binding with { Execution = "missing" }, binding with { Provider = "identity.backend" }, binding with { Caller = typeof(DataExecutionQueryRoleTests) }, binding with { Execution = "example.record.export", Provider = "standardCrud.export" } })
            await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetInternalReadableIdsAsync(invalid, "organization.company", ids));
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetInternalReadableIdsAsync(binding, "identity.account", ids));
        Assert.Equal(1, queries.TargetCalls);
    }

    [Theory]
    [InlineData("workspace.properties.detail", nameof(WorkspaceContents.ReadPropertiesAsync))]
    [InlineData("workspace.editValues.detail", nameof(WorkspaceContents.ReadEditValuesAsync))]
    [InlineData("workspace.objectCopy.detail", nameof(WorkspaceContents.PrepareObjectCopyAsync))]
    public async Task InternalDetailRole_RequiresTheConcreteMemberAndExactlyOneRealId(string execution, string member)
    {
        var (access, _, http, queries) = Create(); http.Request.Headers.Clear(); http.SetEndpoint(null);
        var binding = new DataExecutionInternalReadBinding(execution, "workspace.backend", typeof(WorkspaceContents), member);
        Assert.Same(queries.Plan, await access.GetInternalReadPlanAsync(binding, "part.part", ["resolved-eid"]));
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetInternalReadPlanAsync(binding with { Member = "MissingMethod" }, "part.part", ["resolved-eid"]));
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetInternalReadPlanAsync(binding with { Execution = "workspace.clipboard.copy" }, "part.part", ["resolved-eid"]));
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetInternalReadPlanAsync(binding, "part.part", []));
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetInternalReadPlanAsync(binding, "part.part", ["one", "two"]));
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetInternalReadPlanAsync(binding, "organization.company", ["resolved-eid"]));
        Assert.Equal(1, queries.Calls); Assert.Equal(0, queries.TargetCalls);
    }

    [Fact]
    public void QueryContext_CannotBeSuppliedOrPublishedThroughTheListDto()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var context = new DataExecutionQueryContext(new("example.record.export", "standardCrud.export", "read"), "query");
        var serialized = JsonSerializer.Serialize(new StandardCrudListQuery { ReadContext = context }, options);
        Assert.DoesNotContain("readContext", serialized);
        var injected = JsonSerializer.Deserialize<StandardCrudListQuery>("""
            {"pageSize":101,"readContext":{"binding":{"execution":"example.record.export","provider":"standardCrud.export","stage":"read"},"role":"query"}}
            """, options)!;
        Assert.Null(injected.ReadContext);
        Assert.Equal(101, injected.PageSize);
    }

    private static (DataExecutionAccess Access, DataExecutionQueryContext Context, DefaultHttpContext Http, Queries Queries) Create(string execution = "example.record.export", string? view = null)
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules"));
        var compilation = new CanonicalModulePlanCompiler().Compile("query-engine", candidates);
        Assert.True(compilation.IsSuccess, JsonSerializer.Serialize(compilation.Diagnostics));
        var plan = compilation.Plan!;
        var effective = plan.ActionDataAccessCatalog.Single(item => item.Execution == execution && (view is not null ? item.ViewKey == view : execution != "example.record.update" || item.Surface == "rowActions"));
        var http = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "query-test")], "test")) };
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EndpointModuleAttribute(effective.ModuleId)), "query-role-owner"));
        http.Request.Headers["X-NewP-Data-Declaration"] = effective.Id;
        http.Request.Headers["X-NewP-Feature"] = effective.FeaturePositions.Single(position => execution == "example.record.update"
            ? position.EndsWith("surface:list.rowActions/action:edit", StringComparison.Ordinal)
            : !position.Contains("/surface:", StringComparison.Ordinal) || execution == "example.record.export" && position.EndsWith("/action:export", StringComparison.Ordinal));
        var queries = new Queries();
        return (new(plan, new HttpContextAccessor { HttpContext = http }, queries, queries, queryReads: queries, targetReads: queries),
            new(new(effective.Execution, effective.Provider!, "read"), "query"), http, queries);
    }

    private sealed class ConstructionOnlyOrganization : IOrganizationQuery
    {
        public ValueTask<IOrganizationReadView> ReadAsync(CancellationToken token = default) => throw new InvalidOperationException("DI construction must not read organization data.");
    }
    private sealed class ConstructionOnlyAccounts : IIdentityAccountDirectory
    {
        public ValueTask<IdentityAccountSnapshot?> FindAsync(string id, CancellationToken token = default) => throw new InvalidOperationException("DI construction must not read accounts.");
        public ValueTask<IReadOnlyDictionary<string, IdentityAccountSnapshot>> FindManyAsync(IReadOnlyCollection<string> ids, CancellationToken token = default) => throw new InvalidOperationException("DI construction must not read accounts.");
        public ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(IdentityAccountSearch search, CancellationToken token = default) => throw new InvalidOperationException("DI construction must not search accounts.");
    }

    // 若查询路径意外改为对象枚举，任何单对象调用立即失败，不能用返回小样本掩盖放大。
    private sealed class Queries : IDataScopeReadPlanProvider, IDataScopeObjectAccess, IDataScopeObjectDecisionProvider, IDataScopeReadableIds
    {
        internal int Calls;
        internal int TargetCalls;
        public Task<IReadOnlySet<string>> GetReadableIdsAsync(string resourceKey, IReadOnlyCollection<string> ids, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); TargetCalls++; return Task.FromResult<IReadOnlySet<string>>(ids.ToHashSet(StringComparer.Ordinal)); }
        internal DataScopeReadPlan Plan { get; } = new(row => (string)row["ENAME"]! != "Blocked");
        public Task<DataScopeReadPlan> GetReadPlanAsync(string resourceKey, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(Plan); }
        public Task<bool> IsReadableAsync(string resourceKey, string objectId, CancellationToken token = default) => throw new InvalidOperationException("Query must not precheck individual objects.");
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken token = default) => throw new InvalidOperationException("Query must not precheck individual actions.");
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken token = default) => throw new NotSupportedException();
    }
}

using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataExecutionObjectRoleTests
{
    // 同一角色只有一种资源或混合资源都合法；OR属于实际资源，不要求每类资源出现一次。
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CollectionRole_UsesActualResourceAlternativesAndReadsEachTargetOnce(bool mixed)
    {
        var (access, binding, reads, decisions) = Create();
        DataExecutionTargetInput[] targets = mixed ? [new("example.record", "one"), new("organization.person", "two")] : [new("example.record", "one")];
        var result = await CollectAsync(access, binding, targets);
        Assert.All(result, item => Assert.Equal(DataExecutionDecision.Allowed, item.Decision));
        Assert.Equal(targets.Length, reads.Calls.Count);
        Assert.Contains(("example.record", "one", "delete"), decisions.Calls);
        Assert.DoesNotContain(decisions.Calls, call => call.Resource == "organization.person" && call.Action == "delete");
    }

    // business拒绝可在yield之间被消费；后续对象判权不能抢先覆盖更早的原业务错误。
    [Fact]
    public async Task CollectionRole_DoesNotEvaluateLaterTargetsBeforeTheOwnerConsumesTheFirst()
    {
        var (access, binding, reads, _) = Create();
        await using var iterator = access.EvaluateObjectsAsync(binding, "targets", [new("example.record", "one"), new("organization.person", "two")]).GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        Assert.Equal("one", iterator.Current.Target!.ObjectId);
        Assert.Single(reads.Calls);
    }

    // 基数针对实际参与集合，未知资源和超出合同的集合在触发任何数据裁决前拒绝。
    [Theory]
    [InlineData("empty")]
    [InlineData("overflow")]
    [InlineData("resource")]
    public async Task CollectionRole_RejectsInvalidShapeBeforeDecisions(string problem)
    {
        var (access, binding, reads, decisions) = Create();
        DataExecutionTargetInput[] targets = problem switch { "empty" => [], "resource" => [new("unknown.resource", "one")],
            _ => Enumerable.Range(0, 101).Select(index => new DataExecutionTargetInput("example.record", index.ToString())).ToArray() };
        await Assert.ThrowsAsync<ActionDataAccessException>(() => CollectAsync(access, binding, targets));
        Assert.Empty(reads.Calls); Assert.Empty(decisions.Calls);
    }

    // mode由执行owner选择；copy的OR和cut的edit不能串用，未知分支不降级为默认组合。
    [Fact]
    public async Task CollectionRole_SelectsOnlyTheRegisteredServerBranch()
    {
        var (access, binding, _, _) = Create(branches: true);
        Assert.Equal(DataExecutionDecision.Allowed, Assert.Single(await CollectAsync(access, binding with { Branch = "copy" }, [new("example.record", "one")])).Decision);
        Assert.Equal(DataExecutionDecision.Denied, Assert.Single(await CollectAsync(access, binding with { Branch = "cut" }, [new("example.record", "one")])).Decision);
        await Assert.ThrowsAsync<ActionDataAccessException>(() => CollectAsync(access, binding with { Branch = "invented" }, [new("example.record", "one")]));
    }

    // 真ID由完整read解析器产出；edit使用该ID，业务取回事实，每个新阶段调用重新解析。
    [Fact]
    public async Task Resolver_ProducesTheRealTargetBeforeActionsAndReturnsTypedFacts()
    {
        var resolver = new Resolver();
        var (access, binding, reads, decisions) = Create(resolver: resolver);
        var source = new object();
        for (var phase = 0; phase < 2; phase++)
        {
            var result = Assert.Single(await CollectAsync(access, binding, [new("example.record", null, source)]));
            Assert.Equal(DataExecutionDecision.Allowed, result.Decision);
            Assert.Same(source, result.Target!.Facts);
            Assert.Equal("resolved-target", result.Target.ObjectId);
        }
        Assert.Equal(2, resolver.Calls);
        Assert.Empty(reads.Calls);
        Assert.All(decisions.Calls, call => Assert.Equal("resolved-target", call.Id));
    }

    // 合成执行借真实模块/位置装配，仅测试共享角色引擎，不创建产品API或手工填写功能位置。
    private static (DataExecutionAccess Access, DataExecutionBindingReference Binding, Reads Reads, Decisions Decisions) Create(bool branches = false, Resolver? resolver = null)
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var index = Array.FindIndex(candidates, item => item.ModuleId == "Example");
        var source = new DataExecutionRequirement("targets", "submit", "objects", ["example.record", "organization.person"], [["read"]], 1, 100)
        { ResourceAnyOf = new Dictionary<string, IReadOnlyList<IReadOnlyList<string>>> { ["example.record"] = [["edit"], ["delete"]], ["organization.person"] = [["edit"]] } };
        if (resolver is not null) source = source with { ReadResolverKey = resolver.Key };
        var contract = new DataExecutionContract("example.record.authorization.manage", "Example", "example.backend", "entry", ["example.record.authorize"],
            branches ? [source with { Branch = "copy" }, source with { Branch = "cut", AnyOf = [["read", "edit"]], ResourceAnyOf = null }] : [source])
        { Branches = branches ? ["copy", "cut"] : [] };
        candidates[index] = candidates[index] with
        {
            DataExecutions = candidates[index].DataExecutions.Select(item => item.Key == contract.Key ? contract : item).ToArray(),
            Features = candidates[index].Features.Select(feature => feature with { DataAccess = feature.DataAccess.Select(binding => binding.Execution == contract.Key
                ? binding with { Resources = source.Resources } : binding).ToArray() }).ToArray()
        };
        var compilation = new CanonicalModulePlanCompiler().Compile("role-engine", candidates);
        Assert.True(compilation.IsSuccess, System.Text.Json.JsonSerializer.Serialize(compilation.Diagnostics));
        var plan = compilation.Plan!;
        var effective = plan.ActionDataAccessCatalog.Single(item => item.Execution == contract.Key);
        Assert.Contains("delete", effective.Actions);
        var context = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "role-test")], "test")) };
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new EndpointModuleAttribute("Example")), "synthetic-role-owner"));
        context.Request.Headers["X-NewP-Data-Declaration"] = effective.Id;
        context.Request.Headers["X-NewP-Feature"] = effective.FeaturePositions[0];
        var reads = new Reads(); var decisions = new Decisions();
        return (new(plan, new HttpContextAccessor { HttpContext = context }, reads, decisions, resolvers: resolver is null ? [] : [resolver]),
            new(contract.Key, contract.Provider, "submit"), reads, decisions);
    }

    // 完整消费仅用于测试；生产owner可在每一yield后保留原业务错误优先级。
    private static async Task<IReadOnlyList<DataExecutionTargetDecision>> CollectAsync(IDataExecutionAccess access, DataExecutionBindingReference binding, IReadOnlyList<DataExecutionTargetInput> targets)
    {
        var result = new List<DataExecutionTargetDecision>();
        await foreach (var item in access.EvaluateObjectsAsync(binding, "targets", targets)) result.Add(item);
        return result;
    }

    private sealed class Reads : IDataScopeObjectAccess
    {
        internal List<(string Resource, string Id)> Calls { get; } = [];
        public Task<bool> IsReadableAsync(string resourceKey, string objectId, CancellationToken token = default)
        { Calls.Add((resourceKey, objectId)); return Task.FromResult(true); }
    }

    private sealed class Decisions : IDataScopeObjectDecisionProvider
    {
        internal List<(string Resource, string Id, string Action)> Calls { get; } = [];
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken token = default)
        { Calls.Add((resourceKey, objectId, actionKey)); return Task.FromResult(resourceKey != "example.record" || actionKey != "edit"); }
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken token = default) => throw new NotSupportedException();
    }

    private sealed class Resolver : IDataExecutionTargetResolver
    {
        public string Key => "example.test-position";
        public string ModuleId => "Example";
        internal int Calls { get; private set; }
        public Task<DataExecutionResolvedTarget?> ResolveAsync(DataExecutionTargetInput target, CancellationToken token)
        { Calls++; return Task.FromResult<DataExecutionResolvedTarget?>(new(target.ResourceKey, "resolved-target", target.Source)); }
    }
}

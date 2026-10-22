using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Workspace;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspacePermissionTargetSourceTests
{
    [Fact]
    public async Task PermissionTargetRead_RequiresItsRegisteredMemberAndConsumesTheCompleteResolverOnce()
    {
        var compilation = new CanonicalModulePlanCompiler().Compile("permission-source-318-correction",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compilation.IsSuccess, JsonSerializer.Serialize(compilation.Diagnostics));
        var context = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actual")], "test")) };
        var fullRead = new FullRead(); var unused = new UnusedReads();
        var access = new DataExecutionAccess(compilation.Plan!, new HttpContextAccessor { HttpContext = context }, unused, unused, resolvers: [fullRead]);
        var binding = new DataExecutionInternalBinding(WorkspacePermissionTargetReadResolver.Execution, "workspace.backend", typeof(WorkspaceContents), "ResolveTargetsAsync");
        var target = new DataExecutionTargetInput("part.part", "target-eid", new LinkObjectReference("Part", "target-eid"));
        Assert.Equal(DataExecutionDecision.Allowed, (await access.EvaluateInternalObjectAsync(binding, "target", target)).Decision);
        foreach (var invalid in new[] { binding with { Caller = typeof(WorkspacePermissionTargetSourceTests) },
            binding with { Member = nameof(WorkspaceContents.ReadPropertiesAsync) }, binding with { Execution = "workspace.clipboard.copy" },
            binding with { Provider = "other.backend" } })
            await Assert.ThrowsAsync<ActionDataAccessException>(() => access.EvaluateInternalObjectAsync(invalid, "target", target));
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.EvaluateInternalObjectAsync(binding, "unknown-role", target));
        Assert.Equal(1, fullRead.Calls);
    }

    // 只证明正式来源/角色和resolver分派；实际双read的404/403由真实SQL消费者承担。
    private sealed class FullRead : IDataExecutionTargetResolver
    {
        public string Key => "workspace.permission.target.fullRead"; public string ModuleId => "Workspace";
        internal int Calls;
        public Task<DataExecutionResolvedTarget?> ResolveAsync(DataExecutionTargetInput target, CancellationToken token)
        { Calls++; return Task.FromResult<DataExecutionResolvedTarget?>(new(target.ResourceKey, target.ObjectId!, target.Source)); }
    }
    private sealed class UnusedReads : IDataScopeObjectAccess, IDataScopeObjectDecisionProvider
    {
        public Task<bool> IsReadableAsync(string resourceKey, string objectId, CancellationToken token = default) => throw new InvalidOperationException("Complete resolver owns the original reads.");
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken token = default) => throw new InvalidOperationException("No extra decision is permitted.");
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken token = default) => throw new NotSupportedException();
    }
}
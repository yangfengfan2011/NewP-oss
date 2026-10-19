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

public sealed class WorkspaceFolderEditMemberTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FolderEditInternalRole_AcceptsOnlyItsActualPublicOrInternalMember(bool preparing)
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules"));
        var compiled = new CanonicalModulePlanCompiler().Compile("folder-edit-member-314", candidates);
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext
        { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "member-test")], "test")) } };
        var operations = new CountedOperations();
        var resolver = new CountedEndpoint();
        var access = new DataExecutionAccess(compiled.Plan!, http, operations, operations, resolvers: [resolver]);
        var binding = WorkspaceFolderExecutions.EditTargetCaller(preparing);
        var role = WorkspaceFolderExecutions.EditTargetRole(preparing);
        var target = new DataExecutionTargetInput("workspace.folder", "real-folder", new LinkObjectReference("WorkspaceFolder", "real-folder"));
        Assert.Equal(DataExecutionDecision.Allowed, (await access.EvaluateInternalObjectAsync(binding, role, target)).Decision);
        Assert.Equal(1, resolver.Reads); Assert.Equal(1, operations.Actions);
        foreach (var invalid in new[] { binding with { Caller = typeof(WorkspaceFolderEditMemberTests) }, binding with { Member = "NotARealMember" },
            binding with { Execution = WorkspaceFolderExecutions.Create }, binding with { Provider = "example.backend" } })
            await Assert.ThrowsAsync<ActionDataAccessException>(() => access.EvaluateInternalObjectAsync(invalid, role, target));
        http.HttpContext = null;
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.EvaluateInternalObjectAsync(binding, role, target));
        Assert.Equal(1, resolver.Reads); Assert.Equal(1, operations.Actions);
    }

    // 这里只核真实合同/成员与调用计数，不作为目标读取或edit权限的允许证明。
    private sealed class CountedOperations : IDataScopeObjectAccess, IDataScopeObjectDecisionProvider
    {
        internal int Actions;
        public Task<bool> IsReadableAsync(string resource, string id, CancellationToken token = default) =>
            throw new InvalidOperationException("The formal endpoint resolver must perform this read.");
        public Task<bool> IsAllowedAsync(string resource, string id, string action, CancellationToken token = default)
        { Assert.Equal("workspace.folder", resource); Assert.Equal("real-folder", id); Assert.Equal("edit", action); Actions++; return Task.FromResult(true); }
        public Task<bool> IsCandidateAllowedAsync(string resource, IReadOnlyDictionary<string, object?> values, string action, CancellationToken token = default) =>
            throw new InvalidOperationException("Existing Folder edit cannot become a candidate check.");
    }

    private sealed class CountedEndpoint : IDataExecutionTargetResolver
    {
        internal int Reads;
        public string ModuleId => "Workspace";
        public string Key => "workspace.clipboard.endpoint";
        public Task<DataExecutionResolvedTarget?> ResolveAsync(DataExecutionTargetInput target, CancellationToken token)
        { Reads++; return Task.FromResult<DataExecutionResolvedTarget?>(new(target.ResourceKey, target.ObjectId!, target.Source)); }
    }
}

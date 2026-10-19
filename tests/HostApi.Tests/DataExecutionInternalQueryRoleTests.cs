using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Workspace;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataExecutionQueryRoleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InternalQueryRole_UsesOneOriginalPlanWithItsExactPublicOrInternalMember(bool internalMember)
    {
        var (access, binding, queries) = InternalQuery(internalMember: internalMember);
        Assert.Same(queries.Plan, await access.GetInternalQueryReadPlanAsync(binding, "workspace.folder"));
        Assert.Equal(1, queries.Calls);
        Assert.Equal(0, queries.TargetCalls);
    }

    [Theory]
    [InlineData("execution")]
    [InlineData("provider")]
    [InlineData("caller")]
    [InlineData("member")]
    [InlineData("resource")]
    [InlineData("shape")]
    [InlineData("stage")]
    [InlineData("minimum")]
    [InlineData("maximum")]
    [InlineData("action")]
    [InlineData("extra-role")]
    public async Task InternalQueryRole_RejectsMismatchesWithoutAnyReadPlan(string mismatch)
    {
        var (access, binding, queries) = InternalQuery(mismatch);
        var resource = "workspace.folder";
        switch (mismatch)
        {
            case "execution": binding = binding with { Execution = "workspace.clipboard.copy" }; break;
            case "provider": binding = binding with { Provider = "example.backend" }; break;
            case "caller": binding = binding with { Caller = typeof(DataExecutionQueryRoleTests) }; break;
            case "member": binding = binding with { Member = "MissingMember" }; break;
            case "resource": resource = "identity.account"; break;
        }
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetInternalQueryReadPlanAsync(binding, resource));
        Assert.Equal(0, queries.Calls);
        Assert.Equal(0, queries.TargetCalls);
    }

    // 合成内部来源只测共享入口边界；动作与资源仍由真实基础候选装配，不视为Workspace业务证据。
    private static (IDataExecutionAccess Access, DataExecutionInternalReadBinding Binding, Queries Queries) InternalQuery(
        string? mismatch = null, bool internalMember = false)
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
        var index = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Workspace");
        var binding = internalMember
            ? new DataExecutionInternalReadBinding("workspace.test.query314", "workspace.backend", typeof(LinkEndpointReadScopes), "PrepareAsync")
            : new DataExecutionInternalReadBinding("workspace.test.query314", "workspace.backend", typeof(WorkspaceFolderScope), nameof(WorkspaceFolderScope.BuildAsync));
        var requirement = new DataExecutionRequirement("query", "read", "query", ["workspace.folder"], [["read"]], 0, null);
        requirement = mismatch switch
        {
            "shape" => requirement with { Shape = "objects" },
            "stage" => requirement with { Stage = "prepare" },
            "minimum" => requirement with { MinimumCount = 1 },
            "maximum" => requirement with { MaximumCount = 10 },
            "action" => requirement with { AnyOf = [["read", "edit"]] },
            _ => requirement
        };
        var contract = new DataExecutionContract(binding.Execution, "Workspace", binding.Provider, "internal", [],
            mismatch == "extra-role" ? [requirement, requirement with { Role = "other" }] : [requirement])
        { FixedBindings = [new("internal", binding.Execution, Caller: binding.CallerIdentity)] };
        candidates[index] = candidates[index] with { DataExecutions = [.. candidates[index].DataExecutions, contract] };
        var compilation = new CanonicalModulePlanCompiler().Compile("internal-query-314", candidates);
        Assert.True(compilation.IsSuccess, JsonSerializer.Serialize(compilation.Diagnostics));
        var queries = new Queries();
        return (new DataExecutionAccess(compilation.Plan!, new HttpContextAccessor(), queries, queries,
            queryReads: queries, targetReads: queries), binding, queries);
    }
}

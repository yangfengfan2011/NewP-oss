using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataExecutionQueryRoleTests
{
    [Fact]
    public async Task ApiObjectRole_UsesTheActualTemplateAndOnePlanWithoutAnObjectPrecheck()
    {
        var (access, context, _, queries) = ApiObject();
        Assert.Same(queries.Plan, await access.GetApiObjectReadPlanAsync(context, "workspace.folder", "root-eid"));
        Assert.Equal(1, queries.Calls);
        Assert.Equal(0, queries.TargetCalls);
    }

    [Theory]
    [InlineData("empty-target")]
    [InlineData("route")]
    [InlineData("method")]
    [InlineData("provider")]
    [InlineData("identity")]
    [InlineData("resource")]
    [InlineData("view-header")]
    [InlineData("button-header")]
    [InlineData("blank-button-header")]
    [InlineData("blank-view-header")]
    [InlineData("mixed-headers")]
    [InlineData("role")]
    [InlineData("binding-stage")]
    [InlineData("query-shape")]
    [InlineData("action")]
    [InlineData("extra-role")]
    public async Task ApiObjectRole_RejectsForeignSourcesOrShapesBeforeAnyPlan(string mismatch)
    {
        var (access, context, http, queries) = ApiObject(mismatch);
        var resource = "workspace.folder";
        var target = "root-eid";
        switch (mismatch)
        {
            case "empty-target": target = " "; break;
            case "method": http.Request.Method = "POST"; break;
            case "provider": context = context with { Binding = context.Binding with { Provider = "other.backend" } }; break;
            case "identity": context = context with { Identity = "foreign-api" }; break;
            case "resource": resource = "part.part"; break;
            case "view-header": http.Request.Headers["X-NewP-Data-Declaration"] = "foreign-entry"; break;
            case "button-header": http.Request.Headers["X-NewP-Button"] = "foreign-button"; break;
            case "blank-button-header": http.Request.Headers["X-NewP-Button"] = ""; break;
            case "blank-view-header": http.Request.Headers["X-NewP-Data-Declaration"] = ""; break;
            case "mixed-headers":
                http.Request.Headers["X-NewP-Button"] = "foreign-button";
                http.Request.Headers["X-NewP-Data-Declaration"] = "foreign-entry"; break;
            case "role": context = context with { Role = "query" }; break;
            case "binding-stage": context = context with { Binding = context.Binding with { Stage = "submit" } }; break;
        }
        await Assert.ThrowsAsync<ActionDataAccessException>(() => access.GetApiObjectReadPlanAsync(context, resource, target));
        Assert.Equal(0, queries.Calls);
        Assert.Equal(0, queries.TargetCalls);
    }

    // 只改正式候选的局部API来源/角色形状以控制执行边界；假计划仅计调用，不证明真实根准入或权限。
    private static (IDataExecutionAccess Access, DataExecutionApiObjectContext Context, Microsoft.AspNetCore.Http.DefaultHttpContext Http, Queries Queries)
        ApiObject(string? mismatch = null)
    {
        var result = ApiQuery(template: mismatch == "route" ? "api/foreign" : "api/workspace/spaces/{spaceId}/root", configure: contract =>
        {
            var role = contract.Requirements[0] with { Role = "target", Shape = "object", MinimumCount = 1, MaximumCount = 1 };
            role = mismatch switch
            {
                "query-shape" => role with { Shape = "query", MinimumCount = 0, MaximumCount = null },
                "action" => role with { AnyOf = [["read", "edit"]] },
                _ => role
            };
            return contract with
            {
                Requirements = mismatch == "extra-role" ? [role, role with { Role = "other" }] : [role],
                FixedBindings = contract.FixedBindings.Select(source => source with { Route = "/api/workspace/spaces/{spaceId}/root" }).ToArray()
            };
        });
        return (result.Access, new(result.Context.Binding, result.Context.Identity, "target"), result.Http, result.Queries);
    }
}

using System.Net;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class PartDocumentReadExecutionSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "实际Part标准读取只接受自身list/read声明；普通主体范围与撤权由真实SQL裁剪")]
    public Task PartOwnedReads_RejectMissingAndBorrowedDeclarations_AndRecheckCurrentScope() => VerifyAsync("Part", "part.part");

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "实际Document标准读取只接受自身list/read声明；普通主体范围与撤权由真实SQL裁剪")]
    public Task DocumentOwnedReads_RejectMissingAndBorrowedDeclarations_AndRecheckCurrentScope() => VerifyAsync("Document", "document.document");

    private static async Task VerifyAsync(string module, string resource)
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var descriptor = fixture.Services.GetRequiredService<IStandardCrudResourceRegistry>().FindByResourceKey(resource)!;
        var route = "/" + descriptor.Route;
        var entry = Assert.Single(plan.FeatureCatalog, point => point.ModuleId == module && point.BehaviorKey == resource + ".read");
        var create = Assert.Single(plan.FeatureCatalog, point => point.ModuleId == module && point.BehaviorKey == resource + ".create");
        var manage = Assert.Single(plan.FeatureCatalog, point => point.ModuleId == module && point.Key == "authorize" && point.ActionScope == "list.rowActions");
        var visible = await FeatureHttpTestRequests.CreateAsync(admin, create, route, new { ECODE = "READ-309-A", ENAME = "读取允许" });
        var hidden = await FeatureHttpTestRequests.CreateAsync(admin, create, route, new { ECODE = "READ-309-B", ENAME = "读取拒绝" });
        var visibleId = visible.GetProperty("fields").GetProperty("EID").GetString()!;
        var hiddenId = hidden.GetProperty("fields").GetProperty("EID").GetString()!;
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, module + "Reads309", "正式资源读取", module.ToLowerInvariant() + "-reads309", "读取普通主体", entry);
        using var ordinary = actor.Client;
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, route + "/" + hiddenId + "/permissions",
            new DataScopeObjectDto(resource, hiddenId, [new("hidden-read309", "all", null, 1, new Dictionary<string, string> { ["read"] = "deny" })])))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        var list = OwnedResourceReadHttpRequests.Declaration(fixture, entry, resource, "list");
        var read = OwnedResourceReadHttpRequests.Declaration(fixture, entry, resource, "read");
        var borrowed = Assert.Single(plan.ActionDataAccessCatalog, item => item.ModuleId == module && item.Execution == module.ToLowerInvariant() + ".history.list");
        foreach (var declaration in new ActionDataAccessRequirement?[] { null, read, borrowed })
        {
            using var denied = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, route, dataAccess: declaration);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        using (var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, route + "/" + visibleId, dataAccess: list))
            Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
        var page = await OwnedResourceReadHttpRequests.ReadAsync(fixture, ordinary, entry, resource, "list", route + "?fields=EID,ECODE&keyword=READ-309&pageSize=1");
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Equal(visibleId, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("EID").GetString());
        var detail = await OwnedResourceReadHttpRequests.ReadAsync(fixture, ordinary, entry, resource, "read", route + "/" + visibleId);
        Assert.Equal(visibleId, detail.GetProperty("fields").GetProperty("EID").GetString());
        using (var denied = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, route + "/" + hiddenId, dataAccess: read))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using (var revoked = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, route + "/" + visibleId + "/permissions",
            new DataScopeObjectDto(resource, visibleId, [new("visible-read309", "all", null, 1, new Dictionary<string, string> { ["read"] = "deny" })])))
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using (var stale = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, route + "/" + visibleId, dataAccess: read))
            Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        var empty = await OwnedResourceReadHttpRequests.ReadAsync(fixture, ordinary, entry, resource, "list", route + "?keyword=READ-309");
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
    }
}

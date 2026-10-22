using System.Net;
using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实副本的动态创建人按新EID可信事实求值，不继承源创建人或自动复制ACL")]
    public async Task ObjectCopy_DynamicCreatorUsesTheNewObjectsTrustedCreator()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "动态创建人来源");
        var destination = await Folder(fixture, admin, root, "动态创建人目标");
        var creator = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "DynamicCopyCreator", "源创建者",
            "dynamic-copy-creator", "源创建人", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.createPart"));
        using var creatorClient = creator.Client;
        var copier = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "DynamicCopyCopier", "副本创建者",
            "dynamic-copy-copier", "副本创建人", Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.copy"),
            Point(fixture, "workspace.folder.pasteObjectCopy"), Point(fixture, "workspace.folder.paste"));
        using var copierClient = copier.Client;
        await Command(fixture, creatorClient, "workspace.folder.createPart", "/api/workspace/content/parts",
            new { position = source, values = new { ECODE = "DYNAMIC-COPY-SOURCE", ENAME = "动态源" } });
        var original = Assert.Single(await Rows(fixture, "Part"));
        var originalId = (string)original["EID"]!;
        var originalLink = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["CEID"], originalId));
        var prepared = await Command(fixture, copierClient, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy/prepare",
            new { mode = "copy", sourcePosition = source, linkIds = new[] { originalLink["LID"] }, targetPosition = destination });
        var copiedResult = await Command(fixture, copierClient, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy",
            new { operationId = prepared.GetProperty("operationId").GetString(), values = new { ECODE = "DYNAMIC-COPY-TARGET", ENAME = "动态副本" } });
        Assert.True(copiedResult.GetProperty("succeeded").GetBoolean());
        var copied = Assert.Single(await Rows(fixture, "Part"), row => Equals(row["ECODE"], "DYNAMIC-COPY-TARGET"));
        var copiedId = (string)copied["EID"]!;
        Assert.NotEqual(originalId, copiedId);
        Assert.Equal(creator.AccountId, original["CreatedBy"]);
        Assert.Equal(copier.AccountId, copied["CreatedBy"]);
        using (var scope = fixture.Services.CreateScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>()
                .Set<Dictionary<string, object?>>("DataScopeObjectSubject")
                .CountAsync(row => EF.Property<string>(row, "ResourceKey") == "part.part" && EF.Property<string>(row, "ObjectId") == copiedId));
        var manage = fixture.FeatureCatalog.Single(point => point.ModuleId == "Part" && point.Key == "authorize" && point.ActionScope == "list.rowActions");
        // 分别显式保存相同选择器和意见；这里只验证动态求值，不新增自动复制ACL的产品能力。
        foreach (var id in new[] { originalId, copiedId })
        {
            var input = new DataScopeObjectDto("part.part", id,
                [new("DynamicCopyAll" + id, "all", null, 0, new Dictionary<string, string> { ["read"] = "deny", ["edit"] = "deny" }),
                 new("DynamicCopyCreator" + id, "creator", null, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow" })]);
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, manage, HttpMethod.Put, "/api/part/parts/" + id + "/permissions", input);
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }
        await AssertTargetAsync(creator.AccountId, originalId, true);
        await AssertTargetAsync(creator.AccountId, copiedId, false);
        await AssertTargetAsync(copier.AccountId, originalId, false);
        await AssertTargetAsync(copier.AccountId, copiedId, true);

        async Task AssertTargetAsync(string accountId, string id, bool expected)
        {
            using var scope = fixture.Services.CreateScope();
            var provider = scope.ServiceProvider;
            var accessor = provider.GetRequiredService<IHttpContextAccessor>(); var previous = accessor.HttpContext;
            accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, accountId)], "test-trusted-account")) };
            try
            {
                var decisions = provider.GetRequiredService<IDataScopeObjectDecisionProvider>();
                Assert.Equal(expected, await decisions.IsAllowedAsync("part.part", id, "read"));
                Assert.Equal(expected, await decisions.IsAllowedAsync("part.part", id, "edit"));
                var plan = await provider.GetRequiredService<IDataScopeReadPlanProvider>().GetReadPlanAsync("part.part");
                var rows = provider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("Part").AsNoTracking()
                    .Where(row => EF.Property<string>(row, "EID") == id);
                if (plan.Predicate is not null) rows = rows.Where(plan.Predicate);
                Assert.Equal(expected ? 1 : 0, await rows.CountAsync());
            }
            finally { accessor.HttpContext = previous; }
        }
    }
}

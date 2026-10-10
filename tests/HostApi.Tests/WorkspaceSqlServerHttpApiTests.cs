using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Kernel.ModulePlanning;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "工作空间专用根端点与正式对象读取裁决取交集")]
    public async Task RootRead_ConsumesTheRegisteredResourceReadPlan()
    {
        var fault = new ReadFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = Assert.Single(fixture.FeatureCatalog, p => p.ModuleId == "Workspace" && p.Key == "workspace");
        using var initialized = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        var space = await initialized.Content.ReadFromJsonAsync<JsonElement>();
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "WorkspaceRead", "空间读取", "workspace-reader", "空间读取账号", entry);
        using var ordinary = actor.Client;
        var rule = new DataScopeRuleDto("WorkspaceDeniedRoot", "WorkspaceDeniedRoot", null, "拒绝公共根", true, "workspace.folder",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(space.GetProperty("rootFolderId").GetString())),
            [new("WorkspaceDeniedSubject", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        var save = Assert.Single(fixture.FeatureCatalog, p => p.BehaviorKey == "authorization.dataScope.save");
        using var saved = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/WorkspaceDeniedRoot", rule);
        Assert.True(saved.StatusCode == HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());
        var route = $"/api/workspace/spaces/{space.GetProperty("spaceId").GetString()}/root";
        using var refused = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, route);
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Empty((await FeatureHttpTestRequests.ReadAsync(ordinary, entry, "/api/workspace/spaces")).EnumerateArray());
        Assert.Equal(space.GetProperty("rootFolderId").GetString(), (await FeatureHttpTestRequests.ReadAsync(admin, entry, route)).GetProperty("id").GetString());
        var detail = await FeatureHttpTestRequests.ReadAsync(admin, entry, $"/api/workspace/spaces/roots/{space.GetProperty("rootFolderId").GetString()}");
        Assert.Equal(space.GetProperty("rootFolderId").GetString(), detail.GetProperty("fields").GetProperty("EID").GetString());
        Assert.True(detail.GetProperty("fields").TryGetProperty("CreatedAt_DisplayValue", out _));
        Assert.False(detail.GetProperty("fields").TryGetProperty("OriginSpaceId", out _));
        using var ordinaryDenied = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, $"/api/workspace/spaces/roots/{space.GetProperty("rootFolderId").GetString()}");
        Assert.Equal(HttpStatusCode.NotFound, ordinaryDenied.StatusCode);
        fault.Enabled = true;
        using var failedRead = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, route);
        Assert.Equal(HttpStatusCode.InternalServerError, failedRead.StatusCode);
        fault.Enabled = false;

        var personal = await EnsureAsync(ordinary, entry);
        var privateRule = rule with { Condition = new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(personal.GetProperty("rootFolderId").GetString())) };
        using var replaced = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/WorkspaceDeniedRoot", privateRule);
        Assert.Equal(HttpStatusCode.NoContent, replaced.StatusCode);
        var receipt = await EnsureAsync(ordinary, entry);
        Assert.True(receipt.GetProperty("initialized").GetBoolean());
        Assert.False(receipt.TryGetProperty("rootFolderId", out _));
        var visibleRoots = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, "/api/workspace/spaces");
        var visibleRoot = Assert.Single(visibleRoots.EnumerateArray());
        Assert.Equal("Public", visibleRoot.GetProperty("spaceKind").GetString());
        Assert.Equal(space.GetProperty("rootFolderId").GetString(), visibleRoot.GetProperty("rootFolderId").GetString());
        Assert.DoesNotContain(personal.GetProperty("rootFolderId").GetString()!, visibleRoots.GetRawText(), StringComparison.Ordinal);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "专用资源贡献验证真实owner、model、id及重复身份")]
    public async Task ResourceContributions_RejectInvalidIdentityBeforeReadExecution()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var scope = fixture.Services.CreateScope();
        var services = scope.ServiceProvider;
        Assert.Contains(services.GetRequiredService<DataScopeCatalog>().Resources, r => r.ResourceKey == "workspace.folder");
        var registration = new DataScopeResource("workspace.folder", "Workspace", "WorkspaceFolder", "EID");
        foreach (var invalid in new[]
        {
            new[] { registration, registration },
            new[] { registration with { ModelName = "IdentityAccount" } },
            new[] { registration with { ModuleId = "Missing" } },
            new[] { registration with { ModelName = "Missing" } },
            new[] { registration with { IdField = "ENAME" } }
        })
        {
            var catalog = new DataScopeCatalog(services.GetRequiredService<IStandardCrudResourceRegistry>(),
                services.GetRequiredService<CanonicalModulePlan>(), services.GetRequiredService<EfPlatformDbContext>(),
                services.GetRequiredService<DataScopeIdentities>(), resourceContributions: invalid);
            Assert.Throws<InvalidOperationException>(() => catalog.Resources);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式工作空间 authoring、双根命令和普通主体准入")]
    public async Task Spaces_FormalCommandsAreIdempotentAndPrivateRootsCannotBeReadByOtherActors()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = Assert.Single(fixture.FeatureCatalog, p => p.ModuleId == "Workspace" && p.Key == "workspace");
        Assert.Empty((await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/workspace/spaces")).EnumerateArray());
        fixture.Fault.Mode = "commit";
        using var failedCommit = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        Assert.Equal(HttpStatusCode.InternalServerError, failedCommit.StatusCode);
        fixture.Fault.Mode = "";
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.Equal(0, await db.Set<Dictionary<string, object?>>("WorkspaceSpace").CountAsync());
            Assert.Equal(0, await db.Set<Dictionary<string, object?>>("WorkspaceFolder").CountAsync());
        }
        using var response = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var publicSpace = await response.Content.ReadFromJsonAsync<JsonElement>();
        using var repeated = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        Assert.Equal(publicSpace.ToString(), (await repeated.Content.ReadFromJsonAsync<JsonElement>()).ToString());

        var first = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "WorkspaceA", "工作空间A", "workspace-a", "账号A", entry);
        var second = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "WorkspaceB", "工作空间B", "workspace-b", "账号B", entry);
        var denied = await HttpTestActors.CreateAuthenticatedActorAsync(fixture, admin, "WorkspaceDenied", "未授入口", "workspace-denied", "未授入口账号");
        using var a = first.Client;
        using var b = second.Client;
        using var unauthorized = denied.Client;
        using var rejectEntry = await FeatureHttpTestRequests.SendAsync(unauthorized, entry, HttpMethod.Get, "/api/workspace/spaces");
        Assert.Equal(HttpStatusCode.Forbidden, rejectEntry.StatusCode);
        using var rejectCommand = await FeatureHttpTestRequests.SendAsync(unauthorized, entry, HttpMethod.Post, "/api/workspace/spaces/personal/ensure");
        Assert.Equal(HttpStatusCode.Forbidden, rejectCommand.StatusCode);
        Assert.Single((await FeatureHttpTestRequests.ReadAsync(a, entry, "/api/workspace/spaces")).EnumerateArray());
        using var forged = await FeatureHttpTestRequests.SendAsync(a, entry, HttpMethod.Post, "/api/workspace/spaces/personal/ensure", new { SubjectId = second.AccountId });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        var problem = await forged.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Workspace.Personal.SubjectInputForbidden", problem.GetProperty("code").GetString());
        Assert.Equal("Workspace.SubjectInputForbidden", problem.GetProperty("reasonKey").GetString());
        using var notInitializer = await FeatureHttpTestRequests.SendAsync(a, entry, HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        Assert.Equal(HttpStatusCode.Forbidden, notInitializer.StatusCode);
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => EnsureAsync(a, entry)));
        Assert.Single(concurrent.Select(space => space.GetProperty("spaceId").GetString()).Distinct());
        var personalA = concurrent[0];
        var personalB = await EnsureAsync(b, entry);
        Assert.NotEqual(personalA.GetProperty("spaceId").GetString(), personalB.GetProperty("spaceId").GetString());
        var privateRoute = $"/api/workspace/spaces/{personalA.GetProperty("spaceId").GetString()}/root";
        Assert.Equal(personalA.GetProperty("rootFolderId").GetString(), (await FeatureHttpTestRequests.ReadAsync(a, entry, privateRoute)).GetProperty("id").GetString());
        foreach (var other in new[] { b, admin })
        {
            using var refused = await FeatureHttpTestRequests.SendAsync(other, entry, HttpMethod.Get, privateRoute);
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            using var direct = await FeatureHttpTestRequests.SendAsync(other, entry, HttpMethod.Get,
                $"/api/workspace/spaces/roots/{personalA.GetProperty("rootFolderId").GetString()}");
            Assert.Equal(HttpStatusCode.NotFound, direct.StatusCode);
        }
        var roots = await FeatureHttpTestRequests.ReadAsync(a, entry, "/api/workspace/spaces");
        Assert.Equal(new[] { "Public", "Personal" }, roots.EnumerateArray().Select(space => space.GetProperty("spaceKind").GetString()).ToArray());
        Assert.DoesNotContain("subjectId", roots.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OriginSpaceId", (await FeatureHttpTestRequests.ReadAsync(a, entry, privateRoute)).GetRawText(), StringComparison.OrdinalIgnoreCase);
        await fixture.RestartWithActionsAsync(new Dictionary<string, bool>());
        using var relogged = fixture.CreateClient();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(relogged);
        using var login = await relogged.PostAsJsonAsync("/api/auth/login", new { ECODE = "workspace-a", Password = "Audit-test-secure-938!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(relogged);
        Assert.Equal(personalA.ToString(), (await EnsureAsync(relogged, entry)).ToString());
        Assert.Equal(2, (await FeatureHttpTestRequests.ReadAsync(relogged, entry, "/api/workspace/spaces")).GetArrayLength());

        using var persisted = fixture.Services.CreateScope();
        var stored = persisted.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.Equal(3, await stored.Set<Dictionary<string, object?>>("WorkspaceSpace").CountAsync());
        Assert.Equal(3, await stored.Set<Dictionary<string, object?>>("WorkspaceFolder").CountAsync());
        var own = await stored.Set<Dictionary<string, object?>>("WorkspaceSpace").AsNoTracking()
            .SingleAsync(r => EF.Property<string>(r, "EID") == personalA.GetProperty("spaceId").GetString());
        Assert.Equal("IdentityAccount", own["SubjectModelKey"]);
        Assert.Equal(first.AccountId, own["SubjectId"]);
    }

    private static async Task<JsonElement> EnsureAsync(HttpClient client, ICP.Platform.Kernel.ModulePlanning.FeaturePoint entry)
    {
        using var result = await FeatureHttpTestRequests.SendAsync(client, entry, HttpMethod.Post, "/api/workspace/spaces/personal/ensure");
        Assert.True(result.StatusCode == HttpStatusCode.OK, await result.Content.ReadAsStringAsync());
        return await result.Content.ReadFromJsonAsync<JsonElement>();
    }

    private sealed class ReadFault : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && (command.CommandText.Contains("[WorkspaceFolder]", StringComparison.Ordinal)
                || command.CommandText.Contains("[DataScopeRule]", StringComparison.Ordinal))) throw new InvalidOperationException("injected workspace read failure");
            return ValueTask.FromResult(result);
        }
    }
}

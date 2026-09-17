using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Transactions;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.FeatureHttpTestRequests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class OrganizationAdmissionSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "自动局部恢复保留Cookie但不复活子树停用已撤销会话")]
    public Task AutomaticRecovery_PreservesValidCookieAndNeverRevivesSubtreeSessions() => VerifyRecoveryAsync(false);

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "未知提交自动局部恢复不重放业务写入且不复活旧会话")]
    public Task AutomaticRecovery_UnknownCommitDoesNotReplayOrReviveSessions() => VerifyRecoveryAsync(true);

    private async Task VerifyRecoveryAsync(bool unknownCommit)
    {
        var acknowledgement = new RecoveryCommitFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddSingleton<OrganizationCache, RecoveryCache>();
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(acknowledgement));
        });
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var prepared = await PrepareAsync(admin, http);
        var child = await CreateDepartmentAsync(admin, http, prepared.Company, "RECOVERY-CHILD", prepared.Department);
        await UpdateAsync(admin, http, prepared.Person, new { CompanyId = prepared.Company, DepartmentId = child });
        using var member = await fixture.LoginAsync("admitted", prepared.Password);
        using var second = fixture.CreateClient();
        await LoginAsync(second, "Audit-test-secure-938!");
        var cache = (RecoveryCache)fixture.Services.GetRequiredService<OrganizationCache>();
        var stable = await cache.ReadAsync();

        cache.Arm(!unknownCommit);
        acknowledgement.Enabled = unknownCommit;
        using var write = await SendAsync(admin, http.Organization("companies", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/companies/{prepared.Company}", new { ENAME = "已保存等待恢复" });
        await AssertFailureAsync(write);
        await cache.ReloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using (var unavailable = await member.GetAsync("/api/auth/current-user"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.False(unavailable.Headers.Contains("Set-Cookie"));
        }
        using (var independent = fixture.CreateClient())
        {
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(independent);
            using var login = await independent.PostAsJsonAsync("/api/auth/login", new { ECODE = "admin", Password = "Audit-test-secure-938!" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }
        foreach (var resource in new[] { "companies", "departments", "positions", "persons" })
        {
            using var list = await SendAsync(admin, http.Organization(resource), HttpMethod.Get, $"/api/organization/{resource}");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, list.StatusCode);
        }
        using (var detail = await SendAsync(admin, http.Organization("companies"), HttpMethod.Get, $"/api/organization/companies/{prepared.Company}"))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, detail.StatusCode);
        using (var tree = await SendAsync(admin, fixture.FeatureCatalog.Single(point => point.BehaviorKey == "tree:organization.structure"),
            HttpMethod.Get, "/api/trees/organization.structure/roots?scope=global"))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, tree.StatusCode);
        using (var lookup = await SendAsync(admin, http.Organization("persons", "create", "list.pageActions"), HttpMethod.Get, "/api/organization/person-lookups/companies?pageSize=10"))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, lookup.StatusCode);
        using (var candidates = await SendAsync(admin, http.Organization("persons", "linkUser", "detail.custom"), HttpMethod.Get,
            $"/api/organization/persons/{prepared.Person}/user-binding/candidates"))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, candidates.StatusCode);
        using (var identity = await SendAsync(admin, http.Identity(), HttpMethod.Get, "/api/identity/accounts"))
            Assert.Equal(HttpStatusCode.OK, identity.StatusCode);
        cache.Release.TrySetResult();
        await cache.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/auth/current-user")).StatusCode);
        Assert.Equal("已保存等待恢复", (await cache.ReadAsync()).Companies[prepared.Company]["ENAME"]);
        Assert.Same(stable.Persons, (await cache.ReadAsync()).Persons);
        Assert.Equal(1, cache.FullLoads);

        // 实际子树停用与会话撤销已经共同提交，即使发布失败，也不得在恢复/重新启用后复活。
        cache.Arm(!unknownCommit);
        acknowledgement.Enabled = unknownCommit;
        using var disable = await SendAsync(admin, http.Organization("departments", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/departments/{prepared.Department}", new { IsEnabled = false });
        await AssertFailureAsync(disable);
        await cache.ReloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cache.Release.TrySetResult();
        await cache.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await SetOrganizationStateAsync(admin, http, "departments", prepared.Department, true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/auth/current-user")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/current-user")).StatusCode);
        using var fresh = fixture.CreateClient();
        await LoginAsync(fresh, "Audit-test-secure-938!");
        Assert.Equal(1, cache.FullLoads);
        Assert.Equal(2, cache.RangeLoads);
        Assert.Equal(unknownCommit ? 2 : 0, acknowledgement.Failures);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var saved = await db.Set<Dictionary<string, object?>>("Organization/Company").AsNoTracking().SingleAsync();
        Assert.Equal("已保存等待恢复", saved["ENAME"]);

        async Task AssertFailureAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(unknownCommit ? "Persistence.CommitOutcomeUnknown" : "Persistence.CommittedCacheUnavailable", problem.GetProperty("code").GetString());
            Assert.Contains(unknownCommit ? "未能确认" : "已保存", problem.GetProperty("message").GetString());
        }
    }

    private sealed class RecoveryCommitFault : DbTransactionInterceptor
    {
        public bool Enabled;
        public int Failures;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!Enabled) return Task.CompletedTask;
            Enabled = false; Failures++;
            throw new InvalidOperationException("committed acknowledgement probe");
        }
    }

    private sealed class RecoveryCache(IServiceScopeFactory scopes) : OrganizationCache(scopes)
    {
        private bool _failPublication;
        private bool _recovering;
        public int FullLoads;
        public int RangeLoads;
        public TaskCompletionSource ReloadEntered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Recovered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm(bool failPublication)
        {
            _failPublication = failPublication;
            ReloadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Recovered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        protected override async ValueTask<OrganizationSnapshot> LoadAsync(CancellationToken token)
        { FullLoads++; return await base.LoadAsync(token); }
        protected override async ValueTask<IReadOnlyList<PlatformRecordChange>> ReloadAsync(IReadOnlyList<PlatformRecordChange> changes, CancellationToken token)
        {
            RangeLoads++;
            ReloadEntered.TrySetResult();
            await Release.Task.WaitAsync(token);
            _recovering = true;
            return await base.ReloadAsync(changes, token);
        }
        protected override void Publish(OrganizationSnapshot snapshot)
        {
            if (_failPublication) { _failPublication = false; throw new InvalidOperationException("publication probe"); }
            base.Publish(snapshot);
            if (_recovering) { _recovering = false; Recovered.TrySetResult(); }
        }
    }
}

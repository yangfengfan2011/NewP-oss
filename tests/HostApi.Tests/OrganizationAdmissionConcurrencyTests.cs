using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Authentication.Application;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Transactions;
using ICP.Platform.Persistence.Transactions;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.FeatureHttpTestRequests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class OrganizationAdmissionSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "登录、首次改密、后续轮换先提交后均被组织中断撤销")]
    public async Task SessionCreation_FirstToCommitIsIncludedInInterruptionRevocation()
    {
        foreach (var operation in new[] { "login", "first-password", "rotation" })
            await AssertCreationOrderAsync(operation, true);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "组织中断先提交时三种创建入口不能使用旧准入结果")]
    public async Task SessionCreation_AfterInterruptionCannotUseEarlierCredentialsOrAdmission()
    {
        foreach (var operation in new[] { "login", "first-password", "rotation" })
            await AssertCreationOrderAsync(operation, false);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "资格恢复也不能让已撤销的首次改密或普通会话轮换出新会话")]
    public async Task Rotation_AfterInterruptionAndRestore_RejectsRevokedSourceSession()
    {
        foreach (var operation in new[] { "first-password", "rotation" })
            await AssertCreationOrderAsync(operation, false, true);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "请求认证后等待期间过期的来源会话不能执行首次或普通改密")]
    public async Task Rotation_WhenSourceExpiresBeforeTransaction_CannotCreateReplacement()
    {
        foreach (var firstPassword in new[] { true, false })
        {
            var control = new Control();
            var clock = new MutableClock();
            await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            { ConfigureControl(services, control); services.AddSingleton<TimeProvider>(clock); });
            using var admin = await fixture.LoginAdministratorAsync();
            var prepared = await PrepareAsync(admin, new FeatureHttpTestRequests(fixture));
            using var client = firstPassword ? fixture.CreateClient() : await fixture.LoginAsync("admitted", prepared.Password);
            if (firstPassword) await LoginAsync(client, prepared.Password);
            var current = await client.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            control.BeforeExecute = async path =>
            {
                if (path == "/api/identity/me/password-change") { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
            };
            var changing = client.PostAsJsonAsync("/api/identity/me/password-change", new
            { CurrentPassword = firstPassword ? prepared.Password : "Audit-test-secure-938!", NewPassword = "Must-not-persist-123!", ExpectedRevision = current.GetProperty("revision").GetInt64() });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            clock.Now = clock.Now.AddHours(9);
            release.TrySetResult();
            using var result = await changing;
            Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.Equal(firstPassword ? 1 : 2, await db.Set<Dictionary<string, object?>>("AuthenticationSession")
                .CountAsync(row => EF.Property<string>(row, "AccountEID") == prepared.Account));
            control.BeforeExecute = null;
            using var fresh = fixture.CreateClient();
            await LoginAsync(fresh, firstPassword ? prepared.Password : "Audit-test-secure-938!");
        }
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task AssertCreationOrderAsync(string operation, bool creationFirst, bool restoreBeforeCreation = false, bool ancestor = false)
    {
        var control = new Control();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => ConfigureControl(services, control));
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var prepared = await PrepareAsync(admin, http);
        if (ancestor)
        {
            var child = await CreateDepartmentAsync(admin, http, prepared.Company, "CONCURRENT-CHILD", prepared.Department);
            var leaf = await CreateDepartmentAsync(admin, http, prepared.Company, "CONCURRENT-LEAF", child);
            await UpdateAsync(admin, http, prepared.Person, new { DepartmentId = leaf });
        }
        using var client = operation == "rotation" ? await fixture.LoginAsync("admitted", prepared.Password) : fixture.CreateClient();
        if (operation == "first-password") await LoginAsync(client, prepared.Password);
        var password = operation == "rotation" ? "Audit-test-secure-938!" : prepared.Password;
        var revision = operation == "login" ? 0 : (await client.GetFromJsonAsync<JsonElement>("/api/auth/current-user")).GetProperty("revision").GetInt64();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var path = operation == "login" ? "/api/auth/login" : "/api/identity/me/password-change";
        control.BeforeExecute = async requestPath =>
        {
            if (requestPath.StartsWith("/api/organization/departments/", StringComparison.Ordinal)) mutationEntered.TrySetResult();
            if (!creationFirst && requestPath == path) { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        };
        if (creationFirst) control.AfterRegister = async () => { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20)); };
        var issuance = operation == "login"
            ? client.PostAsJsonAsync(path, new { ECODE = "admitted", Password = password })
            : client.PostAsJsonAsync(path, new { CurrentPassword = password, NewPassword = "Changed-secure-123!", ExpectedRevision = revision });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var interruption = http.SendButtonAsync(admin, http.Organization("departments", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/departments/{prepared.Department}", new { IsEnabled = false });
        await mutationEntered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        if (creationFirst) Assert.False(interruption.IsCompleted);
        else
        {
            using var completed = await interruption;
            Assert.True(completed.IsSuccessStatusCode, await completed.Content.ReadAsStringAsync());
        }
        if (restoreBeforeCreation)
        {
            using var restored = await http.SendButtonAsync(admin, http.Organization("departments", "edit", "list.rowActions"), HttpMethod.Put,
                $"/api/organization/departments/{prepared.Department}", new { IsEnabled = true });
            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        }
        release.TrySetResult();
        using var issued = await issuance;
        Assert.Equal(creationFirst ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, issued.StatusCode);
        if (creationFirst)
        {
            using var completed = await interruption;
            Assert.True(completed.IsSuccessStatusCode, await completed.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/current-user")).StatusCode);
        }
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.Equal(0, await db.Set<Dictionary<string, object?>>("AuthenticationSession")
            .CountAsync(row => EF.Property<string>(row, "AccountEID") == prepared.Account && EF.Property<DateTime?>(row, "RevokedAt") == null));
        control.BeforeExecute = null;
        control.AfterRegister = null;
        using var enabled = await http.SendButtonAsync(admin, http.Organization("departments", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/departments/{prepared.Department}", new { IsEnabled = true });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        using var fresh = fixture.CreateClient();
        await LoginAsync(fresh, creationFirst && operation != "login" ? "Changed-secure-123!" : password);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "资格中断的撤销失败和最终提交失败均回滚组织及会话")]
    public async Task Interruption_WhenRevocationOrCommitFails_RollsBackBothOwners()
    {
        var control = new Control();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => ConfigureControl(services, control));
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var prepared = await PrepareAsync(admin, http);
        using var client = await fixture.LoginAsync("admitted", prepared.Password);
        foreach (var stage in new[] { "revoke", "commit" })
        {
            control.FailAfterRevocation = stage == "revoke";
            fixture.Fault.Mode = stage == "commit" ? "commit" : "";
            using var failed = await http.SendButtonAsync(admin, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put,
                $"/api/organization/persons/{prepared.Person}", new { CompanyId = (string?)null, DepartmentId = (string?)null });
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            control.FailAfterRevocation = false;
            fixture.Fault.Mode = "";
            Assert.Equal(1, control.LastRevocationCount);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/current-user")).StatusCode);
            var current = (await fixture.Services.GetRequiredService<IOrganizationQuery>().ReadAsync()).FindByAccount(prepared.Account)!;
            Assert.Equal(prepared.Department, current.Department!["EID"]);
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var person = await db.Set<Dictionary<string, object?>>("Person").AsNoTracking().SingleAsync();
            Assert.Equal(prepared.Department, person["DepartmentId"]);
            Assert.Equal(1, await db.Set<Dictionary<string, object?>>("AuthenticationSession")
                .CountAsync(row => EF.Property<string>(row, "AccountEID") == prepared.Account && EF.Property<DateTime?>(row, "RevokedAt") == null));
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "请求在组织中断恢复间取得的旧会话事实不能复活")]
    public async Task Validation_InterruptedBetweenSessionAndAdmissionRead_RejectsRevokedSession()
    {
        var control = new Control();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => ConfigureControl(services, control));
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var prepared = await PrepareAsync(admin, http);
        using var client = await fixture.LoginAsync("admitted", prepared.Password);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        control.BeforeAdmission = async account =>
        {
            if (account.AccountEid == prepared.Account) { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        };
        var validating = client.GetAsync("/api/auth/current-user");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await UpdateAsync(admin, http, prepared.Person, new { CompanyId = (string?)null, DepartmentId = (string?)null });
        await UpdateAsync(admin, http, prepared.Person, new { CompanyId = prepared.Company, DepartmentId = prepared.Department });
        release.TrySetResult();
        using var result = await validating;
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }

    private static void ConfigureControl(IServiceCollection services, Control control)
    {
        services.AddScoped<IAuthenticationSessionRegistry>(sp => new ControlledRegistry(sp.GetRequiredService<AuthenticationSessionRegistry>(), control));
        services.AddScoped<IAuthenticationSessionBatchRevoker>(sp => new ControlledRevoker(sp.GetRequiredService<AuthenticationSessionRegistry>(), control));
        services.AddScoped<IIdentityAdmissionPolicy>(sp => new ControlledAdmission(new OrganizationAdmissionPolicy(sp.GetRequiredService<IOrganizationQuery>()), control));
        services.AddScoped<IPlatformTransactionExecutor>(sp => new ControlledTransaction(
            new EfPlatformTransactionExecutor(sp.GetRequiredService<EfPlatformDbContext>(), sp.GetServices<PlatformCommitRegistration>()),
            sp.GetRequiredService<IHttpContextAccessor>(), control));
    }

    private sealed class Control
    {
        public Func<string, Task>? BeforeExecute;
        public Func<Task>? AfterRegister;
        public Func<IdentityAccountSnapshot, Task>? BeforeAdmission;
        public bool FailAfterRevocation;
        public int LastRevocationCount;
    }

    private sealed class ControlledRegistry(IAuthenticationSessionRegistry inner, Control control) : IAuthenticationSessionRegistry
    {
        public ValueTask<bool> IsActiveAsync(string sessionEid, string accountEid, CancellationToken cancellationToken = default) => inner.IsActiveAsync(sessionEid, accountEid, cancellationToken);
        public async ValueTask<AuthenticationSessionIssuance> RegisterAsync(AuthenticationSessionRegistration registration, CancellationToken cancellationToken = default)
        { var result = await inner.RegisterAsync(registration, cancellationToken); if (control.AfterRegister is not null) await control.AfterRegister(); return result; }
        public ValueTask<AuthenticationSessionValidation> ValidateAsync(string credential, CancellationToken cancellationToken = default) => inner.ValidateAsync(credential, cancellationToken);
        public ValueTask<bool> RevokeCurrentAsync(AuthenticationSessionRevocation revocation, CancellationToken cancellationToken = default) => inner.RevokeCurrentAsync(revocation, cancellationToken);
        public ValueTask<int> RevokeAllAsync(AccountAuthenticationSessionsRevocation revocation, CancellationToken cancellationToken = default) => inner.RevokeAllAsync(revocation, cancellationToken);
    }

    private sealed class ControlledRevoker(IAuthenticationSessionBatchRevoker inner, Control control) : IAuthenticationSessionBatchRevoker
    {
        public async ValueTask<int> RevokeAccountsAsync(IReadOnlyCollection<string> ids, string reason, string actor, CancellationToken cancellationToken = default)
        { control.LastRevocationCount = await inner.RevokeAccountsAsync(ids, reason, actor, cancellationToken); if (control.FailAfterRevocation) throw new InvalidOperationException("injected after real session revocation"); return control.LastRevocationCount; }
    }

    private sealed class ControlledAdmission(IIdentityAdmissionPolicy inner, Control control) : IIdentityAdmissionPolicy
    {
        public IReadOnlyCollection<string> GetCoordinationModels(IdentityAccountSnapshot account) => inner.GetCoordinationModels(account);
        public async ValueTask<bool> IsAllowedAsync(IdentityAccountSnapshot account, CancellationToken cancellationToken = default)
        { if (control.BeforeAdmission is not null) await control.BeforeAdmission(account); return await inner.IsAllowedAsync(account, cancellationToken); }
    }

    private sealed class ControlledTransaction(IPlatformTransactionExecutor inner, IHttpContextAccessor http, Control control) : IPlatformTransactionExecutor
    {
        public bool IsActive => inner.IsActive;
        public bool HasParticipants(string modelKey) => inner.HasParticipants(modelKey);
        public void RegisterChange(PlatformRecordChange change) => inner.RegisterChange(change);
        public ValueTask FlushAsync(CancellationToken cancellationToken = default) => inner.FlushAsync(cancellationToken);
        public ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default) => ExecuteAsync([], action, cancellationToken);
        public async ValueTask<T> ExecuteAsync<T>(IReadOnlyCollection<string> models, Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
        { if (control.BeforeExecute is not null) await control.BeforeExecute(http.HttpContext?.Request.Path.Value ?? ""); return await inner.ExecuteAsync(models, action, cancellationToken); }
    }
}

using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Authorization.Http;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class IdentityHttpApiIntegrationTests
{
    [Fact]
    public async Task IdentityRoutes_EnforceAdminBoundaryAndRotateAuthenticatedSelfServiceCookie()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers(options => options.Filters.Add<AuthenticationSessionCookieResultFilter>())
            .AddApplicationPart(typeof(IdentityAccountsController).Assembly);
        builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, static _ => { });
        builder.Services.AddPlatformAuthorization();
        builder.Services.AddSingleton<IFeatureDecisionSource, TestFeatures>();
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("LocalAdmin")));
        builder.Services.AddSingleton<IIdentityManagementQueryService, EmptyQueries>();
        var accounts = new PasswordChangingAccounts();
        builder.Services.AddSingleton<IIdentityAccountApplicationService>(accounts);
        var cookie = new RecordingCookie();
        builder.Services.AddSingleton<IHostAuthenticationCookie>(cookie);

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/identity/accounts")).StatusCode);
        using var ordinary = new HttpRequestMessage(HttpMethod.Get, "/api/identity/accounts");
        ordinary.Headers.Add("X-Test-User", "user-1");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(ordinary)).StatusCode);
        using var administrator = new HttpRequestMessage(HttpMethod.Get, "/api/identity/accounts");
        administrator.Headers.Add("X-Test-User", "admin-1");
        administrator.Headers.Add("X-Test-Role", "LocalAdmin");
        administrator.Headers.Add("X-NewP-Feature", "test-feature");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(administrator)).StatusCode);

        using var passwordChange = new HttpRequestMessage(HttpMethod.Post, "/api/identity/me/password-change")
        {
            Content = JsonContent.Create(new ChangeIdentityPasswordRequest("old-password", "new-password", 1))
        };
        passwordChange.Headers.Add("X-Test-User", "user-1");
        var changed = await client.SendAsync(passwordChange);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("Asia/Shanghai", accounts.LastCommand?.TimeZoneId);
        Assert.Equal("rotated-session-secret", cookie.Credential);
        Assert.DoesNotContain("rotated-session-secret", await changed.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var invalidStatus = new HttpRequestMessage(HttpMethod.Put, "/api/identity/accounts/user-1/status/disable")
        {
            Content = new StringContent(
                """{"targetStatus":"Archived","expectedRevision":1,"reason":"invalid enum"}""",
                Encoding.UTF8,
                "application/json")
        };
        invalidStatus.Headers.Add("X-Test-User", "admin-1");
        invalidStatus.Headers.Add("X-Test-Role", "LocalAdmin");
        invalidStatus.Headers.Add("X-NewP-Feature", "test-feature");
        var invalidStatusResponse = await client.SendAsync(invalidStatus);

        Assert.Equal(HttpStatusCode.BadRequest, invalidStatusResponse.StatusCode);
        Assert.Equal("application/problem+json", invalidStatusResponse.Content.Headers.ContentType?.MediaType);
        using var invalidStatusProblem = JsonDocument.Parse(await invalidStatusResponse.Content.ReadAsStringAsync());
        Assert.Equal("REQUEST_VALIDATION_FAILED", invalidStatusProblem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(invalidStatusProblem.RootElement.GetProperty("traceId").GetString()));
        Assert.True(invalidStatusProblem.RootElement.GetProperty("errors").EnumerateObject().Any());

        foreach (var field in new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt", "CreatedBy_DisplayValue", "CreatedAt_DisplayValue", "UpdatedBy_DisplayValue", "UpdatedAt_DisplayValue" })
        {
            foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
            {
            using var forged = new HttpRequestMessage(method, method == HttpMethod.Post ? "/api/identity/accounts" : "/api/identity/accounts/user-1")
            {
                Content = JsonContent.Create(new Dictionary<string, object?> { ["ecode"] = "audit", ["ename"] = "Audit", [field] = "forged" })
            };
            forged.Headers.Add("X-Test-User", "admin-1");
            forged.Headers.Add("X-Test-Role", "LocalAdmin");
        forged.Headers.Add("X-NewP-Feature", "test-feature");
            var rejected = await client.SendAsync(forged);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            }
        }
    }

    private sealed class TestFeatures : IFeatureDecisionSource
    {
        public ValueTask<PermissionDecision> EvaluateAsync(string subjectId, string position, string moduleId, string behaviorKey, CancellationToken cancellationToken) =>
            ValueTask.FromResult(position == "test-feature" ? PermissionDecision.Granted : PermissionDecision.Denied);
    }

    private sealed class EmptyQueries : IIdentityManagementQueryService
    {
        public ValueTask<IdentityManagementAccountPage> SearchAsync(IdentityManagementAccountSearch search, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new IdentityManagementAccountPage([], search.Page, search.PageSize, 0));

        public ValueTask<IdentityManagementAccountDetail?> FindAsync(string accountEid, int eventPage, int eventPageSize, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IdentityManagementAccountDetail?>(null);

        public ValueTask<IdentityManagementAccountView?> FindAccountAsync(string accountEid, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IdentityManagementAccountView?>(null);

        public ValueTask<IdentityManagementSecurityEventPage?> SearchSecurityEventsAsync(
            string accountEid,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IdentityManagementSecurityEventPage?>(null);
    }

    private sealed class PasswordChangingAccounts : IIdentityAccountApplicationService
    {
        public ChangeIdentityPasswordCommand? LastCommand { get; private set; }

        public ValueTask<IdentityPasswordChangeResult> ChangePasswordAsync(ChangeIdentityPasswordCommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return ValueTask.FromResult(new IdentityPasswordChangeResult(
                Account(command.AccountEid),
                new AuthenticationSessionIssuance
                {
                    Credential = "rotated-session-secret",
                    Session = new AuthenticationSessionSnapshot
                    {
                        SessionEid = "session-2",
                        AccountEid = command.AccountEid,
                        Type = AuthenticationSessionType.Regular,
                        TimeZoneId = "Asia/Shanghai",
                        ExpiresAtUtc = new DateTime(2030, 1, 1, 8, 0, 0, DateTimeKind.Utc)
                    }
                }));
        }

        public ValueTask<IdentityAccountCreationResult> CreateAsync(CreateIdentityAccountCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IdentityAccountSnapshot> UpdateNameAsync(UpdateIdentityAccountNameCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IdentityAccountSnapshot> SetStatusAsync(SetIdentityAccountStatusCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IdentityCredentialResetResult> ResetCredentialAsync(ResetIdentityCredentialCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IdentityAccountSnapshot> DeleteAsync(DeleteIdentityAccountCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static IdentityAccountSnapshot Account(string accountEid) => new()
        {
            AccountEid = accountEid,
            ECODE = "alice",
            ENAME = "Alice",
            Status = IdentityAccountStatus.Enabled,
            Revision = 2
        };
    }

    private sealed class RecordingCookie : IHostAuthenticationCookie
    {
        public string? Credential { get; private set; }
        public string? Read(HttpContext context) => null;
        public bool TryUnprotect(string protectedValue, out string credential) => throw new NotSupportedException();
        public void Append(HttpContext context, string credential, DateTime expiresAtUtc) => Credential = credential;
        public void Delete(HttpContext context) => throw new NotSupportedException();
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "IdentityApiTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var accountEid))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, accountEid.ToString()),
                new(AuthenticationSessionClaimTypes.TimeZoneId, "Asia/Shanghai")
            };
            if (Request.Headers.TryGetValue("X-Test-Role", out var role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}

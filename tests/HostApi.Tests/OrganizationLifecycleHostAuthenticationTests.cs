using System.Net;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Repositories;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Persistence.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using PlatformAuthorizationService = ICP.Platform.Modules.Authorization.Services.IAuthorizationService;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationLifecycleHostAuthenticationTests
{
    [Fact]
    public async Task BoundPersonLifecycle_OldCookieIs401AfterDisableAndNeverRevivesAfterRestoreOrExplicitUserEnable()
    {
        var now = new DateTime(2026, 8, 10, 16, 0, 0, DateTimeKind.Utc);
        var identity = new RecordingIdentityRepository(User(now));
        var sessions = new RecordingSessionRegistry("session-secret", now.AddHours(8));
        var directory = new RepositoryAccountDirectory(identity);
        var person = new RecordingPersonRepository(Person(now));
        var transaction = new SnapshotTransactionExecutor(person, identity, sessions);
        var lifecycle = new PersonLifecycleService(
            person,
            directory,
            new IdentityLifecycleParticipant(identity, sessions, new PlatformObjectIdGenerator(), new FixedTimeProvider(now)),
            transaction,
            new FixedTimeProvider(now));
        var accounts = new IdentityAccountApplicationService(
            identity,
            transaction,
            sessions,
            new PasswordHasher<User>(),
            new UnusedTemporaryCredentialIssuer(),
            new PlatformObjectIdGenerator(),
            new FixedTimeProvider(now));

        await using var app = await StartHostAsync(sessions, directory);
        using var client = app.GetTestClient();
        var cookieHeader = IssueCookieHeader(app, "session-secret", now.AddHours(8));

        Assert.Equal(HttpStatusCode.OK, (await SendProtectedAsync(client, cookieHeader)).StatusCode);

        await lifecycle.SetStatusAsync(
            "Person_1",
            new SetPersonLifecycleStatusRequest(false, 4, "人员离职"),
            new PersonLifecycleActor("User_Admin", "trace-disable"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendProtectedAsync(client, cookieHeader)).StatusCode);
        Assert.True(identity.Current.PersonnelDisableHold);
        Assert.Equal(IdentityAccountStatus.Disabled, identity.Current.Status);

        var blocked = await Assert.ThrowsAsync<IdentityApplicationException>(async () =>
            await accounts.SetStatusAsync(new SetIdentityAccountStatusCommand(
                "User_1",
                IdentityAccountStatus.Enabled,
                identity.Current.Revision,
                "管理员显式恢复",
                new IdentityActorContext("User_Admin", IdentitySecurityActorKind.User, "trace-blocked"))));
        Assert.Equal(IdentityFailureCodes.PersonnelDisableHold, blocked.FailureCode);

        await lifecycle.SetStatusAsync(
            "Person_1",
            new SetPersonLifecycleStatusRequest(true, 5, "人员返岗"),
            new PersonLifecycleActor("User_Admin", "trace-restore"));

        Assert.False(identity.Current.PersonnelDisableHold);
        Assert.Equal(IdentityAccountStatus.Disabled, identity.Current.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendProtectedAsync(client, cookieHeader)).StatusCode);

        var enabled = await accounts.SetStatusAsync(new SetIdentityAccountStatusCommand(
            "User_1",
            IdentityAccountStatus.Enabled,
            identity.Current.Revision,
            "管理员显式恢复",
            new IdentityActorContext("User_Admin", IdentitySecurityActorKind.User, "trace-enable")));

        Assert.Equal(IdentityAccountStatus.Enabled, enabled.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendProtectedAsync(client, cookieHeader)).StatusCode);
        Assert.True(sessions.Revoked);
    }

    private static async Task<WebApplication> StartHostAsync(
        IAuthenticationSessionRegistry sessions,
        IIdentityAccountDirectory directory)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddDataProtection();
        builder.Services.Configure<HostAuthenticationOptions>(options =>
        {
            options.CookieName = "ICP.Platform.Auth";
            options.AllowInsecureDevelopmentCookie = true;
        });
        builder.Services.AddSingleton<IHostAuthenticationCookie, HostAuthenticationCookie>();
        builder.Services.AddSingleton(sessions);
        builder.Services.AddSingleton(directory);
        builder.Services.AddSingleton<PlatformAuthorizationService, NoOpAuthorizationService>();
        builder.Services.AddAuthentication(HostSessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HostSessionAuthenticationHandler>(
                HostSessionAuthenticationHandler.SchemeName,
                static _ => { });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/protected", () => Results.Ok()).RequireAuthorization();
        await app.StartAsync();
        return app;
    }

    private static string IssueCookieHeader(WebApplication app, string credential, DateTime expiresAt)
    {
        var context = new DefaultHttpContext { RequestServices = app.Services };
        app.Services.GetRequiredService<IHostAuthenticationCookie>().Append(context, credential, expiresAt);
        return Assert.Single(context.Response.Headers.SetCookie).ToString().Split(';')[0];
    }

    private static Task<HttpResponseMessage> SendProtectedAsync(HttpClient client, string cookieHeader)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        request.Headers.Add("Cookie", cookieHeader);
        return client.SendAsync(request);
    }

    private static User User(DateTime now) => new()
    {
        EID = "User_1",
        ECODE = "alice",
        NormalizedECODE = "ALICE",
        ENAME = "Alice",
        Status = IdentityAccountStatus.Enabled,
        IsPersonnelBound = true,
        PersonnelDisableHold = false,
        Revision = 7,
        CreatedBy = "User_Admin",
        CreatedAt = now.AddDays(-1),
        UpdatedBy = "User_Admin",
        UpdatedAt = now.AddDays(-1)
    };

    private static PersonUserBindingState Person(DateTime now) => new()
    {
        PersonEid = "Person_1",
        PersonName = "张三",
        IsEnabled = true,
        Revision = 4,
        UserEid = "User_1",
        BoundAt = now.AddDays(-1),
        BoundBy = "User_Admin",
        BindingReason = "入职绑定",
        UpdatedBy = "User_Admin",
        UpdatedAt = now.AddDays(-1)
    };

    private sealed class RecordingPersonRepository(PersonUserBindingState state) : IPersonUserBindingRepository
    {
        public PersonUserBindingState Current { get; set; } = state;

        public ValueTask<PersonUserBindingState?> FindAsync(string personEid, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<PersonUserBindingState?>(Current.PersonEid == personEid ? Current with { } : null);

        public ValueTask<bool> TryBindAsync(string personEid, string userEid, DateTime boundAt, string actorId, string reason, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> TrySetEnablementAsync(string personEid, bool isEnabled, long expectedRevision, string actorId, DateTime changedAt, CancellationToken cancellationToken = default)
        {
            if (Current.PersonEid != personEid || Current.Revision != expectedRevision || Current.IsEnabled == isEnabled)
            {
                return ValueTask.FromResult(false);
            }

            Current = Current with
            {
                IsEnabled = isEnabled,
                Revision = Current.Revision + 1,
                UpdatedBy = actorId,
                UpdatedAt = changedAt
            };
            return ValueTask.FromResult(true);
        }
    }

    private sealed class RecordingIdentityRepository(User user) : IIdentityRepository
    {
        public User Current { get; set; } = Clone(user);
        public List<IdentitySecurityEvent> Events { get; } = [];

        public ValueTask AcquireLocalAdministratorBootstrapLockAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<User?> FindUserAsync(string userEid, CancellationToken cancellationToken = default) => ValueTask.FromResult<User?>(Current.EID == userEid ? Clone(Current) : null);
        public ValueTask<User?> FindUserByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken = default) => ValueTask.FromResult<User?>(Current.NormalizedECODE == normalizedCode ? Clone(Current) : null);
        public ValueTask<IReadOnlyList<User>> SearchUsersAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<User>>([Clone(Current)]);
        public ValueTask<LocalCredential?> FindCredentialAsync(string userEid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<IdentitySecurityEvent>> SearchSecurityEventsAsync(string targetUserEid, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<IdentitySecurityEvent>>(Events.ToArray());
        public void AddUser(User user) => throw new NotSupportedException();
        public void AddCredential(LocalCredential credential) => throw new NotSupportedException();
        public ValueTask ReplaceCredentialAsync(LocalCredential credential, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask RemoveCredentialAsync(string userEid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void AddSecurityEvent(IdentitySecurityEvent securityEvent) => Events.Add(securityEvent);

        public ValueTask<bool> TryReplaceUserAsync(User replacement, long expectedRevision, CancellationToken cancellationToken = default)
        {
            if (Current.Revision != expectedRevision)
            {
                return ValueTask.FromResult(false);
            }

            Current = Clone(replacement);
            return ValueTask.FromResult(true);
        }

        public static User Clone(User source) => new()
        {
            EID = source.EID,
            ECODE = source.ECODE,
            NormalizedECODE = source.NormalizedECODE,
            ENAME = source.ENAME,
            Status = source.Status,
            IsLocalAdministrator = source.IsLocalAdministrator,
            IsPersonnelBound = source.IsPersonnelBound,
            PersonnelDisableHold = source.PersonnelDisableHold,
            Revision = source.Revision,
            CreatedBy = source.CreatedBy,
            CreatedAt = source.CreatedAt,
            UpdatedBy = source.UpdatedBy,
            UpdatedAt = source.UpdatedAt
        };
    }

    private sealed class RepositoryAccountDirectory(RecordingIdentityRepository repository) : IIdentityAccountDirectory
    {
        public async ValueTask<IdentityAccountSnapshot?> FindAsync(string userEid, CancellationToken cancellationToken = default)
        {
            var user = await repository.FindUserAsync(userEid, cancellationToken);
            return user is null ? null : new IdentityAccountSnapshot
            {
                UserEid = user.EID,
                ECODE = user.ECODE,
                ENAME = user.ENAME,
                Status = user.Status,
                Revision = user.Revision,
                IsLocalAdministrator = user.IsLocalAdministrator,
                IsPersonnelBound = user.IsPersonnelBound,
                PersonnelDisableHold = user.PersonnelDisableHold
            };
        }

        public ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(IdentityAccountSearch search, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingSessionRegistry(string credential, DateTime expiresAt) : IAuthenticationSessionRegistry
    {
        public bool Revoked { get; set; }

        public ValueTask<AuthenticationSessionIssuance> RegisterAsync(AuthenticationSessionRegistration registration, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<AuthenticationSessionValidation> ValidateAsync(string candidate, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(candidate != credential
                ? new AuthenticationSessionValidation { Status = AuthenticationSessionValidationStatus.Unknown }
                : new AuthenticationSessionValidation
                {
                    Status = Revoked ? AuthenticationSessionValidationStatus.Revoked : AuthenticationSessionValidationStatus.Active,
                    Session = new AuthenticationSessionSnapshot
                    {
                        SessionEid = "Session_1",
                        UserEid = "User_1",
                        Type = AuthenticationSessionType.Regular,
                        ExpiresAtUtc = expiresAt,
                        RevokedAtUtc = Revoked ? expiresAt.AddHours(-7) : null,
                        RevocationReason = Revoked ? "人员离职" : null
                    }
                });

        public ValueTask<bool> RevokeCurrentAsync(AuthenticationSessionRevocation revocation, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<int> RevokeAllAsync(UserAuthenticationSessionsRevocation revocation, CancellationToken cancellationToken = default)
        {
            var changed = !Revoked;
            Revoked = true;
            return ValueTask.FromResult(changed ? 1 : 0);
        }
    }

    private sealed class SnapshotTransactionExecutor(
        RecordingPersonRepository person,
        RecordingIdentityRepository identity,
        RecordingSessionRegistry sessions) : IPlatformTransactionExecutor
    {
        public async ValueTask<TResult> ExecuteAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> registerChanges, CancellationToken cancellationToken = default)
        {
            var personSnapshot = person.Current with { };
            var userSnapshot = RecordingIdentityRepository.Clone(identity.Current);
            var events = identity.Events.ToArray();
            var revoked = sessions.Revoked;
            try
            {
                return await registerChanges(cancellationToken);
            }
            catch
            {
                person.Current = personSnapshot;
                identity.Current = userSnapshot;
                identity.Events.Clear();
                identity.Events.AddRange(events);
                sessions.Revoked = revoked;
                throw;
            }
        }
    }

    private sealed class NoOpAuthorizationService : PlatformAuthorizationService
    {
        public Task<bool> HasPermissionAsync(string userId, string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> HasAnyPermissionAsync(string userId, IEnumerable<string> permissionCodes, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> HasAllPermissionsAsync(string userId, IEnumerable<string> permissionCodes, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<List<string>> GetUserPermissionsAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(new List<string>());
        public Task<List<string>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(new List<string>());
    }

    private sealed class UnusedTemporaryCredentialIssuer : ITemporaryCredentialIssuer
    {
        public string Issue() => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}

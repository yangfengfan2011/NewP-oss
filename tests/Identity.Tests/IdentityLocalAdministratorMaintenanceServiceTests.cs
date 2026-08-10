using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Persistence.Abstractions;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace ICP.Platform.Identity.Tests;

public sealed class IdentityLocalAdministratorMaintenanceServiceTests
{
    [Fact]
    public async Task BootstrapAsync_OnEmptyIdentity_CreatesCompleteProtectedAdministrator()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.BootstrapAsync(new BootstrapLocalAdministratorCommand(
            "admin",
            "Local Administrator",
            "首次初始化",
            fixture.Actor));

        var user = Assert.Single(fixture.Repository.Users);
        Assert.Equal(result.Account.UserEid, user.EID);
        Assert.Equal("admin", user.ECODE);
        Assert.True(user.IsLocalAdministrator);
        Assert.Equal(IdentityAccountStatus.Enabled, user.Status);
        Assert.True(fixture.Repository.VerifyPassword(user.EID, result.TemporaryCredential));
        Assert.True(Assert.Single(fixture.Repository.Credentials).IsTemporary);
        Assert.Equal(IdentitySecurityEventTypes.LocalAdministratorBootstrapped, Assert.Single(fixture.Repository.SecurityEvents).EventType);
        Assert.Equal(LocalAdministratorAssignmentState.Assigned, await fixture.Assignment.InspectAsync(user.EID));
    }

    [Fact]
    public async Task BootstrapAsync_WhenAnyUserExists_FailsWithoutPartialState()
    {
        var fixture = new Fixture();
        await fixture.Service.BootstrapAsync(new BootstrapLocalAdministratorCommand(
            "admin",
            "Local Administrator",
            "首次初始化",
            fixture.Actor));

        var exception = await Assert.ThrowsAsync<IdentityMaintenanceException>(() => fixture.Service.BootstrapAsync(
            new BootstrapLocalAdministratorCommand("second", "Second", "重复初始化", fixture.Actor)).AsTask());

        Assert.Equal(IdentityMaintenanceFailureCodes.BootstrapAlreadyCompleted, exception.FailureCode);
        Assert.Single(fixture.Repository.Users);
        Assert.Single(fixture.Repository.Credentials);
        Assert.Single(fixture.Repository.SecurityEvents);
    }

    [Fact]
    public async Task BootstrapAsync_WhenRoleAssignmentFails_RollsBackEveryIdentityWrite()
    {
        var fixture = new Fixture();
        fixture.Assignment.FailAssignment = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.BootstrapAsync(
            new BootstrapLocalAdministratorCommand("admin", "Local Administrator", "首次初始化", fixture.Actor)).AsTask());

        Assert.Empty(fixture.Repository.Users);
        Assert.Empty(fixture.Repository.Credentials);
        Assert.Empty(fixture.Repository.SecurityEvents);
        Assert.Empty(fixture.Assignment.Assignments);
    }

    [Fact]
    public async Task ResetAsync_RotatesTemporaryCredentialAndRevokesOldSessions()
    {
        var fixture = new Fixture("bootstrap-secret", "reset-secret");
        var created = await fixture.Service.BootstrapAsync(new BootstrapLocalAdministratorCommand(
            "admin",
            "Local Administrator",
            "首次初始化",
            fixture.Actor));

        var reset = await fixture.Service.ResetAsync(new ResetLocalAdministratorCredentialCommand(
            "离线重置",
            fixture.Actor));

        Assert.Equal(created.Account.UserEid, reset.Account.UserEid);
        Assert.Equal(created.Account.Revision + 1, reset.Account.Revision);
        Assert.False(fixture.Repository.VerifyPassword(reset.Account.UserEid, created.TemporaryCredential));
        Assert.True(fixture.Repository.VerifyPassword(reset.Account.UserEid, reset.TemporaryCredential));
        Assert.Equal("reset-secret", reset.TemporaryCredential);
        Assert.Single(fixture.Sessions.Revocations);
        Assert.Equal(IdentitySecurityEventTypes.LocalAdministratorCredentialReset, fixture.Repository.SecurityEvents.Last().EventType);
        Assert.True(fixture.Repository.Users.Single().IsLocalAdministrator);
        Assert.Equal("admin", fixture.Repository.Users.Single().ECODE);
    }

    [Fact]
    public async Task InspectAsync_ReportsBootstrapAndAdministratorInvariantStates()
    {
        var fixture = new Fixture();

        Assert.Equal(LocalAdministratorReadinessState.BootstrapRequired, (await fixture.Probe.InspectAsync()).State);

        var created = await fixture.Service.BootstrapAsync(new BootstrapLocalAdministratorCommand(
            "admin",
            "Local Administrator",
            "首次初始化",
            fixture.Actor));
        Assert.Equal(LocalAdministratorReadinessState.Ready, (await fixture.Probe.InspectAsync()).State);

        fixture.Assignment.Assignments.Clear();
        var invalid = await fixture.Probe.InspectAsync();
        Assert.Equal(LocalAdministratorReadinessState.InvariantInvalid, invalid.State);
        Assert.Equal("local-admin-role-assignment-invalid", invalid.Diagnostic);
        Assert.Equal(created.Account.UserEid, invalid.UserEid);
    }

    [Fact]
    public async Task InspectAsync_WhenAdministratorCredentialIsMissing_ReportsStableInvariantDiagnostic()
    {
        var fixture = new Fixture();
        await fixture.Service.BootstrapAsync(new BootstrapLocalAdministratorCommand(
            "admin",
            "Local Administrator",
            "首次初始化",
            fixture.Actor));
        fixture.Repository.Credentials.Clear();

        var result = await fixture.Probe.InspectAsync();

        Assert.Equal(LocalAdministratorReadinessState.InvariantInvalid, result.State);
        Assert.Equal("local-administrator-credential-invalid", result.Diagnostic);
    }

    [Fact]
    public async Task InspectAsync_WhenTwoProtectedAdministratorsExist_ReportsStableInvariantDiagnostic()
    {
        var fixture = new Fixture();
        await fixture.Service.BootstrapAsync(new BootstrapLocalAdministratorCommand(
            "admin",
            "Local Administrator",
            "首次初始化",
            fixture.Actor));
        var duplicate = fixture.Repository.Users.Single();
        fixture.Repository.Users.Add(new User
        {
            EID = "User_duplicate",
            ECODE = "other-admin",
            NormalizedECODE = "OTHER-ADMIN",
            ENAME = duplicate.ENAME,
            Status = IdentityAccountStatus.Enabled,
            IsLocalAdministrator = true,
            Revision = 1,
            CreatedBy = duplicate.CreatedBy,
            CreatedAt = duplicate.CreatedAt,
            UpdatedBy = duplicate.UpdatedBy,
            UpdatedAt = duplicate.UpdatedAt
        });

        var result = await fixture.Probe.InspectAsync();

        Assert.Equal(LocalAdministratorReadinessState.InvariantInvalid, result.State);
        Assert.Equal("local-administrator-count-invalid", result.Diagnostic);
    }

    private sealed class Fixture
    {
        public Fixture(params string[] credentials)
        {
            Repository = new InMemoryIdentityRepository();
            Sessions = new RecordingSessionRegistry();
            Assignment = new RecordingAssignment();
            var transaction = new SnapshotTransactionExecutor(Repository, Sessions, Assignment);
            var issuer = new QueueTemporaryCredentialIssuer(credentials.Length == 0 ? ["temporary-secret"] : credentials);
            var objectIds = new SequenceObjectIdGenerator();
            var time = new FixedTimeProvider(new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero));
            Service = new IdentityLocalAdministratorMaintenanceService(
                Repository,
                transaction,
                Sessions,
                Assignment,
                new PasswordHasher<User>(),
                issuer,
                objectIds,
                time);
            Probe = new IdentityLocalAdministratorReadinessProbe(Repository, Assignment);
        }

        public IdentityActorContext Actor { get; } = new(
            IdentityMaintenanceActors.LocalMaintenance,
            IdentitySecurityActorKind.LocalMaintenance,
            "trace-33");

        public InMemoryIdentityRepository Repository { get; }
        public RecordingSessionRegistry Sessions { get; }
        public RecordingAssignment Assignment { get; }
        public IIdentityLocalAdministratorMaintenanceService Service { get; }
        public IIdentityLocalAdministratorReadinessProbe Probe { get; }
    }

    private sealed class RecordingAssignment : ILocalAdministratorRoleAssignment
    {
        public HashSet<string> Assignments { get; } = new(StringComparer.Ordinal);
        public bool FailAssignment { get; set; }

        public ValueTask AssignAsync(string userEid, string actorId, CancellationToken cancellationToken = default)
        {
            if (FailAssignment)
            {
                throw new InvalidOperationException("injected assignment failure");
            }

            Assignments.Add(userEid);
            return ValueTask.CompletedTask;
        }

        public ValueTask<LocalAdministratorAssignmentState> InspectAsync(
            string userEid,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Assignments.Contains(userEid)
                ? LocalAdministratorAssignmentState.Assigned
                : LocalAdministratorAssignmentState.NotAssigned);
    }

    private sealed class SnapshotTransactionExecutor(
        InMemoryIdentityRepository repository,
        RecordingSessionRegistry sessions,
        RecordingAssignment assignment) : IPlatformTransactionExecutor
    {
        public async ValueTask<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, ValueTask<TResult>> registerChanges,
            CancellationToken cancellationToken = default)
        {
            var repositorySnapshot = repository.CreateSnapshot();
            var sessionsSnapshot = sessions.CreateSnapshot();
            var assignments = assignment.Assignments.ToArray();
            try
            {
                return await registerChanges(cancellationToken);
            }
            catch
            {
                repository.Restore(repositorySnapshot);
                sessions.Restore(sessionsSnapshot);
                assignment.Assignments.Clear();
                assignment.Assignments.UnionWith(assignments);
                throw;
            }
        }
    }

    private sealed class QueueTemporaryCredentialIssuer(IEnumerable<string> credentials) : ITemporaryCredentialIssuer
    {
        private readonly Queue<string> _credentials = new(credentials);

        public string Issue() => _credentials.Count > 1 ? _credentials.Dequeue() : _credentials.Peek();
    }

    private sealed class SequenceObjectIdGenerator : IPlatformObjectIdGenerator
    {
        private int _next;
        public string NewId(string objectName) => $"{objectName}_{++_next:D4}";
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingSessionRegistry : IAuthenticationSessionRegistry
    {
        public List<UserAuthenticationSessionsRevocation> Revocations { get; } = [];
        public object CreateSnapshot() => Revocations.ToArray();
        public void Restore(object snapshot)
        {
            Revocations.Clear();
            Revocations.AddRange((UserAuthenticationSessionsRevocation[])snapshot);
        }

        public ValueTask<AuthenticationSessionIssuance> RegisterAsync(AuthenticationSessionRegistration registration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<AuthenticationSessionValidation> ValidateAsync(string credentialDigest, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> RevokeCurrentAsync(AuthenticationSessionRevocation revocation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> RevokeAllAsync(UserAuthenticationSessionsRevocation revocation, CancellationToken cancellationToken = default)
        {
            Revocations.Add(revocation);
            return ValueTask.FromResult(1);
        }
    }
}

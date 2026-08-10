using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Persistence.Abstractions;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class PersonLifecycleServiceTests
{
    private static readonly DateTime ChangedAt = new(2026, 8, 10, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task DisableAsync_CommitsPersonAccountHoldSessionsEventAuditAndRevisionsOnce()
    {
        var fixture = Fixture();

        var result = await fixture.Service.SetStatusAsync(
            "Person_1",
            new SetPersonLifecycleStatusRequest(false, 4, "  人员离职  "),
            new PersonLifecycleActor("User_Admin", "trace-40"));

        Assert.False(result.IsEnabled);
        Assert.Equal(5, result.Revision);
        Assert.Equal(8, result.Account?.Revision);
        Assert.Equal(1, fixture.Transaction.ExecutionCount);
        Assert.Equal(1, fixture.Transaction.SaveCount);
        Assert.False(fixture.Repository.Current.IsEnabled);
        Assert.Equal(5, fixture.Repository.Current.Revision);
        Assert.Equal("User_Admin", fixture.Repository.Current.UpdatedBy);
        Assert.Equal(ChangedAt, fixture.Repository.Current.UpdatedAt);
        Assert.Equal(IdentityAccountStatus.Disabled, fixture.Identity.Account.Status);
        Assert.True(fixture.Identity.Account.PersonnelDisableHold);
        Assert.Equal(1, fixture.Identity.SessionRevocations);
        Assert.Single(fixture.Identity.Events, "人员离职");
        Assert.Equal(["person", "identity", "authentication", "commit"], fixture.Stages);
    }

    [Fact]
    public async Task RestoreAsync_ClearsHoldButKeepsAccountDisabledAndSessionsRevoked()
    {
        var fixture = Fixture(
            person: Person() with { IsEnabled = false, Revision = 5 },
            account: Account() with
            {
                Status = IdentityAccountStatus.Disabled,
                PersonnelDisableHold = true,
                Revision = 8
            },
            existingSessionRevocations: 2);

        var result = await fixture.Service.SetStatusAsync(
            "Person_1",
            new SetPersonLifecycleStatusRequest(true, 5, "人员返岗"),
            new PersonLifecycleActor("User_Admin", "trace-restore"));

        Assert.True(result.IsEnabled);
        Assert.Equal(6, result.Revision);
        Assert.Equal("Disabled", result.Account?.Status);
        Assert.False(result.Account?.PersonnelDisableHold);
        Assert.Equal(2, fixture.Identity.SessionRevocations);
        Assert.Equal(9, fixture.Identity.Account.Revision);
        Assert.Single(fixture.Identity.Events, "人员恢复并解除停用阻断");
    }

    [Fact]
    public async Task SetStatusAsync_UnboundPersonUsesOwnLifecycleWithoutIdentityDependency()
    {
        var fixture = Fixture(person: Person() with { UserEid = null }, directoryUnavailable: true);

        var result = await fixture.Service.SetStatusAsync(
            "Person_1",
            new SetPersonLifecycleStatusRequest(false, 4, "人员停用"),
            new PersonLifecycleActor("User_Admin", "trace-unbound"));

        Assert.False(result.IsEnabled);
        Assert.Null(result.Account);
        Assert.Equal(0, fixture.Identity.SessionRevocations);
        Assert.Empty(fixture.Identity.Events);
    }

    [Fact]
    public async Task SetStatusAsync_RetryOfFullyConvergedTargetDoesNotDriftRevisionOrDuplicateEvent()
    {
        var fixture = Fixture(
            person: Person() with { IsEnabled = false, Revision = 5 },
            account: Account() with
            {
                Status = IdentityAccountStatus.Disabled,
                PersonnelDisableHold = true,
                Revision = 8
            },
            existingSessionRevocations: 1,
            existingEvents: ["人员离职"]);

        var result = await fixture.Service.SetStatusAsync(
            "Person_1",
            new SetPersonLifecycleStatusRequest(false, 4, "重复停用"),
            new PersonLifecycleActor("User_Admin", "trace-retry"));

        Assert.Equal(5, result.Revision);
        Assert.Equal(8, result.Account?.Revision);
        Assert.Equal(0, fixture.Transaction.ExecutionCount);
        Assert.Equal(1, fixture.Identity.SessionRevocations);
        Assert.Single(fixture.Identity.Events, "人员离职");
    }

    [Fact]
    public async Task SetStatusAsync_StalePersonRevisionReturnsStableConflictWithoutMutation()
    {
        var fixture = Fixture(person: Person() with { Revision = 5 });

        var exception = await Assert.ThrowsAsync<PersonLifecycleException>(async () =>
            await fixture.Service.SetStatusAsync(
                "Person_1",
                new SetPersonLifecycleStatusRequest(false, 4, "并发停用"),
                new PersonLifecycleActor("User_Admin", "trace-person-conflict")));

        Assert.Equal(PersonLifecycleFailureCodes.Conflict, exception.FailureCode);
        Assert.True(fixture.Repository.Current.IsEnabled);
        Assert.Equal(5, fixture.Repository.Current.Revision);
        Assert.Equal(0, fixture.Transaction.ExecutionCount);
    }

    [Fact]
    public async Task SetStatusAsync_IdentityRevisionConflictRollsBackPersonAndReturnsLifecycleConflict()
    {
        var fixture = Fixture(failureStage: LifecycleFailureStage.IdentityRevisionConflict);

        var exception = await Assert.ThrowsAsync<PersonLifecycleException>(async () =>
            await fixture.Service.SetStatusAsync(
                "Person_1",
                new SetPersonLifecycleStatusRequest(false, 4, "账号并发"),
                new PersonLifecycleActor("User_Admin", "trace-user-conflict")));

        Assert.Equal(PersonLifecycleFailureCodes.Conflict, exception.FailureCode);
        Assert.True(fixture.Repository.Current.IsEnabled);
        Assert.Equal(4, fixture.Repository.Current.Revision);
        Assert.Equal(IdentityAccountStatus.Enabled, fixture.Identity.Account.Status);
    }

    [Fact]
    public async Task SetStatusAsync_BoundLifecycleFailsClosedWhenIdentityIsUnavailable()
    {
        var fixture = Fixture(directoryUnavailable: true);

        var exception = await Assert.ThrowsAsync<PersonLifecycleException>(async () =>
            await fixture.Service.SetStatusAsync(
                "Person_1",
                new SetPersonLifecycleStatusRequest(false, 4, "依赖故障"),
                new PersonLifecycleActor("User_Admin", "trace-outage")));

        Assert.Equal(PersonLifecycleFailureCodes.DependencyUnavailable, exception.FailureCode);
        Assert.True(fixture.Repository.Current.IsEnabled);
        Assert.Equal(0, fixture.Transaction.ExecutionCount);
    }

    [Theory]
    [InlineData(LifecycleFailureStage.Person)]
    [InlineData(LifecycleFailureStage.User)]
    [InlineData(LifecycleFailureStage.Hold)]
    [InlineData(LifecycleFailureStage.Session)]
    [InlineData(LifecycleFailureStage.Event)]
    [InlineData(LifecycleFailureStage.SaveChanges)]
    public async Task DisableAsync_WhenAnyStageFails_RollsBackAllLifecycleFacts(LifecycleFailureStage stage)
    {
        var fixture = Fixture(failureStage: stage);
        var personBefore = fixture.Repository.Current with { };
        var accountBefore = fixture.Identity.Account with { };

        await Assert.ThrowsAsync<PersonLifecycleException>(async () =>
            await fixture.Service.SetStatusAsync(
                "Person_1",
                new SetPersonLifecycleStatusRequest(false, 4, "逐阶段故障"),
                new PersonLifecycleActor("User_Admin", $"trace-{stage}")));

        Assert.Equal(personBefore, fixture.Repository.Current);
        Assert.Equal(accountBefore, fixture.Identity.Account);
        Assert.Equal(0, fixture.Identity.SessionRevocations);
        Assert.Empty(fixture.Identity.Events);
    }

    private static TestFixture Fixture(
        PersonUserBindingState? person = null,
        IdentityAccountSnapshot? account = null,
        LifecycleFailureStage? failureStage = null,
        bool directoryUnavailable = false,
        int existingSessionRevocations = 0,
        IReadOnlyList<string>? existingEvents = null)
    {
        var stages = new List<string>();
        var repository = new RecordingRepository(person ?? Person(), failureStage, stages);
        var identity = new RecordingIdentityParticipant(account ?? Account(), failureStage, stages)
        {
            SessionRevocations = existingSessionRevocations
        };
        identity.Events.AddRange(existingEvents ?? []);
        var transaction = new SnapshotTransactionExecutor(repository, identity, failureStage);
        var service = new PersonLifecycleService(
            repository,
            new RecordingAccountDirectory(identity, directoryUnavailable),
            identity,
            identity,
            transaction,
            new FixedTimeProvider(ChangedAt));
        return new TestFixture(service, repository, identity, transaction, stages);
    }

    public enum LifecycleFailureStage
    {
        Person,
        User,
        Hold,
        Session,
        Event,
        SaveChanges,
        IdentityRevisionConflict
    }

    private static PersonUserBindingState Person() => new()
    {
        PersonEid = "Person_1",
        PersonName = "张三",
        IsEnabled = true,
        Revision = 4,
        UserEid = "User_1",
        BoundAt = ChangedAt.AddDays(-1),
        BoundBy = "User_Admin",
        BindingReason = "入职绑定",
        UpdatedBy = "User_Creator",
        UpdatedAt = ChangedAt.AddDays(-2)
    };

    private static IdentityAccountSnapshot Account() => new()
    {
        UserEid = "User_1",
        ECODE = "alice",
        ENAME = "Alice",
        Status = IdentityAccountStatus.Enabled,
        Revision = 7,
        IsPersonnelBound = true,
        PersonnelDisableHold = false
    };

    private sealed record TestFixture(
        PersonLifecycleService Service,
        RecordingRepository Repository,
        RecordingIdentityParticipant Identity,
        SnapshotTransactionExecutor Transaction,
        List<string> Stages);

    private sealed class RecordingRepository(PersonUserBindingState initial, LifecycleFailureStage? failureStage, List<string> stages) : IPersonUserBindingRepository
    {
        public PersonUserBindingState Current { get; set; } = initial;

        public ValueTask<PersonUserBindingState?> FindAsync(string personEid, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<PersonUserBindingState?>(Current.PersonEid == personEid ? Current with { } : null);

        public ValueTask<bool> TryBindAsync(string personEid, string userEid, DateTime boundAt, string actorId, string reason, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> TrySetEnablementAsync(
            string personEid,
            bool isEnabled,
            long expectedRevision,
            string actorId,
            DateTime changedAt,
            CancellationToken cancellationToken = default)
        {
            if (failureStage == LifecycleFailureStage.Person)
            {
                throw new InvalidOperationException("person-stage-failed");
            }

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
            stages.Add("person");
            return ValueTask.FromResult(true);
        }
    }

    private sealed class RecordingAccountDirectory(RecordingIdentityParticipant identity, bool unavailable) : IIdentityAccountDirectory
    {
        public ValueTask<IdentityAccountSnapshot?> FindAsync(string userEid, CancellationToken cancellationToken = default)
        {
            if (unavailable)
            {
                throw new TimeoutException("identity-directory-unavailable");
            }

            return ValueTask.FromResult<IdentityAccountSnapshot?>(identity.Account.UserEid == userEid ? identity.Account with { } : null);
        }

        public ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(IdentityAccountSearch search, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingIdentityParticipant(IdentityAccountSnapshot initial, LifecycleFailureStage? failureStage, List<string> stages)
        : IIdentityLifecycleParticipant, IAuthenticationSessionRegistry
    {
        public IdentityAccountSnapshot Account { get; set; } = initial;
        public int SessionRevocations { get; set; }
        public List<string> Events { get; } = [];

        public void StagesForCommit() => stages.Add("commit");

        public ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelBindingAsync(PersonnelBindingRegistration registration, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableAsync(PersonnelAccountDisableRegistration registration, CancellationToken cancellationToken = default)
        {
            if (failureStage == LifecycleFailureStage.IdentityRevisionConflict)
            {
                throw new IdentityLifecycleException(IdentityLifecycleFailureCodes.RevisionConflict, Account);
            }

            Account = Account with
            {
                Status = IdentityAccountStatus.Disabled,
                Revision = Account.Revision + 1
            };
            if (failureStage == LifecycleFailureStage.User)
            {
                throw new InvalidOperationException("user-stage-failed");
            }

            Account = Account with { PersonnelDisableHold = true };
            if (failureStage == LifecycleFailureStage.Hold)
            {
                throw new InvalidOperationException("hold-stage-failed");
            }

            Events.Add(registration.Reason);
            if (failureStage == LifecycleFailureStage.Event)
            {
                throw new InvalidOperationException("event-stage-failed");
            }

            stages.Add("identity");
            return ValueTask.FromResult(new IdentityLifecycleRegistrationResult { Account = Account, Changed = true });
        }

        public ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableHoldReleaseAsync(PersonnelDisableHoldReleaseRegistration registration, CancellationToken cancellationToken = default)
        {
            Account = Account with { PersonnelDisableHold = false, Revision = Account.Revision + 1 };
            Events.Add("人员恢复并解除停用阻断");
            return ValueTask.FromResult(new IdentityLifecycleRegistrationResult { Account = Account, Changed = true });
        }

        public ValueTask<AuthenticationSessionIssuance> RegisterAsync(AuthenticationSessionRegistration registration, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<AuthenticationSessionValidation> ValidateAsync(string credential, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> RevokeCurrentAsync(AuthenticationSessionRevocation revocation, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<int> RevokeAllAsync(UserAuthenticationSessionsRevocation revocation, CancellationToken cancellationToken = default)
        {
            SessionRevocations++;
            stages.Add("authentication");
            if (failureStage == LifecycleFailureStage.Session)
            {
                throw new InvalidOperationException("session-stage-failed");
            }

            return ValueTask.FromResult(1);
        }
    }

    private sealed class SnapshotTransactionExecutor(
        RecordingRepository repository,
        RecordingIdentityParticipant identity,
        LifecycleFailureStage? failureStage) : IPlatformTransactionExecutor
    {
        public int ExecutionCount { get; private set; }
        public int SaveCount { get; private set; }

        public async ValueTask<TResult> ExecuteAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> registerChanges, CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            var person = repository.Current with { };
            var account = identity.Account with { };
            var revocations = identity.SessionRevocations;
            var events = identity.Events.ToArray();
            try
            {
                var result = await registerChanges(cancellationToken);
                if (failureStage == LifecycleFailureStage.SaveChanges)
                {
                    throw new InvalidOperationException("save-changes-stage-failed");
                }

                SaveCount++;
                identity.StagesForCommit();
                return result;
            }
            catch
            {
                repository.Current = person;
                identity.Account = account;
                identity.SessionRevocations = revocations;
                identity.Events.Clear();
                identity.Events.AddRange(events);
                throw;
            }
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}

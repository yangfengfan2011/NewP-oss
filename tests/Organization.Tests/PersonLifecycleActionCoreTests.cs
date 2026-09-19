using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class PersonLifecycleActionCoreTests
{
    private static readonly DateTime ChangedAt = new(2026, 8, 10, 14, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task DisableAsync_CommitsPersonAccountHoldSessionsEventAndAuditOnce()
    {
        var fixture = Fixture();

        var result = (await fixture.Core.ExecuteAsync(
            "Person_1",
            new SetPersonLifecycleStatusRequest(false, "  人员离职  "),
            new PersonLifecycleActor("User_Admin", "trace-40"))).Dto;

        Assert.False(result.IsEnabled);
        Assert.Equal(8, result.Account?.Revision);
        Assert.Equal(1, fixture.Transaction.ExecutionCount);
        Assert.Equal(1, fixture.Transaction.SaveCount);
        Assert.False(fixture.Repository.Current.IsEnabled);
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
            person: Person() with { IsEnabled = false },
            account: Account() with
            {
                Status = IdentityAccountStatus.Disabled,
                PersonnelDisableHold = true,
                Revision = 8
            },
            existingSessionRevocations: 2);

        var result = (await fixture.Core.ExecuteAsync(
            "Person_1",
            new SetPersonLifecycleStatusRequest(true, "人员返岗"),
            new PersonLifecycleActor("User_Admin", "trace-restore"))).Dto;

        Assert.True(result.IsEnabled);
        Assert.Equal("Disabled", result.Account?.Status);
        Assert.False(result.Account?.PersonnelDisableHold);
        Assert.Equal(2, fixture.Identity.SessionRevocations);
        Assert.Equal(9, fixture.Identity.Account.Revision);
        Assert.Single(fixture.Identity.Events, "人员恢复并解除停用阻断");
    }

    [Fact]
    public async Task SetStatusAsync_UnboundPersonUsesOwnLifecycleWithoutIdentityDependency()
    {
        var fixture = Fixture(person: Person() with { AccountEid = null }, directoryUnavailable: true);

        var result = (await fixture.Core.ExecuteAsync(
            "Person_1",
            new SetPersonLifecycleStatusRequest(false, "人员停用"),
            new PersonLifecycleActor("User_Admin", "trace-unbound"))).Dto;

        Assert.False(result.IsEnabled);
        Assert.Null(result.Account);
        Assert.Equal(0, fixture.Identity.SessionRevocations);
        Assert.Empty(fixture.Identity.Events);
    }

    [Fact]
    public async Task SetStatusAsync_RetryOfFullyConvergedTargetDoesNotDuplicateEvent()
    {
        var fixture = Fixture(
            person: Person() with { IsEnabled = false },
            account: Account() with
            {
                Status = IdentityAccountStatus.Disabled,
                PersonnelDisableHold = true,
                Revision = 8
            },
            existingSessionRevocations: 1,
            existingEvents: ["人员离职"]);

        var result = (await fixture.Core.ExecuteAsync(
            "Person_1",
            new SetPersonLifecycleStatusRequest(false, "重复停用"),
            new PersonLifecycleActor("User_Admin", "trace-retry"))).Dto;

        Assert.Equal(8, result.Account?.Revision);
        Assert.Equal(0, fixture.Transaction.ExecutionCount);
        Assert.Equal(1, fixture.Identity.SessionRevocations);
        Assert.Single(fixture.Identity.Events, "人员离职");
    }

    [Fact]
    public async Task SetStatusAsync_IdentityRevisionConflictRollsBackPersonAndReturnsLifecycleConflict()
    {
        var fixture = Fixture(failureStage: LifecycleFailureStage.IdentityRevisionConflict);

        var exception = await Assert.ThrowsAsync<PersonLifecycleException>(async () =>
            await fixture.Core.ExecuteAsync(
                "Person_1",
                new SetPersonLifecycleStatusRequest(false, "账号并发"),
                new PersonLifecycleActor("User_Admin", "trace-user-conflict")));

        Assert.Equal(PersonLifecycleFailureCodes.Conflict, exception.FailureCode);
        Assert.True(fixture.Repository.Current.IsEnabled);
        Assert.Equal(IdentityAccountStatus.Enabled, fixture.Identity.Account.Status);
    }

    [Fact]
    public async Task SetStatusAsync_BoundLifecycleFailsClosedWhenIdentityIsUnavailable()
    {
        var fixture = Fixture(directoryUnavailable: true);

        var exception = await Assert.ThrowsAsync<PersonLifecycleException>(async () =>
            await fixture.Core.ExecuteAsync(
                "Person_1",
                new SetPersonLifecycleStatusRequest(false, "依赖故障"),
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
            await fixture.Core.ExecuteAsync(
                "Person_1",
                new SetPersonLifecycleStatusRequest(false, "逐阶段故障"),
                new PersonLifecycleActor("User_Admin", $"trace-{stage}")));

        Assert.Equal(personBefore, fixture.Repository.Current);
        Assert.Equal(accountBefore, fixture.Identity.Account);
        Assert.Equal(0, fixture.Identity.SessionRevocations);
        Assert.Empty(fixture.Identity.Events);
    }

    [Fact]
    public async Task PublicAdapter_RoutesToObjectExecutorAndRestoresActorScopeOnRejection()
    {
        var fixture = Fixture();
        var initial = fixture.Repository.Current;
        var invocations = new PersonObjectActionInvocationContext();
        var actor = new PersonLifecycleActor("User_Admin", "adapter-trace");
        var current = new object();
        using var cancellation = new CancellationTokenSource();
        var executor = new RejectingObjectExecutor((objectKey, action, targetId, values, token) =>
        {
            Assert.Equal("organization.person", objectKey);
            Assert.Equal(PersonObjectBehavior.DisableAction, action);
            Assert.Equal("Person_1", targetId);
            Assert.Equal("入口原因", values["Reason"]);
            Assert.Same(actor, invocations.Current<PersonLifecycleActor>());
            Assert.Equal(cancellation.Token, token);
            return StandardCrudMutationResult.Failure(new StandardCrudError
            {
                StatusCode = 409,
                Code = "Organization.Person.ExtensionRejected",
                Message = "扩展拒绝",
                Args = new Dictionary<string, object?> { ["current"] = current }
            });
        });
        var adapter = new PersonLifecycleService(() => executor, invocations);

        var error = await Assert.ThrowsAsync<PersonLifecycleException>(() => adapter.SetStatusAsync(
            "Person_1", new SetPersonLifecycleStatusRequest(false, "入口原因"), actor, cancellation.Token).AsTask());

        Assert.Equal("Organization.Person.ExtensionRejected", error.FailureCode);
        Assert.Equal(409, error.StatusCode);
        Assert.Same(current, error.Current);
        Assert.Null(invocations.Current<PersonLifecycleActor>());
        Assert.Equal(initial, fixture.Repository.Current);
        Assert.Equal(0, fixture.Transaction.ExecutionCount);
        Assert.Equal(1, executor.ExecutionCount);
    }

    // 内部核心的隔离门缝保留事务 fallback 与幂等计数；公开入口另由正式模块集成测试覆盖。
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
        var core = new PersonLifecycleActionCore(
            repository,
            new RecordingAccountDirectory(identity, directoryUnavailable),
            identity,
            identity,
            transaction,
            new EntityAuditValueProvider(new FixedAuditStampProvider("User_Admin", ChangedAt)));
        return new TestFixture(core, repository, identity, transaction, stages);
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
        AccountEid = "User_1",
        BoundAt = ChangedAt.AddDays(-1),
        BoundBy = "User_Admin",
        BindingReason = "入职绑定",
        CreatedBy = "User_Creator",
        CreatedAt = ChangedAt.AddDays(-3),
        UpdatedBy = "User_Creator",
        UpdatedAt = ChangedAt.AddDays(-2)
    };

    private static IdentityAccountSnapshot Account() => new()
    {
        AccountEid = "User_1",
        ECODE = "alice",
        ENAME = "Alice",
        Status = IdentityAccountStatus.Enabled,
        Revision = 7,
        IsPersonnelBound = true,
        PersonnelDisableHold = false
    };

    private sealed class RejectingObjectExecutor(
        Func<string, ObjectOperationAction, string?, IReadOnlyDictionary<string, object?>, CancellationToken, StandardCrudMutationResult> execute)
        : IObjectOperationExecutor
    {
        public int ExecutionCount { get; private set; }
        public bool CanExecute(string objectKey, ObjectOperationAction action) => true;
        public Task<StandardCrudMutationResult> ExecuteAsync(string objectKey, ObjectOperationAction action,
            string? targetId, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.FromResult(execute(objectKey, action, targetId, values, cancellationToken));
        }
    }

    private sealed record TestFixture(
        PersonLifecycleActionCore Core,
        RecordingRepository Repository,
        RecordingIdentityParticipant Identity,
        SnapshotTransactionExecutor Transaction,
        List<string> Stages);

    private sealed class RecordingRepository(PersonUserBindingState initial, LifecycleFailureStage? failureStage, List<string> stages) : IPersonUserBindingRepository
    {
        public PersonUserBindingState Current { get; set; } = initial;

        public ValueTask<PersonUserBindingState?> FindAsync(string personEid, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<PersonUserBindingState?>(Current.PersonEid == personEid ? Current with { } : null);

        public ValueTask<bool> TryBindAsync(string personEid, string accountEid, string reason, EntityAuditValues auditValues, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask SetEnablementAsync(
            string personEid,
            bool isEnabled,
            EntityAuditValues auditValues,
            CancellationToken cancellationToken = default)
        {
            if (failureStage == LifecycleFailureStage.Person)
            {
                throw new InvalidOperationException("person-stage-failed");
            }

            Current = Current with
            {
                IsEnabled = isEnabled,
                UpdatedBy = auditValues.UpdatedBy,
                UpdatedAt = auditValues.UpdatedAt
            };
            stages.Add("person");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingAccountDirectory(RecordingIdentityParticipant identity, bool unavailable) : IIdentityAccountDirectory
    {
        public ValueTask<IdentityAccountSnapshot?> FindAsync(string accountEid, CancellationToken cancellationToken = default)
        {
            if (unavailable)
            {
                throw new TimeoutException("identity-directory-unavailable");
            }

            return ValueTask.FromResult<IdentityAccountSnapshot?>(identity.Account.AccountEid == accountEid ? identity.Account with { } : null);
        }

        public ValueTask<IReadOnlyDictionary<string, IdentityAccountSnapshot>> FindManyAsync(
            IReadOnlyCollection<string> accountEids,
            CancellationToken cancellationToken = default)
        {
            if (unavailable)
            {
                throw new TimeoutException("identity-directory-unavailable");
            }

            var results = accountEids
                .Where(accountEid => string.Equals(identity.Account.AccountEid, accountEid, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(
                    accountEid => accountEid,
                    _ => identity.Account with { },
                    StringComparer.Ordinal);
            return ValueTask.FromResult<IReadOnlyDictionary<string, IdentityAccountSnapshot>>(results);
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

        public ValueTask<int> RevokeAllAsync(AccountAuthenticationSessionsRevocation revocation, CancellationToken cancellationToken = default)
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

    private sealed class FixedAuditStampProvider(string actorId, DateTime utcNow) : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent() => new(actorId, utcNow);
    }
}

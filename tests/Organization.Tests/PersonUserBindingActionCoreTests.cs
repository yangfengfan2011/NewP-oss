using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class PersonUserBindingActionCoreTests
{
    private static readonly DateTime BoundAt = new(2026, 8, 10, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task BindAsync_UsesDirectoryCanonicalIdForPersistenceParticipationAndAliasRetry()
    {
        var fixture = Fixture();
        fixture.Directory.Aliases["user_1"] = "User_1";
        var result = (await fixture.Core.ExecuteAsync("Person_1", new BindPersonUserRequest("user_1", 7, "首次绑定"),
            new PersonUserBindingActor("User_Admin", "canonical"))).Dto;
        Assert.Equal("User_1", result.AccountEid);
        Assert.Equal("User_1", result.Account!.AccountEid);
        Assert.Equal("User_1", fixture.Repository.Current.AccountEid);
        Assert.Equal("User_1", fixture.Identity.LastBinding!.AccountEid);

        var repeated = (await fixture.Core.ExecuteAsync("Person_1", new BindPersonUserRequest("user_1", 0, "不覆盖"),
            new PersonUserBindingActor("User_Other", "retry-alias"))).Dto;
        Assert.Equal("User_1", repeated.AccountEid);
        Assert.Equal("首次绑定", repeated.Reason);
        Assert.Equal(1, fixture.Transaction.ExecutionCount);
        Assert.Single(fixture.Identity.Events);
    }

    [Fact]
    public async Task BindAsync_ExistingBindingRejectsDifferentResolvedTarget()
    {
        var fixture = Fixture(person: Person(accountEid: "User_1"), accounts: [Account("User_1"), Account("User_2")]);
        fixture.Directory.Aliases["user_2"] = "User_2";
        var error = await Assert.ThrowsAsync<PersonUserBindingException>(() => fixture.Core.ExecuteAsync("Person_1",
            new BindPersonUserRequest("user_2", 7, "拒绝改绑"), new PersonUserBindingActor("User_Admin", "conflict-alias")).AsTask());
        Assert.Equal(PersonUserBindingFailureCodes.PersonAlreadyBound, error.FailureCode);
        Assert.Equal("User_1", fixture.Repository.Current.AccountEid);
        Assert.Equal(0, fixture.Transaction.ExecutionCount);
    }

    [Fact]
    public async Task SearchCandidatesAsync_ReturnsAtMostTwentyEligibleEnabledOrDisabledAccounts()
    {
        var accounts = Enumerable.Range(1, 24)
            .Select(index => Account(
                $"User_{index:00}",
                index == 1 ? IdentityAccountStatus.Deleted : index % 2 == 0 ? IdentityAccountStatus.Disabled : IdentityAccountStatus.Enabled,
                isLocalAdministrator: index == 2,
                isPersonnelBound: index == 3))
            .ToArray();
        var fixture = Fixture(accounts: accounts);

        var result = await fixture.Core.SearchCandidatesAsync("Person_1", " user ");

        Assert.Equal(20, result.Count);
        Assert.All(result, candidate => Assert.Contains(candidate.Status, new[] { "Enabled", "Disabled" }));
        Assert.DoesNotContain(result, candidate => candidate.AccountEid is "User_01" or "User_02" or "User_03");
        Assert.Equal("user", fixture.Directory.LastSearch?.Query);
        Assert.Equal(false, fixture.Directory.LastSearch?.IsPersonnelBound);
        Assert.Equal(20, fixture.Directory.LastSearch?.Limit);
    }

    [Fact]
    public async Task BindAsync_RegistersPermanentPersonAndIdentityFactsInOneTransaction()
    {
        var fixture = Fixture();

        var result = (await fixture.Core.ExecuteAsync(
            "Person_1",
            new BindPersonUserRequest("User_1", 7, "  入职账号绑定  "),
            new PersonUserBindingActor("User_Admin", "trace-39"))).Dto;

        Assert.Equal("User_1", result.AccountEid);
        Assert.Equal(BoundAt, result.BoundAt);
        Assert.Equal("User_Admin", result.BoundBy);
        Assert.Equal("入职账号绑定", result.Reason);
        Assert.Equal(1, fixture.Transaction.ExecutionCount);
        Assert.Equal(8, fixture.Identity.Account.Revision);
        Assert.True(fixture.Identity.Account.IsPersonnelBound);
        Assert.Equal("User_1", fixture.Repository.Current.AccountEid);
        Assert.Equal("张三", fixture.Repository.Current.PersonName);
    }

    [Fact]
    public async Task BindAsync_SameCombinationIsIdempotentButDifferentCombinationConflicts()
    {
        var fixture = Fixture(person: Person(accountEid: "User_1"));

        var idempotent = (await fixture.Core.ExecuteAsync(
            "Person_1",
            new BindPersonUserRequest("User_1", 0, "不会覆盖既有原因"),
            new PersonUserBindingActor("User_Other", "trace-repeat"))).Dto;

        Assert.Equal("首次绑定", idempotent.Reason);
        Assert.Equal(0, fixture.Transaction.ExecutionCount);
        Assert.Equal(7, fixture.Identity.Account.Revision);

        var conflict = await Assert.ThrowsAsync<PersonUserBindingException>(async () =>
            await fixture.Core.ExecuteAsync(
                "Person_1",
                new BindPersonUserRequest("User_2", 7, "改绑"),
                new PersonUserBindingActor("User_Admin", "trace-conflict")));
        Assert.Equal(PersonUserBindingFailureCodes.PersonAlreadyBound, conflict.FailureCode);
    }

    [Fact]
    public async Task BindAsync_DisabledPersonIsRejectedBeforeIdentityMutation()
    {
        var fixture = Fixture(person: Person(isEnabled: false));

        var exception = await Assert.ThrowsAsync<PersonUserBindingException>(async () =>
            await fixture.Core.ExecuteAsync(
                "Person_1",
                new BindPersonUserRequest("User_1", 7, "停用人员绑定"),
                new PersonUserBindingActor("User_Admin", "trace-disabled")));

        Assert.Equal(PersonUserBindingFailureCodes.PersonDisabled, exception.FailureCode);
        Assert.False(fixture.Identity.Account.IsPersonnelBound);
        Assert.Null(fixture.Repository.Current.AccountEid);
    }

    [Fact]
    public async Task BindAsync_MapsStableIdentityLifecycleConflictWithoutDependingOnIdentityImplementation()
    {
        var fixture = Fixture(failureStage: BindingFailureStage.IdentityRevisionConflict);

        var exception = await Assert.ThrowsAsync<PersonUserBindingException>(async () =>
            await fixture.Core.ExecuteAsync(
                "Person_1",
                new BindPersonUserRequest("User_1", 7, "并发绑定"),
                new PersonUserBindingActor("User_Admin", "trace-contract-conflict")));

        Assert.Equal(PersonUserBindingFailureCodes.RevisionConflict, exception.FailureCode);
        Assert.Null(fixture.Repository.Current.AccountEid);
    }

    [Theory]
    [InlineData(BindingFailureStage.PersonWrite)]
    [InlineData(BindingFailureStage.IdentityRegistration)]
    [InlineData(BindingFailureStage.Commit)]
    public async Task BindAsync_WhenAnyStageFails_RollsBackPersonIdentityEventAndAudit(BindingFailureStage stage)
    {
        var fixture = Fixture(failureStage: stage);
        var personBefore = fixture.Repository.Current with { };
        var accountBefore = fixture.Identity.Account with { };

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await fixture.Core.ExecuteAsync(
                "Person_1",
                new BindPersonUserRequest("User_1", 7, "故障回滚"),
                new PersonUserBindingActor("User_Admin", "trace-rollback")));

        Assert.Equal(personBefore, fixture.Repository.Current);
        Assert.Equal(accountBefore, fixture.Identity.Account);
        Assert.Empty(fixture.Identity.Events);
    }

    [Fact]
    public async Task PublicAdapter_SearchCandidatesDelegatesToBindingRulesWithoutExecutingMutation()
    {
        var fixture = Fixture();
        var executor = new RejectingObjectExecutor((_, _, _, _, _) => throw new InvalidOperationException("query entered object mutation"));
        var adapter = new PersonUserBindingService(fixture.Core, () => executor, new PersonObjectActionInvocationContext());

        var candidates = await adapter.SearchCandidatesAsync("Person_1", " user ");

        Assert.Equal("User_1", Assert.Single(candidates).AccountEid);
        Assert.Equal("user", fixture.Directory.LastSearch!.Query);
        Assert.Equal(0, executor.ExecutionCount);
        Assert.Equal(0, fixture.Transaction.ExecutionCount);
    }

    [Fact]
    public async Task PublicAdapter_RoutesToObjectExecutorAndRestoresActorScopeOnRejection()
    {
        var fixture = Fixture();
        var initial = fixture.Repository.Current;
        var invocations = new PersonObjectActionInvocationContext();
        var actor = new PersonUserBindingActor("User_Admin", "adapter-trace");
        var current = new object();
        using var cancellation = new CancellationTokenSource();
        var executor = new RejectingObjectExecutor((objectKey, action, targetId, values, token) =>
        {
            Assert.Equal("organization.person", objectKey);
            Assert.Equal(PersonObjectBehavior.BindUserAction, action);
            Assert.Equal("Person_1", targetId);
            Assert.Equal("入口原因", values["Reason"]);
            Assert.Same(actor, invocations.Current<PersonUserBindingActor>());
            Assert.Equal(cancellation.Token, token);
            Assert.Equal("User_1", values["AccountEid"]);
            Assert.Equal(7L, values["ExpectedRevision"]);
            return StandardCrudMutationResult.Failure(new StandardCrudError
            {
                StatusCode = 409,
                Code = "Organization.Person.ExtensionRejected",
                Message = "扩展拒绝",
                Args = new Dictionary<string, object?> { ["current"] = current }
            });
        });
        var adapter = new PersonUserBindingService(fixture.Core, () => executor, invocations);

        var error = await Assert.ThrowsAsync<PersonUserBindingException>(() => adapter.BindAsync(
            "Person_1", new BindPersonUserRequest("User_1", 7, "入口原因"), actor, cancellation.Token).AsTask());

        Assert.Equal("Organization.Person.ExtensionRejected", error.FailureCode);
        Assert.Equal(409, error.StatusCode);
        Assert.Same(current, error.Current);
        Assert.Null(invocations.Current<PersonUserBindingActor>());
        Assert.Equal(initial, fixture.Repository.Current);
        Assert.Equal(0, fixture.Transaction.ExecutionCount);
        Assert.Equal(1, executor.ExecutionCount);
    }

    // 内部核心的隔离门缝保留事务 fallback 与幂等计数；公开入口另由正式模块集成测试覆盖。
    private static TestFixture Fixture(
        PersonUserBindingState? person = null,
        IReadOnlyList<IdentityAccountSnapshot>? accounts = null,
        BindingFailureStage? failureStage = null)
    {
        var repository = new InMemoryPersonUserBindingRepository(person ?? Person(), failureStage);
        var directory = new RecordingAccountDirectory(accounts ?? [Account("User_1")]);
        var identity = new RecordingIdentityParticipant(Account("User_1"), failureStage);
        var transaction = new SnapshotTransactionExecutor(repository, identity, failureStage);
        var core = new PersonUserBindingActionCore(
            repository,
            directory,
            identity,
            transaction,
            new EntityAuditValueProvider(new FixedAuditStampProvider("User_Admin", BoundAt)));
        return new TestFixture(core, repository, directory, identity, transaction);
    }

    private static PersonUserBindingState Person(bool isEnabled = true, string? accountEid = null) => new()
    {
        PersonEid = "Person_1",
        PersonName = "张三",
        IsEnabled = isEnabled,
        AccountEid = accountEid,
        BoundAt = accountEid is null ? null : BoundAt,
        BoundBy = accountEid is null ? null : "User_Admin",
        BindingReason = accountEid is null ? null : "首次绑定",
        CreatedBy = "User_Creator",
        CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedBy = "User_Creator",
        UpdatedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static IdentityAccountSnapshot Account(
        string accountEid,
        IdentityAccountStatus status = IdentityAccountStatus.Enabled,
        bool isLocalAdministrator = false,
        bool isPersonnelBound = false) => new()
    {
        AccountEid = accountEid,
        ECODE = accountEid.ToLowerInvariant(),
        ENAME = $"账号 {accountEid}",
        Status = status,
        Revision = 7,
        IsLocalAdministrator = isLocalAdministrator,
        IsPersonnelBound = isPersonnelBound
    };

    public enum BindingFailureStage
    {
        PersonWrite,
        IdentityRegistration,
        IdentityRevisionConflict,
        Commit
    }

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
        PersonUserBindingActionCore Core,
        InMemoryPersonUserBindingRepository Repository,
        RecordingAccountDirectory Directory,
        RecordingIdentityParticipant Identity,
        SnapshotTransactionExecutor Transaction);

    private sealed class RecordingAccountDirectory(IReadOnlyList<IdentityAccountSnapshot> accounts) : IIdentityAccountDirectory
    {
        public IdentityAccountSearch? LastSearch { get; private set; }
        public Dictionary<string, string> Aliases { get; } = new(StringComparer.Ordinal);

        public ValueTask<IdentityAccountSnapshot?> FindAsync(string accountEid, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(accounts.FirstOrDefault(account => account.AccountEid == Aliases.GetValueOrDefault(accountEid, accountEid)));

        public ValueTask<IReadOnlyDictionary<string, IdentityAccountSnapshot>> FindManyAsync(
            IReadOnlyCollection<string> accountEids,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, IdentityAccountSnapshot>>(
                accounts
                    .Where(account => accountEids.Contains(account.AccountEid, StringComparer.Ordinal))
                    .ToDictionary(account => account.AccountEid, account => account, StringComparer.Ordinal));

        public ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(
            IdentityAccountSearch search,
            CancellationToken cancellationToken = default)
        {
            LastSearch = search;
            return ValueTask.FromResult(accounts);
        }
    }

    private sealed class RecordingIdentityParticipant(
        IdentityAccountSnapshot account,
        BindingFailureStage? failureStage) : IIdentityLifecycleParticipant
    {
        public IdentityAccountSnapshot Account { get; set; } = account;
        public List<string> Events { get; } = [];
        public PersonnelBindingRegistration? LastBinding { get; private set; }

        public ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelBindingAsync(
            PersonnelBindingRegistration registration,
            CancellationToken cancellationToken = default)
        {
            LastBinding = registration;
            if (failureStage == BindingFailureStage.IdentityRegistration)
            {
                throw new InvalidOperationException("identity-registration-failed");
            }

            if (failureStage == BindingFailureStage.IdentityRevisionConflict)
            {
                throw new IdentityLifecycleException(
                    IdentityLifecycleFailureCodes.RevisionConflict,
                    Account);
            }

            Account = Account with { IsPersonnelBound = true, Revision = Account.Revision + 1 };
            Events.Add(registration.Reason);
            return ValueTask.FromResult(new IdentityLifecycleRegistrationResult { Account = Account, Changed = true });
        }

        public ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableAsync(
            PersonnelAccountDisableRegistration registration,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IdentityLifecycleRegistrationResult> RegisterPersonnelDisableHoldReleaseAsync(
            PersonnelDisableHoldReleaseRegistration registration,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class InMemoryPersonUserBindingRepository(
        PersonUserBindingState person,
        BindingFailureStage? failureStage) : IPersonUserBindingRepository
    {
        public PersonUserBindingState Current { get; set; } = person;

        public ValueTask<PersonUserBindingState?> FindAsync(string personEid, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<PersonUserBindingState?>(Current.PersonEid == personEid ? Current with { } : null);

        public ValueTask<bool> TryBindAsync(
            string personEid,
            string accountEid,
            string reason,
            EntityAuditValues auditValues,
            CancellationToken cancellationToken = default)
        {
            if (failureStage == BindingFailureStage.PersonWrite)
            {
                throw new InvalidOperationException("person-write-failed");
            }

            if (Current.PersonEid != personEid || !Current.IsEnabled || Current.AccountEid is not null)
            {
                return ValueTask.FromResult(false);
            }

            Current = Current with
            {
                AccountEid = accountEid,
                BoundAt = auditValues.UpdatedAt,
                BoundBy = auditValues.UpdatedBy,
                BindingReason = reason,
                UpdatedBy = auditValues.UpdatedBy,
                UpdatedAt = auditValues.UpdatedAt
            };
            return ValueTask.FromResult(true);
        }

        public ValueTask SetEnablementAsync(
            string personEid,
            bool isEnabled,
            EntityAuditValues auditValues,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SnapshotTransactionExecutor(
        InMemoryPersonUserBindingRepository repository,
        RecordingIdentityParticipant identity,
        BindingFailureStage? failureStage) : IPlatformTransactionExecutor
    {
        public int ExecutionCount { get; private set; }

        public async ValueTask<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, ValueTask<TResult>> registerChanges,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            var person = repository.Current with { };
            var account = identity.Account with { };
            var events = identity.Events.ToArray();
            try
            {
                var result = await registerChanges(cancellationToken);
                if (failureStage == BindingFailureStage.Commit)
                {
                    throw new InvalidOperationException("commit-failed");
                }

                return result;
            }
            catch
            {
                repository.Current = person;
                identity.Account = account;
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

using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Repositories;
using ICP.Platform.Persistence.Abstractions;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace ICP.Platform.Identity.Tests;

public sealed class IdentityAccountApplicationServiceTests
{
    [Fact]
    public void TemporaryCredentialIssuer_UsesSharedValueOnlyWhenDevelopmentExplicitlyEnablesIt()
    {
        Assert.Equal(
            "123456",
            new DefaultTemporaryCredentialIssuer("Development", useSharedDevelopmentCredential: true).Issue());

        Assert.Throws<InvalidOperationException>(() =>
            new DefaultTemporaryCredentialIssuer("Production", useSharedDevelopmentCredential: true).Issue());
        var productionIssuer = new DefaultTemporaryCredentialIssuer(
            "Production",
            useSharedDevelopmentCredential: false);
        var first = productionIssuer.Issue();
        var second = productionIssuer.Issue();
        Assert.NotEqual(first, second);
        Assert.DoesNotContain("123456", new[] { first, second });
    }

    [Fact]
    public async Task CreateAsync_RejectsActorKindOutsideSecurityEventContract()
    {
        var fixture = new Fixture();
        var invalidActor = fixture.Actor with { ActorKind = (IdentitySecurityActorKind)999 };

        var error = await Assert.ThrowsAsync<IdentityApplicationException>(() => fixture.Service.CreateAsync(
            new CreateIdentityAccountCommand("alice", "Alice", invalidActor)).AsTask());

        Assert.Equal(IdentityFailureCodes.InvalidActor, error.FailureCode);
        Assert.Empty(fixture.Repository.SecurityEvents);
    }

    [Fact]
    public async Task CreateAsync_NormalizesAccountCodeAndAtomicallyIssuesTemporaryCredential()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CreateAsync(new CreateIdentityAccountCommand(
            "  ａlice  ",
            "  艾丽丝  ",
            fixture.Actor));

        Assert.Equal("alice", result.Account.ECODE);
        Assert.Equal("艾丽丝", result.Account.ENAME);
        Assert.Equal(IdentityAccountStatus.Enabled, result.Account.Status);
        Assert.Equal(1, result.Account.Revision);
        Assert.Equal("temporary-secret", result.TemporaryCredential);
        Assert.True(fixture.Repository.VerifyPassword(result.Account.UserEid, "temporary-secret"));
        Assert.Single(fixture.Repository.SecurityEvents, item =>
            item.TargetUserEID == result.Account.UserEid
            && item.EventType == IdentitySecurityEventTypes.AccountCreated
            && !item.Reason.Contains("temporary-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_WhenSecurityEventRegistrationFails_RollsBackAccountAndCredential()
    {
        var fixture = new Fixture();
        fixture.Repository.FailOnSecurityEvent = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CreateAsync(
            new CreateIdentityAccountCommand("alice", "Alice", fixture.Actor)).AsTask());

        Assert.Empty(fixture.Repository.Users);
        Assert.Empty(fixture.Repository.Credentials);
        Assert.Empty(fixture.Repository.SecurityEvents);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("bad!")]
    public async Task CreateAsync_WithInvalidAccountCode_RejectsBeforeWriting(string accountCode)
    {
        var fixture = new Fixture();

        var error = await Assert.ThrowsAsync<IdentityApplicationException>(() => fixture.Service.CreateAsync(
            new CreateIdentityAccountCommand(accountCode, "Valid name", fixture.Actor)).AsTask());

        Assert.Equal(IdentityFailureCodes.InvalidAccountCode, error.FailureCode);
        Assert.Empty(fixture.Repository.Users);
    }

    [Fact]
    public async Task CreateAsync_WithCaseEquivalentReservedCode_ReturnsStableConflict()
    {
        var fixture = new Fixture();
        await fixture.Service.CreateAsync(new CreateIdentityAccountCommand("Alice", "First", fixture.Actor));

        var error = await Assert.ThrowsAsync<IdentityApplicationException>(() => fixture.Service.CreateAsync(
            new CreateIdentityAccountCommand("ＡＬＩＣＥ", "Second", fixture.Actor)).AsTask());

        Assert.Equal(IdentityFailureCodes.AccountCodeConflict, error.FailureCode);
        Assert.Single(fixture.Repository.Users);
    }

    [Fact]
    public async Task UpdateNameAsync_WithExpectedRevision_ChangesOnlyMutableNameAndAudit()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync();
        var original = fixture.Repository.Users.Single();

        var updated = await fixture.Service.UpdateNameAsync(new UpdateIdentityAccountNameCommand(
            created.Account.UserEid,
            "  新显示名  ",
            created.Account.Revision,
            fixture.Actor));

        Assert.Equal("新显示名", updated.ENAME);
        Assert.Equal(created.Account.ECODE, updated.ECODE);
        Assert.Equal(2, updated.Revision);
        var stored = fixture.Repository.Users.Single();
        Assert.Equal(original.CreatedAt, stored.CreatedAt);
        Assert.Equal(original.CreatedBy, stored.CreatedBy);
        Assert.Single(fixture.Repository.SecurityEvents, item => item.EventType == IdentitySecurityEventTypes.DisplayNameChanged);
    }

    [Fact]
    public async Task UpdateNameAsync_WithStaleRevision_ReturnsLatestSecretFreeSnapshot()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync();

        var error = await Assert.ThrowsAsync<IdentityApplicationException>(() => fixture.Service.UpdateNameAsync(
            new UpdateIdentityAccountNameCommand(created.Account.UserEid, "Changed", 0, fixture.Actor)).AsTask());

        Assert.Equal(IdentityFailureCodes.RevisionConflict, error.FailureCode);
        Assert.Equal(created.Account, error.LatestAccount);
        Assert.DoesNotContain("temporary-secret", error.ToString(), StringComparison.Ordinal);
        Assert.Single(fixture.Repository.SecurityEvents);
    }

    [Fact]
    public async Task SetStatusAsync_WhenTargetAlreadyReached_ConvergesWithoutRevisionOrEvent()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync();

        var result = await fixture.Service.SetStatusAsync(new SetIdentityAccountStatusCommand(
            created.Account.UserEid,
            IdentityAccountStatus.Enabled,
            0,
            "retry",
            fixture.Actor));

        Assert.Equal(created.Account, result);
        Assert.Single(fixture.Repository.SecurityEvents);
        Assert.Empty(fixture.Sessions.Revocations);
    }

    [Fact]
    public async Task SetStatusAsync_DisablesAndRevokesSessionsInSameTransaction()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync();

        var disabled = await fixture.Service.SetStatusAsync(new SetIdentityAccountStatusCommand(
            created.Account.UserEid,
            IdentityAccountStatus.Disabled,
            created.Account.Revision,
            "  人工停用  ",
            fixture.Actor));

        Assert.Equal(IdentityAccountStatus.Disabled, disabled.Status);
        Assert.Equal(2, disabled.Revision);
        Assert.Single(fixture.Sessions.Revocations, item => item.Reason == "人工停用");
        Assert.Single(fixture.Repository.SecurityEvents, item =>
            item.EventType == IdentitySecurityEventTypes.AccountDisabled
            && item.Reason == "人工停用");
    }

    [Fact]
    public async Task SetStatusAsync_WhenRevocationFails_RollsBackDisableAndEvent()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync();
        fixture.Sessions.FailOnRevoke = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SetStatusAsync(
            new SetIdentityAccountStatusCommand(
                created.Account.UserEid,
                IdentityAccountStatus.Disabled,
                created.Account.Revision,
                "disable",
                fixture.Actor)).AsTask());

        Assert.Equal(IdentityAccountStatus.Enabled, fixture.Repository.Users.Single().Status);
        Assert.Single(fixture.Repository.SecurityEvents);
        Assert.Empty(fixture.Sessions.Revocations);
    }

    [Fact]
    public async Task SetStatusAsync_RejectsProtectedAdministratorAndPersonnelHold()
    {
        var administratorFixture = new Fixture();
        var administrator = await administratorFixture.CreateAsync();
        administratorFixture.Repository.Users.Single().IsLocalAdministrator = true;

        var administratorError = await Assert.ThrowsAsync<IdentityApplicationException>(() =>
            administratorFixture.Service.SetStatusAsync(new SetIdentityAccountStatusCommand(
                administrator.Account.UserEid,
                IdentityAccountStatus.Disabled,
                administrator.Account.Revision,
                "disable",
                administratorFixture.Actor)).AsTask());
        Assert.Equal(IdentityFailureCodes.LocalAdministratorProtected, administratorError.FailureCode);

        var heldFixture = new Fixture();
        var held = await heldFixture.CreateAsync();
        heldFixture.Repository.Users.Single().Status = IdentityAccountStatus.Disabled;
        heldFixture.Repository.Users.Single().PersonnelDisableHold = true;
        var holdError = await Assert.ThrowsAsync<IdentityApplicationException>(() =>
            heldFixture.Service.SetStatusAsync(new SetIdentityAccountStatusCommand(
                held.Account.UserEid,
                IdentityAccountStatus.Enabled,
                held.Account.Revision,
                "enable",
                heldFixture.Actor)).AsTask());
        Assert.Equal(IdentityFailureCodes.PersonnelDisableHold, holdError.FailureCode);
    }

    [Fact]
    public async Task ResetCredentialAsync_ReturnsNewSecretOnceAndKeepsDisabledStatus()
    {
        var fixture = new Fixture("initial-secret", "new-temporary-secret");
        var created = await fixture.CreateAsync();
        fixture.Repository.Users.Single().Status = IdentityAccountStatus.Disabled;

        var reset = await fixture.Service.ResetCredentialAsync(new ResetIdentityCredentialCommand(
            created.Account.UserEid,
            created.Account.Revision,
            "reset compromised password",
            fixture.Actor));

        Assert.Equal(IdentityAccountStatus.Disabled, reset.Account.Status);
        Assert.Equal(2, reset.Account.Revision);
        Assert.Equal("new-temporary-secret", reset.TemporaryCredential);
        Assert.True(fixture.Repository.VerifyPassword(created.Account.UserEid, "new-temporary-secret"));
        Assert.True(fixture.Repository.Credentials.Single().IsTemporary);
        Assert.Single(fixture.Sessions.Revocations);
        Assert.DoesNotContain("new-temporary-secret", fixture.Repository.SecurityEvents.Last().Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetCredentialAsync_WhenRevocationFails_RollsBackHashRevisionAndEvent()
    {
        var fixture = new Fixture("initial-secret", "new-temporary-secret");
        var created = await fixture.CreateAsync();
        fixture.Sessions.FailOnRevoke = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ResetCredentialAsync(
            new ResetIdentityCredentialCommand(
                created.Account.UserEid,
                created.Account.Revision,
                "reset",
                fixture.Actor)).AsTask());

        Assert.True(fixture.Repository.VerifyPassword(created.Account.UserEid, "initial-secret"));
        Assert.False(fixture.Repository.VerifyPassword(created.Account.UserEid, "new-temporary-secret"));
        Assert.Equal(1, fixture.Repository.Users.Single().Revision);
        Assert.Single(fixture.Repository.SecurityEvents);
    }

    [Fact]
    public async Task SecurityCommands_WithStaleRevision_ReturnStableLatestSnapshotWithoutSideEffects()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync();

        var statusError = await Assert.ThrowsAsync<IdentityApplicationException>(() => fixture.Service.SetStatusAsync(
            new SetIdentityAccountStatusCommand(
                created.Account.UserEid,
                IdentityAccountStatus.Disabled,
                0,
                "disable",
                fixture.Actor)).AsTask());
        var resetError = await Assert.ThrowsAsync<IdentityApplicationException>(() => fixture.Service.ResetCredentialAsync(
            new ResetIdentityCredentialCommand(created.Account.UserEid, 0, "reset", fixture.Actor)).AsTask());
        var deleteError = await Assert.ThrowsAsync<IdentityApplicationException>(() => fixture.Service.DeleteAsync(
            new DeleteIdentityAccountCommand(created.Account.UserEid, 0, "delete", fixture.Actor)).AsTask());

        Assert.All(new[] { statusError, resetError, deleteError }, error =>
        {
            Assert.Equal(IdentityFailureCodes.RevisionConflict, error.FailureCode);
            Assert.Equal(created.Account, error.LatestAccount);
        });
        Assert.Empty(fixture.Sessions.Revocations);
        Assert.Single(fixture.Repository.SecurityEvents);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("has space1")]
    [InlineData("123456")]
    public async Task ChangePasswordAsync_RejectsInvalidPermanentPassword(string password)
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync();

        var error = await Assert.ThrowsAsync<IdentityApplicationException>(() => fixture.Service.ChangePasswordAsync(
            new ChangeIdentityPasswordCommand(
                created.Account.UserEid,
                "temporary-secret",
                password,
                created.Account.Revision,
                fixture.Actor)).AsTask());

        Assert.Equal(IdentityFailureCodes.InvalidPassword, error.FailureCode);
    }

    [Fact]
    public async Task ChangePasswordAsync_RejectsUnchangedAndPersistsValidOriginalUnicodeInput()
    {
        var fixture = new Fixture("12345678");
        var created = await fixture.CreateAsync();

        var unchanged = await Assert.ThrowsAsync<IdentityApplicationException>(() => fixture.Service.ChangePasswordAsync(
            new ChangeIdentityPasswordCommand(
                created.Account.UserEid,
                "12345678",
                "12345678",
                created.Account.Revision,
                fixture.Actor)).AsTask());
        Assert.Equal(IdentityFailureCodes.CredentialUnchanged, unchanged.FailureCode);

        var updated = await fixture.Service.ChangePasswordAsync(new ChangeIdentityPasswordCommand(
            created.Account.UserEid,
            "12345678",
            "密码🔐Abc123",
            created.Account.Revision,
            fixture.Actor));

        Assert.Equal(2, updated.Revision);
        Assert.False(fixture.Repository.Credentials.Single().IsTemporary);
        Assert.True(fixture.Repository.VerifyPassword(created.Account.UserEid, "密码🔐Abc123"));
        Assert.Single(fixture.Sessions.Revocations);
    }

    [Fact]
    public async Task ChangePasswordAsync_UsesUnicodeRuneBoundariesAt64And65()
    {
        var acceptedFixture = new Fixture("initial-secret");
        var acceptedAccount = await acceptedFixture.CreateAsync();
        var acceptedPassword = new string('密', 64);

        var changed = await acceptedFixture.Service.ChangePasswordAsync(new ChangeIdentityPasswordCommand(
            acceptedAccount.Account.UserEid,
            "initial-secret",
            acceptedPassword,
            acceptedAccount.Account.Revision,
            acceptedFixture.Actor));

        Assert.Equal(2, changed.Revision);
        Assert.True(acceptedFixture.Repository.VerifyPassword(changed.UserEid, acceptedPassword));

        var rejectedFixture = new Fixture("initial-secret");
        var rejectedAccount = await rejectedFixture.CreateAsync();
        var error = await Assert.ThrowsAsync<IdentityApplicationException>(() =>
            rejectedFixture.Service.ChangePasswordAsync(new ChangeIdentityPasswordCommand(
                rejectedAccount.Account.UserEid,
                "initial-secret",
                new string('密', 65),
                rejectedAccount.Account.Revision,
                rejectedFixture.Actor)).AsTask());
        Assert.Equal(IdentityFailureCodes.InvalidPassword, error.FailureCode);
    }

    [Fact]
    public async Task DeleteAsync_PreservesTombstoneAndCodeWhileRemovingCredentialAndRevokingSessions()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync();

        var deleted = await fixture.Service.DeleteAsync(new DeleteIdentityAccountCommand(
            created.Account.UserEid,
            created.Account.Revision,
            "account retired",
            fixture.Actor));

        Assert.Equal(IdentityAccountStatus.Deleted, deleted.Status);
        Assert.Equal(created.Account.ECODE, deleted.ECODE);
        Assert.Empty(fixture.Repository.Credentials);
        Assert.Single(fixture.Sessions.Revocations);
        Assert.Single(fixture.Repository.SecurityEvents, item => item.EventType == IdentitySecurityEventTypes.AccountDeleted);

        var retried = await fixture.Service.DeleteAsync(new DeleteIdentityAccountCommand(
            created.Account.UserEid,
            0,
            "retry",
            fixture.Actor));
        Assert.Equal(deleted, retried);
        Assert.Single(fixture.Sessions.Revocations);

        var conflict = await Assert.ThrowsAsync<IdentityApplicationException>(() => fixture.Service.CreateAsync(
            new CreateIdentityAccountCommand(created.Account.ECODE, "replacement", fixture.Actor)).AsTask());
        Assert.Equal(IdentityFailureCodes.AccountCodeConflict, conflict.FailureCode);
    }

    [Fact]
    public async Task DeleteAndReset_RejectLocalAdministrator_AndDeleteRejectsPersonnelBoundAccount()
    {
        var administratorFixture = new Fixture();
        var administrator = await administratorFixture.CreateAsync();
        administratorFixture.Repository.Users.Single().IsLocalAdministrator = true;

        var deleteAdministratorError = await Assert.ThrowsAsync<IdentityApplicationException>(() =>
            administratorFixture.Service.DeleteAsync(new DeleteIdentityAccountCommand(
                administrator.Account.UserEid,
                administrator.Account.Revision,
                "delete",
                administratorFixture.Actor)).AsTask());
        var resetAdministratorError = await Assert.ThrowsAsync<IdentityApplicationException>(() =>
            administratorFixture.Service.ResetCredentialAsync(new ResetIdentityCredentialCommand(
                administrator.Account.UserEid,
                administrator.Account.Revision,
                "reset",
                administratorFixture.Actor)).AsTask());

        Assert.Equal(IdentityFailureCodes.LocalAdministratorProtected, deleteAdministratorError.FailureCode);
        Assert.Equal(IdentityFailureCodes.LocalAdministratorProtected, resetAdministratorError.FailureCode);

        var fixture = new Fixture();
        var created = await fixture.CreateAsync();
        fixture.Repository.Users.Single().IsPersonnelBound = true;

        var boundError = await Assert.ThrowsAsync<IdentityApplicationException>(() => fixture.Service.DeleteAsync(
            new DeleteIdentityAccountCommand(created.Account.UserEid, created.Account.Revision, "delete", fixture.Actor)).AsTask());

        Assert.Equal(IdentityFailureCodes.PersonnelBound, boundError.FailureCode);
        Assert.NotEmpty(fixture.Repository.Credentials);
    }

    [Fact]
    public async Task AccountDirectory_ExcludesDeletedByDefaultAndAllowsExplicitTombstoneFilter()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync();
        await fixture.Service.DeleteAsync(new DeleteIdentityAccountCommand(
            created.Account.UserEid,
            created.Account.Revision,
            "retired",
            fixture.Actor));
        IIdentityAccountDirectory directory = new IdentityAccountDirectory(fixture.Repository);

        var defaultResults = await directory.SearchAsync(new IdentityAccountSearch { Query = "ali" });
        var deletedResults = await directory.SearchAsync(new IdentityAccountSearch
        {
            Status = IdentityAccountStatus.Deleted
        });

        Assert.Empty(defaultResults);
        Assert.Single(deletedResults);
        Assert.Equal(IdentityAccountStatus.Deleted, deletedResults[0].Status);
    }

    [Fact]
    public async Task LifecycleParticipant_RegistersPermanentPersonnelBindingAndConvergesRetry()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync();
        var registration = new PersonnelBindingRegistration
        {
            PersonEid = "Person_1",
            UserEid = created.Account.UserEid,
            ExpectedRevision = created.Account.Revision,
            Reason = "bind person",
            ActorId = fixture.Actor.ActorId,
            TraceId = fixture.Actor.TraceId
        };

        var changed = await fixture.Transaction.ExecuteAsync(token =>
            fixture.Participant.RegisterPersonnelBindingAsync(registration, token));
        var retried = await fixture.Transaction.ExecuteAsync(token =>
            fixture.Participant.RegisterPersonnelBindingAsync(registration with { ExpectedRevision = 0 }, token));

        Assert.True(changed.Changed);
        Assert.True(changed.Account.IsPersonnelBound);
        Assert.False(retried.Changed);
        Assert.Equal(changed.Account, retried.Account);
        Assert.Single(fixture.Repository.SecurityEvents, item => item.EventType == IdentitySecurityEventTypes.PersonnelBound);
    }

    [Fact]
    public async Task LifecycleParticipant_RejectsAdministratorBinding()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync();
        fixture.Repository.Users.Single().IsLocalAdministrator = true;

        var error = await Assert.ThrowsAsync<IdentityApplicationException>(() => fixture.Transaction.ExecuteAsync(token =>
            fixture.Participant.RegisterPersonnelBindingAsync(new PersonnelBindingRegistration
            {
                PersonEid = "Person_1",
                UserEid = created.Account.UserEid,
                ExpectedRevision = created.Account.Revision,
                Reason = "bind person",
                ActorId = fixture.Actor.ActorId,
                TraceId = fixture.Actor.TraceId
            }, token)).AsTask());

        Assert.Equal(IdentityFailureCodes.LocalAdministratorProtected, error.FailureCode);
    }

    [Fact]
    public async Task LifecycleParticipant_DisablesWithHoldThenReleasesHoldWithoutEnabling()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync();
        var disabled = await fixture.Transaction.ExecuteAsync(token =>
            fixture.Participant.RegisterPersonnelDisableAsync(new PersonnelAccountDisableRegistration
            {
                UserEid = created.Account.UserEid,
                ExpectedRevision = created.Account.Revision,
                Reason = "person disabled",
                ActorId = fixture.Actor.ActorId,
                TraceId = fixture.Actor.TraceId
            }, token));

        var released = await fixture.Transaction.ExecuteAsync(token =>
            fixture.Participant.RegisterPersonnelDisableHoldReleaseAsync(new PersonnelDisableHoldReleaseRegistration
            {
                UserEid = created.Account.UserEid,
                ExpectedRevision = disabled.Account.Revision,
                ActorId = fixture.Actor.ActorId,
                TraceId = fixture.Actor.TraceId
            }, token));

        Assert.Equal(IdentityAccountStatus.Disabled, disabled.Account.Status);
        Assert.True(disabled.Account.PersonnelDisableHold);
        Assert.Single(fixture.Sessions.Revocations);
        Assert.Equal(IdentityAccountStatus.Disabled, released.Account.Status);
        Assert.False(released.Account.PersonnelDisableHold);
        Assert.Equal(3, released.Account.Revision);
    }

    private sealed class Fixture
    {
        public Fixture(params string[] temporaryCredentials)
        {
            Repository = new InMemoryIdentityRepository();
            Sessions = new RecordingSessionRegistry();
            Transaction = new SnapshotTransactionExecutor(Repository, Sessions);
            Service = new IdentityAccountApplicationService(
                Repository,
                Transaction,
                Sessions,
                new PasswordHasher<User>(),
                new QueueTemporaryCredentialIssuer(
                    temporaryCredentials.Length == 0 ? ["temporary-secret"] : temporaryCredentials),
                new SequenceObjectIdGenerator(),
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero)));
            Participant = new IdentityLifecycleParticipant(
                Repository,
                Sessions,
                new SequenceObjectIdGenerator(),
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero)));
        }

        public IdentityActorContext Actor { get; } = new(
            "User_actor",
            IdentitySecurityActorKind.User,
            "trace-32");

        public InMemoryIdentityRepository Repository { get; }

        public RecordingSessionRegistry Sessions { get; }

        internal SnapshotTransactionExecutor Transaction { get; }

        public IdentityAccountApplicationService Service { get; }

        public IdentityLifecycleParticipant Participant { get; }

        public ValueTask<IdentityAccountCreationResult> CreateAsync() => Service.CreateAsync(
            new CreateIdentityAccountCommand("alice", "Alice", Actor));
    }

    private sealed class QueueTemporaryCredentialIssuer(IEnumerable<string> credentials) : ITemporaryCredentialIssuer
    {
        private readonly Queue<string> _credentials = new(credentials);

        public string Issue()
        {
            if (_credentials.Count > 1)
            {
                return _credentials.Dequeue();
            }

            return _credentials.Peek();
        }
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

    internal sealed class SnapshotTransactionExecutor(
        InMemoryIdentityRepository repository,
        RecordingSessionRegistry sessions) : IPlatformTransactionExecutor
    {
        public async ValueTask<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, ValueTask<TResult>> registerChanges,
            CancellationToken cancellationToken = default)
        {
            var repositorySnapshot = repository.CreateSnapshot();
            var sessionsSnapshot = sessions.CreateSnapshot();
            try
            {
                return await registerChanges(cancellationToken);
            }
            catch
            {
                repository.Restore(repositorySnapshot);
                sessions.Restore(sessionsSnapshot);
                throw;
            }
        }
    }

    internal sealed class RecordingSessionRegistry : IAuthenticationSessionRegistry
    {
        public List<UserAuthenticationSessionsRevocation> Revocations { get; } = [];

        public bool FailOnRevoke { get; set; }

        public object CreateSnapshot() => Revocations.ToArray();

        public void Restore(object snapshot)
        {
            Revocations.Clear();
            Revocations.AddRange((UserAuthenticationSessionsRevocation[])snapshot);
        }

        public ValueTask<AuthenticationSessionSnapshot> RegisterAsync(
            AuthenticationSessionRegistration registration,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<AuthenticationSessionValidation> ValidateAsync(
            string credentialDigest,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> RevokeCurrentAsync(
            AuthenticationSessionRevocation revocation,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<int> RevokeAllAsync(
            UserAuthenticationSessionsRevocation revocation,
            CancellationToken cancellationToken = default)
        {
            if (FailOnRevoke)
            {
                throw new InvalidOperationException("injected session failure");
            }

            Revocations.Add(revocation);
            return ValueTask.FromResult(1);
        }
    }
}

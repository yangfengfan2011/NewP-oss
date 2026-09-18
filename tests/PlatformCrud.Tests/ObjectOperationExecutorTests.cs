using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.Transactions;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class ObjectOperationExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_UsesDeclaredModelAndOneAuditOperationScope()
    {
        var transaction = new RecordingTransactionExecutor();
        var stamps = new CountingStampProvider();
        var auditValues = new EntityAuditValueProvider(stamps);
        var behavior = new RecordingBehavior(PersonDefinition(), StandardCrudMutationResult.Success());
        var executor = new ObjectOperationExecutor([behavior], auditValues, transaction);

        var context = new ObjectOperationContext(
            "organization.person",
            ObjectOperationAction.Create,
            null,
            new Dictionary<string, object?> { ["PersonNo"] = "P-001" });
        var result = await executor.ExecuteAsync(context.ObjectKey, context.Action, context.TargetId, context.Values);

        Assert.True(result.Succeeded);
        Assert.Equal(["Organization/Person"], transaction.LastModelKeys);
        Assert.Equal(1, transaction.Executions);
        Assert.Equal(1, stamps.Reads);
        Assert.NotSame(context, behavior.LastContext);
        Assert.Equal("P-001", behavior.LastContext!.Values["PersonNo"]);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBehaviorRejects_ReturnsFailureFromInsideTransaction()
    {
        var error = new StandardCrudError
        {
            StatusCode = 400,
            Code = "Organization.Person.NameRequired",
            Message = "人员姓名不能为空。"
        };
        var transaction = new RecordingTransactionExecutor();
        var executor = new ObjectOperationExecutor(
            [new RecordingBehavior(PersonDefinition(), StandardCrudMutationResult.Failure(error))],
            new EntityAuditValueProvider(new CountingStampProvider()),
            transaction);

        var result = await executor.ExecuteAsync(
            "organization.person",
            ObjectOperationAction.Create,
            null,
            new Dictionary<string, object?>());

        Assert.False(result.Succeeded);
        Assert.Same(error, result.Error);
        Assert.True(transaction.DelegateFailed);
    }

    [Fact]
    public void Constructor_WhenOpenActionIsUnsupported_RejectsRegistration()
    {
        var invalid = new ObjectBehaviorDefinition(
            "organization.person",
            "Organization",
            "Organization/Person",
            [ObjectOperationAction.Create],
            [ObjectOperationAction.Update]);

        var exception = Assert.Throws<InvalidOperationException>(() => new ObjectOperationExecutor(
            [new RecordingBehavior(invalid, StandardCrudMutationResult.Success())],
            new EntityAuditValueProvider(new CountingStampProvider()),
            new RecordingTransactionExecutor()));

        Assert.Equal("OBJECT_OPERATION_OPEN_ACTION_UNSUPPORTED: organization.person/update", exception.Message);
    }

    [Fact]
    public void Constructor_WhenBehaviorHasNoTransactionExecutor_FailsClosed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new ObjectOperationExecutor(
            [new RecordingBehavior(PersonDefinition(), StandardCrudMutationResult.Success())],
            new EntityAuditValueProvider(new CountingStampProvider())));

        Assert.Equal("OBJECT_OPERATION_TRANSACTION_EXECUTOR_REQUIRED", exception.Message);
    }

    [Theory]
    [InlineData(true, false, "Persistence.CommittedCacheUnavailable")]
    [InlineData(false, true, "Persistence.CommitOutcomeUnknown")]
    public async Task ExecuteAsync_WhenTransactionReportsCommitState_PreservesStableFailure(
        bool databaseCommitted,
        bool commitOutcomeUnknown,
        string expectedCode)
    {
        var executor = new ObjectOperationExecutor(
            [new RecordingBehavior(PersonDefinition(), StandardCrudMutationResult.Success())],
            new EntityAuditValueProvider(new CountingStampProvider()),
            new FailingTransactionExecutor(new PlatformCacheUnavailableException(
                "Organization",
                databaseCommitted,
                new InvalidOperationException("injected"),
                commitOutcomeUnknown)));

        var result = await executor.ExecuteAsync(
            "organization.person",
            ObjectOperationAction.Create,
            null,
            new Dictionary<string, object?>());

        Assert.False(result.Succeeded);
        Assert.Equal(503, result.Error?.StatusCode);
        Assert.Equal(expectedCode, result.Error?.Code);
    }

    private static ObjectBehaviorDefinition PersonDefinition() => new(
        "organization.person",
        "Organization",
        "Organization/Person",
        [ObjectOperationAction.Create],
        [ObjectOperationAction.Create]);

    private sealed class RecordingBehavior(
        ObjectBehaviorDefinition definition,
        StandardCrudMutationResult result) : ObjectBehaviorBase(definition)
    {
        public ObjectOperationContext? LastContext { get; private set; }

        protected override ValueTask<StandardCrudMutationResult> CreateAsync(
            ObjectOperationContext context,
            CancellationToken cancellationToken)
        {
            LastContext = context;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingTransactionExecutor : IPlatformTransactionExecutor
    {
        public IReadOnlyCollection<string>? LastModelKeys { get; private set; }
        public int Executions { get; private set; }
        public bool DelegateFailed { get; private set; }

        public ValueTask<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, ValueTask<TResult>> registerChanges,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync([], registerChanges, cancellationToken);

        public async ValueTask<TResult> ExecuteAsync<TResult>(
            IReadOnlyCollection<string> modelKeys,
            Func<CancellationToken, ValueTask<TResult>> registerChanges,
            CancellationToken cancellationToken = default)
        {
            LastModelKeys = modelKeys;
            Executions++;
            try
            {
                return await registerChanges(cancellationToken);
            }
            catch
            {
                DelegateFailed = true;
                throw;
            }
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class FailingTransactionExecutor(Exception exception) : IPlatformTransactionExecutor
    {
        public ValueTask<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, ValueTask<TResult>> registerChanges,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<TResult>(exception);

        public ValueTask<TResult> ExecuteAsync<TResult>(
            IReadOnlyCollection<string> modelKeys,
            Func<CancellationToken, ValueTask<TResult>> registerChanges,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<TResult>(exception);
    }

    private sealed class CountingStampProvider : IEntityAuditStampProvider
    {
        public int Reads { get; private set; }

        public EntityAuditStamp GetCurrent()
        {
            Reads++;
            return new EntityAuditStamp("actor", new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc));
        }
    }
}

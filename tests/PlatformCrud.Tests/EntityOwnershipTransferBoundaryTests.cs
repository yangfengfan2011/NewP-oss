using System.Text.Json;
using Xunit.Abstractions;
using ICP.Platform.Authorization.Contracts;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Ownership;
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Transactions;
using ICP.Platform.Persistence.Ownership;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class EntityOwnershipTransferBoundaryTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SameBoundary_CommitsOnce_AndEscapedStateCannotCommitInALaterTransaction()
    {
        await using var fixture = await Fixture.CreateAsync();
        EntityOwnershipTransferState? escaped = null;
        var result = await fixture.Transfer.WithinTransferAsync([Fixture.Target], "recipient", async (state, token) =>
        {
            escaped = state;
            Assert.Equal("actor", Assert.Single(state.Objects).Before["OwnerId"]);
            var committed = await fixture.Transfer.CommitAsync(state, token);
            var repeated = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Transfer.CommitAsync(state, token));
            Assert.Equal("ENTITY_OWNERSHIP_TRANSFER_STATE_INVALID", repeated.Message);
            return committed;
        });
        Assert.Equal(1, result.Changed);
        await fixture.Transactions.ExecuteAsync(async token =>
        {
            var expired = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Transfer.CommitAsync(escaped!, token));
            Assert.Equal("ENTITY_OWNERSHIP_TRANSFER_STATE_INVALID", expired.Message);
            return true;
        });
        Assert.Equal(1, fixture.Accounts.Reads);
        Assert.Equal(1, fixture.Receiver.Received);
        Assert.Single(fixture.Audit.Changes);
    }

    [Fact]
    public async Task PreparationState_CannotCommitOrReadTheRecipientDirectory()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Transfer.WithinPreparationAsync([Fixture.Target], async (state, token) =>
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Transfer.CommitAsync(state, token));
            Assert.Equal("ENTITY_OWNERSHIP_TRANSFER_STATE_INVALID", failure.Message);
            return true;
        });
        Assert.Equal(0, fixture.Accounts.Reads);
        Assert.Equal(0, fixture.Receiver.Received);
        Assert.Empty(fixture.Audit.Changes);
    }

    [Fact]
    public async Task FailedBoundary_ExpiresBeforeTheNextTransactionAndNeverStartsReceiving()
    {
        await using var fixture = await Fixture.CreateAsync();
        EntityOwnershipTransferState? escaped = null;
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Transfer.WithinTransferAsync<bool>([Fixture.Target], "recipient",
            (state, _) => { escaped = state; throw new OperationCanceledException(); }));
        await fixture.Transactions.ExecuteAsync(async token =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Transfer.CommitAsync(escaped!, token));
            return true;
        });
        Assert.Equal(0, fixture.Accounts.Reads);
        Assert.Equal(0, fixture.Receiver.Received);
        Assert.Empty(fixture.Audit.Changes);
    }

    [Fact]
    public async Task InitializationState_IsOneShot_AndStatusOrEscapedStatesCannotWrite()
    {
        await using var fixture = await Fixture.CreateAsync();
        EntityOwnershipInitializationState? escaped = null;
        await fixture.Maintenance.WithinStatusAsync(fixture.Descriptor, Fixture.Target.ObjectId, async (state, token) =>
        {
            var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Maintenance.InitializeSelectedAsync(state, token));
            Assert.Equal("ENTITY_OWNERSHIP_INITIALIZATION_STATE_INVALID", denied.Message);
            return true;
        }, default);
        Assert.Equal(0, fixture.OwnershipAccounts.Reads);
        await fixture.Maintenance.WithinInitializationAsync(fixture.Descriptor, [Fixture.Target.ObjectId], async (state, token) =>
        {
            escaped = state;
            await foreach (var target in fixture.Maintenance.PrepareTargetsAsync(state, token))
                Assert.Equal(Fixture.Target.ObjectId, target.ObjectId);
            Assert.Equal(0, fixture.ObjectUpdates);
            Assert.Equal(0, (await fixture.Maintenance.InitializeSelectedAsync(state, token)).Changed);
            Assert.Equal(1, fixture.ObjectUpdates);
            var repeated = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Maintenance.InitializeOneAsync(state, null, token));
            Assert.Equal("ENTITY_OWNERSHIP_INITIALIZATION_STATE_INVALID", repeated.Message);
            return true;
        }, default);
        await fixture.Transactions.ExecuteAsync<bool>(async token =>
        {
            var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Maintenance.InitializeSelectedAsync(escaped!, token));
            Assert.Equal("ENTITY_OWNERSHIP_INITIALIZATION_STATE_INVALID", denied.Message);
            return true;
        });
        Assert.Equal(1, fixture.OwnershipAccounts.Reads);
        Assert.Empty(fixture.Audit.Changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Maintenance_FirstEditDenialPrecedesLaterReadDenialOrMissingFact(bool missingLater)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (!missingLater) await fixture.AddObjectAsync(ReadChecks.LaterId);
        fixture.ReadChecks.DenyLaterRead = !missingLater;
        var handler = new InitializationHandler(fixture.Maintenance);
        // 真实公共业务边界；回调代表平台自动目标裁决的外部端口，不能允许业务越过全部目标检查。
        var error = await Assert.ThrowsAsync<EntityOwnershipException>(() => handler.WithinBoundaryAsync(
            new([Fixture.Target.ObjectId, ReadChecks.LaterId], "batch"), "initializeOwnership", async (state, token) =>
            {
                var targets = Assert.IsType<ButtonTargetSequence>(handler.Target(state));
                await foreach (var actual in targets.Targets(token))
                    if (await fixture.ReadChecks.CheckAsync(actual.ObjectName, Assert.IsType<ButtonExecutionTarget>(actual).ObjectId, ["edit"], token) != DataExecutionDecision.Allowed)
                        throw new ActionDataAccessException(403, "DataScope.ActionDenied");
                return await handler.ExecuteAsync(state, token);
            }, default));
        Assert.Equal(0, fixture.ObjectUpdates);
        Assert.Equal(0, fixture.OwnershipAccounts.Reads);
        Assert.Empty(fixture.Audit.Changes);
        output.WriteLine(JsonSerializer.Serialize(new { missingLater, status = error.StatusCode, error.Code,
            calls = fixture.ReadChecks.Calls, updates = fixture.ObjectUpdates, directoryReads = fixture.OwnershipAccounts.Reads, audit = fixture.Audit.Changes.Count }));
        Assert.Equal(403, error.StatusCode);
        Assert.Equal("EntityOwnership.EditDenied", error.Code);
        Assert.Equal(new[] { Fixture.Target.ObjectId + ":read", Fixture.Target.ObjectId + ":edit" }, fixture.ReadChecks.Calls);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("Owned_absent")]
    public async Task Maintenance_UnknownTargetIs404WithoutWritesOrAnEditTarget(string id)
    {
        await using var fixture = await Fixture.CreateAsync();
        var handler = new InitializationHandler(fixture.Maintenance);
        var error = await Assert.ThrowsAsync<EntityOwnershipException>(() => handler.WithinBoundaryAsync(new([id], "status"),
            "initializeOwnership", async (state, token) =>
            {
                var sequence = Assert.IsType<ButtonTargetSequence>(handler.Target(state));
                await foreach (var target in sequence.Targets(token))
                    await fixture.ReadChecks.CheckAsync(target.ObjectName, Assert.IsType<ButtonExecutionTarget>(target).ObjectId, ["edit"], token);
                return await handler.ExecuteAsync(state, token);
            }, default));
        Assert.Equal(404, error.StatusCode);
        Assert.Equal("EntityOwnership.TargetUnavailable", error.Code);
        Assert.DoesNotContain(fixture.ReadChecks.Calls, call => call.EndsWith(":edit", StringComparison.Ordinal));
        Assert.Equal(0, fixture.ObjectUpdates);
        Assert.Equal(0, fixture.OwnershipAccounts.Reads);
        Assert.Empty(fixture.Audit.Changes);
        output.WriteLine(JsonSerializer.Serialize(new { id, status = error.StatusCode, error.Code, calls = fixture.ReadChecks.Calls,
            updates = fixture.ObjectUpdates, directoryReads = fixture.OwnershipAccounts.Reads, audit = fixture.Audit.Changes.Count }));
    }

    private sealed class InitializationHandler(EntityOwnershipMaintenance maintenance)
        : ObjectOwnershipInitializationButtonHandler("test.owned", new Registry(), maintenance);

    private sealed class Fixture(SqliteConnection connection, EfPlatformDbContext db, OwnershipCommands commands) : IAsyncDisposable
    {
        internal static readonly EntityOwnershipTransferTarget Target = new("test.owned", "Owned_official");
        internal int ObjectUpdates => commands.ObjectUpdates;
        internal Accounts Accounts { get; } = new();
        internal Receiver Receiver { get; } = new();
        internal Recorder Audit { get; } = new();
        internal EfPlatformTransactionExecutor Transactions { get; private set; } = null!;
        internal EntityOwnershipTransfer Transfer { get; private set; } = null!;
        internal EntityOwnershipMaintenance Maintenance { get; private set; } = null!;
        internal OwnershipAccounts OwnershipAccounts { get; } = new();
        internal ReadChecks ReadChecks { get; } = new();
        internal StandardCrudDescriptor Descriptor => new Registry().GetAll().Single();

        internal static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var values = new EntityAuditValueProvider(new Stamp());
            var commands = new OwnershipCommands();
            var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>().UseSqlite(connection).AddInterceptors(commands)
                .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options,
                [new ModelDefinition("Owned", "Owned", ModelKind.Entity,
                [
                    new("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
                    new("OwnerId", "OwnerId", FieldKind.Scalar, FieldValueType.ObjectId, "string", false, false, false),
                    new("OwnerDepartmentId", "OwnerDepartmentId", FieldKind.Scalar, FieldValueType.ObjectId, "string", false, false, false),
                    new("CreatedBy", "CreatedBy", FieldKind.Audit, FieldValueType.User, "string", true, false, true),
                    new("CreatedAt", "CreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
                    new("UpdatedBy", "UpdatedBy", FieldKind.Audit, FieldValueType.User, "string", true, false, true),
                    new("UpdatedAt", "UpdatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true)
                ], [])], values);
            await db.Database.EnsureCreatedAsync();
            db.Set<Dictionary<string, object?>>("Owned").Add(new() { ["EID"] = Target.ObjectId, ["OwnerId"] = "actor", ["OwnerDepartmentId"] = "old-department" });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var fixture = new Fixture(connection, db, commands) { Transactions = new(db) };
            // 本测试只核真实业务边界；授权流水线的允许/拒绝由正式SQL首片证明，不能以空执行合同代替它。
            fixture.Transfer = new(db, fixture.Transactions, values, new Registry(), [], [fixture.Receiver], fixture.Audit, fixture.Accounts);
            fixture.Maintenance = new(db, null!, fixture.Transactions, values,
                new EntityOwnershipBackfill(db, fixture.Transactions, fixture.OwnershipAccounts, new EntityOwnershipAudit(fixture.Audit)), fixture.ReadChecks);
            return fixture;
        }
        internal async Task AddObjectAsync(string id)
        {
            db.Set<Dictionary<string, object?>>("Owned").Add(new() { ["EID"] = id, ["OwnerId"] = "actor", ["OwnerDepartmentId"] = "old-department" });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class ReadChecks : IObjectActionAccess
    {
        internal const string LaterId = "Owned_zlater";
        internal bool DenyLaterRead;
        internal List<string> Calls { get; } = [];
        public Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions, CancellationToken cancellationToken = default)
        {
            Calls.Add(objectId + ":" + string.Join(",", actions));
            return Task.FromResult(actions.SequenceEqual(["read"])
                ? DenyLaterRead && objectId == LaterId ? DataExecutionDecision.NotFound : DataExecutionDecision.Allowed
                : DataExecutionDecision.Denied);
        }
    }

    private sealed class OwnershipCommands : DbCommandInterceptor
    {
        internal int ObjectUpdates;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"Owned\"", StringComparison.Ordinal)) ObjectUpdates++;
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class Registry : IStandardCrudResourceRegistry
    {
        private static readonly StandardCrudDescriptor Descriptor = new()
        {
            ResourceKey = "test.owned", ModuleId = "Test", ModelName = "Owned", Route = "api/test/owned",
            IdField = "EID", DisplayField = "EID", FieldSet = new([], [], [], [])
        };
        public IReadOnlyList<StandardCrudDescriptor> GetAll() => [Descriptor];
        public StandardCrudDescriptor? FindByResourceKey(string resourceKey) => resourceKey == Descriptor.ResourceKey ? Descriptor : null;
        public StandardCrudDescriptor? FindByRoute(string route) => route == Descriptor.Route ? Descriptor : null;
    }
    private sealed class Accounts : ITransactionalAccountOwnershipDirectory
    {
        internal int Reads;
        public ValueTask<AccountOwnership?> FindAsync(string accountId, CancellationToken cancellationToken = default)
        { Reads++; return ValueTask.FromResult<AccountOwnership?>(new(accountId, "current-department")); }
    }
    private sealed class OwnershipAccounts : IAccountOwnershipDirectory
    {
        internal int Reads;
        public ValueTask<IReadOnlyDictionary<string, AccountOwnership>> FindManyAsync(IReadOnlyCollection<string> accountIds, CancellationToken cancellationToken = default)
        { Reads++; return ValueTask.FromResult<IReadOnlyDictionary<string, AccountOwnership>>(accountIds.ToDictionary(id => id, id => new AccountOwnership(id, "old-department"))); }
        public ValueTask<IReadOnlyDictionary<string, string>> FindDepartmentNamesAsync(IReadOnlyCollection<string> departmentIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<AccountOwnershipCandidate>> SearchAsync(string? query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Receiver : IEntityOwnershipTransferReceiver
    {
        internal int Received;
        public ValueTask PrepareAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ReceiveAsync(AccountOwnership recipient, IReadOnlyCollection<EntityOwnershipTransferTarget> targets, CancellationToken cancellationToken = default)
        { Received += targets.Count; return ValueTask.CompletedTask; }
    }
    private sealed class Recorder : IBusinessAuditRecorder
    {
        internal List<BusinessAuditChange> Changes { get; } = [];
        public ValueTask RegisterAsync(BusinessAuditChange change, CancellationToken cancellationToken = default)
        { Changes.Add(change); return ValueTask.CompletedTask; }
    }
    private sealed class Stamp : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent() => new("actor", new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc));
    }
}

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

public sealed class EntityOwnershipTransferBoundaryTests
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

    private sealed class Fixture(SqliteConnection connection, EfPlatformDbContext db) : IAsyncDisposable
    {
        internal static readonly EntityOwnershipTransferTarget Target = new("test.owned", "Owned_official");
        internal Accounts Accounts { get; } = new();
        internal Receiver Receiver { get; } = new();
        internal Recorder Audit { get; } = new();
        internal EfPlatformTransactionExecutor Transactions { get; private set; } = null!;
        internal EntityOwnershipTransfer Transfer { get; private set; } = null!;

        internal static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var values = new EntityAuditValueProvider(new Stamp());
            var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>().UseSqlite(connection)
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
            var fixture = new Fixture(connection, db) { Transactions = new(db) };
            // 本测试只核真实业务边界；授权流水线的允许/拒绝由正式SQL首片证明，不能以空执行合同代替它。
            fixture.Transfer = new(db, fixture.Transactions, values, null!, new Registry(), [], [fixture.Receiver], fixture.Audit, fixture.Accounts);
            return fixture;
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
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

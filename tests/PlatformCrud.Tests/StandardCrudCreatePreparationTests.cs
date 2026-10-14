using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.Tests.Support;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using Xunit;

namespace ICP.Platform.Crud.Tests;

/// <summary>上传准入复用创建候选规则时，不能登记业务行或取得写事务。</summary>
public sealed class StandardCrudCreatePreparationTests
{
    [Fact]
    public async Task PrepareCreateCandidate_AppliesTrustedAuditAndCompleteCreateDecisionWithoutRegisteringOrStartingTransaction()
    {
        var repository = new PreparationRepository([]);
        var transaction = new PreparationTransaction();
        var decision = new CandidateDecision();
        var ids = new CandidateIds();
        var writer = Writer(repository, transaction, decision, ids);
        var result = await writer.PrepareCreateCandidateAsync(Descriptor(), new Dictionary<string, object?> { ["PersonNo"] = "P001", ["ENAME"] = "准入候选" });
        Assert.True(result.Succeeded);
        Assert.Equal(1, repository.UniqueChecks);
        Assert.Equal(1, decision.CandidateChecks);
        Assert.Equal(1, ids.Calls);
        var candidate = decision.Candidate!;
        Assert.Equal("Person_00000000000000000000000000000001", candidate["EID"]);
        Assert.Equal("P001", candidate["PersonNo"]);
        Assert.Equal("准入候选", candidate["ENAME"]);
        Assert.Equal("trusted-actor", candidate["CreatedBy"]);
        Assert.Equal(Stamp.Time, candidate["CreatedAt"]);
        Assert.Equal("trusted-actor", candidate["UpdatedBy"]);
        Assert.Equal(Stamp.Time, candidate["UpdatedAt"]);
        Assert.Equal(0, transaction.Executions);
        Assert.Equal(0, transaction.Flushes);
        Assert.Equal(0, repository.Writes);
        Assert.Equal(0, repository.Registrations);
        Assert.Null(await repository.GetByIdAsync(Descriptor(), (string)candidate["EID"]!));
    }

    [Fact]
    public async Task PrepareCreateCandidate_RejectsServerOwnedInputBeforeUniqueQueryOrCandidateDecision()
    {
        var repository = new PreparationRepository([]);
        var transaction = new PreparationTransaction();
        var decision = new CandidateDecision();
        var ids = new CandidateIds();
        var result = await Writer(repository, transaction, decision, ids).PrepareCreateCandidateAsync(Descriptor(),
            new Dictionary<string, object?> { ["PersonNo"] = "P001", ["ENAME"] = "候选", ["CreatedBy"] = "forged" });
        Assert.False(result.Succeeded);
        Assert.Equal("StandardCrud.ServerOwnedFieldReadOnly", result.Error!.Code);
        Assert.Equal(0, repository.UniqueChecks);
        Assert.Equal(0, decision.CandidateChecks);
        Assert.Equal(0, ids.Calls);
        Assert.Equal(0, transaction.Executions);
        Assert.Equal(0, repository.Writes);
        Assert.Equal(0, repository.Registrations);
    }

    [Fact]
    public async Task PrepareCreateCandidate_PreservesUniqueBeforeCreateDecisionAndDoesNotTreatPriorSuccessAsAuthorization()
    {
        var repository = new PreparationRepository([new() { ["EID"] = "existing", ["PersonNo"] = "P001", ["ENAME"] = "原人员" }]);
        var transaction = new PreparationTransaction();
        var decision = new CandidateDecision();
        var ids = new CandidateIds();
        var writer = Writer(repository, transaction, decision, ids);
        var duplicate = await writer.PrepareCreateCandidateAsync(Descriptor(), new Dictionary<string, object?> { ["PersonNo"] = "P001", ["ENAME"] = "候选" });
        Assert.False(duplicate.Succeeded);
        Assert.Equal("Person.NumberDuplicated", duplicate.Error!.Code);
        Assert.Equal(0, decision.CandidateChecks);
        Assert.Equal(0, ids.Calls);
        var validValues = new Dictionary<string, object?> { ["PersonNo"] = "P002", ["ENAME"] = "合法候选" };
        Assert.True((await writer.PrepareCreateCandidateAsync(Descriptor(), validValues)).Succeeded);
        decision.Allow = false;
        var revoked = await writer.PrepareCreateCandidateAsync(Descriptor(), validValues);
        Assert.False(revoked.Succeeded);
        Assert.Equal("DataScope.ActionDenied", revoked.Error!.Code);
        Assert.Equal(2, decision.CandidateChecks);
        Assert.Equal(0, transaction.Executions);
        Assert.Equal(0, transaction.Flushes);
        Assert.Equal(0, repository.Writes);
        Assert.Equal(0, repository.Registrations);
    }

    private static StandardCrudDefaultWriter Writer(PreparationRepository repository, PreparationTransaction transaction, CandidateDecision decision, CandidateIds ids) =>
        new(repository, [], [], auditValueProvider: new EntityAuditValueProvider(new Stamp()), transactionExecutor: transaction, dataScope: decision, objectIds: ids);

    private static StandardCrudDescriptor Descriptor() => new()
    {
        ResourceKey = "organization.person", ModuleId = "Organization", ModelName = "Person", Route = "api/organization/persons", IdField = "EID", DisplayField = "ENAME",
        RequiresCreationRecord = true,
        FieldSet = new(["EID", "PersonNo", "ENAME"], ["EID", "PersonNo", "ENAME", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"], ["PersonNo", "ENAME"], ["ENAME"]),
        FieldPolicies = [.. new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" }.Select(field => new StandardCrudFieldPolicy
            { Field = field, StorageRequired = true, ValueOwner = StandardCrudFieldValueOwner.Server })],
        UniqueConstraints = [new() { Key = "personNo", Fields = ["PersonNo"], ErrorCode = "Person.NumberDuplicated", Message = "人员编号已存在。", ReasonKey = "person.numberDuplicated" }]
    };

    private sealed class PreparationRepository(IEnumerable<Dictionary<string, object?>> rows) : InMemoryStandardCrudRepository(rows), IStandardCrudRepository, IStandardCrudCreationParticipant
    {
        public int UniqueChecks { get; private set; }
        public int Writes { get; private set; }
        public int Registrations { get; private set; }
        public override Task<bool> ExistsAsync(StandardCrudDescriptor descriptor, StandardCrudUniqueConstraint constraint, IReadOnlyDictionary<string, object?> equalityValues,
            string? excludedId = null, CancellationToken cancellationToken = default)
        { UniqueChecks++; return base.ExistsAsync(descriptor, constraint, equalityValues, excludedId, cancellationToken); }
        Task<IReadOnlyDictionary<string, object?>> IStandardCrudRepository.CreateAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken)
        { Writes++; throw new InvalidOperationException("Preparation unexpectedly wrote a business row."); }
        public IReadOnlyDictionary<string, object?> RegisterCreate(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values)
        { Registrations++; throw new InvalidOperationException("Preparation unexpectedly registered a tracked row."); }
    }

    private sealed class PreparationTransaction : IPlatformTransactionExecutor
    {
        public int Executions { get; private set; }
        public int Flushes { get; private set; }
        public bool HasParticipants(string modelKey) => true;
        public ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> registerChanges, CancellationToken cancellationToken = default)
        { Executions++; throw new InvalidOperationException("Preparation unexpectedly opened a write transaction."); }
        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        { Flushes++; throw new InvalidOperationException("Preparation unexpectedly flushed tracked changes."); }
    }

    private sealed class CandidateDecision : IDataScopeObjectDecisionProvider
    {
        public bool Allow { get; set; } = true;
        public int CandidateChecks { get; private set; }
        public IReadOnlyDictionary<string, object?>? Candidate { get; private set; }
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A create candidate is not an existing object.");
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken cancellationToken = default)
        { Assert.Equal("organization.person", resourceKey); Assert.Equal("create", actionKey); CandidateChecks++; Candidate = candidate; return Task.FromResult(Allow); }
    }

    private sealed class CandidateIds : IPlatformObjectIdGenerator
    {
        public int Calls { get; private set; }
        public string NewId(string objectName) { Assert.Equal("Person", objectName); return "Person_" + (++Calls).ToString("x32"); }
    }
    private sealed class Stamp : IEntityAuditStampProvider
    {
        internal static readonly DateTime Time = new(2026, 10, 14, 1, 2, 3, DateTimeKind.Utc);
        public EntityAuditStamp GetCurrent() => new("trusted-actor", Time);
    }
}

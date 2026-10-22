using ICP.Platform.Crud.Ownership;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.Ownership;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class EntityOwnershipValuesTests
{
    [Fact]
    public async Task OnlyOwnerId_DoesNotRequireAnUnselectedDepartment_AndAuditsOnlySelectedFacts()
    {
        var values = new Dictionary<string, object?>();
        var service = new EntityOwnershipValues(new EntityAuditValueProvider(new Stamp()), new Accounts());
        await service.ApplyCreateAsync(values, ["OwnerId"]);
        Assert.Equal("account", values["OwnerId"]);
        Assert.False(values.ContainsKey("OwnerDepartmentId"));
        var recorder = new Recorder();
        await new EntityOwnershipAudit(recorder).RegisterAsync("test.selected", "object", values, false);
        Assert.Equal("OwnerId", Assert.Single(Assert.Single(recorder.Changes).Fields).Field);
    }

    [Fact]
    public async Task NoSelection_DoesNotReadAuditOrRequireTheDirectory()
    {
        var values = new Dictionary<string, object?>();
        await new EntityOwnershipValues(new EntityAuditValueProvider(null)).ApplyCreateAsync(values, ["ENAME"]);
        Assert.Empty(values);
    }

    [Fact]
    public async Task SelectedDepartment_WithoutTrustedDepartmentFailsClosed()
    {
        var values = new Dictionary<string, object?>();
        var service = new EntityOwnershipValues(new EntityAuditValueProvider(new Stamp()), new Accounts());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyCreateAsync(values, ["OwnerDepartmentId"]).AsTask());
        Assert.Empty(values);
    }

    private sealed class Stamp : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent() => new("account", new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc));
    }
    private sealed class Accounts : IAccountOwnershipDirectory
    {
        public ValueTask<IReadOnlyDictionary<string, AccountOwnership>> FindManyAsync(IReadOnlyCollection<string> accountIds, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, AccountOwnership>>(new Dictionary<string, AccountOwnership> { ["account"] = new("account", null) });
        public ValueTask<IReadOnlyDictionary<string, string>> FindDepartmentNamesAsync(IReadOnlyCollection<string> departmentIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<AccountOwnershipCandidate>> SearchAsync(string? query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Recorder : IBusinessAuditRecorder
    {
        internal List<BusinessAuditChange> Changes { get; } = [];
        public ValueTask RegisterAsync(BusinessAuditChange change, CancellationToken cancellationToken = default) { Changes.Add(change); return ValueTask.CompletedTask; }
    }
}

using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Repositories;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using Microsoft.AspNetCore.Identity;

namespace ICP.Platform.Identity.Tests;

internal sealed class InMemoryIdentityRepository : IIdentityRepository
{
    public List<IdentityAccount> Accounts { get; } = [];

    public List<LocalCredential> Credentials { get; } = [];

    public List<IdentitySecurityEvent> SecurityEvents { get; } = [];

    public bool FailOnSecurityEvent { get; set; }

    public int SecurityEventSearchCount { get; private set; }

    public ValueTask AcquireLocalAdministratorBootstrapLockAsync(CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask<IdentityAccount?> FindAccountAsync(string accountEid, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Clone(Accounts.SingleOrDefault(item => item.EID == accountEid)));

    public ValueTask<IdentityAccount?> FindAccountByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Clone(Accounts.SingleOrDefault(item => item.NormalizedECODE == normalizedCode)));

    public ValueTask<IReadOnlyList<IdentityAccount>> SearchAccountsAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<IdentityAccount>>(Accounts.Select(item => Clone(item)!).ToArray());

    public ValueTask<LocalCredential?> FindCredentialAsync(string accountEid, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Clone(Credentials.SingleOrDefault(item => item.AccountEID == accountEid)));

    public ValueTask<IReadOnlyList<IdentitySecurityEvent>> SearchSecurityEventsAsync(
        string targetAccountEid,
        CancellationToken cancellationToken = default)
    {
        SecurityEventSearchCount++;
        return ValueTask.FromResult<IReadOnlyList<IdentitySecurityEvent>>(SecurityEvents
            .Where(item => item.TargetAccountEID == targetAccountEid)
            .Select(Clone)
            .ToArray());
    }

    public void AddAccount(IdentityAccount account)
    {
        if (Accounts.Any(item => item.NormalizedECODE == account.NormalizedECODE))
        {
            throw new InvalidOperationException("duplicate account code");
        }

        Accounts.Add(Clone(account)!);
    }

    public void AddCredential(LocalCredential credential) => Credentials.Add(Clone(credential)!);

    public ValueTask ReplaceCredentialAsync(LocalCredential credential, CancellationToken cancellationToken = default)
    {
        var index = Credentials.FindIndex(item => item.AccountEID == credential.AccountEID);
        Credentials[index] = Clone(credential)!;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveCredentialAsync(string accountEid, CancellationToken cancellationToken = default)
    {
        Credentials.RemoveAll(item => item.AccountEID == accountEid);
        return ValueTask.CompletedTask;
    }

    public void AddSecurityEvent(IdentitySecurityEvent securityEvent)
    {
        if (FailOnSecurityEvent)
        {
            throw new InvalidOperationException("injected event failure");
        }

        SecurityEvents.Add(Clone(securityEvent));
    }

    public ValueTask<bool> TryReplaceAccountAsync(IdentityAccount account, long expectedRevision, CancellationToken cancellationToken = default)
    {
        var index = Accounts.FindIndex(item => item.EID == account.EID && item.Revision == expectedRevision);
        if (index < 0)
        {
            return ValueTask.FromResult(false);
        }

        Accounts[index] = Clone(account)!;
        return ValueTask.FromResult(true);
    }

    public bool VerifyPassword(string accountEid, string password)
    {
        var account = Accounts.Single(item => item.EID == accountEid);
        var credential = Credentials.Single(item => item.AccountEID == accountEid);
        return new PasswordHasher<IdentityAccount>().VerifyHashedPassword(account, credential.PasswordHash, password)
            != PasswordVerificationResult.Failed;
    }

    public object CreateSnapshot() => new Snapshot(
        Accounts.Select(item => Clone(item)!).ToArray(),
        Credentials.Select(item => Clone(item)!).ToArray(),
        SecurityEvents.Select(Clone).ToArray());

    public void Restore(object snapshotObject)
    {
        var snapshot = (Snapshot)snapshotObject;
        Accounts.Clear();
        Accounts.AddRange(snapshot.Accounts.Select(item => Clone(item)!));
        Credentials.Clear();
        Credentials.AddRange(snapshot.Credentials.Select(item => Clone(item)!));
        SecurityEvents.Clear();
        SecurityEvents.AddRange(snapshot.Events.Select(Clone));
    }

    private static IdentityAccount? Clone(IdentityAccount? item) => item is null ? null : new IdentityAccount
    {
        EID = item.EID,
        ECODE = item.ECODE,
        NormalizedECODE = item.NormalizedECODE,
        ENAME = item.ENAME,
        Status = item.Status,
        IsLocalAdministrator = item.IsLocalAdministrator,
        IsPersonnelBound = item.IsPersonnelBound,
        PersonnelDisableHold = item.PersonnelDisableHold,
        Revision = item.Revision,
        CreatedBy = item.CreatedBy,
        CreatedAt = item.CreatedAt,
        UpdatedBy = item.UpdatedBy,
        UpdatedAt = item.UpdatedAt
    };

    private static LocalCredential? Clone(LocalCredential? item) => item is null ? null : new LocalCredential
    {
        EID = item.EID,
        AccountEID = item.AccountEID,
        PasswordHash = item.PasswordHash,
        IsTemporary = item.IsTemporary,
        CreatedBy = item.CreatedBy,
        CreatedAt = item.CreatedAt,
        UpdatedBy = item.UpdatedBy,
        UpdatedAt = item.UpdatedAt
    };

    private static IdentitySecurityEvent Clone(IdentitySecurityEvent item) => new()
    {
        EID = item.EID,
        TargetAccountEID = item.TargetAccountEID,
        EventType = item.EventType,
        Reason = item.Reason,
        OccurredAt = item.OccurredAt,
        TraceId = item.TraceId,
        ActorKind = item.ActorKind,
        ActorAccountEID = item.ActorAccountEID,
        CreatedBy = item.CreatedBy,
        CreatedAt = item.CreatedAt,
        UpdatedBy = item.UpdatedBy,
        UpdatedAt = item.UpdatedAt
    };

    private sealed record Snapshot(IdentityAccount[] Accounts, LocalCredential[] Credentials, IdentitySecurityEvent[] Events);
}

internal sealed class UnusedPlatformQueryExecutor : IPlatformQueryExecutor
{
    public Task<PlatformQueryResult> QueryAsync(
        PlatformQueryDescriptor descriptor,
        PlatformQueryRequest request,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

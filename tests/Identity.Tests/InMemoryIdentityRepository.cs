using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Repositories;
using Microsoft.AspNetCore.Identity;

namespace ICP.Platform.Identity.Tests;

internal sealed class InMemoryIdentityRepository : IIdentityRepository
{
    public List<User> Users { get; } = [];

    public List<LocalCredential> Credentials { get; } = [];

    public List<IdentitySecurityEvent> SecurityEvents { get; } = [];

    public bool FailOnSecurityEvent { get; set; }

    public ValueTask AcquireLocalAdministratorBootstrapLockAsync(CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask<User?> FindUserAsync(string userEid, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Clone(Users.SingleOrDefault(item => item.EID == userEid)));

    public ValueTask<User?> FindUserByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Clone(Users.SingleOrDefault(item => item.NormalizedECODE == normalizedCode)));

    public ValueTask<IReadOnlyList<User>> SearchUsersAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<User>>(Users.Select(item => Clone(item)!).ToArray());

    public ValueTask<LocalCredential?> FindCredentialAsync(string userEid, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Clone(Credentials.SingleOrDefault(item => item.UserEID == userEid)));

    public void AddUser(User user)
    {
        if (Users.Any(item => item.NormalizedECODE == user.NormalizedECODE))
        {
            throw new InvalidOperationException("duplicate account code");
        }

        Users.Add(Clone(user)!);
    }

    public void AddCredential(LocalCredential credential) => Credentials.Add(Clone(credential)!);

    public ValueTask ReplaceCredentialAsync(LocalCredential credential, CancellationToken cancellationToken = default)
    {
        var index = Credentials.FindIndex(item => item.UserEID == credential.UserEID);
        Credentials[index] = Clone(credential)!;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveCredentialAsync(string userEid, CancellationToken cancellationToken = default)
    {
        Credentials.RemoveAll(item => item.UserEID == userEid);
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

    public ValueTask<bool> TryReplaceUserAsync(User user, long expectedRevision, CancellationToken cancellationToken = default)
    {
        var index = Users.FindIndex(item => item.EID == user.EID && item.Revision == expectedRevision);
        if (index < 0)
        {
            return ValueTask.FromResult(false);
        }

        Users[index] = Clone(user)!;
        return ValueTask.FromResult(true);
    }

    public bool VerifyPassword(string userEid, string password)
    {
        var user = Users.Single(item => item.EID == userEid);
        var credential = Credentials.Single(item => item.UserEID == userEid);
        return new PasswordHasher<User>().VerifyHashedPassword(user, credential.PasswordHash, password)
            != PasswordVerificationResult.Failed;
    }

    public object CreateSnapshot() => new Snapshot(
        Users.Select(item => Clone(item)!).ToArray(),
        Credentials.Select(item => Clone(item)!).ToArray(),
        SecurityEvents.Select(Clone).ToArray());

    public void Restore(object snapshotObject)
    {
        var snapshot = (Snapshot)snapshotObject;
        Users.Clear();
        Users.AddRange(snapshot.Users.Select(item => Clone(item)!));
        Credentials.Clear();
        Credentials.AddRange(snapshot.Credentials.Select(item => Clone(item)!));
        SecurityEvents.Clear();
        SecurityEvents.AddRange(snapshot.Events.Select(Clone));
    }

    private static User? Clone(User? item) => item is null ? null : new User
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
        UserEID = item.UserEID,
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
        TargetUserEID = item.TargetUserEID,
        EventType = item.EventType,
        Reason = item.Reason,
        OccurredAt = item.OccurredAt,
        TraceId = item.TraceId,
        ActorKind = item.ActorKind,
        ActorUserEID = item.ActorUserEID,
        CreatedBy = item.CreatedBy,
        CreatedAt = item.CreatedAt,
        UpdatedBy = item.UpdatedBy,
        UpdatedAt = item.UpdatedAt
    };

    private sealed record Snapshot(User[] Users, LocalCredential[] Credentials, IdentitySecurityEvent[] Events);
}

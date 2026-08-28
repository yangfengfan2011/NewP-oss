using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Modules.Authentication.Models;
using ICP.Platform.Modules.Authentication.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authentication.Persistence.Ef;

public sealed class EfAuthenticationSessionRepository : IAuthenticationSessionRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfAuthenticationSessionRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(AuthenticationSession session) => Sessions.Add(ToEntity(session));

    public async ValueTask<AuthenticationSession?> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        var entity = await Sessions.AsNoTracking().FirstOrDefaultAsync(
            row => EF.Property<string>(row, "TokenHash") == tokenHash,
            cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async ValueTask<bool> RevokeCurrentAsync(
        string sessionEid,
        DateTime revokedAtUtc,
        string reason,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.IsRelational())
        {
            EnsureRelationalTransaction();
            var affected = await Sessions
                .Where(row => EF.Property<string>(row, "EID") == sessionEid
                    && EF.Property<DateTime?>(row, "RevokedAt") == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => EF.Property<DateTime?>(row, "RevokedAt"), revokedAtUtc)
                    .SetProperty(row => EF.Property<string?>(row, "RevocationReason"), reason)
                    .SetProperty(row => EF.Property<string>(row, "UpdatedBy"), actorId)
                    .SetProperty(row => EF.Property<DateTime>(row, "UpdatedAt"), revokedAtUtc),
                    cancellationToken);
            return affected == 1;
        }

        var entity = await Sessions.FirstOrDefaultAsync(
            row => EF.Property<string>(row, "EID") == sessionEid
                && EF.Property<DateTime?>(row, "RevokedAt") == null,
            cancellationToken);
        if (entity is null)
        {
            return false;
        }

        ApplyRevocation(entity, revokedAtUtc, reason, actorId);
        return true;
    }

    public async ValueTask<int> RevokeAllAsync(
        string accountEid,
        DateTime revokedAtUtc,
        string reason,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.IsRelational())
        {
            EnsureRelationalTransaction();
            return await Sessions
                .Where(row => EF.Property<string>(row, "AccountEID") == accountEid
                    && EF.Property<DateTime?>(row, "RevokedAt") == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => EF.Property<DateTime?>(row, "RevokedAt"), revokedAtUtc)
                    .SetProperty(row => EF.Property<string?>(row, "RevocationReason"), reason)
                    .SetProperty(row => EF.Property<string>(row, "UpdatedBy"), actorId)
                    .SetProperty(row => EF.Property<DateTime>(row, "UpdatedAt"), revokedAtUtc),
                    cancellationToken);
        }

        var entities = await Sessions
            .Where(row => EF.Property<string>(row, "AccountEID") == accountEid
                && EF.Property<DateTime?>(row, "RevokedAt") == null)
            .ToListAsync(cancellationToken);
        foreach (var entity in entities)
        {
            ApplyRevocation(entity, revokedAtUtc, reason, actorId);
        }

        return entities.Count;
    }

    public async ValueTask<int> PurgeAsync(
        DateTime retentionCutoffUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var candidates = Sessions
            .Where(row =>
                (EF.Property<DateTime?>(row, "RevokedAt") != null
                    && EF.Property<DateTime?>(row, "RevokedAt") <= retentionCutoffUtc)
                || (EF.Property<DateTime?>(row, "RevokedAt") == null
                    && EF.Property<DateTime>(row, "ExpiresAt") <= retentionCutoffUtc))
            .OrderBy(row => EF.Property<DateTime>(row, "ExpiresAt"))
            .ThenBy(row => EF.Property<string>(row, "EID"))
            .Take(batchSize);

        if (_dbContext.Database.IsRelational())
        {
            EnsureRelationalTransaction();
            return await candidates.ExecuteDeleteAsync(cancellationToken);
        }

        var entities = await candidates.ToListAsync(cancellationToken);
        Sessions.RemoveRange(entities);
        return entities.Count;
    }

    private DbSet<Dictionary<string, object?>> Sessions =>
        _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey("Authentication/AuthenticationSession"));

    private void EnsureRelationalTransaction()
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Authentication session writes require IPlatformTransactionExecutor.");
        }
    }

    private static void ApplyRevocation(
        IDictionary<string, object?> entity,
        DateTime revokedAtUtc,
        string reason,
        string actorId)
    {
        entity["RevokedAt"] = revokedAtUtc;
        entity["RevocationReason"] = reason;
        entity["UpdatedBy"] = actorId;
        entity["UpdatedAt"] = revokedAtUtc;
    }

    // 把完整会话聚合映射到 Authentication 拥有的共享运行时实体。
    private static Dictionary<string, object?> ToEntity(AuthenticationSession session) => new(StringComparer.Ordinal)
    {
        ["EID"] = session.EID,
        ["AccountEID"] = session.AccountEID,
        ["TokenHash"] = session.TokenHash,
        ["SessionType"] = session.SessionType.ToString(),
        ["TimeZoneId"] = session.TimeZoneId,
        ["ExpiresAt"] = session.ExpiresAt,
        ["RevokedAt"] = session.RevokedAt,
        ["RevocationReason"] = session.RevocationReason,
        ["CreatedBy"] = session.CreatedBy,
        ["CreatedAt"] = session.CreatedAt,
        ["UpdatedBy"] = session.UpdatedBy,
        ["UpdatedAt"] = session.UpdatedAt
    };

    // 从持久化实体恢复完整会话事实，供注册表验证和轮换链消费。
    private static AuthenticationSession Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        EID = GetString(entity, "EID"),
        AccountEID = GetString(entity, "AccountEID"),
        TokenHash = GetString(entity, "TokenHash"),
        SessionType = Enum.Parse<AuthenticationSessionType>(GetString(entity, "SessionType"), ignoreCase: false),
        TimeZoneId = GetString(entity, "TimeZoneId"),
        ExpiresAt = GetDateTime(entity, "ExpiresAt"),
        RevokedAt = GetNullableDateTime(entity, "RevokedAt"),
        RevocationReason = entity.TryGetValue("RevocationReason", out var reason) ? reason as string : null,
        CreatedBy = GetString(entity, "CreatedBy"),
        CreatedAt = GetDateTime(entity, "CreatedAt"),
        UpdatedBy = GetString(entity, "UpdatedBy"),
        UpdatedAt = GetDateTime(entity, "UpdatedAt")
    };

    private static string GetString(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is string text ? text : string.Empty;

    private static DateTime GetDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : default;

    private static DateTime? GetNullableDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : null;
}

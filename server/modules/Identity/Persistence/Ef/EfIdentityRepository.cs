using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Repositories;
using ICP.Platform.Persistence.EfCore;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ICP.Platform.Modules.Identity.Persistence.Ef;

public sealed class EfIdentityRepository : IIdentityRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfIdentityRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask AcquireLocalAdministratorBootstrapLockAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return;
        }

        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Identity bootstrap lock requires IPlatformTransactionExecutor.");
        // Transaction owner 让锁与 User/Credential/Event/角色分配共用提交边界；事务结束后 SQL Server 自动释放。
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandType = CommandType.Text;
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = N'NewP.Identity.LocalAdministratorBootstrap', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 15000; SELECT @result;";
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        if (result < 0)
        {
            throw new InvalidOperationException("IDENTITY_BOOTSTRAP_LOCK_UNAVAILABLE");
        }
    }

    public async ValueTask<User?> FindUserAsync(
        string userEid,
        CancellationToken cancellationToken = default)
    {
        var entity = await Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => EF.Property<string>(row, "EID") == userEid,
                cancellationToken);
        return entity is null ? null : MapUser(entity);
    }

    public async ValueTask<User?> FindUserByNormalizedCodeAsync(
        string normalizedCode,
        CancellationToken cancellationToken = default)
    {
        var entity = await Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => EF.Property<string>(row, "NormalizedECODE") == normalizedCode,
                cancellationToken);
        return entity is null ? null : MapUser(entity);
    }

    public async ValueTask<IReadOnlyList<User>> SearchUsersAsync(CancellationToken cancellationToken = default) =>
        (await Users.AsNoTracking().ToListAsync(cancellationToken)).Select(MapUser).ToArray();

    public async ValueTask<LocalCredential?> FindCredentialAsync(
        string userEid,
        CancellationToken cancellationToken = default)
    {
        var entity = await Credentials
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => EF.Property<string>(row, "UserEID") == userEid,
                cancellationToken);
        return entity is null ? null : MapCredential(entity);
    }

    public async ValueTask<IReadOnlyList<IdentitySecurityEvent>> SearchSecurityEventsAsync(
        string targetUserEid,
        CancellationToken cancellationToken = default) =>
        (await SecurityEvents
            .AsNoTracking()
            .Where(row => EF.Property<string>(row, "TargetUserEID") == targetUserEid)
            .ToListAsync(cancellationToken))
        .Select(MapSecurityEvent)
        .ToArray();

    public void AddUser(User user) => Users.Add(ToEntity(user));

    public void AddCredential(LocalCredential credential) => Credentials.Add(ToEntity(credential));

    public async ValueTask ReplaceCredentialAsync(
        LocalCredential credential,
        CancellationToken cancellationToken = default)
    {
        var entity = await Credentials.FirstOrDefaultAsync(
            row => EF.Property<string>(row, "UserEID") == credential.UserEID,
            cancellationToken);
        if (entity is null)
        {
            throw new InvalidOperationException("Identity LocalCredential is missing for the target account.");
        }

        CopyToEntity(credential, entity);
    }

    public async ValueTask RemoveCredentialAsync(
        string userEid,
        CancellationToken cancellationToken = default)
    {
        var entity = await Credentials.FirstOrDefaultAsync(
            row => EF.Property<string>(row, "UserEID") == userEid,
            cancellationToken);
        if (entity is not null)
        {
            Credentials.Remove(entity);
        }
    }

    public void AddSecurityEvent(IdentitySecurityEvent securityEvent) =>
        SecurityEvents.Add(ToEntity(securityEvent));

    public async ValueTask<bool> TryReplaceUserAsync(
        User user,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Database.IsRelational())
        {
            var entity = await Users.FirstOrDefaultAsync(
                row => EF.Property<string>(row, "EID") == user.EID
                    && EF.Property<long>(row, "Revision") == expectedRevision,
                cancellationToken);
            if (entity is null)
            {
                return false;
            }

            CopyToEntity(user, entity);
            return true;
        }

        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Identity security changes require IPlatformTransactionExecutor.");
        }

        // Revision 是账号安全聚合的唯一并发版本；条件更新在 provider 内完成 claim，
        // 后续凭据、会话和事件变化仍由外层共享事务统一提交或回滚。
        var affected = await Users
            .Where(row => EF.Property<string>(row, "EID") == user.EID
                && EF.Property<long>(row, "Revision") == expectedRevision)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => EF.Property<string>(row, "ENAME"), user.ENAME)
                    .SetProperty(row => EF.Property<string>(row, "Status"), user.Status.ToString())
                    .SetProperty(row => EF.Property<bool>(row, "IsLocalAdministrator"), user.IsLocalAdministrator)
                    .SetProperty(row => EF.Property<bool>(row, "IsPersonnelBound"), user.IsPersonnelBound)
                    .SetProperty(row => EF.Property<bool>(row, "PersonnelDisableHold"), user.PersonnelDisableHold)
                    .SetProperty(row => EF.Property<long>(row, "Revision"), user.Revision)
                    .SetProperty(row => EF.Property<string>(row, "UpdatedBy"), user.UpdatedBy)
                    .SetProperty(row => EF.Property<DateTime>(row, "UpdatedAt"), user.UpdatedAt),
                cancellationToken);
        return affected == 1;
    }

    private DbSet<Dictionary<string, object?>> Users =>
        _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey("Identity/User"));

    private DbSet<Dictionary<string, object?>> Credentials =>
        _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey("Identity/LocalCredential"));

    private DbSet<Dictionary<string, object?>> SecurityEvents =>
        _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey("Identity/IdentitySecurityEvent"));

    private static User MapUser(IReadOnlyDictionary<string, object?> entity) => new()
    {
        EID = GetString(entity, "EID"),
        ECODE = GetString(entity, "ECODE"),
        NormalizedECODE = GetString(entity, "NormalizedECODE"),
        ENAME = GetString(entity, "ENAME"),
        Status = Enum.Parse<IdentityAccountStatus>(GetString(entity, "Status"), ignoreCase: false),
        IsLocalAdministrator = GetBool(entity, "IsLocalAdministrator"),
        IsPersonnelBound = GetBool(entity, "IsPersonnelBound"),
        PersonnelDisableHold = GetBool(entity, "PersonnelDisableHold"),
        Revision = GetLong(entity, "Revision"),
        CreatedBy = GetString(entity, "CreatedBy"),
        CreatedAt = GetDateTime(entity, "CreatedAt"),
        UpdatedBy = GetString(entity, "UpdatedBy"),
        UpdatedAt = GetDateTime(entity, "UpdatedAt")
    };

    private static LocalCredential MapCredential(IReadOnlyDictionary<string, object?> entity) => new()
    {
        EID = GetString(entity, "EID"),
        UserEID = GetString(entity, "UserEID"),
        PasswordHash = GetString(entity, "PasswordHash"),
        IsTemporary = GetBool(entity, "IsTemporary"),
        CreatedBy = GetString(entity, "CreatedBy"),
        CreatedAt = GetDateTime(entity, "CreatedAt"),
        UpdatedBy = GetString(entity, "UpdatedBy"),
        UpdatedAt = GetDateTime(entity, "UpdatedAt")
    };

    private static IdentitySecurityEvent MapSecurityEvent(IReadOnlyDictionary<string, object?> entity) => new()
    {
        EID = GetString(entity, "EID"),
        TargetUserEID = GetString(entity, "TargetUserEID"),
        EventType = GetString(entity, "EventType"),
        Reason = GetString(entity, "Reason"),
        OccurredAt = GetDateTime(entity, "OccurredAt"),
        TraceId = GetString(entity, "TraceId"),
        ActorKind = Enum.Parse<IdentitySecurityActorKind>(GetString(entity, "ActorKind"), ignoreCase: false),
        ActorUserEID = GetNullableString(entity, "ActorUserEID"),
        CreatedBy = GetString(entity, "CreatedBy"),
        CreatedAt = GetDateTime(entity, "CreatedAt"),
        UpdatedBy = GetString(entity, "UpdatedBy"),
        UpdatedAt = GetDateTime(entity, "UpdatedAt")
    };

    private static Dictionary<string, object?> ToEntity(User user)
    {
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal);
        CopyToEntity(user, entity);
        return entity;
    }

    private static void CopyToEntity(User user, IDictionary<string, object?> entity)
    {
        entity["EID"] = user.EID;
        entity["ECODE"] = user.ECODE;
        entity["NormalizedECODE"] = user.NormalizedECODE;
        entity["ENAME"] = user.ENAME;
        entity["Status"] = user.Status.ToString();
        entity["IsLocalAdministrator"] = user.IsLocalAdministrator;
        entity["IsPersonnelBound"] = user.IsPersonnelBound;
        entity["PersonnelDisableHold"] = user.PersonnelDisableHold;
        entity["Revision"] = user.Revision;
        entity["CreatedBy"] = user.CreatedBy;
        entity["CreatedAt"] = user.CreatedAt;
        entity["UpdatedBy"] = user.UpdatedBy;
        entity["UpdatedAt"] = user.UpdatedAt;
    }

    private static Dictionary<string, object?> ToEntity(LocalCredential credential)
    {
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal);
        CopyToEntity(credential, entity);
        return entity;
    }

    private static void CopyToEntity(LocalCredential credential, IDictionary<string, object?> entity)
    {
        entity["EID"] = credential.EID;
        entity["UserEID"] = credential.UserEID;
        entity["PasswordHash"] = credential.PasswordHash;
        entity["IsTemporary"] = credential.IsTemporary;
        entity["CreatedBy"] = credential.CreatedBy;
        entity["CreatedAt"] = credential.CreatedAt;
        entity["UpdatedBy"] = credential.UpdatedBy;
        entity["UpdatedAt"] = credential.UpdatedAt;
    }

    private static Dictionary<string, object?> ToEntity(IdentitySecurityEvent securityEvent) => new(StringComparer.Ordinal)
    {
        ["EID"] = securityEvent.EID,
        ["TargetUserEID"] = securityEvent.TargetUserEID,
        ["EventType"] = securityEvent.EventType,
        ["Reason"] = securityEvent.Reason,
        ["OccurredAt"] = securityEvent.OccurredAt,
        ["TraceId"] = securityEvent.TraceId,
        ["ActorKind"] = securityEvent.ActorKind.ToString(),
        ["ActorUserEID"] = securityEvent.ActorUserEID,
        ["CreatedBy"] = securityEvent.CreatedBy,
        ["CreatedAt"] = securityEvent.CreatedAt,
        ["UpdatedBy"] = securityEvent.UpdatedBy,
        ["UpdatedAt"] = securityEvent.UpdatedAt
    };

    private static string GetString(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is string text ? text : string.Empty;

    private static string? GetNullableString(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) ? value as string : null;

    private static bool GetBool(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is bool boolean && boolean;

    private static long GetLong(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is long number ? number : 0;

    private static DateTime GetDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : default;
}

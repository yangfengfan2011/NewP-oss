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
        // Transaction owner 让锁与 IdentityAccount/Credential/Event/角色分配共用提交边界；事务结束后 SQL Server 自动释放。
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

    public async ValueTask<IdentityAccount?> FindAccountAsync(
        string accountEid,
        CancellationToken cancellationToken = default)
    {
        var entity = await Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => EF.Property<string>(row, "EID") == accountEid,
                cancellationToken);
        return entity is null ? null : MapAccount(entity);
    }

    /// <summary>
    /// 通过 exact EID 集合批量读取账号聚合，供跨上下文显示解析避免逐行查询。
    /// </summary>
    public async ValueTask<IReadOnlyDictionary<string, IdentityAccount>> FindAccountsByIdsAsync(
        IReadOnlyCollection<string> accountEids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountEids);
        var ids = accountEids
            .Where(static accountEid => !string.IsNullOrWhiteSpace(accountEid))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<string, IdentityAccount>(StringComparer.Ordinal);
        }

        return (await Accounts
            .AsNoTracking()
            .Where(row => ids.Contains(EF.Property<string>(row, "EID")))
            .ToListAsync(cancellationToken))
            .Select(MapAccount)
            .ToDictionary(static account => account.EID, StringComparer.Ordinal);
    }

    public async ValueTask<IdentityAccount?> FindAccountByNormalizedCodeAsync(
        string normalizedCode,
        CancellationToken cancellationToken = default)
    {
        var entity = await Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => EF.Property<string>(row, "NormalizedECODE") == normalizedCode,
                cancellationToken);
        return entity is null ? null : MapAccount(entity);
    }

    public async ValueTask<IReadOnlyList<IdentityAccount>> SearchAccountsAsync(CancellationToken cancellationToken = default) =>
        (await Accounts.AsNoTracking().ToListAsync(cancellationToken)).Select(MapAccount).ToArray();

    public async ValueTask<LocalCredential?> FindCredentialAsync(
        string accountEid,
        CancellationToken cancellationToken = default)
    {
        var entity = await Credentials
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => EF.Property<string>(row, "AccountEID") == accountEid,
                cancellationToken);
        return entity is null ? null : MapCredential(entity);
    }

    public async ValueTask<IReadOnlyList<IdentitySecurityEvent>> SearchSecurityEventsAsync(
        string targetAccountEid,
        CancellationToken cancellationToken = default) =>
        (await SecurityEvents
            .AsNoTracking()
            .Where(row => EF.Property<string>(row, "TargetAccountEID") == targetAccountEid)
            .ToListAsync(cancellationToken))
        .Select(MapSecurityEvent)
        .ToArray();

    public void AddAccount(IdentityAccount account) => Accounts.Add(ToEntity(account));

    public void AddCredential(LocalCredential credential) => Credentials.Add(ToEntity(credential));

    public async ValueTask ReplaceCredentialAsync(
        LocalCredential credential,
        CancellationToken cancellationToken = default)
    {
        var entity = await Credentials.FirstOrDefaultAsync(
            row => EF.Property<string>(row, "AccountEID") == credential.AccountEID,
            cancellationToken);
        if (entity is null)
        {
            throw new InvalidOperationException("Identity LocalCredential is missing for the target account.");
        }

        CopyToEntity(credential, entity);
    }

    public async ValueTask RemoveCredentialAsync(
        string accountEid,
        CancellationToken cancellationToken = default)
    {
        var entity = await Credentials.FirstOrDefaultAsync(
            row => EF.Property<string>(row, "AccountEID") == accountEid,
            cancellationToken);
        if (entity is not null)
        {
            Credentials.Remove(entity);
        }
    }

    public void AddSecurityEvent(IdentitySecurityEvent securityEvent) =>
        SecurityEvents.Add(ToEntity(securityEvent));

    public async ValueTask<bool> TryReplaceAccountAsync(
        IdentityAccount account,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Database.IsRelational())
        {
            var entity = await Accounts.FirstOrDefaultAsync(
                row => EF.Property<string>(row, "EID") == account.EID
                    && EF.Property<long>(row, "Revision") == expectedRevision,
                cancellationToken);
            if (entity is null)
            {
                return false;
            }

            CopyToEntity(account, entity);
            return true;
        }

        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Identity security changes require IPlatformTransactionExecutor.");
        }

        // Revision 是账号安全聚合的唯一并发版本；条件更新在 provider 内完成 claim，
        // 后续凭据、会话和事件变化仍由外层共享事务统一提交或回滚。
        var affected = await Accounts
            .Where(row => EF.Property<string>(row, "EID") == account.EID
                && EF.Property<long>(row, "Revision") == expectedRevision)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => EF.Property<string>(row, "ENAME"), account.ENAME)
                    .SetProperty(row => EF.Property<string>(row, "Status"), account.Status.ToString())
                    .SetProperty(row => EF.Property<bool>(row, "IsLocalAdministrator"), account.IsLocalAdministrator)
                    .SetProperty(row => EF.Property<bool>(row, "IsPersonnelBound"), account.IsPersonnelBound)
                    .SetProperty(row => EF.Property<bool>(row, "PersonnelDisableHold"), account.PersonnelDisableHold)
                    .SetProperty(row => EF.Property<long>(row, "Revision"), account.Revision)
                    .SetProperty(row => EF.Property<string>(row, "UpdatedBy"), account.UpdatedBy)
                    .SetProperty(row => EF.Property<DateTime>(row, "UpdatedAt"), account.UpdatedAt),
                cancellationToken);
        return affected == 1;
    }

    private DbSet<Dictionary<string, object?>> Accounts =>
        _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey("Identity/IdentityAccount"));

    private DbSet<Dictionary<string, object?>> Credentials =>
        _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey("Identity/LocalCredential"));

    private DbSet<Dictionary<string, object?>> SecurityEvents =>
        _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey("Identity/IdentitySecurityEvent"));

    private static IdentityAccount MapAccount(IReadOnlyDictionary<string, object?> entity) => new()
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
        AccountEID = GetString(entity, "AccountEID"),
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
        TargetAccountEID = GetString(entity, "TargetAccountEID"),
        EventType = GetString(entity, "EventType"),
        Reason = GetString(entity, "Reason"),
        OccurredAt = GetDateTime(entity, "OccurredAt"),
        TraceId = GetString(entity, "TraceId"),
        ActorKind = Enum.Parse<IdentitySecurityActorKind>(GetString(entity, "ActorKind"), ignoreCase: false),
        ActorAccountEID = GetNullableString(entity, "ActorAccountEID"),
        CreatedBy = GetString(entity, "CreatedBy"),
        CreatedAt = GetDateTime(entity, "CreatedAt"),
        UpdatedBy = GetString(entity, "UpdatedBy"),
        UpdatedAt = GetDateTime(entity, "UpdatedAt")
    };

    private static Dictionary<string, object?> ToEntity(IdentityAccount account)
    {
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal);
        CopyToEntity(account, entity);
        return entity;
    }

    private static void CopyToEntity(IdentityAccount account, IDictionary<string, object?> entity)
    {
        entity["EID"] = account.EID;
        entity["ECODE"] = account.ECODE;
        entity["NormalizedECODE"] = account.NormalizedECODE;
        entity["ENAME"] = account.ENAME;
        entity["Status"] = account.Status.ToString();
        entity["IsLocalAdministrator"] = account.IsLocalAdministrator;
        entity["IsPersonnelBound"] = account.IsPersonnelBound;
        entity["PersonnelDisableHold"] = account.PersonnelDisableHold;
        entity["Revision"] = account.Revision;
        entity["CreatedBy"] = account.CreatedBy;
        entity["CreatedAt"] = account.CreatedAt;
        entity["UpdatedBy"] = account.UpdatedBy;
        entity["UpdatedAt"] = account.UpdatedAt;
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
        entity["AccountEID"] = credential.AccountEID;
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
        ["TargetAccountEID"] = securityEvent.TargetAccountEID,
        ["EventType"] = securityEvent.EventType,
        ["Reason"] = securityEvent.Reason,
        ["OccurredAt"] = securityEvent.OccurredAt,
        ["TraceId"] = securityEvent.TraceId,
        ["ActorKind"] = securityEvent.ActorKind.ToString(),
        ["ActorAccountEID"] = securityEvent.ActorAccountEID,
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

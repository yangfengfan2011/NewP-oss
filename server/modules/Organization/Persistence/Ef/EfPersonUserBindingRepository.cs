using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

/// <summary>人员专用 SQL 在共享事务内报告完整实际变更，缓存维护由统一提交参与者负责。</summary>
public sealed class EfPersonUserBindingRepository(EfPlatformDbContext dbContext, IPlatformTransactionExecutor? transactions = null) : IPersonUserBindingRepository
{
    /// <summary>
    /// 读取人员绑定与生命周期状态。
    /// </summary>
    public async ValueTask<PersonUserBindingState?> FindAsync(
        string personEid,
        CancellationToken cancellationToken = default)
    {
        var entity = await Persons
            .AsNoTracking()
            .FirstOrDefaultAsync(
                row => EF.Property<string>(row, "EID") == personEid,
                cancellationToken);
        return entity is null ? null : Map(entity);
    }

    /// <summary>
    /// 在人员可绑定时登记账号绑定事实，提交仍由外层事务统一负责。
    /// </summary>
    public async ValueTask<bool> TryBindAsync(
        string personEid,
        string accountEid,
        DateTime boundAt,
        string actorId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Person binding writes require IPlatformTransactionExecutor.");
        }

        var before = await ReadBeforeWriteAsync(personEid, cancellationToken);
        var affected = await Persons
            .Where(row => EF.Property<string>(row, "EID") == personEid
                && EF.Property<bool>(row, "IsEnabled")
                && EF.Property<string?>(row, "AccountEID") == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => EF.Property<string?>(row, "AccountEID"), accountEid)
                    .SetProperty(row => EF.Property<DateTime?>(row, "AccountBoundAt"), boundAt)
                    .SetProperty(row => EF.Property<string?>(row, "AccountBoundBy"), actorId)
                    .SetProperty(row => EF.Property<string?>(row, "AccountBindingReason"), reason)
                    .SetProperty(row => EF.Property<string?>(row, "UpdatedBy"), actorId)
                    .SetProperty(row => EF.Property<DateTime?>(row, "UpdatedAt"), boundAt),
                cancellationToken);
        if (affected == 1) await RegisterActualChangeAsync(personEid, before, cancellationToken);
        return affected == 1;
    }

    /// <summary>
    /// 登记人员启停状态，提交仍由外层事务统一负责。
    /// </summary>
    public async ValueTask SetEnablementAsync(
        string personEid,
        bool isEnabled,
        string actorId,
        DateTime changedAt,
        CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Person lifecycle writes require IPlatformTransactionExecutor.");
        }

        var before = await ReadBeforeWriteAsync(personEid, cancellationToken);
        var affected = await Persons
            .Where(row => EF.Property<string>(row, "EID") == personEid)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => EF.Property<bool>(row, "IsEnabled"), isEnabled)
                    .SetProperty(row => EF.Property<string?>(row, "UpdatedBy"), actorId)
                    .SetProperty(row => EF.Property<DateTime?>(row, "UpdatedAt"), changedAt),
                cancellationToken);
        if (affected == 1) await RegisterActualChangeAsync(personEid, before, cancellationToken);
    }

    // 特殊 SQL 不进入 EF 跟踪；仅有已注册缓存时读取完整前值，且必须已取得外层事务协调权。
    private async Task<IReadOnlyDictionary<string, object?>?> ReadBeforeWriteAsync(string id, CancellationToken cancellationToken)
    {
        if (transactions?.HasParticipants("Person") is not true) return null;
        if (!transactions.IsActive) throw new InvalidOperationException("Person writes require an active platform transaction.");
        return await Persons.AsNoTracking().SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == id, cancellationToken);
    }

    // 用数据库实际写后值（包括手动审计盖章）登记，不复制缓存算法，也不提前发布。
    private async Task RegisterActualChangeAsync(string id, IReadOnlyDictionary<string, object?>? before, CancellationToken cancellationToken)
    {
        if (transactions?.HasParticipants("Person") is not true) return;
        var after = await Persons.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == id, cancellationToken);
        transactions.RegisterChange(new PlatformRecordChange("Person", id, before, after));
    }

    private DbSet<Dictionary<string, object?>> Persons =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Person"));

    private static PersonUserBindingState Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        PersonEid = GetString(entity, "EID") ?? string.Empty,
        PersonName = GetString(entity, "ENAME") ?? string.Empty,
        IsEnabled = GetBool(entity, "IsEnabled"),
        AccountEid = GetString(entity, "AccountEID"),
        BoundAt = GetDateTime(entity, "AccountBoundAt"),
        BoundBy = GetString(entity, "AccountBoundBy"),
        BindingReason = GetString(entity, "AccountBindingReason"),
        UpdatedBy = GetString(entity, "UpdatedBy"),
        UpdatedAt = GetDateTime(entity, "UpdatedAt")
    };

    private static string? GetString(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) ? value as string : null;

    private static bool GetBool(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is bool boolean && boolean;

    private static DateTime? GetDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : null;
}

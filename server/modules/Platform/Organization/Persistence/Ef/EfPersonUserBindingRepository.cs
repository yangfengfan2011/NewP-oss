using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Transactions;
using ICP.Platform.Persistence.Auditing;
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
        string reason,
        EntityAuditValues auditValues,
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
                    .SetProperty(row => EF.Property<DateTime?>(row, "AccountBoundAt"), auditValues.UpdatedAt)
                    .SetProperty(row => EF.Property<string?>(row, "AccountBoundBy"), auditValues.UpdatedBy)
                    .SetProperty(row => EF.Property<string?>(row, "AccountBindingReason"), reason)
                    .SetProperty(row => EF.Property<string?>(row, "UpdatedBy"), auditValues.UpdatedBy)
                    .SetProperty(row => EF.Property<DateTime?>(row, "UpdatedAt"), auditValues.UpdatedAt),
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
        EntityAuditValues auditValues,
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
                    .SetProperty(row => EF.Property<string?>(row, "UpdatedBy"), auditValues.UpdatedBy)
                    .SetProperty(row => EF.Property<DateTime?>(row, "UpdatedAt"), auditValues.UpdatedAt),
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

    // 记录和主键都来自数据库实际写后值；请求别名不能成为缓存标识，也不提前发布。
    private async Task RegisterActualChangeAsync(string id, IReadOnlyDictionary<string, object?>? before, CancellationToken cancellationToken)
    {
        var after = await Persons.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == id, cancellationToken);
        SynchronizeTrackedRecord(after);
        if (transactions?.HasParticipants("Person") is not true) return;
        transactions.RegisterChange(new PlatformRecordChange("Person", (string)after["EID"]!, before, after));
    }

    // ExecuteUpdate 绕过 change tracker；把数据库实际值同步到同一事务内已跟踪的 Person，防止后续普通更新覆盖专用写入。
    private void SynchronizeTrackedRecord(IReadOnlyDictionary<string, object?> after)
    {
        var id = (string)after["EID"]!;
        foreach (var entity in Persons.Local
            .Where(row => string.Equals(GetString(row, "EID"), id, StringComparison.Ordinal))
            .ToArray())
        {
            var entry = dbContext.Entry(entity);
            foreach (var property in entry.Properties)
            {
                var value = after[property.Metadata.Name];
                property.CurrentValue = value;
                property.OriginalValue = value;
            }
            entry.State = EntityState.Unchanged;
        }
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
        CreatedBy = GetString(entity, "CreatedBy"),
        CreatedAt = GetDateTime(entity, "CreatedAt"),
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

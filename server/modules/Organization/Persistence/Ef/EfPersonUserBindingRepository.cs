using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

public sealed class EfPersonUserBindingRepository(EfPlatformDbContext dbContext) : IPersonUserBindingRepository
{
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

    public async ValueTask<bool> TryBindAsync(
        string personEid,
        string userEid,
        DateTime boundAt,
        string actorId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Person binding writes require IPlatformTransactionExecutor.");
        }

        var affected = await Persons
            .Where(row => EF.Property<string>(row, "EID") == personEid
                && EF.Property<bool>(row, "IsEnabled")
                && EF.Property<string?>(row, "UserEID") == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => EF.Property<string?>(row, "UserEID"), userEid)
                    .SetProperty(row => EF.Property<DateTime?>(row, "UserBoundAt"), boundAt)
                    .SetProperty(row => EF.Property<string?>(row, "UserBoundBy"), actorId)
                    .SetProperty(row => EF.Property<string?>(row, "UserBindingReason"), reason)
                    .SetProperty(row => EF.Property<string?>(row, "UpdatedBy"), actorId)
                    .SetProperty(row => EF.Property<DateTime?>(row, "UpdatedAt"), boundAt),
                cancellationToken);
        return affected == 1;
    }

    private DbSet<Dictionary<string, object?>> Persons =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Person"));

    private static PersonUserBindingState Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        PersonEid = GetString(entity, "EID") ?? string.Empty,
        PersonName = GetString(entity, "ENAME") ?? string.Empty,
        IsEnabled = GetBool(entity, "IsEnabled"),
        UserEid = GetString(entity, "UserEID"),
        BoundAt = GetDateTime(entity, "UserBoundAt"),
        BoundBy = GetString(entity, "UserBoundBy"),
        BindingReason = GetString(entity, "UserBindingReason"),
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

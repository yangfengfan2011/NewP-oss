using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

public sealed class EfPersonUserLinkRepository : IPersonUserLinkRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfPersonUserLinkRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PersonUserLink?> GetByPersonIdAsync(string personId, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(link => EF.Property<string>(link, "PersonId") == personId, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<bool> ExistsByUserIdAsync(
        string userId,
        string? excludedPersonId = null,
        CancellationToken cancellationToken = default)
    {
        var entities = await Entities.AsNoTracking().ToListAsync(cancellationToken);
        return entities.Any(entity =>
            string.Equals(GetString(entity, "UserId"), userId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(GetString(entity, "PersonId"), excludedPersonId, StringComparison.Ordinal));
    }

    public async Task UpsertAsync(PersonUserLink link, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .FirstOrDefaultAsync(existing => EF.Property<string>(existing, "PersonId") == link.PersonId, cancellationToken);

        if (entity is null)
        {
            Entities.Add(ToEntity(link));
        }
        else
        {
            CopyToEntity(link, entity);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private DbSet<Dictionary<string, object?>> Entities => _dbContext.Set<Dictionary<string, object?>>("PersonUserLink");

    private static PersonUserLink Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        Id = GetString(entity, "EID") ?? string.Empty,
        PersonId = GetString(entity, "PersonId") ?? string.Empty,
        UserId = GetString(entity, "UserId") ?? string.Empty,
        LinkedAt = GetDateTime(entity, "LinkedAt"),
        LinkedBy = GetString(entity, "LinkedBy")
    };

    private static Dictionary<string, object?> ToEntity(PersonUserLink link)
    {
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            // PersonUserLink 拥有独立 EID；PersonId 只保留为人员引用和 upsert 唯一边界。
            ["EID"] = link.Id,
            ["PersonId"] = link.PersonId
        };

        CopyToEntity(link, entity);
        return entity;
    }

    private static void CopyToEntity(PersonUserLink link, IDictionary<string, object?> entity)
    {
        entity["EID"] = link.Id;
        entity["PersonId"] = link.PersonId;
        entity["UserId"] = link.UserId;
        entity["LinkedAt"] = link.LinkedAt;
        entity["LinkedBy"] = string.IsNullOrWhiteSpace(link.LinkedBy) ? "system" : link.LinkedBy;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) ? value as string : null;

    private static DateTime GetDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : default;
}

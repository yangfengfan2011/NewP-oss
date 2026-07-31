using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

public sealed class EfPositionRepository : IPositionRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfPositionRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Position>> GetByDepartmentIdAsync(string departmentId, CancellationToken cancellationToken = default)
    {
        var entities = await Entities
            .AsNoTracking()
            .Where(position => EF.Property<string>(position, "DepartmentId") == departmentId)
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task<Position?> GetByIdAsync(string positionId, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(position => EF.Property<string>(position, "EID") == positionId, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyDictionary<string, Position>> GetByIdsAsync(
        IReadOnlyCollection<string> positionIds,
        CancellationToken cancellationToken = default)
    {
        if (positionIds.Count == 0)
        {
            return new Dictionary<string, Position>(StringComparer.Ordinal);
        }

        var entities = await Entities
            .AsNoTracking()
            .Where(position => positionIds.Contains(EF.Property<string>(position, "EID")))
            .ToListAsync(cancellationToken);

        return entities
            .Select(Map)
            .ToDictionary(position => position.Id, StringComparer.Ordinal);
    }

    public async Task CreateAsync(Position position, CancellationToken cancellationToken = default)
    {
        Entities.Add(ToEntity(position));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Position position, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .FirstOrDefaultAsync(existing => EF.Property<string>(existing, "EID") == position.Id, cancellationToken);

        if (entity is null)
        {
            return;
        }

        CopyToEntity(position, entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private DbSet<Dictionary<string, object?>> Entities => _dbContext.Set<Dictionary<string, object?>>("Position");

    private static Position Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        Id = GetString(entity, "EID") ?? string.Empty,
        CompanyId = GetString(entity, "CompanyId") ?? string.Empty,
        DepartmentId = GetString(entity, "DepartmentId") ?? string.Empty,
        Code = GetString(entity, "ECODE") ?? string.Empty,
        Name = GetString(entity, "ENAME") ?? string.Empty,
        EmploymentStatus = GetEmploymentStatus(entity, "EmploymentStatus"),
        IsEnabled = GetBool(entity, "IsEnabled"),
        SortOrder = GetInt(entity, "SortOrder"),
        CreatedAt = GetDateTime(entity, "CreatedAt"),
        UpdatedAt = GetNullableDateTime(entity, "UpdatedAt")
    };

    private static Dictionary<string, object?> ToEntity(Position position)
    {
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["EID"] = position.Id
        };

        CopyToEntity(position, entity);
        return entity;
    }

    private static void CopyToEntity(Position position, IDictionary<string, object?> entity)
    {
        // 领域模型保留 Id，metadata 投影后的实体主键统一写入 EID。
        entity["EID"] = position.Id;
        entity["CompanyId"] = position.CompanyId;
        entity["DepartmentId"] = position.DepartmentId;
        entity["ECODE"] = position.Code;
        entity["ENAME"] = position.Name;
        entity["EmploymentStatus"] = position.EmploymentStatus.ToString();
        entity["IsEnabled"] = position.IsEnabled;
        entity["SortOrder"] = position.SortOrder.ToString();
        entity["CreatedAt"] = position.CreatedAt;
        entity["UpdatedAt"] = position.UpdatedAt;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) ? value as string : null;

    private static bool GetBool(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is bool boolean && boolean;

    private static int GetInt(IReadOnlyDictionary<string, object?> entity, string key) =>
        int.TryParse(GetString(entity, key), out var value) ? value : 0;

    private static EmploymentStatus GetEmploymentStatus(IReadOnlyDictionary<string, object?> entity, string key) =>
        Enum.TryParse<EmploymentStatus>(GetString(entity, key), true, out var value)
            ? value
            : EmploymentStatus.FullTime;

    private static DateTime GetDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : default;

    private static DateTime? GetNullableDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : null;
}

using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

public sealed class EfPersonRepository : IPersonRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfPersonRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Person?> GetByIdAsync(string personId, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(person => EF.Property<string>(person, "EID") == personId, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<Person>> GetListAsync(
        string? keyword,
        string? companyId,
        string? departmentId,
        EmploymentStatus? employmentStatus,
        CancellationToken cancellationToken = default)
    {
        var entities = await Entities.AsNoTracking().ToListAsync(cancellationToken);

        IEnumerable<IReadOnlyDictionary<string, object?>> query = entities;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(entity =>
                ContainsIgnoreCase(GetString(entity, "PersonNo"), keyword) ||
                ContainsIgnoreCase(GetString(entity, "ENAME"), keyword));
        }

        if (!string.IsNullOrWhiteSpace(companyId))
        {
            query = query.Where(entity => string.Equals(GetString(entity, "CompanyId"), companyId, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(departmentId))
        {
            query = query.Where(entity => string.Equals(GetString(entity, "DepartmentId"), departmentId, StringComparison.Ordinal));
        }

        if (employmentStatus.HasValue)
        {
            query = query.Where(entity => GetEmploymentStatus(entity, "EmploymentStatus") == employmentStatus.Value);
        }

        return query.Select(Map).ToList();
    }

    public async Task<bool> ExistsPersonNoAsync(
        string? companyId,
        string personNo,
        string? excludedPersonId = null,
        CancellationToken cancellationToken = default)
    {
        var entities = await Entities.AsNoTracking().ToListAsync(cancellationToken);
        return entities.Any(entity =>
            string.Equals(GetString(entity, "CompanyId"), companyId, StringComparison.Ordinal) &&
            !string.Equals(GetString(entity, "EID"), excludedPersonId, StringComparison.Ordinal) &&
            string.Equals(GetString(entity, "PersonNo"), personNo, StringComparison.OrdinalIgnoreCase));
    }

    public async Task CreateAsync(Person person, CancellationToken cancellationToken = default)
    {
        Entities.Add(ToEntity(person));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Person person, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .FirstOrDefaultAsync(existing => EF.Property<string>(existing, "EID") == person.Id, cancellationToken);

        if (entity is null)
        {
            return;
        }

        CopyToEntity(person, entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private DbSet<Dictionary<string, object?>> Entities => _dbContext.Set<Dictionary<string, object?>>("Person");

    private static Person Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        Id = GetString(entity, "EID") ?? string.Empty,
        PersonNo = GetString(entity, "PersonNo") ?? string.Empty,
        Name = GetString(entity, "ENAME") ?? string.Empty,
        CompanyId = GetString(entity, "CompanyId"),
        DepartmentId = GetString(entity, "DepartmentId"),
        PositionId = GetString(entity, "PositionId"),
        EmploymentStatus = GetEmploymentStatus(entity, "EmploymentStatus"),
        IsEnabled = GetBool(entity, "IsEnabled"),
        CreatedAt = GetDateTime(entity, "CreatedAt"),
        UpdatedAt = GetNullableDateTime(entity, "UpdatedAt")
    };

    private static Dictionary<string, object?> ToEntity(Person person)
    {
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["EID"] = person.Id
        };

        CopyToEntity(person, entity);
        return entity;
    }

    private static void CopyToEntity(Person person, IDictionary<string, object?> entity)
    {
        // 领域模型保留 Id，metadata 投影后的实体主键统一写入 EID。
        entity["EID"] = person.Id;
        entity["PersonNo"] = person.PersonNo;
        entity["ENAME"] = person.Name;
        entity["CompanyId"] = person.CompanyId;
        entity["DepartmentId"] = person.DepartmentId;
        entity["PositionId"] = person.PositionId;
        entity["EmploymentStatus"] = person.EmploymentStatus.ToString();
        entity["IsEnabled"] = person.IsEnabled;
        entity["CreatedAt"] = person.CreatedAt;
        entity["UpdatedAt"] = person.UpdatedAt;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) ? value as string : null;

    private static bool ContainsIgnoreCase(string? source, string keyword) =>
        !string.IsNullOrWhiteSpace(source) &&
        source.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static bool GetBool(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is bool boolean && boolean;

    private static EmploymentStatus GetEmploymentStatus(IReadOnlyDictionary<string, object?> entity, string key) =>
        Enum.TryParse<EmploymentStatus>(GetString(entity, key), true, out var value)
            ? value
            : EmploymentStatus.FullTime;

    private static DateTime GetDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : default;

    private static DateTime? GetNullableDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : null;
}

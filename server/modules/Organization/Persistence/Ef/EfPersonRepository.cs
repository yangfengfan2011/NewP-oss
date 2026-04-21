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

    public async Task<Person?> GetByIdAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(person => EF.Property<string>(person, "Id") == personId.ToString(), cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<Person>> GetListAsync(
        string? keyword,
        Guid? companyId,
        Guid? departmentId,
        EmploymentStatus? employmentStatus,
        CancellationToken cancellationToken = default)
    {
        var entities = await Entities.AsNoTracking().ToListAsync(cancellationToken);

        IEnumerable<IReadOnlyDictionary<string, object?>> query = entities;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(entity =>
                ContainsIgnoreCase(GetString(entity, "PersonNo"), keyword) ||
                ContainsIgnoreCase(GetString(entity, "Name"), keyword));
        }

        if (companyId.HasValue)
        {
            query = query.Where(entity => GetNullableGuid(entity, "CompanyId") == companyId.Value);
        }

        if (departmentId.HasValue)
        {
            query = query.Where(entity => GetNullableGuid(entity, "DepartmentId") == departmentId.Value);
        }

        if (employmentStatus.HasValue)
        {
            query = query.Where(entity => GetEmploymentStatus(entity, "EmploymentStatus") == employmentStatus.Value);
        }

        return query.Select(Map).ToList();
    }

    public async Task<bool> ExistsPersonNoAsync(
        Guid? companyId,
        string personNo,
        Guid? excludedPersonId = null,
        CancellationToken cancellationToken = default)
    {
        var entities = await Entities.AsNoTracking().ToListAsync(cancellationToken);
        return entities.Any(entity =>
            GetNullableGuid(entity, "CompanyId") == companyId &&
            GetGuid(entity, "Id") != excludedPersonId &&
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
            .FirstOrDefaultAsync(existing => EF.Property<string>(existing, "Id") == person.Id.ToString(), cancellationToken);

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
        Id = GetGuid(entity, "Id"),
        PersonNo = GetString(entity, "PersonNo") ?? string.Empty,
        Name = GetString(entity, "Name") ?? string.Empty,
        CompanyId = GetNullableGuid(entity, "CompanyId"),
        DepartmentId = GetNullableGuid(entity, "DepartmentId"),
        PositionId = GetNullableGuid(entity, "PositionId"),
        EmploymentStatus = GetEmploymentStatus(entity, "EmploymentStatus"),
        IsEnabled = GetBool(entity, "IsEnabled"),
        CreatedAt = GetDateTime(entity, "CreatedAt"),
        UpdatedAt = GetNullableDateTime(entity, "UpdatedAt")
    };

    private static Dictionary<string, object?> ToEntity(Person person)
    {
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Id"] = person.Id.ToString()
        };

        CopyToEntity(person, entity);
        return entity;
    }

    private static void CopyToEntity(Person person, IDictionary<string, object?> entity)
    {
        entity["Id"] = person.Id.ToString();
        entity["PersonNo"] = person.PersonNo;
        entity["Name"] = person.Name;
        entity["CompanyId"] = person.CompanyId?.ToString();
        entity["DepartmentId"] = person.DepartmentId?.ToString();
        entity["PositionId"] = person.PositionId?.ToString();
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

    private static Guid GetGuid(IReadOnlyDictionary<string, object?> entity, string key) =>
        Guid.TryParse(GetString(entity, key), out var value) ? value : Guid.Empty;

    private static Guid? GetNullableGuid(IReadOnlyDictionary<string, object?> entity, string key) =>
        Guid.TryParse(GetString(entity, key), out var value) ? value : null;

    private static EmploymentStatus GetEmploymentStatus(IReadOnlyDictionary<string, object?> entity, string key) =>
        Enum.TryParse<EmploymentStatus>(GetString(entity, key), true, out var value)
            ? value
            : EmploymentStatus.FullTime;

    private static DateTime GetDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : default;

    private static DateTime? GetNullableDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : null;
}

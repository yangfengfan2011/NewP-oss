using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

public sealed class EfDepartmentRepository : IDepartmentRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfDepartmentRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Department>> GetByCompanyIdAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var entities = await Entities
            .AsNoTracking()
            .Where(department => EF.Property<string>(department, "CompanyId") == companyId.ToString())
            .ToListAsync(cancellationToken);

        return entities.Select(Map).ToList();
    }

    public async Task<Department?> GetByIdAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(department => EF.Property<string>(department, "EID") == departmentId.ToString(), cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<bool> ExistsSiblingCodeAsync(
        Guid companyId,
        Guid? parentDepartmentId,
        string code,
        Guid? excludedDepartmentId = null,
        CancellationToken cancellationToken = default)
    {
        var entities = await Entities.AsNoTracking().ToListAsync(cancellationToken);
        return entities.Any(entity =>
            GetGuid(entity, "CompanyId") == companyId &&
            GetNullableGuid(entity, "ParentDepartmentId") == parentDepartmentId &&
            GetGuid(entity, "EID") != excludedDepartmentId &&
            string.Equals(GetString(entity, "Code"), code, StringComparison.OrdinalIgnoreCase));
    }

    public async Task CreateAsync(Department department, CancellationToken cancellationToken = default)
    {
        Entities.Add(ToEntity(department));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Department department, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .FirstOrDefaultAsync(existing => EF.Property<string>(existing, "EID") == department.Id.ToString(), cancellationToken);

        if (entity is null)
        {
            return;
        }

        CopyToEntity(department, entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private DbSet<Dictionary<string, object?>> Entities => _dbContext.Set<Dictionary<string, object?>>("Department");

    private static Department Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        Id = GetGuid(entity, "EID"),
        CompanyId = GetGuid(entity, "CompanyId"),
        ParentDepartmentId = GetNullableGuid(entity, "ParentDepartmentId"),
        Code = GetString(entity, "Code") ?? string.Empty,
        Name = GetString(entity, "Name") ?? string.Empty,
        IsEnabled = GetBool(entity, "IsEnabled"),
        SortOrder = GetInt(entity, "SortOrder"),
        CreatedAt = GetDateTime(entity, "CreatedAt"),
        UpdatedAt = GetNullableDateTime(entity, "UpdatedAt")
    };

    private static Dictionary<string, object?> ToEntity(Department department)
    {
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["EID"] = department.Id.ToString()
        };

        CopyToEntity(department, entity);
        return entity;
    }

    private static void CopyToEntity(Department department, IDictionary<string, object?> entity)
    {
        // 领域模型保留 Id，metadata 投影后的实体主键统一写入 EID。
        entity["EID"] = department.Id.ToString();
        entity["CompanyId"] = department.CompanyId.ToString();
        entity["ParentDepartmentId"] = department.ParentDepartmentId?.ToString();
        entity["Code"] = department.Code;
        entity["Name"] = department.Name;
        entity["IsEnabled"] = department.IsEnabled;
        entity["SortOrder"] = department.SortOrder.ToString();
        entity["CreatedAt"] = department.CreatedAt;
        entity["UpdatedAt"] = department.UpdatedAt;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) ? value as string : null;

    private static bool GetBool(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is bool boolean && boolean;

    private static Guid GetGuid(IReadOnlyDictionary<string, object?> entity, string key) =>
        Guid.TryParse(GetString(entity, key), out var value) ? value : Guid.Empty;

    private static Guid? GetNullableGuid(IReadOnlyDictionary<string, object?> entity, string key) =>
        Guid.TryParse(GetString(entity, key), out var value) ? value : null;

    private static int GetInt(IReadOnlyDictionary<string, object?> entity, string key) =>
        int.TryParse(GetString(entity, key), out var value) ? value : 0;

    private static DateTime GetDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : default;

    private static DateTime? GetNullableDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : null;
}

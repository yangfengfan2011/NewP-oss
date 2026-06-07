using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

public sealed class EfCompanyRepository : ICompanyRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfCompanyRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Company>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await Entities.AsNoTracking().ToListAsync(cancellationToken);
        return entities.Select(Map).ToList();
    }

    public async Task<Company?> GetByIdAsync(string companyId, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .AsNoTracking()
            .FirstOrDefaultAsync(company => EF.Property<string>(company, "EID") == companyId, cancellationToken);

        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyDictionary<string, Company>> GetByIdsAsync(
        IReadOnlyCollection<string> companyIds,
        CancellationToken cancellationToken = default)
    {
        if (companyIds.Count == 0)
        {
            return new Dictionary<string, Company>(StringComparer.Ordinal);
        }

        var entities = await Entities
            .AsNoTracking()
            .Where(company => companyIds.Contains(EF.Property<string>(company, "EID")))
            .ToListAsync(cancellationToken);

        return entities
            .Select(Map)
            .ToDictionary(company => company.Id, StringComparer.Ordinal);
    }

    public async Task<Company?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var entities = await Entities.AsNoTracking().ToListAsync(cancellationToken);
        var entity = entities.FirstOrDefault(company =>
            string.Equals(GetString(company, "ECODE"), code, StringComparison.OrdinalIgnoreCase));

        return entity is null ? null : Map(entity);
    }

    public async Task CreateAsync(Company company, CancellationToken cancellationToken = default)
    {
        Entities.Add(ToEntity(company));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Company company, CancellationToken cancellationToken = default)
    {
        var entity = await Entities
            .FirstOrDefaultAsync(existing => EF.Property<string>(existing, "EID") == company.Id, cancellationToken);

        if (entity is null)
        {
            return;
        }

        CopyToEntity(company, entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private DbSet<Dictionary<string, object?>> Entities => _dbContext.Set<Dictionary<string, object?>>("Company");

    private static Company Map(IReadOnlyDictionary<string, object?> entity) => new()
    {
        Id = GetString(entity, "EID") ?? string.Empty,
        Code = GetString(entity, "ECODE") ?? string.Empty,
        Name = GetString(entity, "ENAME") ?? string.Empty,
        IsEnabled = GetBool(entity, "IsEnabled"),
        SortOrder = GetInt(entity, "SortOrder"),
        CreatedAt = GetDateTime(entity, "CreatedAt"),
        UpdatedAt = GetNullableDateTime(entity, "UpdatedAt")
    };

    private static Dictionary<string, object?> ToEntity(Company company)
    {
        var entity = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["EID"] = company.Id
        };

        CopyToEntity(company, entity);
        return entity;
    }

    private static void CopyToEntity(Company company, IDictionary<string, object?> entity)
    {
        // 领域模型保留 Id，metadata 投影后的实体主键统一写入 EID。
        entity["EID"] = company.Id;
        entity["ECODE"] = company.Code;
        entity["ENAME"] = company.Name;
        entity["IsEnabled"] = company.IsEnabled;
        entity["SortOrder"] = company.SortOrder.ToString();
        entity["CreatedAt"] = company.CreatedAt;
        entity["UpdatedAt"] = company.UpdatedAt;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) ? value as string : null;

    private static bool GetBool(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is bool boolean && boolean;

    private static int GetInt(IReadOnlyDictionary<string, object?> entity, string key) =>
        int.TryParse(GetString(entity, key), out var value) ? value : 0;

    private static DateTime GetDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : default;

    private static DateTime? GetNullableDateTime(IReadOnlyDictionary<string, object?> entity, string key) =>
        entity.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : null;
}

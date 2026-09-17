using System.Collections.Frozen;
using System.Collections.Immutable;
using ICP.Platform.Organization.Contracts;

namespace ICP.Platform.Modules.Organization.Caching;

/// <summary>稳定业务查询的唯一适配；每次先通过缓存可用性及发布协调，再在同一不可变结果上解析。</summary>
public sealed class OrganizationQuery(OrganizationCache cache) : IOrganizationQuery
{
    /// <inheritdoc/>
    public async ValueTask<IOrganizationReadView> ReadAsync(CancellationToken cancellationToken = default) =>
        new ReadView(await cache.ReadAsync(cancellationToken));

    private sealed class ReadView(OrganizationSnapshot snapshot) : IOrganizationReadView
    {
        /// <inheritdoc/>
        public IReadOnlyDictionary<string, object?>? Find(OrganizationRecordKind kind, string id) =>
            Records(kind).GetValueOrDefault(id);

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, object?>? FindCompanyByCode(string code) =>
            snapshot.CompanyCodes.TryGetValue(code.Trim(), out var id) ? snapshot.Companies[id] : null;

        /// <inheritdoc/>
        public CurrentOrganization? FindByAccount(string accountId)
        {
            if (!snapshot.PersonByAccount.TryGetValue(accountId, out var personId)) return null;
            var person = snapshot.Persons[personId];
            var company = person.GetValueOrDefault("CompanyId") is string companyId ? snapshot.Companies[companyId] : null;
            var department = person.GetValueOrDefault("DepartmentId") is string departmentId ? snapshot.Departments[departmentId] : null;
            var ancestors = ImmutableArray.CreateBuilder<IReadOnlyDictionary<string, object?>>();
            var current = department;
            // 发布前已验证同公司、无环和引用完整性；这里只沿实际上级链走索引，不扫描全量档案。
            while (current?.GetValueOrDefault("ParentDepartmentId") is string parent)
            {
                current = snapshot.Departments[parent];
                ancestors.Add(current);
            }
            return new(person, company, department, ancestors.ToImmutable());
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, OrganizationName> ResolveNames(OrganizationRecordKind kind, IReadOnlyCollection<string> ids)
        {
            var records = Records(kind);
            var result = new Dictionary<string, OrganizationName>(StringComparer.Ordinal);
            foreach (var id in ids)
                if (!result.ContainsKey(id) && records.TryGetValue(id, out var row))
                    result.Add(id, new((string)row["ENAME"]!, row["IsEnabled"] is true));
            return result.ToFrozenDictionary(StringComparer.Ordinal);
        }

        // 每种档案固定映射到唯一发布集合，不暴露缓存实现或可写实体。
        private ImmutableDictionary<string, IReadOnlyDictionary<string, object?>> Records(OrganizationRecordKind kind) => kind switch
        {
            OrganizationRecordKind.Company => snapshot.Companies,
            OrganizationRecordKind.Department => snapshot.Departments,
            OrganizationRecordKind.Position => snapshot.Positions,
            OrganizationRecordKind.Person => snapshot.Persons,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}

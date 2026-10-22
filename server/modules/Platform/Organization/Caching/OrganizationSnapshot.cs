using System.Collections.Immutable;

namespace ICP.Platform.Modules.Organization.Caching;

/// <summary>同一次发布的完整原始档案和直接关系；所有集合及记录均不可变。</summary>
public sealed record OrganizationSnapshot
{
    public ImmutableDictionary<string, IReadOnlyDictionary<string, object?>> Companies { get; init; } = EmptyRecords;
    public ImmutableDictionary<string, IReadOnlyDictionary<string, object?>> Departments { get; init; } = EmptyRecords;
    public ImmutableDictionary<string, IReadOnlyDictionary<string, object?>> Positions { get; init; } = EmptyRecords;
    public ImmutableDictionary<string, IReadOnlyDictionary<string, object?>> Persons { get; init; } = EmptyRecords;
    public ImmutableDictionary<string, string> CompanyCodes { get; init; } = ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase);
    public ImmutableDictionary<(string CompanyId, string Code), string> DepartmentCodes { get; init; } = ImmutableDictionary<(string, string), string>.Empty;
    public ImmutableDictionary<string, string> PersonNumbers { get; init; } = ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase);
    public ImmutableDictionary<string, string> PersonByAccount { get; init; } = ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);
    public ImmutableDictionary<string, ImmutableHashSet<string>> DepartmentsByCompany { get; init; } = EmptyRelations;
    public ImmutableDictionary<string, ImmutableHashSet<string>> ChildDepartments { get; init; } = EmptyRelations;
    public ImmutableDictionary<string, ImmutableHashSet<string>> PersonsByCompany { get; init; } = EmptyRelations;
    public ImmutableDictionary<string, ImmutableHashSet<string>> PersonsByDepartment { get; init; } = EmptyRelations;
    public ImmutableDictionary<string, ImmutableHashSet<string>> PersonsByPosition { get; init; } = EmptyRelations;

    private static readonly ImmutableDictionary<string, IReadOnlyDictionary<string, object?>> EmptyRecords =
        ImmutableDictionary.Create<string, IReadOnlyDictionary<string, object?>>(StringComparer.Ordinal);
    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> EmptyRelations =
        ImmutableDictionary.Create<string, ImmutableHashSet<string>>(StringComparer.Ordinal);
}

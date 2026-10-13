using System.Linq.Expressions;
using ICP.Platform.Modules.Organization.Directories;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

/// <summary>
/// 在组织目录的业务范围内独立解析当前关联，统一分批读取、存在性与可读状态。
/// </summary>
internal static class OrganizationLookupCurrentValueReader
{
    // scopedRows 只包含当前值适用的业务范围，不包含候选关键词、启用或层级排除条件。
    internal static async Task<IReadOnlyList<OrganizationDirectoryCurrentItem<T>>> ReadAsync<T>(
        IQueryable<Dictionary<string, object?>> scopedRows,
        IReadOnlyCollection<string>? currentValueIds,
        Expression<Func<Dictionary<string, object?>, bool>>? readPredicate,
        Func<IReadOnlyDictionary<string, object?>, T> map,
        CancellationToken cancellationToken)
    {
        var result = new List<OrganizationDirectoryCurrentItem<T>>();
        var normalizedIds = (currentValueIds ?? [])
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal);
        foreach (var ids in normalizedIds.Chunk(100))
        {
            var rows = scopedRows.AsNoTracking()
                .Where(row => ids.Contains(EF.Property<string>(row, "EID")));
            var existing = await rows.Select(row => EF.Property<string>(row, "EID"))
                .ToListAsync(cancellationToken);
            if (readPredicate is not null) rows = rows.Where(readPredicate);
            var readable = (await rows.ToListAsync(cancellationToken))
                .ToDictionary(row => (string)row["EID"]!, StringComparer.Ordinal);
            foreach (var id in existing)
            {
                result.Add(readable.TryGetValue(id, out var row)
                    ? new OrganizationDirectoryCurrentItem<T>.Readable(map(row))
                    : new OrganizationDirectoryCurrentItem<T>.Masked(id));
            }
        }
        return result;
    }
}

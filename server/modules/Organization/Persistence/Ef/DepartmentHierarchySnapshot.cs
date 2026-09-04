using ICP.Platform.Modules.Organization.Directories;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Persistence.Ef;

internal enum DepartmentParentRelationship
{
    Allowed,
    Self,
    Descendant,
    Corrupted
}

/// <summary>
/// 持有一次有界公司层级读取，并在内存中完成后代排除和祖先防环裁决。
/// </summary>
internal sealed class DepartmentHierarchySnapshot
{
    private readonly IReadOnlyDictionary<string, string?> _parents;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _children;

    // 单次有界读取后的索引只在当前请求内存活，避免逐层数据库往返。
    private DepartmentHierarchySnapshot(IReadOnlyList<DepartmentHierarchyNode> nodes)
    {
        _parents = nodes.ToDictionary(static node => node.Id, static node => node.ParentId, StringComparer.Ordinal);
        _children = nodes
            .Where(static node => node.ParentId is not null)
            .GroupBy(static node => node.ParentId!, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<string>)group.Select(static node => node.Id).ToArray(),
                StringComparer.Ordinal);
    }

    // Take(max + 1) 让超限成为确定失败，同时把物化量和后续 SQL 参数限制在 1024。
    internal static async Task<DepartmentHierarchySnapshot> LoadAsync(
        IQueryable<Dictionary<string, object?>> departments,
        string companyId,
        CancellationToken cancellationToken)
    {
        var nodes = await departments
            .Where(row => EF.Property<string>(row, "CompanyId") == companyId)
            .OrderBy(row => EF.Property<string>(row, "EID"))
            .Select(row => new DepartmentHierarchyNode(
                EF.Property<string>(row, "EID"),
                EF.Property<string?>(row, "ParentDepartmentId")))
            .Take(DepartmentHierarchyLimits.MaximumDepartmentsPerCompany + 1)
            .ToListAsync(cancellationToken);
        if (nodes.Count > DepartmentHierarchyLimits.MaximumDepartmentsPerCompany)
        {
            throw new DepartmentHierarchyLimitExceededException();
        }

        return new DepartmentHierarchySnapshot(nodes);
    }

    // 遍历已物化的有界索引，返回当前部门及全部后代作为数据库分页前的排除集合。
    internal IReadOnlyCollection<string> ReadSubtreeIds(string currentDepartmentId)
    {
        var subtree = new HashSet<string>(StringComparer.Ordinal) { currentDepartmentId };
        var pending = new Queue<string>();
        pending.Enqueue(currentDepartmentId);
        while (pending.TryDequeue(out var parentId))
        {
            if (!_children.TryGetValue(parentId, out var children))
            {
                continue;
            }

            foreach (var childId in children)
            {
                if (subtree.Add(childId))
                {
                    pending.Enqueue(childId);
                }
            }
        }

        return subtree;
    }

    // 沿有界父索引裁决候选关系；断链或既有环都失败关闭为损坏状态。
    internal DepartmentParentRelationship ClassifyParent(
        string currentDepartmentId,
        string candidateParentId)
    {
        if (string.Equals(currentDepartmentId, candidateParentId, StringComparison.Ordinal))
        {
            return DepartmentParentRelationship.Self;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = candidateParentId;
        while (cursor is not null)
        {
            if (string.Equals(cursor, currentDepartmentId, StringComparison.Ordinal))
            {
                return DepartmentParentRelationship.Descendant;
            }

            if (!visited.Add(cursor) || !_parents.TryGetValue(cursor, out cursor))
            {
                return DepartmentParentRelationship.Corrupted;
            }
        }

        return DepartmentParentRelationship.Allowed;
    }

    private sealed record DepartmentHierarchyNode(string Id, string? ParentId);
}

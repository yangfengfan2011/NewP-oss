using ICP.Platform.Persistence.Transactions;

namespace ICP.Platform.Modules.Organization.Caching;

/// <summary>只校验组织自有的结构关系；停用档案与未分配人员都是有效缓存事实。</summary>
internal static class OrganizationSnapshotValidation
{
    // 首次加载遍历全部档案；上级链使用已完成集合，避免每个节点重复走完整链。
    public static void ValidateAll(OrganizationSnapshot snapshot, CancellationToken cancellationToken)
    {
        ValidateDepartments(snapshot, snapshot.Departments.Keys, cancellationToken);
        foreach (var id in snapshot.Persons.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidatePerson(snapshot, id);
        }
    }

    // 日常候选只检查变更记录与删除节点的直接引用，不扫描全部人员或部门。
    public static void ValidateChanges(OrganizationSnapshot snapshot, IReadOnlyList<PlatformRecordChange> changes, CancellationToken cancellationToken)
    {
        ValidateDepartments(snapshot, changes.Where(c => c.ModelKey == "Organization/Department").Select(c => c.Id).Distinct(), cancellationToken);
        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (change.ModelKey)
            {
                case "Organization/Company" when !snapshot.Companies.ContainsKey(change.Id):
                    Require(!snapshot.DepartmentsByCompany.ContainsKey(change.Id) && !snapshot.PersonsByCompany.ContainsKey(change.Id));
                    break;
                case "Organization/Department" when !snapshot.Departments.ContainsKey(change.Id):
                    Require(!snapshot.ChildDepartments.ContainsKey(change.Id) && !snapshot.PersonsByDepartment.ContainsKey(change.Id));
                    break;
                case "Organization/Position" when !snapshot.Positions.ContainsKey(change.Id):
                    Require(!snapshot.PersonsByPosition.ContainsKey(change.Id));
                    break;
                case "Organization/Person" when snapshot.Persons.ContainsKey(change.Id):
                    ValidatePerson(snapshot, change.Id);
                    break;
            }
        }
    }

    // 结构校验不等同于准入：包含停用上级，但拒绝不存在、跨公司或成环的父级。
    private static void ValidateDepartments(OrganizationSnapshot snapshot, IEnumerable<string> ids, CancellationToken cancellationToken)
    {
        var completed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!snapshot.Departments.ContainsKey(id)) continue;
            var path = new HashSet<string>(StringComparer.Ordinal);
            string? current = id;
            while (current is not null && !completed.Contains(current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Require(path.Add(current));
                Require(snapshot.Departments.TryGetValue(current, out var row));
                var company = Text(row!, "CompanyId");
                Require(company is not null && snapshot.Companies.ContainsKey(company));
                var parent = Text(row!, "ParentDepartmentId");
                if (parent is not null)
                    Require(snapshot.Departments.TryGetValue(parent, out var parentRow) && Text(parentRow, "CompanyId") == company);
                current = parent;
            }
            completed.UnionWith(path);
        }
    }

    // 准备阶段允许公司/部门同时为空，岗位和永久账号关系独立保存。
    private static void ValidatePerson(OrganizationSnapshot snapshot, string id)
    {
        var row = snapshot.Persons[id];
        var company = Text(row, "CompanyId");
        var department = Text(row, "DepartmentId");
        Require((company is null) == (department is null));
        if (company is not null)
            Require(snapshot.Companies.ContainsKey(company) && snapshot.Departments.TryGetValue(department!, out var departmentRow)
                && Text(departmentRow, "CompanyId") == company);
        var position = Text(row, "PositionId");
        Require(position is null || snapshot.Positions.ContainsKey(position));
    }

    // 损坏关系必须使整份候选失败，不发布部分档案或静默忽略引用。
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Organization cache contains inconsistent relationships.");
    }

    // 所有可选关系使用当前原始标量，不读取其他上下文的数据。
    private static string? Text(IReadOnlyDictionary<string, object?> row, string field) => row.GetValueOrDefault(field) as string;
}

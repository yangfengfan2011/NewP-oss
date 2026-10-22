using ICP.Platform.Organization.Contracts;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>归属与换父共享同一次组织视图，沿目标的实际祖先索引验证完整启用链。</summary>
internal static class OrganizationTargetChain
{
    // 公司由调用者提供字段级错误；部门链不扫描全组织，也不随层数往返数据库。
    public static bool IsEnabled(IOrganizationReadView view, string companyId, string departmentId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? current = departmentId;
        while (current is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(current)) return false;
            var row = view.Find(OrganizationRecordKind.Department, current);
            if (row is null || row.GetValueOrDefault("IsEnabled") is not true
                || !Equals(row.GetValueOrDefault("CompanyId"), companyId)) return false;
            current = row.GetValueOrDefault("ParentDepartmentId") as string;
        }
        return true;
    }
}

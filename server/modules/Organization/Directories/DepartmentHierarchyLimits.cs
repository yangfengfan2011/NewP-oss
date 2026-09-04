namespace ICP.Platform.Modules.Organization.Directories;

/// <summary>
/// 定义部门层级裁决的固定容量边界与稳定失败契约。
/// </summary>
public static class DepartmentHierarchyLimits
{
    /// <summary>单个公司可参与一次层级裁决的最大部门数量。</summary>
    public const int MaximumDepartmentsPerCompany = 1024;

    /// <summary>超过层级裁决容量时返回的稳定错误码。</summary>
    public const string ErrorCode = "Organization.Department.HierarchyLimitExceeded";
}

/// <summary>
/// 表示单公司部门数量超过层级裁决的固定安全边界。
/// </summary>
public sealed class DepartmentHierarchyLimitExceededException : InvalidOperationException
{
    /// <summary>
    /// 创建包含稳定错误码和容量的层级超限异常。
    /// </summary>
    public DepartmentHierarchyLimitExceededException()
        : base($"单个公司的部门层级最多支持 {DepartmentHierarchyLimits.MaximumDepartmentsPerCompany} 个部门参与一次裁决。")
    {
    }

    /// <summary>获取稳定错误码。</summary>
    public string Code => DepartmentHierarchyLimits.ErrorCode;

    /// <summary>获取当前层级裁决的固定容量。</summary>
    public int MaximumDepartments => DepartmentHierarchyLimits.MaximumDepartmentsPerCompany;
}

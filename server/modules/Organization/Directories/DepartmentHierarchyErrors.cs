namespace ICP.Platform.Modules.Organization.Directories;

internal static class DepartmentHierarchyErrors
{
    internal const string Corrupted = "Organization.Department.HierarchyCorrupted";
}

internal sealed class DepartmentHierarchyCorruptedException : InvalidOperationException
{
    // Lookup 无字段可供用户修复，使用稳定资源错误阻止从损坏层级继续选择上级。
    internal DepartmentHierarchyCorruptedException()
        : base("现有部门层级包含环，不能读取可选上级。")
    {
    }

    internal string Code => DepartmentHierarchyErrors.Corrupted;
}

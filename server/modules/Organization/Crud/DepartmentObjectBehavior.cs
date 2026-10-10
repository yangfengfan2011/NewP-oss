using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>
/// 部门普通写入的对象声明；创建、编辑、调父级、启停和条件删除复用标准 CRUD 核心。
/// </summary>
public static class DepartmentObjectBehavior
{
    /// <summary>部门普通创建、更新和条件删除共享统一行为与前后横切。</summary>
    public static ObjectBehaviorDefinition BehaviorDefinition { get; } = new(
        "organization.department",
        "Organization",
        "Department",
        [ObjectOperationAction.Create, ObjectOperationAction.Update, ObjectOperationAction.Delete],
        [ObjectOperationAction.Create, ObjectOperationAction.Update, ObjectOperationAction.Delete],
        ["EID", "CompanyId", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"]);
}

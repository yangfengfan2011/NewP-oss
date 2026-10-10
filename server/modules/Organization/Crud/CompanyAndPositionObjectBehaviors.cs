using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>
/// 声明公司对象支持的正式动作、横切开放范围和受保护输入；实现直接采用公共默认行为。
/// </summary>
public static class CompanyObjectBehavior
{
    /// <summary>公司普通创建、更新和条件删除共享统一行为与前后横切。</summary>
    public static ObjectBehaviorDefinition BehaviorDefinition { get; } = new(
        "organization.company",
        "Organization",
        "Company",
        [ObjectOperationAction.Create, ObjectOperationAction.Update, ObjectOperationAction.Delete],
        [ObjectOperationAction.Create, ObjectOperationAction.Update, ObjectOperationAction.Delete],
        ["EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"]);
}

/// <summary>
/// 声明岗位对象支持的正式动作、横切开放范围和受保护输入；实现直接采用公共默认行为。
/// </summary>
public static class PositionObjectBehavior
{
    /// <summary>岗位普通创建、更新和条件删除共享统一行为与前后横切。</summary>
    public static ObjectBehaviorDefinition BehaviorDefinition { get; } = new(
        "organization.position",
        "Organization",
        "Position",
        [ObjectOperationAction.Create, ObjectOperationAction.Update, ObjectOperationAction.Delete],
        [ObjectOperationAction.Create, ObjectOperationAction.Update, ObjectOperationAction.Delete],
        ["EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"]);
}

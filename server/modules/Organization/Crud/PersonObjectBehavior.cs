using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>
/// 声明人员普通 CRUD 的对象操作；专用生命周期和账号绑定由业务服务拥有。
/// </summary>
public static class PersonObjectBehavior
{
    /// <summary>人员普通创建、更新和条件删除共享默认行为与前后横切。</summary>
    public static ObjectBehaviorDefinition BehaviorDefinition { get; } = new(
        "organization.person",
        "Organization",
        "Person",
        [ObjectOperationAction.Create, ObjectOperationAction.Update, ObjectOperationAction.Delete],
        [ObjectOperationAction.Create, ObjectOperationAction.Update, ObjectOperationAction.Delete],
        ["EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"]);
}

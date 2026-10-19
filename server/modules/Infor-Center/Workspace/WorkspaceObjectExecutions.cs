using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Modules.Workspace;

/// <summary>组合入口与目标owner分别形成自己的正式来源，不携带已允许结果。</summary>
public static class WorkspaceObjectExecutions
{
    public const string CreatePart = "workspace.content.createPart";
    public const string CreateDocument = "workspace.content.createDocument";
    public const string Delete = "workspace.content.delete";
    public const string DeleteTarget = "workspace.content.delete.target";
    /// <summary>Part组合创建只消费真实提交时存在的父对象，不制造打开表单预检。</summary>
    public static DataExecutionBindingReference CreatePartBinding() => new(CreatePart, WorkspaceFolderExecutions.Provider, "submit");
    /// <summary>Part真实候选在owner补齐系统字段后按已登记内部创建合同裁决。</summary>
    public static ObjectOperationWriteSource.Declared PartCreateSource() => new(new(
        new("part.part.internal.create", "standardCrud.create", "submit"), "part.part", null)
        { Internal = new("part.part.internal.create", "standardCrud.create", typeof(WorkspaceContents), nameof(WorkspaceContents.CreateObjectAsync)) });
    /// <summary>只有既有文件暂存prepare与正式创建submit消费父要求，不对取消追加对象权限。</summary>
    public static DataExecutionBindingReference CreateDocumentBinding(string stage) => new(CreateDocument, WorkspaceFolderExecutions.Provider, stage);
    /// <summary>对象删除的公共入口只消费真实父位置，目标资格在原LID解析点独立裁决。</summary>
    public static DataExecutionBindingReference DeleteBinding() => new(Delete, WorkspaceFolderExecutions.Provider, "submit");
    /// <summary>单个与选择删除共享同一真实协调调用者，不能与移除引用执行串用。</summary>
    public static DataExecutionInternalBinding DeleteTargetCaller() => new(DeleteTarget, WorkspaceFolderExecutions.Provider,
        typeof(WorkspaceContents), nameof(WorkspaceContents.MutateObjectsAsync));
    /// <summary>所属对象在原before-facts时点重验自身固定写合同，不继承外层已允许标记。</summary>
    public static ObjectOperationWriteSource.Declared MutationSource(string model, string id, ObjectOperationAction action)
    {
        var resource = model switch { "Part" => "part.part", "Document" => "document.document", _ => throw new ArgumentOutOfRangeException(nameof(model)) };
        var operation = action == ObjectOperationAction.Update ? "update" : action == ObjectOperationAction.Delete ? "delete"
            : throw new ArgumentOutOfRangeException(nameof(action));
        var key = resource + "." + operation;
        return new(new(new(key, "standardCrud." + operation, "submit"), resource, id)
            { Internal = new(key, "standardCrud." + operation, typeof(WorkspaceContents), nameof(WorkspaceContents.MutateObjectsAsync)) });
    }
}

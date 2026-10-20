using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Modules.Workspace;

/// <summary>组合入口与目标owner分别形成自己的正式来源，不携带已允许结果。</summary>
public static class WorkspaceObjectExecutions
{
    public const string CreatePart = "workspace.content.createPart";
    public const string CreateDocument = "workspace.content.createDocument";
    /// <summary>Part组合创建只消费真实提交时存在的父对象，不制造打开表单预检。</summary>
    public static DataExecutionBindingReference CreatePartBinding() => new(CreatePart, WorkspaceFolderExecutions.Provider, "submit");
    /// <summary>Part真实候选在owner补齐系统字段后按已登记内部创建合同裁决。</summary>
    public static ObjectOperationWriteSource.Declared PartCreateSource() => new(new(
        new("part.part.internal.create", "standardCrud.create", "submit"), "part.part", null)
        { Internal = new("part.part.internal.create", "standardCrud.create", typeof(WorkspaceContents), nameof(WorkspaceContents.CreateObjectAsync)) });
    /// <summary>只有既有文件暂存prepare与正式创建submit消费父要求，不对取消追加对象权限。</summary>
    public static DataExecutionBindingReference CreateDocumentBinding(string stage) => new(CreateDocument, WorkspaceFolderExecutions.Provider, stage);
}

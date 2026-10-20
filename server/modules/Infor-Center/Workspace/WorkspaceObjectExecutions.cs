using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.ObjectOperations;

namespace ICP.Platform.Modules.Workspace;

/// <summary>组合入口与目标owner分别形成自己的正式来源，不携带已允许结果。</summary>
public static class WorkspaceObjectExecutions
{
    public const string CreateDocument = "workspace.content.createDocument";
    /// <summary>只有既有文件暂存prepare与正式创建submit消费父要求，不对取消追加对象权限。</summary>
    public static DataExecutionBindingReference CreateDocumentBinding(string stage) => new(CreateDocument, WorkspaceFolderExecutions.Provider, stage);
}

using System.Globalization;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Modules.Audit.Contracts;

namespace ICP.Platform.Modules.Workspace;

/// <summary>Workspace 显式声明关系业务事实；Audit 取得可信主体、时间和 Trace，原平台事务统一保存提交。</summary>
public sealed class WorkspaceReferenceAudit(IBusinessAuditRecorder recorder)
{
    /// <summary>真副本区分原引用复制；同事务登记可信源和新对象，安全引用不携带来源名称或路径。</summary>
    public ValueTask ObjectCopiedAsync(string sourceLinkId, string sourceParentId, LinkObjectReference source, string targetLinkId,
        string targetParentId, string targetId, CancellationToken token) => recorder.RegisterAsync(new(
            "workspace.object.copied", "workspace.content", targetLinkId,
            [new("SourceLID", null, BusinessAuditValue.Reference("workspace.content", sourceLinkId)),
                new("SourcePEID", null, BusinessAuditValue.Reference("workspace.folder", sourceParentId)),
                new("SourceObject", null, BusinessAuditValue.Reference(source.ModelKey, source.Id)),
                new("PEID", null, BusinessAuditValue.Reference("workspace.folder", targetParentId)),
                new("ChildModelKey", null, BusinessAuditValue.Text(source.ModelKey)),
                new("CEID", null, BusinessAuditValue.Reference(source.ModelKey, targetId))]), token);

    /// <summary>浅副本记录新建独立文件夹事实；来源使用正式安全引用，不保存名称、路径或旧审计。</summary>
    public ValueTask FolderCopiedAsync(string sourceLinkId, string sourceFolderId, string folderId, string parentId, string rootLinkId,
        CancellationToken cancellationToken) => recorder.RegisterAsync(new(
            "workspace.folder.shallowCopied", "workspace.folder", folderId,
            [new("SourceLID", null, BusinessAuditValue.Reference("workspace.content", sourceLinkId)),
                new("RootLID", null, BusinessAuditValue.Reference("workspace.content", rootLinkId)),
                new("SourcePEID", null, BusinessAuditValue.Reference("workspace.folder", sourceFolderId)),
                new("PEID", null, BusinessAuditValue.Reference("workspace.folder", parentId)),
                new("CEID", null, BusinessAuditValue.Reference("workspace.folder", folderId))]), cancellationToken);

    /// <summary>结构复制每个新对象记录来源和新身份；安全引用不携带原路径、字段或旧历史。
    /// </summary>
    public ValueTask StructureCopiedAsync(string sourceId, string folderId, CancellationToken token) => recorder.RegisterAsync(new(
        "workspace.folder.structureCopied", "workspace.folder", folderId,
        [new("SourcePEID", null, BusinessAuditValue.Reference("workspace.folder", sourceId)),
            new("CEID", null, BusinessAuditValue.Reference("workspace.folder", folderId))]), token);

    /// <summary>从锁内核证的源关系及本次登记的新 LID 记录复制；不保存完整位置、客户端载荷或对象影子字段。</summary>
    public ValueTask CopiedAsync(string sourceLinkId, string sourceParentId, string targetLinkId, string targetParentId,
        LinkObjectReference endpoint, CancellationToken cancellationToken) => recorder.RegisterAsync(new(
            "workspace.reference.copied", "workspace.content", targetLinkId,
            [new("SourceLID", null, BusinessAuditValue.Reference("workspace.content", sourceLinkId)),
                new("SourcePEID", null, BusinessAuditValue.Reference("workspace.folder", sourceParentId)),
                new("PEID", null, BusinessAuditValue.Reference("workspace.folder", targetParentId)),
                new("ChildModelKey", null, BusinessAuditValue.Text(endpoint.ModelKey)),
                new("CEID", null, BusinessAuditValue.Reference(endpoint.ModelKey, endpoint.Id))]), cancellationToken);

    /// <summary>真实移动以新 LID 为目标，保留被删除的源 LID、源父及目标关系事实；原事务决定记录是否生效。</summary>
    public ValueTask MovedAsync(string sourceLinkId, string sourceParentId, string targetLinkId, string targetParentId,
        LinkObjectReference endpoint, CancellationToken cancellationToken) => recorder.RegisterAsync(new(
            "workspace.reference.moved", "workspace.content", targetLinkId,
            [new("SourceLID", null, BusinessAuditValue.Reference("workspace.content", sourceLinkId)),
                new("SourcePEID", null, BusinessAuditValue.Reference("workspace.folder", sourceParentId)),
                new("PEID", null, BusinessAuditValue.Reference("workspace.folder", targetParentId)),
                new("ChildModelKey", null, BusinessAuditValue.Text(endpoint.ModelKey)),
                new("CEID", null, BusinessAuditValue.Reference(endpoint.ModelKey, endpoint.Id))]), cancellationToken);

    /// <summary>既有关系换位保留父与端点上下文，只记录真实旧次序到最终新次序，不登记事务内借位的 NULL。</summary>
    public ValueTask ReorderedAsync(string linkId, string parentId, LinkObjectReference endpoint, long before, long after,
        CancellationToken cancellationToken) => recorder.RegisterAsync(new("workspace.reference.reordered", "workspace.content", linkId,
            [new("PEID", BusinessAuditValue.Reference("workspace.folder", parentId), BusinessAuditValue.Reference("workspace.folder", parentId)),
                new("ChildModelKey", BusinessAuditValue.Text(endpoint.ModelKey), BusinessAuditValue.Text(endpoint.ModelKey)),
                new("CEID", BusinessAuditValue.Reference(endpoint.ModelKey, endpoint.Id), BusinessAuditValue.Reference(endpoint.ModelKey, endpoint.Id)),
                new("TreeOrdinal", BusinessAuditValue.Text(before.ToString(CultureInfo.InvariantCulture)), BusinessAuditValue.Text(after.ToString(CultureInfo.InvariantCulture)))]), cancellationToken);

    /// <summary>单条和批量移除均以确切旧 LID 为目标，字段记录旧关系事实到显式空值，不表示删除对象。</summary>
    public ValueTask RemovedAsync(string linkId, string parentId, LinkObjectReference endpoint, CancellationToken cancellationToken) =>
        recorder.RegisterAsync(new("workspace.reference.removed", "workspace.content", linkId,
            [new("PEID", BusinessAuditValue.Reference("workspace.folder", parentId), BusinessAuditValue.Reference("workspace.folder", null)),
                new("ChildModelKey", BusinessAuditValue.Text(endpoint.ModelKey), BusinessAuditValue.Text(null)),
                new("CEID", BusinessAuditValue.Reference(endpoint.ModelKey, endpoint.Id), BusinessAuditValue.Reference(endpoint.ModelKey, null))]), cancellationToken);
}

using System.Globalization;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Modules.Audit.Contracts;

namespace ICP.Platform.Modules.Workspace;

/// <summary>Workspace 显式声明关系业务事实；Audit 取得可信主体、时间和 Trace，原平台事务统一保存提交。</summary>
public sealed class WorkspaceReferenceAudit(IBusinessAuditRecorder recorder)
{
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

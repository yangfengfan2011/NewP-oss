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
}

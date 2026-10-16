using ICP.Platform.Crud.Models;

namespace ICP.Platform.Modules.Document.Contracts;

/// <summary>Document拥有的不可由HTTP客户端编辑的确认快照；Workspace只持久绑定和回传。</summary>
public sealed record DocumentCopySource(string Confirmation, string Code, string Name, string? Description, int FileCount, long TotalBytes);

/// <summary>Document拥有当前regular及文件集合；字节准备必须在Workspace图锁和业务事务外。</summary>
public interface IDocumentCopies
{
    /// <summary>复用正常创建候选规则核对新编码、字段和create资格；不准备文件，也不套用上传策略。</summary>
    Task<StandardCrudError?> ValidateCopyCandidateAsync(IReadOnlyDictionary<string, object?> values, CancellationToken token = default);
    /// <summary>核证真实当前身份、read和真副本资格，返回一致确认与安全规模，不要求download。</summary>
    Task<DocumentCopySource> ReadCopySourceAsync(string documentId, CancellationToken token = default);
    /// <summary>重验确认后按持久操作key准备完整主附件；不创建业务对象。</summary>
    Task PrepareCopyFilesAsync(string confirmation, string operationKey, CancellationToken token = default);
    /// <summary>仅取消该主体、该操作的未确认准备，不释放已提交使用。</summary>
    Task CancelCopyFilesAsync(string confirmation, string operationKey, CancellationToken token = default);
}

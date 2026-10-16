namespace ICP.Platform.Modules.Files.Contracts;

/// <summary>本主体的复制准备结果；request key 由可信业务操作分配，不作为来源授权。</summary>
public sealed record FileCopyPreparation(string RequestKey, string State, string? FileId, string? FileName,
    string? ContentType, long? Length, string? Sha256);

/// <summary>从真实 owner 使用派生的独立物理复制；传输与最终业务确认分离。</summary>
public interface IFileCopyService
{
    /// <summary>独立短事务登记来源及接收意图后流式克隆；不采用上传大小或格式限制，不要求 download。</summary>
    Task<FileCopyPreparation> PrepareCopyAsync(string requestKey, FileUsageReference source, string targetOwnerKey, CancellationToken token = default);
    /// <summary>按主体和用途核对持久状态；不存在返回 null，不重新传输或创建业务对象。</summary>
    Task<FileCopyPreparation?> ReadCopyAsync(string requestKey, string targetOwnerKey, CancellationToken token = default);
    /// <summary>在原业务事务中以真实 Added 目标证明确认新使用，不保存或提交。</summary>
    Task<ConfirmedFileUsage> ConfirmCopyAsync(string requestKey, FileOwnerReference target, CancellationToken token = default);
    /// <summary>取消本人未确认准备；先提交取消事实，再中断本准备writer，已确认内容保持。</summary>
    Task CancelCopyAsync(string requestKey, string targetOwnerKey, CancellationToken token = default);
}

namespace ICP.Platform.Modules.Files.Contracts;

/// <summary>本主体的复制准备结果；request key 由可信业务操作分配，不作为来源授权。</summary>
public sealed record FileCopyPreparation(string RequestKey, string State, string? FileId, string? FileName,
    string? ContentType, long? Length, string? Sha256);

/// <summary>从真实 owner 使用派生的独立物理复制；传输与最终业务确认分离。</summary>
public interface IFileCopyService
{
    /// <summary>owner提供本次实际读取产生的操作上下文，由其policy核证来源，独立短事务登记意图后流式克隆；不采用上传准入或要求download。</summary>
    Task<FileCopyPreparation> PrepareCopyAsync(string requestKey, FileUsageReference source, string targetOwnerKey, FileOwnerOperationContext context, CancellationToken token = default);
    /// <summary>按主体和用途核对持久状态；不存在返回 null，不重新传输或创建业务对象。</summary>
    Task<FileCopyPreparation?> ReadCopyAsync(string requestKey, string targetOwnerKey, CancellationToken token = default);
    /// <summary>在原事务中分别交owner核证源上下文与目标真实创建来源/Added证明；不新增目标read，不保存或提交。</summary>
    Task<ConfirmedFileUsage> ConfirmCopyAsync(string requestKey, FileOwnerReference target, FileOwnerOperationContext source, FileOwnerOperationContext creation, CancellationToken token = default);
    /// <summary>取消本人未确认准备；先提交取消事实，再中断本准备writer，已确认内容保持。</summary>
    Task CancelCopyAsync(string requestKey, string targetOwnerKey, CancellationToken token = default);
}
